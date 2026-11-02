using Panoptes.Data;
using Panoptes.Models.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Panoptes.Services.Media
{
    // dotnet run -- media:migrate
    // Αντιγράφει τα τοπικά αρχεία (Media:Root) στο S3/R2 (Media:S3), στο σωστό bucket για το καθένα.
    // Δεν σβήνει τίποτα τοπικά και μπορεί να ξανατρέξει: ό,τι υπάρχει ήδη στο bucket προσπερνιέται.
    public static class MediaMigration
    {
        public const string Command = "media:migrate";

        public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken = default)
        {
            var options = services.GetRequiredService<IOptions<MediaOptions>>();
            if (options.Value.S3.Problem() is string problem)
            {
                Console.Error.WriteLine(problem);
                return 1;
            }

            var source = new FileSystemMediaStorage(services.GetRequiredService<MediaPaths>());
            using var target = new S3MediaStorage(options);
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var areas = await db.MediaItems.AsNoTracking()
                .Select(i => new { i.Id, i.Visibility, i.Purpose, i.DeletedAt })
                .ToDictionaryAsync(i => i.Id, i => MediaAccess.StorageArea(i.Visibility, i.Purpose, i.DeletedAt), cancellationToken);
            var versions = await db.MediaVersions.AsNoTracking().ToListAsync(cancellationToken);

            int copied = 0, skipped = 0, missing = 0;
            foreach (var version in versions)
            {
                if (!areas.TryGetValue(version.MediaItemId, out var area)) continue;
                var other = area == MediaVisibility.Public ? MediaVisibility.Private : MediaVisibility.Public;

                foreach (var variant in version.Variants)
                {
                    if (await target.ExistsAsync(area, variant.StorageKey, cancellationToken))
                    {
                        skipped++;
                        continue;
                    }

                    var stored = await source.OpenReadAsync(area, variant.StorageKey, cancellationToken)
                        ?? await source.OpenReadAsync(other, variant.StorageKey, cancellationToken);
                    if (stored == null)
                    {
                        missing++;
                        Console.WriteLine($"Missing locally: {variant.StorageKey}");
                        continue;
                    }

                    await using (stored.Content)
                        await target.WriteAsync(area, variant.StorageKey, stored.Content, cancellationToken);
                    copied++;
                    if (copied % 100 == 0)
                        Console.WriteLine($"{copied} files copied…");
                }
            }

            Console.WriteLine($"Media migration done: {copied} copied, {skipped} already in the bucket, {missing} missing locally.");
            Console.WriteLine(missing == 0
                ? "Next: set Media:Provider to S3 and restart the app. Keep the local folder until you've checked everything."
                : "Some files were missing locally; the rest were copied. Check the keys above before switching Media:Provider to S3.");
            return 0;
        }
    }
}
