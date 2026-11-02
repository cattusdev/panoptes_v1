using System.Collections.Concurrent;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace Panoptes_v1.Services.Media
{
    // Κοινοί κανόνες για το σερβίρισμα αρχείων (δημόσια μέσω static files, ιδιωτικά μέσω controller)
    public static class MediaDelivery
    {
        // Μόνο οι τύποι που αποθηκεύουμε εμείς· ποτέ HTML. Τα κείμενα πάντα ως text/plain.
        public static readonly FileExtensionContentTypeProvider ContentTypes = new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".svg"] = "image/svg+xml",
            [".pdf"] = "application/pdf",
            [".docx"] = FileSniffer.ContentTypes[MediaFormat.Docx],
            [".xlsx"] = FileSniffer.ContentTypes[MediaFormat.Xlsx],
            [".pptx"] = FileSniffer.ContentTypes[MediaFormat.Pptx],
            [".odt"] = FileSniffer.ContentTypes[MediaFormat.Odt],
            [".ods"] = FileSniffer.ContentTypes[MediaFormat.Ods],
            [".odp"] = FileSniffer.ContentTypes[MediaFormat.Odp],
            [".txt"] = "text/plain; charset=utf-8",
            [".csv"] = "text/plain; charset=utf-8",
            [".zip"] = "application/zip",
        });

        public const string PublicCache = "public, max-age=31536000, immutable"; // Τα URLs έχουν την έκδοση: δεν αλλάζουν ποτέ
        public const string PrivateCache = "private, max-age=600";

        public static string ContentTypeFor(string fileName) =>
            ContentTypes.TryGetContentType(fileName, out var contentType) ? contentType : "application/octet-stream";

        // nosniff παντού· έγγραφα/zip πάντα ως λήψη (εκτός PDF)· SVG σε sandbox ώστε να μη μπορεί να τρέξει τίποτα
        public static void ApplyHeaders(HttpResponse response, string fileName, string cacheControl, string? downloadName = null)
        {
            var headers = response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.CacheControl = cacheControl;

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (extension == ".svg")
                headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";

            if (DispositionFor(fileName, downloadName) is string disposition)
                headers.ContentDisposition = disposition;
        }

        // attachment για λήψη ή για τύπους που δεν ανοίγουν στον browser· null = inline
        public static string? DispositionFor(string fileName, string? downloadName = null)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var inline = extension is ".jpg" or ".png" or ".gif" or ".webp" or ".svg" or ".pdf" or ".txt" or ".csv";
            if (downloadName == null && inline) return null;

            var disposition = new ContentDispositionHeaderValue("attachment");
            disposition.SetHttpFileName(downloadName ?? Path.GetFileName(fileName)); // filename + filename* (UTF-8)
            return disposition.ToString();
        }
    }

    // Ένα chunk τη φορά ανά upload (δύο παράλληλες προσπάθειες δεν γράφουν ποτέ μαζί στο ίδιο αρχείο)
    public class MediaUploadLocks
    {
        private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

        public async Task<IDisposable> AcquireAsync(Guid uploadId, CancellationToken cancellationToken)
        {
            var gate = _locks.GetOrAdd(uploadId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(cancellationToken);
            return new Releaser(gate);
        }

        public void Forget(Guid uploadId) => _locks.TryRemove(uploadId, out _);

        private sealed class Releaser(SemaphoreSlim gate) : IDisposable
        {
            public void Dispose() => gate.Release();
        }
    }
}
