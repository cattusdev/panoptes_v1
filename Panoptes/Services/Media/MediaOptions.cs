using Panoptes.Models.Media;

namespace Panoptes.Services.Media
{
    // Ρυθμίσεις από το section "Media" του appsettings (ή env vars: Media__Root κ.λπ.)
    public class MediaOptions
    {
        public const string Section = "Media";
        public const int MaxChunkBytes = 8 * 1024 * 1024; // Πάνω όριο ενός chunk (το RequestSizeLimit θέλει σταθερά)
        private const long Mb = 1024 * 1024;

        public const string FileSystemProvider = "FileSystem";
        public const string S3Provider = "S3";

        public string Provider { get; set; } = FileSystemProvider; // FileSystem (δίσκος) ή S3 (Cloudflare R2 / οποιοδήποτε S3)
        public S3StorageOptions S3 { get; set; } = new();
        public string Root { get; set; } = "App_Data/media";        // Σχετικό με τον φάκελο της εφαρμογής ή απόλυτο
        public string TempRoot { get; set; } = "App_Data/media-tmp";

        // Επιτρεπτοί τύποι ανά είδος (χωρισμένοι με κόμμα, για να αλλάζουν εύκολα και από env vars)
        public string ImageExtensions { get; set; } = "jpg,jpeg,png,gif,webp";
        public string DocumentExtensions { get; set; } = "pdf,docx,xlsx,pptx,odt,ods,odp,txt,csv";
        public string ArchiveExtensions { get; set; } = "zip";
        public bool AllowSvg { get; set; } // SVG μπορεί να κρύβει scripts: κλειστό εκτός αν το ανοίξεις (τότε καθαρίζεται)

        public long MaxImageBytes { get; set; } = 20 * Mb;
        public long MaxDocumentBytes { get; set; } = 50 * Mb;
        public long MaxArchiveBytes { get; set; } = 100 * Mb;

        public long MaxPixels { get; set; } = 50_000_000; // Σύνολο pixels όλων των frames (προστασία από "decompression bombs")
        public int MaxFrames { get; set; } = 300;

        public int ThumbSize { get; set; } = 320;
        public int MediumWidth { get; set; } = 800;
        public int LargeWidth { get; set; } = 1600;

        public int MaxZipEntries { get; set; } = 10_000;
        public long MaxZipUncompressedBytes { get; set; } = 1024 * Mb;
        public int MaxZipCompressionRatio { get; set; } = 100;

        public int MaxVersions { get; set; } = 10;
        public int TrashRetentionDays { get; set; } = 30;
        public int CleanupIntervalMinutes { get; set; } = 60; // Καθαρισμός στο παρασκήνιο (0 = ποτέ)
        public int ChunkSize { get; set; } = 5 * 1024 * 1024;
        public int MaxConcurrentProcessing { get; set; } // 0 = μισοί πυρήνες
        public long MinFreeDiskMb { get; set; } = 500;
        public int UploadsPerMinute { get; set; } = 120; // Νέα uploads ανά χρήστη (τα chunks έχουν 20πλάσιο όριο)

        public IReadOnlyList<string> ExtensionsFor(MediaKind kind) => (kind switch
        {
            MediaKind.Image => AllowSvg ? ImageExtensions + ",svg" : ImageExtensions,
            MediaKind.Document => DocumentExtensions,
            _ => ArchiveExtensions,
        }).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
          .Select(e => e.TrimStart('.').ToLowerInvariant())
          .ToList();

        public long MaxBytesFor(MediaKind kind) => kind switch
        {
            MediaKind.Image => MaxImageBytes,
            MediaKind.Document => MaxDocumentBytes,
            _ => MaxArchiveBytes,
        };

        // Το είδος ενός αρχείου από την κατάληξη (null = δεν επιτρέπεται)
        public MediaKind? KindForExtension(string extension)
        {
            var ext = extension.TrimStart('.').ToLowerInvariant();
            foreach (var kind in Enum.GetValues<MediaKind>())
                if (ExtensionsFor(kind).Contains(ext)) return kind;
            return null;
        }
    }

    // Media:S3. Τα κλειδιά μπαίνουν σε user secrets / env vars (Media__S3__AccessKey), ποτέ στο appsettings.json.
    public class S3StorageOptions
    {
        public string ServiceUrl { get; set; } = "";    // R2: https://<account-id>.r2.cloudflarestorage.com
        public string Region { get; set; } = "auto";    // R2: auto
        public string AccessKey { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public string PublicBucket { get; set; } = "";  // Ανοιχτό στο internet (custom domain ή r2.dev)
        public string PrivateBucket { get; set; } = ""; // Κλειστό: τα αρχεία περνούν από την εφαρμογή
        public string PublicBaseUrl { get; set; } = ""; // Το URL του public bucket, π.χ. https://media.example.com
        public bool ForcePathStyle { get; set; } = true;

        public string? Problem()
        {
            if (string.IsNullOrWhiteSpace(ServiceUrl) || string.IsNullOrWhiteSpace(AccessKey) || string.IsNullOrWhiteSpace(SecretKey))
                return "Media:S3 needs ServiceUrl, AccessKey and SecretKey.";
            if (string.IsNullOrWhiteSpace(PublicBucket) || string.IsNullOrWhiteSpace(PrivateBucket) || PublicBucket == PrivateBucket)
                return "Media:S3 needs two different buckets: PublicBucket and PrivateBucket.";
            if (!Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var url) || url.Scheme is not ("https" or "http"))
                return "Media:S3:PublicBaseUrl must be the public bucket's URL, e.g. https://media.example.com.";
            return null;
        }
    }
}
