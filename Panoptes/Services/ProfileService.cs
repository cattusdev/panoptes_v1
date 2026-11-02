using Panoptes.Data;
using Panoptes.Models;
using Panoptes.Models.Media;
using Panoptes.Services.Media;
using Microsoft.EntityFrameworkCore;

namespace Panoptes.Services
{
    // Φωτογραφία προφίλ: ένα αρχείο media με Purpose = Avatar (εκτός βιβλιοθήκης, το βλέπουν όσοι έχουν κάνει login)
    public class ProfileService
    {
        private readonly ApplicationDbContext _db;
        private readonly MediaService _media;

        public ProfileService(ApplicationDbContext db, MediaService media)
        {
            _db = db;
            _media = media;
        }

        // userId -> URL της μικρογραφίας (μόνο όσοι έχουν φωτογραφία)
        public async Task<Dictionary<string, string>> AvatarUrlsAsync(IEnumerable<string> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var rows = await _db.UserProfiles.AsNoTracking()
                .Where(p => ids.Contains(p.UserId) && p.Avatar != null && p.Avatar.DeletedAt == null)
                .Select(p => new { p.UserId, Item = p.Avatar!, Version = p.Avatar!.Versions.First(v => v.Id == p.Avatar.CurrentVersionId) })
                .ToListAsync();

            var urls = new Dictionary<string, string>();
            foreach (var row in rows)
            {
                var thumb = row.Version.Variants.FirstOrDefault(v => v.Name == ImageProcessor.Thumb);
                if (thumb != null) urls[row.UserId] = _media.FileUrl(row.Item, row.Version, thumb);
            }
            return urls;
        }

        public async Task<string?> AvatarUrlAsync(string userId) =>
            (await AvatarUrlsAsync([userId])).GetValueOrDefault(userId);

        // Η γλώσσα που διάλεξε ο χρήστης (null = η προεπιλογή της εφαρμογής)
        public async Task<string?> LanguageAsync(string userId) =>
            await _db.UserProfiles.Where(p => p.UserId == userId).Select(p => p.Language).FirstOrDefaultAsync();

        // Για emails: η γλώσσα του παραλήπτη από το email του (null αν δεν έχει λογαριασμό ή προτίμηση)
        public async Task<string?> LanguageForEmailAsync(string email)
        {
            var normalized = email.ToUpperInvariant();
            return await _db.Users.Where(u => u.NormalizedEmail == normalized)
                .Join(_db.UserProfiles, u => u.Id, p => p.UserId, (u, p) => p.Language)
                .FirstOrDefaultAsync();
        }

        public async Task SetLanguageAsync(string userId, string? language)
        {
            var profile = await _db.UserProfiles.FindAsync(userId);
            if (profile == null)
            {
                if (language == null) return;
                profile = new UserProfile { UserId = userId };
                _db.UserProfiles.Add(profile);
            }
            profile.Language = language;
            await _db.SaveChangesAsync();
        }

        // Η νέα φωτογραφία (ήδη ανεβασμένη) γίνεται η τρέχουσα· η προηγούμενη σβήνεται
        public async Task<bool> SetAvatarAsync(string userId, Guid mediaId)
        {
            var item = await _db.MediaItems.FirstOrDefaultAsync(i =>
                i.Id == mediaId && i.Purpose == MediaPurpose.Avatar && i.OwnerId == userId && i.DeletedAt == null && i.Kind == MediaKind.Image);
            if (item == null) return false;

            var profile = await _db.UserProfiles.FindAsync(userId);
            if (profile == null)
            {
                profile = new UserProfile { UserId = userId };
                _db.UserProfiles.Add(profile);
            }

            var previous = profile.AvatarMediaId;
            profile.AvatarMediaId = item.Id;
            await _db.SaveChangesAsync();

            if (previous != null && previous != item.Id)
                await DeleteItemAsync(previous.Value);
            return true;
        }

        public async Task RemoveAvatarAsync(string userId)
        {
            var profile = await _db.UserProfiles.FindAsync(userId);
            if (profile?.AvatarMediaId is not Guid previous) return;

            profile.AvatarMediaId = null;
            await _db.SaveChangesAsync();
            await DeleteItemAsync(previous);
        }

        // Όταν σβήνεται ένας χρήστης: και η φωτογραφία του
        public async Task DeleteAllForUserAsync(string userId)
        {
            var avatars = await _db.MediaItems.Include(i => i.Versions)
                .Where(i => i.OwnerId == userId && i.Purpose == MediaPurpose.Avatar)
                .ToListAsync();
            if (avatars.Count > 0)
                await _media.DeletePermanentlyAsync(avatars);
        }

        private async Task DeleteItemAsync(Guid id)
        {
            var item = await _db.MediaItems.Include(i => i.Versions).FirstOrDefaultAsync(i => i.Id == id);
            if (item != null)
                await _media.DeletePermanentlyAsync([item]);
        }
    }
}
