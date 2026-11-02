using System.Security.Claims;
using Panoptes.Constants;
using Panoptes.Data;
using Panoptes.Models.Media;
using Panoptes.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Panoptes.Api
{
    [ApiController]
    [Route("api/media/folders")]
    [Authorize]
    public class MediaFoldersController : ControllerBase
    {
        private const int MaxDepth = 10;
        private static readonly char[] InvalidNameChars = ['/', '\\', ':', '*', '?', '"', '<', '>', '|'];

        private readonly ApplicationDbContext _db;
        private readonly MediaService _media;
        private readonly MediaUsage _usage;
        private readonly IStringLocalizer<SharedResource> _t;

        public MediaFoldersController(ApplicationDbContext db, MediaService media, MediaUsage usage, IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _db = db;
            _media = media;
            _usage = usage;
        }

        // Όλοι οι φάκελοι μαζί (είναι λίγοι): το δέντρο το χτίζει ο client
        [HttpGet]
        [Authorize(Policy = Permissions.Media.View)]
        public async Task<ActionResult<IReadOnlyList<MediaFolderDto>>> List()
        {
            var folders = await _db.MediaFolders.AsNoTracking().OrderBy(f => f.Name).ToListAsync();
            var counts = await _db.MediaItems
                .Where(i => i.Purpose == MediaPurpose.Library && i.DeletedAt == null && i.FolderId != null)
                .GroupBy(i => i.FolderId)
                .Select(g => new { FolderId = g.Key!.Value, Count = g.Count() })
                .ToDictionaryAsync(g => g.FolderId, g => g.Count);

            return folders.Select(f => ToDto(f, counts.GetValueOrDefault(f.Id))).ToList();
        }

        [HttpPost]
        [Authorize(Policy = Permissions.Media.Upload)]
        public async Task<ActionResult<MediaFolderDto>> Create(FolderRequest request)
        {
            var name = CleanName(request.Name);
            if (name == null) return InvalidName();

            var folders = await _db.MediaFolders.ToListAsync();
            if (request.ParentId is Guid parentId && folders.All(f => f.Id != parentId))
                return Problem(_t["The parent folder no longer exists."], statusCode: StatusCodes.Status400BadRequest);
            if (Depth(folders, request.ParentId) + 1 > MaxDepth)
                return Problem(_t["Folders can be nested up to {0} levels.", MaxDepth], statusCode: StatusCodes.Status400BadRequest);
            if (NameTaken(folders, request.ParentId, name, exceptId: null))
                return NameTakenProblem();

            var folder = new MediaFolder
            {
                Id = Guid.NewGuid(),
                Name = name,
                NormalizedName = Normalize(name),
                ParentId = request.ParentId,
                CreatedById = User.FindFirstValue(ClaimTypes.NameIdentifier)!,
                CreatedAt = DateTime.UtcNow,
            };
            _db.MediaFolders.Add(folder);
            if (!await TrySaveAsync()) return NameTakenProblem();

            return Ok(ToDto(folder, 0));
        }

        [HttpPut("{id:guid}")]
        [Authorize(Policy = Permissions.Media.Edit)]
        public async Task<ActionResult<MediaFolderDto>> Rename(Guid id, FolderRequest request)
        {
            var folders = await _db.MediaFolders.ToListAsync();
            var folder = folders.FirstOrDefault(f => f.Id == id);
            if (folder == null) return NotFound();
            if (!MediaAccess.CanEditFolder(User, folder))
                return Problem(_t["You can only rename your own folders."], statusCode: StatusCodes.Status403Forbidden);

            var name = CleanName(request.Name);
            if (name == null) return InvalidName();
            if (NameTaken(folders, folder.ParentId, name, exceptId: id))
                return NameTakenProblem();

            folder.Name = name;
            folder.NormalizedName = Normalize(name);
            if (!await TrySaveAsync()) return NameTakenProblem();

            return Ok(ToDto(folder, await CountItemsAsync(id)));
        }

        [HttpPost("{id:guid}/move")]
        [Authorize(Policy = Permissions.Media.Edit)]
        public async Task<ActionResult<MediaFolderDto>> Move(Guid id, MoveFolderRequest request)
        {
            var folders = await _db.MediaFolders.ToListAsync();
            var folder = folders.FirstOrDefault(f => f.Id == id);
            if (folder == null) return NotFound();
            if (!MediaAccess.CanEditFolder(User, folder))
                return Problem(_t["You can only move your own folders."], statusCode: StatusCodes.Status403Forbidden);

            if (request.ParentId is Guid parentId)
            {
                if (folders.All(f => f.Id != parentId))
                    return Problem(_t["The destination folder no longer exists."], statusCode: StatusCodes.Status400BadRequest);
                // Όχι μέσα στον εαυτό του ή σε υποφάκελό του (κύκλος)
                if (Descendants(folders, id).Append(id).Contains(parentId))
                    return Problem(_t["A folder can't be moved into itself or one of its subfolders."], statusCode: StatusCodes.Status400BadRequest);
            }

            if (Depth(folders, request.ParentId) + 1 + SubtreeHeight(folders, id) > MaxDepth)
                return Problem(_t["Folders can be nested up to {0} levels.", MaxDepth], statusCode: StatusCodes.Status400BadRequest);
            if (NameTaken(folders, request.ParentId, folder.Name, exceptId: id))
                return NameTakenProblem();

            folder.ParentId = request.ParentId;
            if (!await TrySaveAsync()) return NameTakenProblem();

            return Ok(ToDto(folder, await CountItemsAsync(id)));
        }

        // Τι θα επηρεάσει η διαγραφή (για το μήνυμα επιβεβαίωσης)
        [HttpGet("{id:guid}/summary")]
        [Authorize(Policy = Permissions.Media.View)]
        public async Task<ActionResult<MediaFolderSummaryDto>> Summary(Guid id)
        {
            var folders = await _db.MediaFolders.AsNoTracking().ToListAsync();
            if (folders.All(f => f.Id != id)) return NotFound();

            var subtree = Descendants(folders, id).Append(id).ToList();
            var files = await _db.MediaItems.CountAsync(i => i.Purpose == MediaPurpose.Library && i.DeletedAt == null && i.FolderId != null && subtree.Contains(i.FolderId.Value));
            return new MediaFolderSummaryDto(files, subtree.Count - 1);
        }

        // Άδειος φάκελος: σβήνεται. Με περιεχόμενο (withContents=true): τα αρχεία πάνε στον κάδο
        // (θυμούνται τον φάκελό τους) και οι φάκελοι σβήνονται.
        [HttpDelete("{id:guid}")]
        [Authorize(Policy = Permissions.Media.Delete)]
        public async Task<IActionResult> Delete(Guid id, bool withContents = false)
        {
            var folders = await _db.MediaFolders.ToListAsync();
            var folder = folders.FirstOrDefault(f => f.Id == id);
            if (folder == null) return NotFound();
            if (!MediaAccess.CanDeleteFolder(User, folder))
                return Problem(_t["You can only delete your own folders."], statusCode: StatusCodes.Status403Forbidden);

            var subtree = Descendants(folders, id).Append(id).ToList();
            var items = await _db.MediaItems.Include(i => i.Versions)
                .Where(i => i.Purpose == MediaPurpose.Library && i.DeletedAt == null && i.FolderId != null && subtree.Contains(i.FolderId.Value))
                .ToListAsync();

            if ((items.Count > 0 || subtree.Count > 1) && !withContents)
                return Problem(_t["The folder isn't empty."], statusCode: StatusCodes.Status409Conflict);
            if (items.Any(i => !MediaAccess.CanDelete(User, i)))
                return Problem(_t["The folder contains files you can't delete."], statusCode: StatusCodes.Status403Forbidden);
            if (await _usage.BlockingUsageAsync(items.Select(i => i.Id).ToList()) is string used)
                return Problem(used, statusCode: StatusCodes.Status409Conflict);

            if (items.Count > 0)
                await _media.TrashAsync(items);

            _db.MediaFolders.RemoveRange(folders.Where(f => subtree.Contains(f.Id)));
            await _db.SaveChangesAsync();
            return NoContent();
        }

        private MediaFolderDto ToDto(MediaFolder folder, int itemCount) => new(
            folder.Id,
            folder.Name,
            folder.ParentId,
            itemCount,
            MediaAccess.CanEditFolder(User, folder),
            MediaAccess.CanDeleteFolder(User, folder));

        private Task<int> CountItemsAsync(Guid folderId) =>
            _db.MediaItems.CountAsync(i => i.FolderId == folderId && i.DeletedAt == null && i.Purpose == MediaPurpose.Library);

        private static string? CleanName(string name)
        {
            var clean = string.Join(' ', name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (clean.Length is 0 or > 100 || clean is "." or ".." || clean.IndexOfAny(InvalidNameChars) >= 0 || clean.Any(char.IsControl))
                return null;
            return clean;
        }

        private static string Normalize(string name) => name.ToUpperInvariant();

        private static bool NameTaken(List<MediaFolder> folders, Guid? parentId, string name, Guid? exceptId) =>
            folders.Any(f => f.ParentId == parentId && f.Id != exceptId && f.NormalizedName == Normalize(name));

        private static IEnumerable<Guid> Descendants(List<MediaFolder> folders, Guid id)
        {
            foreach (var child in folders.Where(f => f.ParentId == id))
            {
                yield return child.Id;
                foreach (var descendant in Descendants(folders, child.Id))
                    yield return descendant;
            }
        }

        // Βάθος ενός φακέλου (ρίζα = 0)
        private static int Depth(List<MediaFolder> folders, Guid? id)
        {
            var depth = 0;
            while (id is Guid current && depth <= MaxDepth)
            {
                depth++;
                id = folders.FirstOrDefault(f => f.Id == current)?.ParentId;
            }
            return depth;
        }

        private static int SubtreeHeight(List<MediaFolder> folders, Guid id) =>
            folders.Where(f => f.ParentId == id).Select(f => 1 + SubtreeHeight(folders, f.Id)).DefaultIfEmpty(0).Max();

        // Το unique index πιάνει και ταυτόχρονες δημιουργίες με το ίδιο όνομα
        private async Task<bool> TrySaveAsync()
        {
            try
            {
                await _db.SaveChangesAsync();
                return true;
            }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                return false;
            }
        }

        private ActionResult InvalidName() =>
            Problem(_t["Folder names must be 1–100 characters and can't contain / \\ : * ? \" < > |"], statusCode: StatusCodes.Status400BadRequest);

        private ActionResult NameTakenProblem() =>
            Problem(_t["A folder with this name already exists here."], statusCode: StatusCodes.Status409Conflict);
    }
}
