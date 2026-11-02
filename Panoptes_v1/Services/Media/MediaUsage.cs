using Panoptes_v1.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace Panoptes_v1.Services.Media
{
    // Πού χρησιμοποιείται ένα αρχείο της βιβλιοθήκης. Ό,τι χρησιμοποιείται (π.χ. το λογότυπο)
    // δεν πάει στον κάδο ούτε γίνεται ιδιωτικό, ώστε να μη "σπάσει" κάτι που φαίνεται στο site.
    // Αρχεία συνδεδεμένα με εγγραφές (MediaAttachment) δεν πάνε στον κάδο, δεν σβήνονται και δεν γίνονται δημόσια.
    public class MediaUsage
    {
        private readonly ApplicationDbContext _db;
        private readonly IStringLocalizer<SharedResource> _t;

        public MediaUsage(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
        {
            _db = db;
            _t = localizer;
        }

        // Μήνυμα για το πρώτο αρχείο που χρησιμοποιείται, ή null αν κανένα.
        // mustStayPublic: το μήνυμα για αλλαγή σε ιδιωτικό (αντί για κάδο).
        public async Task<string?> BlockingUsageAsync(IReadOnlyCollection<Guid> ids, bool mustStayPublic = false)
        {
            if (ids.Count == 0) return null;

            if (!mustStayPublic && await FirstAttachedTitleAsync(ids) is string attached)
                return _t["“{0}” is attached to records. Remove it from them first.", attached];

            var branding = await _db.Branding.AsNoTracking().FirstOrDefaultAsync();
            if (branding == null) return null;

            // Ολόκληρες προτάσεις (μεταφράζονται)
            var uses = new[]
            {
                (branding.LogoMediaId, mustStayPublic
                    ? "“{0}” is used as the app logo. It must stay public. Choose another one in Settings → Branding first."
                    : "“{0}” is used as the app logo. Choose another one in Settings → Branding first."),
                (branding.FaviconMediaId, mustStayPublic
                    ? "“{0}” is used as the favicon. It must stay public. Choose another one in Settings → Branding first."
                    : "“{0}” is used as the favicon. Choose another one in Settings → Branding first."),
            };
            foreach (var (id, message) in uses)
            {
                if (id is Guid used && ids.Contains(used))
                {
                    var title = await _db.MediaItems.Where(i => i.Id == used).Select(i => i.Title).FirstOrDefaultAsync();
                    return _t[message, title ?? ""];
                }
            }
            return null;
        }

        // Μήνυμα αν κάποιο αρχείο δεν επιτρέπεται να γίνει δημόσιο (τα δημόσια σερβίρονται χωρίς έλεγχο), ή null
        public async Task<string?> PublicBlockAsync(IReadOnlyCollection<Guid> ids) =>
            await FirstAttachedTitleAsync(ids) is string attached
                ? _t["“{0}” is attached to records and must stay private.", attached].Value
                : null;

        private Task<string?> FirstAttachedTitleAsync(IReadOnlyCollection<Guid> ids) =>
            ids.Count == 0
                ? Task.FromResult<string?>(null)
                : _db.MediaItems.Where(i => ids.Contains(i.Id) && _db.MediaAttachments.Any(a => a.MediaItemId == i.Id))
                    .OrderBy(i => i.Title).Select(i => (string?)i.Title).FirstOrDefaultAsync();
    }
}
