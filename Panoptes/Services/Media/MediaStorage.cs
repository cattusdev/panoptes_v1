using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Panoptes.Models.Media;
using Microsoft.Extensions.Options;

namespace Panoptes.Services.Media
{
    // Πού ζουν τα αρχεία. Σήμερα τοπικός φάκελος· αργότερα S3/Cloudflare R2 με την ίδια διεπαφή (αλλαγή μόνο στο config).
    // Δύο "περιοχές": public (σερβίρεται απευθείας, χωρίς έλεγχο) και private (μόνο μέσα από τον έλεγχο δικαιωμάτων).
    public interface IMediaStorage
    {
        Task WriteAsync(MediaVisibility area, string key, Stream content, CancellationToken cancellationToken = default);
        Task MoveAsync(string key, MediaVisibility from, MediaVisibility to, CancellationToken cancellationToken = default);
        Task DeleteAsync(string key, CancellationToken cancellationToken = default); // Από όποια περιοχή κι αν είναι
        Task<bool> ExistsAsync(MediaVisibility area, string key, CancellationToken cancellationToken = default);

        // Όλα τα αρχεία μιας περιοχής (για τον καθαρισμό)
        IAsyncEnumerable<StoredFile> ListAsync(MediaVisibility area, CancellationToken cancellationToken = default);

        // Άμεσο URL για αρχεία της public περιοχής
        string PublicUrl(string key);

        // Για να το σερβίρει ο controller (ιδιωτικά αρχεία)· null αν δεν υπάρχει
        Task<StoredContent?> OpenReadAsync(MediaVisibility area, string key, CancellationToken cancellationToken = default);
    }

    public record StoredFile(string Key, DateTime LastModifiedUtc);

    public sealed record StoredContent(Stream Content, DateTime LastModifiedUtc);

    // Τα μονοπάτια της εφαρμογής, απόλυτα (τα σχετικά λύνονται ως προς τον φάκελο της εφαρμογής)
    public class MediaPaths
    {
        public MediaPaths(IOptions<MediaOptions> options, IWebHostEnvironment environment)
        {
            Root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.Root));
            Temp = Path.GetFullPath(Path.Combine(environment.ContentRootPath, options.Value.TempRoot));
            Public = Path.Combine(Root, "public");
            Private = Path.Combine(Root, "private");
        }

        public string Root { get; }
        public string Temp { get; }
        public string Public { get; }
        public string Private { get; }

        public void EnsureCreated()
        {
            Directory.CreateDirectory(Public);
            Directory.CreateDirectory(Private);
            Directory.CreateDirectory(Temp);
        }

        public string TempFile(Guid uploadId) => Path.Combine(Temp, $"{uploadId:N}.part");
    }

    public static partial class MediaKeys
    {
        public const string PublicRequestPath = "/media/p";

        // {itemId}/v{αριθμός}/{variant}.{ext}: μόνο GUID και αριθμοί, ποτέ ονόματα που έδωσε ο χρήστης
        public static string For(Guid itemId, int version, string variant, string extension) =>
            $"{itemId:N}/v{version}/{variant}.{extension}";

        public static bool IsValid(string key) => KeyPattern().IsMatch(key);

        [GeneratedRegex("^[0-9a-f]{32}/v[0-9]{1,6}/[a-z0-9]{1,20}\\.[a-z0-9]{1,10}$")]
        private static partial Regex KeyPattern();
    }

    public class FileSystemMediaStorage : IMediaStorage
    {
        private readonly MediaPaths _paths;

        public FileSystemMediaStorage(MediaPaths paths)
        {
            _paths = paths;
        }

        public async Task WriteAsync(MediaVisibility area, string key, Stream content, CancellationToken cancellationToken = default)
        {
            var path = PathFor(area, key);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            // Γράψιμο σε προσωρινό και μετά rename: ποτέ μισό αρχείο στη θέση του κανονικού
            var temp = path + ".tmp";
            await using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                await content.CopyToAsync(file, cancellationToken);
            File.Move(temp, path, overwrite: true);
        }

        public Task MoveAsync(string key, MediaVisibility from, MediaVisibility to, CancellationToken cancellationToken = default)
        {
            if (from == to) return Task.CompletedTask;

            var source = PathFor(from, key);
            var target = PathFor(to, key);
            if (File.Exists(source))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(source, target, overwrite: true);
                DeleteEmptyParents(source, from);
            }
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            foreach (var area in new[] { MediaVisibility.Public, MediaVisibility.Private })
            {
                var path = PathFor(area, key);
                if (File.Exists(path))
                {
                    File.Delete(path);
                    DeleteEmptyParents(path, area);
                }
            }
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(MediaVisibility area, string key, CancellationToken cancellationToken = default) =>
            Task.FromResult(File.Exists(PathFor(area, key)));

        public async IAsyncEnumerable<StoredFile> ListAsync(MediaVisibility area, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask; // Ο δίσκος απαντά συγχρονισμένα· το S3 θα φέρνει σελίδες
            var root = AreaRoot(area);
            if (!Directory.Exists(root)) yield break;

            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var key = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                if (MediaKeys.IsValid(key))
                    yield return new StoredFile(key, File.GetLastWriteTimeUtc(path));
            }
        }

        public string PublicUrl(string key) => $"{MediaKeys.PublicRequestPath}/{key}";

        public Task<StoredContent?> OpenReadAsync(MediaVisibility area, string key, CancellationToken cancellationToken = default)
        {
            var path = PathFor(area, key);
            if (!File.Exists(path)) return Task.FromResult<StoredContent?>(null);

            // FileShare.Delete: μια μετακίνηση/διαγραφή την ώρα που σερβίρεται δεν αποτυγχάνει
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
            return Task.FromResult<StoredContent?>(new StoredContent(stream, File.GetLastWriteTimeUtc(path)));
        }

        private string AreaRoot(MediaVisibility area) => area == MediaVisibility.Public ? _paths.Public : _paths.Private;

        private string PathFor(MediaVisibility area, string key)
        {
            if (!MediaKeys.IsValid(key))
                throw new ArgumentException("Invalid storage key.", nameof(key));
            return Path.Combine(AreaRoot(area), key.Replace('/', Path.DirectorySeparatorChar));
        }

        // Καθαρίζει τους άδειους φακέλους {item}/v{n} και {item} μετά από delete/move
        private void DeleteEmptyParents(string filePath, MediaVisibility area)
        {
            var root = AreaRoot(area);
            var dir = Path.GetDirectoryName(filePath);
            for (var i = 0; i < 2 && dir != null && dir.Length > root.Length; i++)
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
                dir = Path.GetDirectoryName(dir);
            }
        }
    }
}
