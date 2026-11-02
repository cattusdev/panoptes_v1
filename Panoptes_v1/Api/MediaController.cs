using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;
using Panoptes_v1.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Panoptes_v1.Api
{
    [ApiController]
    [Route("api/media")]
    [Authorize]
    public class MediaController : ControllerBase
    {
        private const int MaxPageSize = 100;

        private readonly ApplicationDbContext _db;
        private readonly MediaService _media;
        private readonly MediaUsage _usage;
        private readonly MediaAttachments _attachments;
        private readonly IStringLocalizer<SharedResource> _t;

        public MediaController(ApplicationDbContext db, MediaService media, MediaUsage usage, MediaAttachments attachments, IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _db = db;
            _media = media;
            _usage = usage;
            _attachments = attachments;
        }

        // Τα αρχεία της βιβλιοθήκης που επιτρέπεται να δει ο χρήστης (οι συνδέσεις με εγγραφές μπορεί να τα κρύβουν)
        private IQueryable<MediaItem> Library(IQueryable<MediaItem> source) =>
            _attachments.VisibleTo(source.Where(i => i.Purpose == MediaPurpose.Library), User);

        // Τα όρια για τον client (ίδιος έλεγχος και στον browser, πριν ανέβει οτιδήποτε)
        [HttpGet("settings")]
        public ActionResult<MediaSettingsDto> Settings()
        {
            var options = _media.Options;
            var kinds = Enum.GetValues<MediaKind>()
                .Select(kind => new MediaKindLimitsDto(kind, options.ExtensionsFor(kind), options.MaxBytesFor(kind)))
                .ToList();
            return new MediaSettingsDto(options.ChunkSize, options.MaxPixels, options.MaxVersions, options.TrashRetentionDays, options.AllowSvg, kinds);
        }

        // GET /api/media?folderId=&search=&kind=&owner=mine&visibility=&sort=newest|oldest|name&trash=&cursor=&limit=
        // Η αναζήτηση ψάχνει σε όλους τους φακέλους. Σελιδοποίηση με cursor (σταθερή όταν προστίθενται αρχεία).
        [HttpGet]
        [Authorize(Policy = Permissions.Media.View)]
        public async Task<ActionResult<MediaPageDto>> List(
            Guid? folderId, string? search, MediaKind? kind, string? owner, MediaVisibility? visibility,
            string sort = "newest", bool trash = false, string? cursor = null, int limit = 60)
        {
            limit = Math.Clamp(limit, 1, MaxPageSize);
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var query = Library(_db.MediaItems.AsNoTracking());
            query = trash ? query.Where(i => i.DeletedAt != null) : query.Where(i => i.DeletedAt == null);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var pattern = Like.Contains(search.Trim());
                query = query.Where(i => EF.Functions.ILike(i.Title, pattern) ||
                    i.Versions.Any(v => v.Id == i.CurrentVersionId && EF.Functions.ILike(v.OriginalFileName, pattern)));
            }
            else if (!trash)
            {
                query = query.Where(i => i.FolderId == folderId);
            }

            if (kind != null) query = query.Where(i => i.Kind == kind);
            if (visibility != null) query = query.Where(i => i.Visibility == visibility);
            if (owner == "mine") query = query.Where(i => i.OwnerId == userId);

            var position = Cursor.Decode(cursor);
            switch (sort)
            {
                case "name":
                    if (position != null)
                    {
                        var (title, sequence) = (position.Title ?? "", position.Sequence);
                        query = query.Where(i => string.Compare(i.Title, title) > 0 || (i.Title == title && i.Sequence > sequence));
                    }
                    query = query.OrderBy(i => i.Title).ThenBy(i => i.Sequence);
                    break;
                case "oldest":
                    if (position != null) query = query.Where(i => i.Sequence > position.Sequence);
                    query = query.OrderBy(i => i.Sequence);
                    break;
                default:
                    if (position != null) query = query.Where(i => i.Sequence < position.Sequence);
                    query = query.OrderByDescending(i => i.Sequence);
                    break;
            }

            var rows = await Project(query.Take(limit + 1)).ToListAsync();
            var hasMore = rows.Count > limit;
            if (hasMore) rows.RemoveAt(rows.Count - 1);

            var next = hasMore ? Cursor.Encode(new Cursor(sort == "name" ? rows[^1].Item.Title : null, rows[^1].Item.Sequence)) : null;
            return new MediaPageDto(rows.Select(r => MediaDtos.Item(r.Item, r.Version, r.OwnerEmail, User, _media)).ToList(), next);
        }

        [HttpGet("{id:guid}")]
        [Authorize(Policy = Permissions.Media.View)]
        public async Task<ActionResult<MediaItemDto>> Get(Guid id)
        {
            var row = await Project(Library(_db.MediaItems.AsNoTracking()).Where(i => i.Id == id)).FirstOrDefaultAsync();
            if (row == null) return NotFound();
            return MediaDtos.Item(row.Item, row.Version, row.OwnerEmail, User, _media);
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = Permissions.Media.Edit)]
        public async Task<ActionResult<MediaItemDto>> Update(Guid id, UpdateMediaRequest request)
        {
            var item = await Library(_db.MediaItems.Include(i => i.Versions))
                .FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null);
            if (item == null) return NotFound();
            if (!MediaAccess.CanEdit(User, item))
                return Problem(_t["You can only edit your own files."], statusCode: StatusCodes.Status403Forbidden);

            if (request.Visibility == MediaVisibility.Private && item.Visibility == MediaVisibility.Public &&
                await _usage.BlockingUsageAsync([item.Id], mustStayPublic: true) is string usedPrivate)
                return Problem(usedPrivate, statusCode: StatusCodes.Status409Conflict);
            if (request.Visibility == MediaVisibility.Public && item.Visibility == MediaVisibility.Private &&
                await _usage.PublicBlockAsync([item.Id]) is string attached)
                return Problem(attached, statusCode: StatusCodes.Status409Conflict);

            item.Title = request.Title.Trim();
            item.AltText = NullIfBlank(request.AltText);
            item.Description = NullIfBlank(request.Description);
            item.UpdatedAt = DateTime.UtcNow;

            if (item.Visibility != request.Visibility)
                await _media.SetVisibilityAsync(item, request.Visibility);
            else
                await _db.SaveChangesAsync();

            return await Get(id);
        }

        [HttpPost("move")]
        [Authorize(Policy = Permissions.Media.Edit)]
        public async Task<IActionResult> Move(MoveMediaRequest request)
        {
            if (request.FolderId is Guid folderId && !await _db.MediaFolders.AnyAsync(f => f.Id == folderId))
                return Problem(_t["The folder no longer exists."], statusCode: StatusCodes.Status400BadRequest);

            var items = await LoadAsync(request.Ids, trashed: false);
            if (items.Any(i => !MediaAccess.CanEdit(User, i)))
                return Problem(_t["You can only move your own files."], statusCode: StatusCodes.Status403Forbidden);

            foreach (var item in items)
            {
                item.FolderId = request.FolderId;
                item.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();
            return Ok(new { moved = items.Count });
        }

        [HttpPost("trash")]
        [Authorize(Policy = Permissions.Media.Delete)]
        public async Task<IActionResult> Trash(MediaIdsRequest request)
        {
            var items = await LoadAsync(request.Ids, trashed: false);
            if (items.Any(i => !MediaAccess.CanDelete(User, i)))
                return Problem(_t["You can only delete your own files."], statusCode: StatusCodes.Status403Forbidden);
            if (await _usage.BlockingUsageAsync(items.Select(i => i.Id).ToList()) is string used)
                return Problem(used, statusCode: StatusCodes.Status409Conflict);

            await _media.TrashAsync(items);
            return Ok(new { trashed = items.Count });
        }

        [HttpPost("restore")]
        [Authorize(Policy = Permissions.Media.Delete)]
        public async Task<IActionResult> Restore(MediaIdsRequest request)
        {
            var items = await LoadAsync(request.Ids, trashed: true);
            if (items.Any(i => !MediaAccess.CanDelete(User, i)))
                return Problem(_t["You can only restore your own files."], statusCode: StatusCodes.Status403Forbidden);

            await _media.RestoreAsync(items);
            return Ok(new { restored = items.Count });
        }

        // Οριστική διαγραφή: μόνο από τον κάδο
        [HttpPost("purge")]
        [Authorize(Policy = Permissions.Media.Delete)]
        public async Task<IActionResult> Purge(MediaIdsRequest request)
        {
            var items = await LoadAsync(request.Ids, trashed: true);
            if (items.Any(i => !MediaAccess.CanDelete(User, i)))
                return Problem(_t["You can only delete your own files."], statusCode: StatusCodes.Status403Forbidden);

            if (await _usage.BlockingUsageAsync(items.Select(i => i.Id).ToList()) is string used)
                return Problem(used, statusCode: StatusCodes.Status409Conflict);

            var deleted = await _media.DeletePermanentlyAsync(items);
            return Ok(new { deleted });
        }

        // Άδειασμα κάδου: ό,τι μπορείς να διαγράψεις (τα δικά σου, ή όλα με ManageAll)
        [HttpPost("trash/empty")]
        [Authorize(Policy = Permissions.Media.Delete)]
        public async Task<IActionResult> EmptyTrash()
        {
            var items = (await Library(_db.MediaItems.Include(i => i.Versions))
                    .Where(i => i.DeletedAt != null)
                    .ToListAsync())
                .Where(i => MediaAccess.CanDelete(User, i))
                .ToList();

            var deleted = await _media.DeletePermanentlyAsync(items);
            return Ok(new { deleted });
        }

        [HttpGet("{id:guid}/versions")]
        [Authorize(Policy = Permissions.Media.View)]
        public async Task<ActionResult<IReadOnlyList<MediaVersionDto>>> Versions(Guid id)
        {
            var item = await Library(_db.MediaItems.AsNoTracking()).FirstOrDefaultAsync(i => i.Id == id);
            if (item == null) return NotFound();

            var versions = await _db.MediaVersions.AsNoTracking()
                .Where(v => v.MediaItemId == id)
                .OrderByDescending(v => v.Number)
                .Select(v => new { Version = v, CreatedBy = _db.Users.Where(u => u.Id == v.CreatedById).Select(u => u.Email).FirstOrDefault() })
                .ToListAsync();

            return versions.Select(v => MediaDtos.Version(item, v.Version, v.CreatedBy, _media)).ToList();
        }

        [HttpPost("{id:guid}/versions/{versionId:guid}/restore")]
        [Authorize(Policy = Permissions.Media.Edit)]
        public async Task<ActionResult<MediaItemDto>> RestoreVersion(Guid id, Guid versionId)
        {
            var item = await Library(_db.MediaItems.Include(i => i.Versions))
                .FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null);
            var version = item?.Versions.FirstOrDefault(v => v.Id == versionId);
            if (item == null || version == null) return NotFound();
            if (!MediaAccess.CanEdit(User, item))
                return Problem(_t["You can only edit your own files."], statusCode: StatusCodes.Status403Forbidden);

            await _media.RestoreVersionAsync(item, version);
            return await Get(id);
        }

        [HttpDelete("{id:guid}/versions/{versionId:guid}")]
        [Authorize(Policy = Permissions.Media.Delete)]
        public async Task<IActionResult> DeleteVersion(Guid id, Guid versionId)
        {
            var item = await Library(_db.MediaItems.Include(i => i.Versions))
                .FirstOrDefaultAsync(i => i.Id == id && i.DeletedAt == null);
            var version = item?.Versions.FirstOrDefault(v => v.Id == versionId);
            if (item == null || version == null) return NotFound();
            if (!MediaAccess.CanDelete(User, item))
                return Problem(_t["You can only delete your own files."], statusCode: StatusCodes.Status403Forbidden);
            if (version.Id == item.CurrentVersionId)
                return Problem(_t["The current version can't be deleted. Restore another version first."], statusCode: StatusCodes.Status400BadRequest);

            await _media.DeleteVersionAsync(version);
            return NoContent();
        }

        private record Row(MediaItem Item, MediaVersion Version, string? OwnerEmail);

        private IQueryable<Row> Project(IQueryable<MediaItem> query) =>
            query.Select(i => new Row(
                i,
                i.Versions.First(v => v.Id == i.CurrentVersionId),
                _db.Users.Where(u => u.Id == i.OwnerId).Select(u => u.Email).FirstOrDefault()));

        private Task<List<MediaItem>> LoadAsync(IEnumerable<Guid> ids, bool trashed)
        {
            var idList = ids.Distinct().ToList();
            return Library(_db.MediaItems.Include(i => i.Versions))
                .Where(i => idList.Contains(i.Id) && (trashed ? i.DeletedAt != null : i.DeletedAt == null))
                .ToListAsync();
        }

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        // Η θέση στη λίστα (τελευταίο αρχείο της σελίδας), κωδικοποιημένη για το URL
        private record Cursor(string? Title, long Sequence)
        {
            public static string Encode(Cursor cursor) =>
                WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(cursor)));

            public static Cursor? Decode(string? value)
            {
                if (string.IsNullOrEmpty(value)) return null;
                try
                {
                    return JsonSerializer.Deserialize<Cursor>(WebEncoders.Base64UrlDecode(value));
                }
                catch (Exception e) when (e is FormatException or JsonException)
                {
                    return null;
                }
            }
        }
    }
}
