using System.Globalization;
using System.Security.Cryptography;
using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Panoptes_v1.Services.Media
{
    public record IngestRequest(
        string TempPath,
        string FileName,
        string UserId,
        MediaPurpose Purpose,
        MediaVisibility Visibility,
        Guid? FolderId,
        Guid? ReplaceItemId);

    // Η μοναδική διαδρομή για να μπει ένα αρχείο στο σύστημα (βιβλιοθήκη, νέα έκδοση, avatar, branding)
    // και οι αλλαγές στον κύκλο ζωής του (ορατότητα, κάδος, εκδόσεις).
    public class MediaService
    {
        private readonly ApplicationDbContext _db;
        private readonly IMediaStorage _storage;
        private readonly ImageProcessor _images;
        private readonly MediaOptions _options;

        public MediaService(ApplicationDbContext db, IMediaStorage storage, ImageProcessor images, IOptions<MediaOptions> options)
        {
            _db = db;
            _storage = storage;
            _images = images;
            _options = options.Value;
        }

        public MediaOptions Options => _options;

        public async Task<MediaItem> IngestAsync(IngestRequest request, CancellationToken cancellationToken = default)
        {
            // 1. Τι είναι πραγματικά το αρχείο (όχι τι λέει το όνομα)
            var detected = FileSniffer.Inspect(request.TempPath, request.FileName, _options);
            var size = new FileInfo(request.TempPath).Length;
            if (size == 0)
                throw new MediaValidationException("The file is empty.");
            if (size > _options.MaxBytesFor(detected.Kind))
                throw TooLarge(detected.Kind);

            var now = DateTime.UtcNow;
            MediaItem item;
            var isNew = request.ReplaceItemId == null;
            if (isNew)
            {
                item = new MediaItem
                {
                    Id = Guid.NewGuid(),
                    Title = Truncate(Path.GetFileNameWithoutExtension(CleanFileName(request.FileName)), 255),
                    Kind = detected.Kind,
                    Visibility = request.Visibility,
                    Purpose = request.Purpose,
                    FolderId = request.FolderId,
                    OwnerId = request.UserId,
                    CreatedAt = now,
                };
            }
            else
            {
                item = await _db.MediaItems.Include(i => i.Versions)
                    .FirstOrDefaultAsync(i => i.Id == request.ReplaceItemId && i.DeletedAt == null, cancellationToken)
                    ?? throw new MediaValidationException("The file you're replacing no longer exists.");
                if (item.Kind != detected.Kind)
                    throw new MediaValidationException(item.Kind switch
                    {
                        MediaKind.Image => "Image can only be replaced with another image.",
                        MediaKind.Document => "Document can only be replaced with another document.",
                        _ => "Archive can only be replaced with another archive.",
                    });
            }

            var number = item.Versions.Count == 0 ? 1 : item.Versions.Max(v => v.Number) + 1;
            var version = new MediaVersion
            {
                Id = Guid.NewGuid(),
                MediaItemId = item.Id,
                Number = number,
                OriginalFileName = CleanFileName(request.FileName),
                Extension = detected.Extension,
                ContentType = detected.ContentType,
                Sha256 = await Sha256Async(request.TempPath, cancellationToken),
                CreatedById = request.UserId,
                CreatedAt = now,
            };

            // 2. Αρχεία στο storage (με τα τελικά κλειδιά) και μετά η βάση. Αν αποτύχει η βάση, σβήνονται τα αρχεία.
            var area = MediaAccess.StorageArea(item);
            var written = new List<string>();
            try
            {
                if (detected.IsRasterImage)
                {
                    var processed = await _images.ProcessAsync(request.TempPath, detected.Format, cancellationToken);
                    version.Width = processed.Width;
                    version.Height = processed.Height;
                    version.DominantColor = processed.DominantColor;
                    foreach (var variant in processed.Variants)
                    {
                        var key = MediaKeys.For(item.Id, number, variant.Name, variant.Extension);
                        await _storage.WriteAsync(area, key, new MemoryStream(variant.Data), cancellationToken);
                        written.Add(key);
                        version.Variants.Add(new MediaVariant
                        {
                            Name = variant.Name,
                            Width = variant.Width,
                            Height = variant.Height,
                            SizeBytes = variant.Data.Length,
                            ContentType = variant.ContentType,
                            StorageKey = key,
                        });
                        if (variant.Name == ImageProcessor.Original)
                        {
                            version.SizeBytes = variant.Data.Length;
                            version.ContentType = variant.ContentType;
                            version.Extension = variant.Extension;
                        }
                    }
                }
                else
                {
                    // Έγγραφα/zip αποθηκεύονται όπως είναι· τα SVG καθαρίζονται πρώτα
                    var key = MediaKeys.For(item.Id, number, ImageProcessor.Original, detected.Extension);
                    long stored;
                    if (detected.Format == MediaFormat.Svg)
                    {
                        var clean = SvgSanitizer.Sanitize(request.TempPath);
                        await _storage.WriteAsync(area, key, new MemoryStream(clean), cancellationToken);
                        stored = clean.Length;
                    }
                    else
                    {
                        await using var file = File.OpenRead(request.TempPath);
                        await _storage.WriteAsync(area, key, file, cancellationToken);
                        stored = size;
                    }
                    written.Add(key);
                    version.SizeBytes = stored;
                    version.Variants.Add(new MediaVariant
                    {
                        Name = ImageProcessor.Original,
                        SizeBytes = stored,
                        ContentType = detected.ContentType,
                        StorageKey = key,
                    });
                }

                item.CurrentVersionId = version.Id;
                item.UpdatedAt = now;
                item.Versions.Add(version);
                if (isNew)
                    _db.MediaItems.Add(item);
                else
                    _db.MediaVersions.Add(version);

                await _db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                foreach (var key in written)
                    await _storage.DeleteAsync(key, CancellationToken.None);
                _db.ChangeTracker.Clear(); // Τίποτα μισό να μη σωθεί σε επόμενο SaveChanges
                throw;
            }

            if (!isNew)
                await PruneVersionsAsync(item, cancellationToken);

            return item;
        }

        // Αλλαγή ορατότητας: τα αρχεία μετακινούνται ανάμεσα στις περιοχές public/private
        public async Task SetVisibilityAsync(MediaItem item, MediaVisibility visibility, CancellationToken cancellationToken = default)
        {
            var before = MediaAccess.StorageArea(item);
            item.Visibility = visibility;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            await MoveFilesAsync(item, before, MediaAccess.StorageArea(item), cancellationToken);
        }

        public async Task TrashAsync(IReadOnlyList<MediaItem> items, CancellationToken cancellationToken = default)
        {
            var moves = new List<(MediaItem Item, MediaVisibility From)>();
            var now = DateTime.UtcNow;
            foreach (var item in items.Where(i => i.DeletedAt == null))
            {
                moves.Add((item, MediaAccess.StorageArea(item)));
                item.DeletedAt = now;
                item.OriginalFolderId = item.FolderId;
            }
            await _db.SaveChangesAsync(cancellationToken);

            // Στον κάδο τίποτα δεν μένει δημόσιο
            foreach (var (item, from) in moves)
                await MoveFilesAsync(item, from, MediaAccess.StorageArea(item), cancellationToken);
        }

        public async Task RestoreAsync(IReadOnlyList<MediaItem> items, CancellationToken cancellationToken = default)
        {
            var folderIds = items.Select(i => i.OriginalFolderId).OfType<Guid>().Distinct().ToList();
            var existingFolders = await _db.MediaFolders.Where(f => folderIds.Contains(f.Id)).Select(f => f.Id).ToListAsync(cancellationToken);

            var restored = items.Where(i => i.DeletedAt != null).ToList();
            foreach (var item in restored)
            {
                item.DeletedAt = null;
                item.FolderId = item.OriginalFolderId is Guid folderId && existingFolders.Contains(folderId) ? folderId : null;
                item.OriginalFolderId = null;
                item.UpdatedAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var item in restored)
                await MoveFilesAsync(item, MediaVisibility.Private, MediaAccess.StorageArea(item), cancellationToken);
        }

        // Οριστική διαγραφή: πρώτα η βάση, μετά τα αρχεία (ό,τι μείνει το καθαρίζει το cleanup)
        public async Task DeletePermanentlyAsync(IReadOnlyList<MediaItem> items, CancellationToken cancellationToken = default)
        {
            var keys = items.SelectMany(i => i.Versions).SelectMany(v => v.Variants).Select(v => v.StorageKey).ToList();
            _db.MediaItems.RemoveRange(items);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var key in keys)
                await _storage.DeleteAsync(key, cancellationToken);
        }

        public async Task RestoreVersionAsync(MediaItem item, MediaVersion version, CancellationToken cancellationToken = default)
        {
            item.CurrentVersionId = version.Id;
            item.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }

        public async Task DeleteVersionAsync(MediaVersion version, CancellationToken cancellationToken = default)
        {
            var keys = version.Variants.Select(v => v.StorageKey).ToList();
            _db.MediaVersions.Remove(version);
            await _db.SaveChangesAsync(cancellationToken);
            foreach (var key in keys)
                await _storage.DeleteAsync(key, cancellationToken);
        }

        // Κρατά τις τελευταίες N εκδόσεις (και πάντα την τρέχουσα)
        public async Task PruneVersionsAsync(MediaItem item, CancellationToken cancellationToken = default)
        {
            var extra = item.Versions
                .Where(v => v.Id != item.CurrentVersionId)
                .OrderByDescending(v => v.Number)
                .Skip(Math.Max(0, _options.MaxVersions - 1))
                .ToList();
            foreach (var version in extra)
            {
                item.Versions.Remove(version);
                await DeleteVersionAsync(version, cancellationToken);
            }
        }

        // URL ενός αρχείου: δημόσια αρχεία απευθείας από το storage (cache για πάντα), τα υπόλοιπα μέσα από τον έλεγχο δικαιωμάτων
        public string FileUrl(MediaItem item, MediaVersion version, MediaVariant variant) =>
            MediaAccess.StorageArea(item) == MediaVisibility.Public
                ? _storage.PublicUrl(variant.StorageKey)
                : $"/media/{item.Id:N}/{version.Number}/{variant.Name}";

        public static string DownloadUrl(MediaItem item, MediaVersion version) =>
            $"/media/{item.Id:N}/{version.Number}/{ImageProcessor.Original}?download=true";

        // Ολόκληρες προτάσεις ανά είδος (μεταφράζονται)
        public MediaValidationException TooLarge(MediaKind kind) => new(kind switch
        {
            MediaKind.Image => "The file is too large. Images can be up to {0}.",
            MediaKind.Document => "The file is too large. Documents can be up to {0}.",
            _ => "The file is too large. Archives can be up to {0}.",
        }, FormatSize(_options.MaxBytesFor(kind)));

        public static string FormatSize(long bytes) =>
            bytes >= 1024 * 1024
                ? string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024):0.#} MB")
                : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, bytes / 1024)} KB");

        // Μόνο το όνομα (χωρίς φακέλους), χωρίς χαρακτήρες ελέγχου, έως 255
        public static string CleanFileName(string fileName)
        {
            var name = Path.GetFileName(fileName.Replace('\\', '/').Split('/').Last());
            name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (name.Length > 255)
            {
                var extension = Path.GetExtension(name);
                name = name[..(255 - extension.Length)] + extension;
            }
            return name.Length == 0 ? "file" : name;
        }

        private async Task MoveFilesAsync(MediaItem item, MediaVisibility from, MediaVisibility to, CancellationToken cancellationToken)
        {
            if (from == to) return;
            foreach (var variant in item.Versions.SelectMany(v => v.Variants))
                await _storage.MoveAsync(variant.StorageKey, from, to, cancellationToken);
        }

        private static async Task<string> Sha256Async(string path, CancellationToken cancellationToken)
        {
            await using var file = File.OpenRead(path);
            return Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken)).ToLowerInvariant();
        }

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
    }
}
