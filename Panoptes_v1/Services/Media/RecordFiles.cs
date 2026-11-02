using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Api;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;

namespace Panoptes_v1.Services.Media
{
    // Ένα αρχείο συνδεδεμένο με μια εγγραφή: το αρχείο, με ποια εγγραφή, πότε και από ποιον
    public record RecordFileDto(MediaItemDto Item, string EntityType, string EntityId, DateTime LinkedAt, string? LinkedBy);

    // Η καρτέλα "Files" κάθε εγγραφής (ARCHITECTURE §8): λίστα, σύνδεση αρχείου της βιβλιοθήκης, αποσύνδεση.
    // Το module της εγγραφής ελέγχει πρώτα ότι ο χρήστης βλέπει / αλλάζει την εγγραφή και δίνει το AttachmentTarget της
    // (με το ProjectId της). Τα αρχεία περνούν πάντα από το MediaAttachments.VisibleTo: η πιο αυστηρή σύνδεση κερδίζει.
    public class RecordFiles
    {
        private readonly ApplicationDbContext _db;
        private readonly MediaAttachments _attachments;
        private readonly MediaService _media;

        public RecordFiles(ApplicationDbContext db, MediaAttachments attachments, MediaService media)
        {
            _db = db;
            _attachments = attachments;
            _media = media;
        }

        // Τα αρχεία της βιβλιοθήκης τα βλέπει όποιος έχει Media.View (ίδιος κανόνας με το κατέβασμα)
        public static bool CanSeeFiles(ClaimsPrincipal user) => Permissions.Has(user, Permissions.Media.View);

        // Τα αρχεία μιας ή περισσότερων εγγραφών (π.χ. ο πελάτης και τα projects του), το πιο πρόσφατο πρώτο
        public async Task<List<RecordFileDto>> ListAsync(IReadOnlyCollection<(string EntityType, string EntityId)> records, ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (records.Count == 0 || !CanSeeFiles(user)) return [];

            var keys = records.Select(r => r.EntityType + "|" + r.EntityId).ToList();
            var items = _attachments.VisibleTo(_db.MediaItems.Where(i => i.DeletedAt == null), user);
            var rows = await _db.MediaAttachments.AsNoTracking()
                .Where(a => keys.Contains(a.EntityType + "|" + a.EntityId) && items.Any(i => i.Id == a.MediaItemId))
                .OrderByDescending(a => a.CreatedAt).ThenBy(a => a.Id)
                .Select(a => new
                {
                    a.EntityType,
                    a.EntityId,
                    a.CreatedAt,
                    a.CreatedById,
                    Item = a.MediaItem!,
                    Version = a.MediaItem!.Versions.First(v => v.Id == a.MediaItem.CurrentVersionId),
                })
                .Take(500)
                .ToListAsync(cancellationToken);

            var userIds = rows.SelectMany(r => new[] { r.CreatedById, r.Item.OwnerId }).Distinct().ToList();
            var emails = await _db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Email, cancellationToken);

            return rows.Select(r => new RecordFileDto(
                MediaDtos.Item(r.Item, r.Version, emails.GetValueOrDefault(r.Item.OwnerId), user, _media),
                r.EntityType,
                r.EntityId,
                DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc),
                emails.GetValueOrDefault(r.CreatedById))).ToList();
        }

        // Σύνδεση ενός αρχείου που ο χρήστης βλέπει. null = δεν υπάρχει / δεν το βλέπει (404). Created = false: ήταν ήδη
        // συνδεδεμένο (ασφαλές να ξανατρέξει). Δημόσιο ή στον κάδο: BusinessRuleException (409).
        public async Task<(string Title, bool Created)?> AttachAsync(Guid mediaItemId, AttachmentTarget target, ClaimsPrincipal user,
            CancellationToken cancellationToken = default)
        {
            if (!CanSeeFiles(user)) return null;
            var item = await _attachments.VisibleTo(_db.MediaItems.Where(i => i.Id == mediaItemId && i.Purpose == MediaPurpose.Library), user)
                .FirstOrDefaultAsync(cancellationToken);
            if (item == null) return null;
            if (MediaAttachments.CannotAttach(item) is string reason)
                throw new BusinessRuleException("media.cannotAttach", reason);

            var existed = await _db.MediaAttachments.AnyAsync(a =>
                a.MediaItemId == item.Id && a.EntityType == target.EntityType && a.EntityId == target.EntityId, cancellationToken);
            await _attachments.AttachAsync(item, target, user.FindFirstValue(ClaimTypes.NameIdentifier)!, cancellationToken);
            return (item.Title, !existed);
        }

        // Αποσύνδεση: σβήνει μόνο τη σύνδεση, το αρχείο μένει στη βιβλιοθήκη. null = δεν ήταν συνδεδεμένο.
        public async Task<string?> DetachAsync(Guid mediaItemId, string entityType, string entityId, CancellationToken cancellationToken = default)
        {
            var title = await _db.MediaItems.Where(i => i.Id == mediaItemId).Select(i => i.Title).FirstOrDefaultAsync(cancellationToken);
            return title != null && await _attachments.DetachAsync(mediaItemId, entityType, entityId, cancellationToken) ? title : null;
        }
    }
}
