using System.Security.Claims;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;
using Panoptes_v1.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Panoptes_v1.Api
{
    // Ανέβασμα σε κομμάτια (chunks), ένας δρόμος για κάθε μέγεθος αρχείου:
    //   POST   /api/media/uploads          { fileName, size, folderId, visibility, replaceItemId } -> { id, chunkSize }
    //   PATCH  /api/media/uploads/{id}     Upload-Offset + bytes -> 204 (συνεχίζει) ή 200 + αρχείο (τελείωσε)
    //   HEAD   /api/media/uploads/{id}     -> Upload-Offset (συνέχεια μετά από διακοπή)
    //   GET    /api/media/uploads/{id}     -> το αρχείο που δημιουργήθηκε
    //   DELETE /api/media/uploads/{id}     -> αναίρεση (σβήνει ό,τι μόλις ανέβηκε)
    [ApiController]
    [Route("api/media/uploads")]
    [Authorize]
    public class MediaUploadsController : ControllerBase
    {
        private const string OffsetHeader = "Upload-Offset";

        private readonly ApplicationDbContext _db;
        private readonly MediaService _media;
        private readonly MediaPaths _paths;
        private readonly MediaUploadLocks _locks;
        private readonly IStringLocalizer<SharedResource> _t;

        public MediaUploadsController(ApplicationDbContext db, MediaService media, MediaPaths paths, MediaUploadLocks locks,
            IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _db = db;
            _media = media;
            _paths = paths;
            _locks = locks;
        }

        private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        [HttpPost]
        [EnableRateLimiting(MediaRateLimits.Uploads)]
        public async Task<ActionResult<UploadStartedDto>> Start(StartUploadRequest request)
        {
            if (request.Purpose == MediaPurpose.Avatar)
            {
                // Φωτογραφία προφίλ: κάθε συνδεδεμένος χρήστης, μόνο για τον εαυτό του (το ορίζει μετά το /api/account/avatar)
                if (request.ReplaceItemId != null)
                    return Problem(_t["Profile photos can't replace library files."], statusCode: StatusCodes.Status400BadRequest);
            }
            else if (request.Purpose != MediaPurpose.Library)
            {
                return Problem(_t["This kind of upload isn't supported."], statusCode: StatusCodes.Status400BadRequest);
            }
            else if (request.ReplaceItemId is Guid replaceId)
            {
                var item = await _db.MediaItems.AsNoTracking()
                    .FirstOrDefaultAsync(i => i.Id == replaceId && i.DeletedAt == null && i.Purpose == MediaPurpose.Library);
                if (item == null || !MediaAccess.CanView(User, item)) return NotFound();
                if (!MediaAccess.CanEdit(User, item))
                    return Problem(_t["You can only replace your own files."], statusCode: StatusCodes.Status403Forbidden);
            }
            else if (!Permissions.Has(User, Permissions.Media.Upload))
            {
                return Problem(_t["You don't have permission to upload files."], statusCode: StatusCodes.Status403Forbidden);
            }

            MediaKind kind;
            try
            {
                kind = FileSniffer.CheckName(request.FileName, _media.Options);
            }
            catch (MediaValidationException e)
            {
                return Problem(e.Localize(_t), statusCode: StatusCodes.Status400BadRequest);
            }

            if (request.Purpose == MediaPurpose.Avatar && (kind != MediaKind.Image || FileSniffer.ExtensionOf(request.FileName) == "svg"))
                return Problem(_t["Profile photos must be JPG, PNG, GIF or WebP images."], statusCode: StatusCodes.Status400BadRequest);
            if (request.Size <= 0)
                return Problem(_t["The file is empty."], statusCode: StatusCodes.Status400BadRequest);
            if (request.Size > _media.Options.MaxBytesFor(kind))
                return Problem(_media.TooLarge(kind).Localize(_t), statusCode: StatusCodes.Status400BadRequest);

            if (request.Purpose == MediaPurpose.Library && request.ReplaceItemId == null && request.FolderId is Guid folderId && !await _db.MediaFolders.AnyAsync(f => f.Id == folderId))
                return Problem(_t["The folder no longer exists."], statusCode: StatusCodes.Status400BadRequest);

            // Χώρος στον δίσκο: το προσωρινό αρχείο + τα μεγέθη/αντίγραφα που θα φτιαχτούν
            var freeBytes = new DriveInfo(Path.GetPathRoot(_paths.Temp)!).AvailableFreeSpace;
            if (freeBytes < request.Size * 3 + _media.Options.MinFreeDiskMb * 1024 * 1024)
                return Problem(_t["The server is running out of storage space. Please contact an administrator."], statusCode: StatusCodes.Status507InsufficientStorage);

            var upload = new MediaUpload
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                FileName = MediaService.CleanFileName(request.FileName),
                Length = request.Size,
                FolderId = request.ReplaceItemId == null && request.Purpose == MediaPurpose.Library ? request.FolderId : null,
                Visibility = request.Purpose == MediaPurpose.Library ? request.Visibility : MediaVisibility.Private,
                Purpose = request.Purpose,
                ReplaceItemId = request.ReplaceItemId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.MediaUploads.Add(upload);
            await _db.SaveChangesAsync();
            await System.IO.File.WriteAllBytesAsync(_paths.TempFile(upload.Id), []);

            return Ok(new UploadStartedDto(upload.Id, _media.Options.ChunkSize));
        }

        [HttpHead("{id:guid}")]
        public async Task<IActionResult> Offset(Guid id)
        {
            var upload = await FindAsync(id);
            if (upload == null) return NotFound();

            var path = _paths.TempFile(id);
            Response.Headers[OffsetHeader] = (upload.CompletedAt != null ? upload.Length : System.IO.File.Exists(path) ? new FileInfo(path).Length : 0).ToString();
            return Ok();
        }

        [HttpPatch("{id:guid}")]
        [RequestSizeLimit(MediaOptions.MaxChunkBytes + 64 * 1024)]
        [EnableRateLimiting(MediaRateLimits.Chunks)]
        public async Task<IActionResult> Chunk(Guid id, CancellationToken cancellationToken)
        {
            var upload = await FindAsync(id);
            if (upload == null) return NotFound();

            // Επανάληψη του τελευταίου chunk μετά από επιτυχία: ίδια απάντηση
            if (upload.CompletedAt != null)
                return await ResultAsync(upload);

            if (!long.TryParse(Request.Headers[OffsetHeader], out var offset) || offset < 0)
                return Problem(_t["Missing or invalid Upload-Offset header."], statusCode: StatusCodes.Status400BadRequest);
            if (Request.ContentLength is not long length)
                return StatusCode(StatusCodes.Status411LengthRequired);
            if (length > MediaOptions.MaxChunkBytes)
                return StatusCode(StatusCodes.Status413PayloadTooLarge);

            using var _ = await _locks.AcquireAsync(id, cancellationToken);

            var path = _paths.TempFile(id);
            if (!System.IO.File.Exists(path)) return NotFound();
            var current = new FileInfo(path).Length;

            // Κομμάτι που έχουμε ήδη (retry): δεν ξαναγράφεται
            if (offset < current && offset + length <= current)
                return Progress(current);
            if (offset != current)
            {
                Response.Headers[OffsetHeader] = current.ToString();
                return Problem(_t["The upload is out of sync. It will resume from the last received byte."], statusCode: StatusCodes.Status409Conflict);
            }
            if (current + length > upload.Length)
                return Problem(_t["The file is larger than announced."], statusCode: StatusCodes.Status400BadRequest);

            long written;
            await using (var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await Request.Body.CopyToAsync(file, cancellationToken);
                written = file.Length - current;
            }
            if (written != length)
            {
                Truncate(path, current);
                return Problem(_t["The chunk was incomplete."], statusCode: StatusCodes.Status400BadRequest);
            }

            if (current + length < upload.Length)
                return Progress(current + length);

            // Τελευταίο κομμάτι: έλεγχος, επεξεργασία, αποθήκευση
            MediaItem item;
            try
            {
                item = await _media.IngestAsync(new IngestRequest(
                    path, upload.FileName, upload.UserId, upload.Purpose, upload.Visibility, upload.FolderId, upload.ReplaceItemId), cancellationToken);
            }
            catch (MediaValidationException e)
            {
                await DiscardAsync(id);
                return Problem(e.Localize(_t), statusCode: StatusCodes.Status422UnprocessableEntity);
            }
            catch (MediaBusyException e)
            {
                // Το τελευταίο chunk "ξεγράφεται" ώστε η επανάληψη να το ξαναστείλει και να ξαναγίνει η επεξεργασία
                Truncate(path, current);
                Response.Headers.RetryAfter = "5";
                return Problem(_t[e.Message], statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            System.IO.File.Delete(path);
            upload = (await _db.MediaUploads.FindAsync(id))!;
            upload.ResultItemId = item.Id;
            upload.ResultVersionId = item.CurrentVersionId;
            upload.CompletedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(CancellationToken.None);
            _locks.Forget(id);

            return await ResultAsync(upload);
        }

        [HttpGet("{id:guid}")]
        public async Task<IActionResult> Get(Guid id)
        {
            var upload = await FindAsync(id);
            if (upload?.CompletedAt == null) return NotFound();
            return await ResultAsync(upload);
        }

        // Αναίρεση (FilePond "undo"): σβήνει το αρχείο ή την έκδοση που δημιούργησε αυτό το upload
        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Revert(Guid id)
        {
            var upload = await FindAsync(id);
            if (upload == null) return NotFound();

            if (upload.ResultItemId is Guid itemId)
            {
                var item = await _db.MediaItems.Include(i => i.Versions).FirstOrDefaultAsync(i => i.Id == itemId);
                if (item != null && upload.ReplaceItemId == null)
                {
                    await _media.DeletePermanentlyAsync([item]);
                }
                else if (item != null && item.CurrentVersionId == upload.ResultVersionId && item.Versions.Count > 1)
                {
                    var created = item.Versions.First(v => v.Id == upload.ResultVersionId);
                    var previous = item.Versions.Where(v => v.Id != created.Id).MaxBy(v => v.Number)!;
                    await _media.RestoreVersionAsync(item, previous);
                    await _media.DeleteVersionAsync(created);
                }
            }

            await DiscardAsync(id);
            return NoContent();
        }

        private Task<MediaUpload?> FindAsync(Guid id) =>
            _db.MediaUploads.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.UserId == UserId);

        private async Task<IActionResult> ResultAsync(MediaUpload upload)
        {
            var item = await _db.MediaItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == upload.ResultItemId);
            if (item == null) return NotFound();
            var version = await _db.MediaVersions.AsNoTracking().FirstAsync(v => v.Id == item.CurrentVersionId);
            var ownerEmail = await _db.Users.Where(u => u.Id == item.OwnerId).Select(u => u.Email).FirstOrDefaultAsync();
            return Ok(MediaDtos.Item(item, version, ownerEmail, User, _media));
        }

        private IActionResult Progress(long offset)
        {
            Response.Headers[OffsetHeader] = offset.ToString();
            return NoContent();
        }

        private async Task DiscardAsync(Guid id)
        {
            var path = _paths.TempFile(id);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            await _db.MediaUploads.Where(u => u.Id == id).ExecuteDeleteAsync();
            _locks.Forget(id);
        }

        private static void Truncate(string path, long length)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Write);
            file.SetLength(length);
        }
    }

    public static class MediaRateLimits
    {
        public const string Uploads = "media-uploads";
        public const string Chunks = "media-chunks";
    }
}
