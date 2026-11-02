using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Panoptes_v1.Models.Media;
using Panoptes_v1.Services.Media;

namespace Panoptes_v1.Api
{
    public record MediaUrlsDto(string? Thumb, string? Medium, string? Large, string Original, string Download);

    public record MediaVersionDto(
        Guid Id,
        int Number,
        string FileName,
        string Extension,
        string ContentType,
        long SizeBytes,
        int? Width,
        int? Height,
        string? DominantColor,
        DateTime CreatedAt,
        string? CreatedBy,
        bool IsCurrent,
        MediaUrlsDto Urls);

    public record MediaItemDto(
        Guid Id,
        string Title,
        string? AltText,
        string? Description,
        MediaKind Kind,
        MediaVisibility Visibility,
        Guid? FolderId,
        string OwnerId,
        string? OwnerEmail,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        DateTime? DeletedAt,
        MediaVersionDto Version,
        bool CanEdit,
        bool CanDelete);

    public record MediaPageDto(IReadOnlyList<MediaItemDto> Items, string? NextCursor);

    public record MediaKindLimitsDto(MediaKind Kind, IReadOnlyList<string> Extensions, long MaxBytes);

    public record MediaSettingsDto(int ChunkSize, long MaxPixels, int MaxVersions, int TrashRetentionDays, bool AllowSvg, IReadOnlyList<MediaKindLimitsDto> Kinds);

    public record MediaFolderDto(Guid Id, string Name, Guid? ParentId, int ItemCount, bool CanEdit, bool CanDelete);

    public record MediaFolderSummaryDto(int Files, int Folders);

    public record UploadStartedDto(Guid Id, int ChunkSize);

    public class StartUploadRequest
    {
        [Required, StringLength(255)]
        public string FileName { get; set; } = "";

        public long Size { get; set; }

        public Guid? FolderId { get; set; }
        public MediaVisibility Visibility { get; set; } = MediaVisibility.Private;
        public Guid? ReplaceItemId { get; set; } // Νέα έκδοση ενός υπάρχοντος αρχείου
        public MediaPurpose Purpose { get; set; } = MediaPurpose.Library; // Avatar: φωτογραφία προφίλ (για όλους τους χρήστες)
    }

    public class UpdateMediaRequest
    {
        [Required, StringLength(255, MinimumLength = 1)]
        public string Title { get; set; } = "";

        [StringLength(500)]
        public string? AltText { get; set; }

        [StringLength(2000)]
        public string? Description { get; set; }

        public MediaVisibility Visibility { get; set; }
    }

    public class MediaIdsRequest
    {
        [Required, MinLength(1), MaxLength(500)]
        public List<Guid> Ids { get; set; } = [];
    }

    public class MoveMediaRequest : MediaIdsRequest
    {
        public Guid? FolderId { get; set; } // null = ρίζα
    }

    public class FolderRequest
    {
        [Required, StringLength(100, MinimumLength = 1)]
        public string Name { get; set; } = "";

        public Guid? ParentId { get; set; }
    }

    public class MoveFolderRequest
    {
        public Guid? ParentId { get; set; }
    }

    public static class MediaDtos
    {
        public static MediaItemDto Item(MediaItem item, MediaVersion version, string? ownerEmail, ClaimsPrincipal user, MediaService media) => new(
            item.Id,
            item.Title,
            item.AltText,
            item.Description,
            item.Kind,
            item.Visibility,
            item.FolderId,
            item.OwnerId,
            ownerEmail,
            item.CreatedAt,
            item.UpdatedAt,
            item.DeletedAt,
            Version(item, version, null, media),
            MediaAccess.CanEdit(user, item),
            MediaAccess.CanDelete(user, item));

        public static MediaVersionDto Version(MediaItem item, MediaVersion version, string? createdBy, MediaService media)
        {
            string? Url(string name)
            {
                var variant = version.Variants.FirstOrDefault(v => v.Name == name);
                return variant == null ? null : media.FileUrl(item, version, variant);
            }

            var urls = new MediaUrlsDto(
                Url(ImageProcessor.Thumb),
                Url(ImageProcessor.Medium),
                Url(ImageProcessor.Large),
                Url(ImageProcessor.Original) ?? "",
                MediaService.DownloadUrl(item, version));

            return new MediaVersionDto(
                version.Id,
                version.Number,
                version.OriginalFileName,
                version.Extension,
                version.ContentType,
                version.SizeBytes,
                version.Width,
                version.Height,
                version.DominantColor,
                version.CreatedAt,
                createdBy,
                version.Id == item.CurrentVersionId,
                urls);
        }
    }
}
