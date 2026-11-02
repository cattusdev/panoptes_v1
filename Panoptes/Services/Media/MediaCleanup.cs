using Panoptes.Data;
using Panoptes.Models.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Panoptes.Services.Media
{
    public record MediaCleanupResult(
        int UploadSessions,
        int TempFiles,
        int TrashedItems,
        int Versions,
        int UnusedAvatars,
        int OrphanFiles,
        int MisplacedFiles)
    {
        public int Total => UploadSessions + TempFiles + TrashedItems + Versions + UnusedAvatars + OrphanFiles + MisplacedFiles;
    }

    // Το "σκούπισμα" της αποθήκης: ημιτελή uploads, κάδος μετά τη διορία, εκδόσεις πάνω από το όριο,
    // φωτογραφίες προφίλ που δεν χρησιμοποιήθηκαν, αρχεία χωρίς εγγραφή και αρχεία στη λάθος περιοχή.
    public class MediaCleanup
    {
        public static readonly TimeSpan StaleUploadAge = TimeSpan.FromHours(24);
        public static readonly TimeSpan OrphanFileAge = TimeSpan.FromHours(1); // Το ingest γράφει τα αρχεία λίγο πριν τη βάση

        private static readonly SemaphoreSlim Gate = new(1, 1); // Ποτέ δύο καθαρισμοί μαζί (timer και test endpoint)

        private readonly ApplicationDbContext _db;
        private readonly MediaService _media;
        private readonly IMediaStorage _storage;
        private readonly MediaPaths _paths;
        private readonly MediaUploadLocks _locks;
        private readonly MediaOptions _options;
        private readonly ILogger<MediaCleanup> _logger;

        public MediaCleanup(
            ApplicationDbContext db,
            MediaService media,
            IMediaStorage storage,
            MediaPaths paths,
            MediaUploadLocks locks,
            IOptions<MediaOptions> options,
            ILogger<MediaCleanup> logger)
        {
            _db = db;
            _media = media;
            _storage = storage;
            _paths = paths;
            _locks = locks;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<MediaCleanupResult> RunAsync(CancellationToken cancellationToken = default)
        {
            await Gate.WaitAsync(cancellationToken);
            try
            {
                var now = DateTime.UtcNow;
                var (sessions, tempFiles) = await RemoveStaleUploadsAsync(now, cancellationToken);
                var trashed = await EmptyExpiredTrashAsync(now, cancellationToken);
                var versions = await PruneVersionsAsync(cancellationToken);
                var avatars = await RemoveUnusedAvatarsAsync(now, cancellationToken);
                var (orphans, misplaced) = await SyncFilesAsync(now, cancellationToken);

                var result = new MediaCleanupResult(sessions, tempFiles, trashed, versions, avatars, orphans, misplaced);
                if (result.Total > 0)
                    _logger.LogInformation("Media cleanup: {@Result}", result);
                return result;
            }
            finally
            {
                Gate.Release();
            }
        }

        private async Task<(int Sessions, int TempFiles)> RemoveStaleUploadsAsync(DateTime now, CancellationToken cancellationToken)
        {
            var cutoff = now - StaleUploadAge;
            var stale = await _db.MediaUploads.Where(u => u.CreatedAt < cutoff).Select(u => u.Id).ToListAsync(cancellationToken);
            foreach (var id in stale)
            {
                using (await _locks.AcquireAsync(id, cancellationToken))
                {
                    DeleteFile(_paths.TempFile(id));
                    await _db.MediaUploads.Where(u => u.Id == id).ExecuteDeleteAsync(cancellationToken);
                }
                _locks.Forget(id);
            }

            // Προσωρινά αρχεία χωρίς session (π.χ. μετά από crash)
            var live = (await _db.MediaUploads.Select(u => u.Id).ToListAsync(cancellationToken))
                .Select(_paths.TempFile)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var tempFiles = 0;
            foreach (var path in Directory.EnumerateFiles(_paths.Temp).ToList())
            {
                if (live.Contains(path) || File.GetLastWriteTimeUtc(path) >= cutoff) continue;
                DeleteFile(path);
                tempFiles++;
            }

            return (stale.Count, tempFiles);
        }

        private async Task<int> EmptyExpiredTrashAsync(DateTime now, CancellationToken cancellationToken)
        {
            var cutoff = now.AddDays(-_options.TrashRetentionDays);
            var count = 0;
            while (true)
            {
                var batch = await _db.MediaItems.Include(i => i.Versions)
                    .Where(i => i.DeletedAt != null && i.DeletedAt < cutoff)
                    .OrderBy(i => i.Id)
                    .Take(100)
                    .ToListAsync(cancellationToken);
                if (batch.Count == 0) return count;

                await _media.DeletePermanentlyAsync(batch, cancellationToken);
                count += batch.Count;
            }
        }

        // Αν μειωθεί το Media:MaxVersions, οι παλιές εκδόσεις φεύγουν εδώ (κανονικά κλαδεύονται στο ανέβασμα)
        private async Task<int> PruneVersionsAsync(CancellationToken cancellationToken)
        {
            var ids = await _db.MediaItems
                .Where(i => i.Versions.Count > _options.MaxVersions)
                .Select(i => i.Id)
                .ToListAsync(cancellationToken);

            var count = 0;
            foreach (var id in ids)
            {
                var item = await _db.MediaItems.Include(i => i.Versions).FirstAsync(i => i.Id == id, cancellationToken);
                var before = item.Versions.Count;
                await _media.PruneVersionsAsync(item, cancellationToken);
                count += before - item.Versions.Count;
            }
            return count;
        }

        // Φωτογραφίες που ανέβηκαν αλλά δεν ορίστηκαν ποτέ (π.χ. έκλεισε η σελίδα στη μέση)
        private async Task<int> RemoveUnusedAvatarsAsync(DateTime now, CancellationToken cancellationToken)
        {
            var cutoff = now - StaleUploadAge;
            var unused = await _db.MediaItems.Include(i => i.Versions)
                .Where(i => i.Purpose == MediaPurpose.Avatar && i.CreatedAt < cutoff && !_db.UserProfiles.Any(p => p.AvatarMediaId == i.Id))
                .ToListAsync(cancellationToken);
            if (unused.Count > 0)
                await _media.DeletePermanentlyAsync(unused, cancellationToken);
            return unused.Count;
        }

        // Αρχεία χωρίς εγγραφή σβήνονται· αρχεία στη λάθος περιοχή (π.χ. μετακίνηση που διακόπηκε) πάνε στη σωστή,
        // ώστε ένα ιδιωτικό αρχείο να μη μείνει ποτέ δημόσιο.
        private async Task<(int Orphans, int Misplaced)> SyncFilesAsync(DateTime now, CancellationToken cancellationToken)
        {
            var areas = await ItemAreasAsync(null, cancellationToken);
            var expected = new Dictionary<string, (Guid ItemId, MediaVisibility Area)>();
            await foreach (var version in _db.MediaVersions.AsNoTracking().AsAsyncEnumerable().WithCancellation(cancellationToken))
            {
                if (!areas.TryGetValue(version.MediaItemId, out var area)) continue;
                foreach (var variant in version.Variants)
                    expected[variant.StorageKey] = (version.MediaItemId, area);
            }

            var cutoff = now - OrphanFileAge;
            var orphans = 0;
            var misplaced = new List<(string Key, Guid ItemId, MediaVisibility From)>();
            foreach (var area in new[] { MediaVisibility.Public, MediaVisibility.Private })
            {
                var files = new List<StoredFile>();
                await foreach (var file in _storage.ListAsync(area, cancellationToken))
                    files.Add(file);

                foreach (var file in files)
                {
                    if (expected.TryGetValue(file.Key, out var target))
                    {
                        if (target.Area != area)
                            misplaced.Add((file.Key, target.ItemId, area));
                    }
                    else if (file.LastModifiedUtc < cutoff)
                    {
                        await _storage.DeleteAsync(file.Key, cancellationToken);
                        orphans++;
                    }
                }
            }

            // Ξανά από τη βάση: η ορατότητα μπορεί να άλλαξε όσο διαβάζαμε τα αρχεία
            var current = await ItemAreasAsync(misplaced.Select(m => m.ItemId).Distinct().ToList(), cancellationToken);
            var moved = 0;
            foreach (var (key, itemId, from) in misplaced)
            {
                if (!current.TryGetValue(itemId, out var area) || area == from) continue;
                await _storage.MoveAsync(key, from, area, cancellationToken);
                moved++;
            }

            return (orphans, moved);
        }

        private Task<Dictionary<Guid, MediaVisibility>> ItemAreasAsync(List<Guid>? ids, CancellationToken cancellationToken)
        {
            var query = _db.MediaItems.AsNoTracking();
            if (ids != null)
                query = query.Where(i => ids.Contains(i.Id));
            return query
                .Select(i => new { i.Id, i.Visibility, i.Purpose, i.DeletedAt })
                .ToDictionaryAsync(i => i.Id, i => MediaAccess.StorageArea(i.Visibility, i.Purpose, i.DeletedAt), cancellationToken);
        }

        private static void DeleteFile(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    // Τρέχει τον καθαρισμό λίγο μετά την εκκίνηση και μετά κάθε Media:CleanupIntervalMinutes
    public class MediaCleanupService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly MediaOptions _options;
        private readonly ILogger<MediaCleanupService> _logger;

        public MediaCleanupService(IServiceScopeFactory scopes, IOptions<MediaOptions> options, ILogger<MediaCleanupService> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.CleanupIntervalMinutes <= 0) return;

            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                using var timer = new PeriodicTimer(TimeSpan.FromMinutes(_options.CleanupIntervalMinutes));
                do
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<MediaCleanup>().RunAsync(stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _logger.LogError(e, "Media cleanup failed");
                    }
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // Η εφαρμογή σταματά
            }
        }
    }
}
