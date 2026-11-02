using System.Text.Json.Serialization;

namespace Panoptes.Models.Media
{
    [JsonConverter(typeof(JsonStringEnumConverter<MediaKind>))]
    public enum MediaKind { Image, Document, Archive }

    // Private: μόνο με Media.View · Public: οποιοσδήποτε έχει το link (π.χ. εικόνες του site)
    [JsonConverter(typeof(JsonStringEnumConverter<MediaVisibility>))]
    public enum MediaVisibility { Private, Public }

    // Library: η κοινή βιβλιοθήκη · Avatar/Branding: αρχεία συστήματος που δεν εμφανίζονται στη βιβλιοθήκη
    [JsonConverter(typeof(JsonStringEnumConverter<MediaPurpose>))]
    public enum MediaPurpose { Library, Avatar, Branding }

    // Οντότητες που δεν γράφονται στο audit log (π.χ. τα upload sessions που αλλάζουν συνεχώς)
    public interface INotAudited { }

    public class MediaFolder
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = "";
        public string NormalizedName { get; set; } = ""; // Για μοναδικό όνομα ανά γονέα χωρίς διάκριση πεζών/κεφαλαίων
        public Guid? ParentId { get; set; }
        public MediaFolder? Parent { get; set; }
        public string CreatedById { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

    public class MediaItem
    {
        public Guid Id { get; set; }
        public long Sequence { get; set; } // Αύξων αριθμός από τη βάση: σταθερή σειρά για το infinite scroll
        public Guid? FolderId { get; set; }
        public MediaFolder? Folder { get; set; }
        public string Title { get; set; } = "";
        public string? AltText { get; set; }
        public string? Description { get; set; }
        public MediaKind Kind { get; set; }
        public MediaVisibility Visibility { get; set; }
        public MediaPurpose Purpose { get; set; }
        public string OwnerId { get; set; } = "";

        // Χωρίς FK (κυκλική εξάρτηση με το MediaVersion): το ελέγχει ο MediaService
        public Guid CurrentVersionId { get; set; }
        public List<MediaVersion> Versions { get; set; } = [];

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Κάδος: DeletedAt != null. Ο φάκελος κρατιέται για το Restore (ακόμα κι αν σβηστεί ο φάκελος)
        public DateTime? DeletedAt { get; set; }
        public Guid? OriginalFolderId { get; set; }
    }

    public class MediaVersion
    {
        public Guid Id { get; set; }
        public Guid MediaItemId { get; set; }
        public MediaItem? MediaItem { get; set; }
        public int Number { get; set; }
        public string OriginalFileName { get; set; } = "";
        public string Extension { get; set; } = "";
        public string ContentType { get; set; } = "";
        public long SizeBytes { get; set; } // Μέγεθος του original όπως αποθηκεύτηκε
        public string Sha256 { get; set; } = ""; // Του αρχείου όπως ανέβηκε
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string? DominantColor { get; set; }
        public string CreatedById { get; set; } = "";
        public DateTime CreatedAt { get; set; }

        // jsonb: original + (για εικόνες) thumb/medium/large
        public List<MediaVariant> Variants { get; set; } = [];
    }

    public class MediaVariant
    {
        public string Name { get; set; } = "";
        public int? Width { get; set; }
        public int? Height { get; set; }
        public long SizeBytes { get; set; }
        public string ContentType { get; set; } = "";
        public string StorageKey { get; set; } = "";
    }

    // Ένα ανέβασμα σε εξέλιξη (chunks). Τα bytes ζουν σε προσωρινό αρχείο, όχι στη βάση.
    public class MediaUpload : INotAudited
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = "";
        public string FileName { get; set; } = "";
        public long Length { get; set; }
        public Guid? FolderId { get; set; }
        public MediaVisibility Visibility { get; set; }
        public MediaPurpose Purpose { get; set; }
        public Guid? ReplaceItemId { get; set; }
        public Guid? ResultItemId { get; set; }
        public Guid? ResultVersionId { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
    }
}
