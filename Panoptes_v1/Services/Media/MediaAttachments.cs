using System.Security.Claims;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;
using Panoptes_v1.Services.Access;
using Microsoft.EntityFrameworkCore;

namespace Panoptes_v1.Services.Media
{
    // Πού είναι συνδεδεμένο ένα αρχείο (EntityType/EntityId) και μέσα σε ποιο project ζει η εγγραφή.
    public record AttachmentTarget(string EntityType, string EntityId, Guid? ProjectId = null, string? RequiredPermission = null);

    // Συνδέσεις αρχείων με εγγραφές και η πρόσβαση που προκύπτει από αυτές.
    // Κανόνας: ισχύει η πιο αυστηρή σύνδεση. Ένα αρχείο ενός restricted project δεν φαίνεται σε όποιον δεν βλέπει το project,
    // ούτε μέσα από τη βιβλιοθήκη, την αναζήτηση, τον κάδο, τους φακέλους ή το link του αρχείου, ακόμα κι αν έχει και άλλη σύνδεση.
    public class MediaAttachments
    {
        private readonly ApplicationDbContext _db;
        private readonly IProjectAccess _projects;

        public MediaAttachments(ApplicationDbContext db, IProjectAccess projects)
        {
            _db = db;
            _projects = projects;
        }

        // Φίλτρο για κάθε query αρχείων. Αρχεία χωρίς συνδέσεις: όπως πριν (κοινή βιβλιοθήκη).
        public IQueryable<MediaItem> VisibleTo(IQueryable<MediaItem> items, ClaimsPrincipal user)
        {
            if (user.IsInRole(SystemRoles.Admin)) return items;

            var attachments = _db.MediaAttachments;
            var granted = Permissions.GetAll().Where(p => Permissions.Has(user, p)).ToList();
            items = items.Where(i => !attachments.Any(a =>
                a.MediaItemId == i.Id && a.RequiredPermission != null && !granted.Contains(a.RequiredPermission)));

            var scope = _projects.VisibleProjects(user);
            if (scope.All) return items;
            if (scope.Ids is IQueryable<Guid> projectIds)
                return items.Where(i => !attachments.Any(a =>
                    a.MediaItemId == i.Id && a.ProjectId != null && !projectIds.Contains(a.ProjectId.Value)));
            return items.Where(i => !attachments.Any(a => a.MediaItemId == i.Id && a.ProjectId != null));
        }

        public Task<bool> CanSeeAsync(ClaimsPrincipal user, Guid itemId, CancellationToken cancellationToken = default) =>
            VisibleTo(_db.MediaItems.Where(i => i.Id == itemId), user).AnyAsync(cancellationToken);

        public async Task<HashSet<Guid>> AttachedAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken = default)
        {
            if (ids.Count == 0) return [];
            var attached = await _db.MediaAttachments.Where(a => ids.Contains(a.MediaItemId))
                .Select(a => a.MediaItemId).Distinct().ToListAsync(cancellationToken);
            return attached.ToHashSet();
        }

        // Γιατί δεν γίνεται η σύνδεση (μήνυμα για μετάφραση), ή null.
        // Μόνο αρχεία της βιβλιοθήκης, ιδιωτικά (τα δημόσια σερβίρονται χωρίς κανέναν έλεγχο) και όχι από τον κάδο.
        public static string? CannotAttach(MediaItem item) =>
            item.Purpose != MediaPurpose.Library || item.DeletedAt != null ? "The file no longer exists."
            : item.Visibility == MediaVisibility.Public ? "Public files can't be attached to records. Make the file private first."
            : null;

        // Ήδη συνδεδεμένο με την ίδια εγγραφή: επιστρέφει την υπάρχουσα σύνδεση (ασφαλές να ξανατρέξει)
        public async Task<MediaAttachment> AttachAsync(MediaItem item, AttachmentTarget target, string userId, CancellationToken cancellationToken = default)
        {
            if (CannotAttach(item) is string reason)
                throw new InvalidOperationException(reason);

            var existing = await FindAsync(item.Id, target, cancellationToken);
            if (existing != null) return existing;

            var attachment = new MediaAttachment
            {
                Id = Guid.NewGuid(),
                MediaItemId = item.Id,
                EntityType = target.EntityType,
                EntityId = target.EntityId,
                ProjectId = target.ProjectId,
                RequiredPermission = target.RequiredPermission,
                CreatedById = userId,
                CreatedAt = DateTime.UtcNow,
            };
            _db.MediaAttachments.Add(attachment);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return attachment;
            }
            catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: Npgsql.PostgresErrorCodes.UniqueViolation })
            {
                // Ταυτόχρονη σύνδεση του ίδιου αρχείου: κρατάμε αυτή που μπήκε πρώτη
                _db.Entry(attachment).State = EntityState.Detached;
                return (await FindAsync(item.Id, target, cancellationToken))!;
            }
        }

        // Αποσύνδεση: σβήνει μόνο τη σύνδεση, όχι το αρχείο (μπορεί να χρησιμοποιείται αλλού)
        public async Task<bool> DetachAsync(Guid itemId, string entityType, string entityId, CancellationToken cancellationToken = default)
        {
            var attachment = await _db.MediaAttachments.FirstOrDefaultAsync(a =>
                a.MediaItemId == itemId && a.EntityType == entityType && a.EntityId == entityId, cancellationToken);
            if (attachment == null) return false;
            _db.MediaAttachments.Remove(attachment);
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }

        private Task<MediaAttachment?> FindAsync(Guid itemId, AttachmentTarget target, CancellationToken cancellationToken) =>
            _db.MediaAttachments.FirstOrDefaultAsync(a =>
                a.MediaItemId == itemId && a.EntityType == target.EntityType && a.EntityId == target.EntityId, cancellationToken);
    }
}
