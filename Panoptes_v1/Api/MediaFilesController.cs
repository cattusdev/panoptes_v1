using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;
using Panoptes_v1.Services.Media;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Panoptes_v1.Api
{
    // Σερβίρει τα αρχεία μετά από έλεγχο δικαιωμάτων: /media/{id}/{έκδοση}/{thumb|medium|large|original}[?download=true]
    // Τα δημόσια σερβίρονται κατευθείαν: /media/p/... (static files) ή από το public bucket (S3/R2).
    [ApiController]
    [Route("media")]
    [AllowAnonymous]
    public class MediaFilesController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IMediaStorage _storage;
        private readonly MediaAttachments _attachments;

        public MediaFilesController(ApplicationDbContext db, IMediaStorage storage, MediaAttachments attachments)
        {
            _db = db;
            _storage = storage;
            _attachments = attachments;
        }

        [HttpGet("{id:guid}/{version:int}/{variant}")]
        public async Task<IActionResult> Get(Guid id, int version, string variant, bool download = false)
        {
            // Χωρίς δικαίωμα: 404 (όχι 403), ώστε να μη φαίνεται ποια αρχεία υπάρχουν
            var item = await _db.MediaItems.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
            if (item == null || !MediaAccess.CanView(User, item)) return NotFound();
            if (item.Purpose == MediaPurpose.Library && !await _attachments.CanSeeAsync(User, id, HttpContext.RequestAborted)) return NotFound();

            var mediaVersion = await _db.MediaVersions.AsNoTracking().FirstOrDefaultAsync(v => v.MediaItemId == id && v.Number == version);
            var file = mediaVersion?.Variants.FirstOrDefault(v => v.Name == variant);
            if (mediaVersion == null || file == null) return NotFound();

            // Αν μια μετακίνηση δεν ολοκληρώθηκε (π.χ. διακοπή), το αρχείο ίσως είναι ακόμα στην άλλη περιοχή
            var area = MediaAccess.StorageArea(item);
            var other = area == MediaVisibility.Public ? MediaVisibility.Private : MediaVisibility.Public;
            var stored = await _storage.OpenReadAsync(area, file.StorageKey, HttpContext.RequestAborted)
                ?? await _storage.OpenReadAsync(other, file.StorageKey, HttpContext.RequestAborted);
            if (stored == null) return NotFound();

            var cache = area == MediaVisibility.Public ? MediaDelivery.PublicCache : MediaDelivery.PrivateCache;
            MediaDelivery.ApplyHeaders(Response, file.StorageKey, cache, download ? mediaVersion.OriginalFileName : null);
            return File(stored.Content, MediaDelivery.ContentTypeFor(file.StorageKey), stored.LastModifiedUtc, entityTag: null!, enableRangeProcessing: true);
        }
    }
}
