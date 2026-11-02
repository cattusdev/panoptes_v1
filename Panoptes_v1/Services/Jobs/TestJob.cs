using System.Collections.Concurrent;
using Panoptes_v1.Data;
using Panoptes_v1.Models.Media;

namespace Panoptes_v1.Services.Jobs
{
    // Μόνο για τα E2E tests (Diagnostics:EnableTestEndpoints): μετράει πόσες φορές έτρεξε κάθε key,
    // αποτυγχάνει όσες φορές του ζητηθεί και, αν ζητηθεί, φτιάχνει έναν φάκελο (για να φανεί η "υπογραφή" του job στο audit log).
    public record TestJobRequest(string Key, int FailTimes = 0, bool CreateFolder = false, int? MaxAttempts = null);

    public class TestJob : IJobHandler
    {
        public const string JobType = "test.record";

        private static readonly ConcurrentDictionary<string, int> RunCounts = new();

        private readonly ApplicationDbContext _db;

        public TestJob(ApplicationDbContext db)
        {
            _db = db;
        }

        public string Type => JobType;

        public static int Runs(string key) => RunCounts.GetValueOrDefault(key);

        public async Task RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            var request = context.Read<TestJobRequest>();
            var run = RunCounts.AddOrUpdate(request.Key, 1, (_, count) => count + 1);
            if (run <= request.FailTimes)
                throw new InvalidOperationException($"Simulated failure {run} of {request.FailTimes}");

            if (request.CreateFolder)
            {
                _db.MediaFolders.Add(new MediaFolder
                {
                    Id = Guid.NewGuid(),
                    Name = request.Key,
                    NormalizedName = request.Key.ToUpperInvariant(),
                    CreatedById = "system",
                    CreatedAt = DateTime.UtcNow,
                });
                await _db.SaveChangesAsync(cancellationToken);
            }
        }
    }
}
