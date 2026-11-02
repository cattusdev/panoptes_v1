using System.Security.Claims;
using Panoptes.Constants;
using Panoptes.Models.Media;

namespace Panoptes.Services.Media
{
    // Ποιος βλέπει / αλλάζει τι. Κοινή βιβλιοθήκη: με Media.View βλέπεις τα πάντα,
    // αλλά αλλάζεις/σβήνεις μόνο τα δικά σου (εκτός αν έχεις Media.ManageAll).
    public static class MediaAccess
    {
        public static bool CanView(ClaimsPrincipal user, MediaItem item)
        {
            if (item.DeletedAt != null)
                return Permissions.Has(user, Permissions.Media.View);

            return item.Purpose switch
            {
                MediaPurpose.Library => item.Visibility == MediaVisibility.Public || Permissions.Has(user, Permissions.Media.View),
                MediaPurpose.Avatar => user.Identity?.IsAuthenticated == true,
                _ => true, // Branding: λογότυπο/favicon φαίνονται και στις δημόσιες σελίδες
            };
        }

        public static bool CanEdit(ClaimsPrincipal user, MediaItem item) =>
            item.Purpose == MediaPurpose.Library && Permissions.Has(user, Permissions.Media.Edit) && IsOwnerOrManager(user, item.OwnerId);

        public static bool CanDelete(ClaimsPrincipal user, MediaItem item) =>
            item.Purpose == MediaPurpose.Library && Permissions.Has(user, Permissions.Media.Delete) && IsOwnerOrManager(user, item.OwnerId);

        public static bool CanEditFolder(ClaimsPrincipal user, MediaFolder folder) =>
            Permissions.Has(user, Permissions.Media.Edit) && IsOwnerOrManager(user, folder.CreatedById);

        public static bool CanDeleteFolder(ClaimsPrincipal user, MediaFolder folder) =>
            Permissions.Has(user, Permissions.Media.Delete) && IsOwnerOrManager(user, folder.CreatedById);

        public static bool IsOwnerOrManager(ClaimsPrincipal user, string ownerId) =>
            Permissions.Has(user, Permissions.Media.ManageAll) || user.FindFirstValue(ClaimTypes.NameIdentifier) == ownerId;

        // Σε ποια περιοχή του storage ζουν τα αρχεία: public μόνο όσο το αρχείο είναι δημόσιο και όχι στον κάδο
        public static MediaVisibility StorageArea(MediaItem item) => StorageArea(item.Visibility, item.Purpose, item.DeletedAt);

        public static MediaVisibility StorageArea(MediaVisibility visibility, MediaPurpose purpose, DateTime? deletedAt) =>
            deletedAt == null && (visibility == MediaVisibility.Public || purpose == MediaPurpose.Branding)
                ? MediaVisibility.Public
                : MediaVisibility.Private;
    }
}
