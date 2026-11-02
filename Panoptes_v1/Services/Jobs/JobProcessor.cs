using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Panoptes_v1.Services.Jobs
{
    // Εκτελεί τις δουλειές που είναι έτοιμες. Κάθε δουλειά "κλειδώνεται" στη βάση (FOR UPDATE SKIP LOCKED),
    // άρα δύο runners (ή ο runner και ένα test) δεν την τρέχουν ποτέ ταυτόχρονα.
    public class JobProcessor
    {
        private const int MaxErrorLength = 2000;

        private readonly ApplicationDbContext _db;
        private readonly IServiceScopeFactory _scopes;
        private readonly JobOptions _options;
        private readonly ILogger<JobProcessor> _logger;

        public JobProcessor(ApplicationDbContext db, IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<JobProcessor> logger)
        {
            _db = db;
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        // Τρέχει όσες είναι έτοιμες (έως maxJobs) και επιστρέφει πόσες έτρεξαν
        public async Task<int> RunDueAsync(int maxJobs = 50, CancellationToken cancellationToken = default)
        {
            var count = 0;
            while (count < maxJobs && await ClaimNextAsync(cancellationToken) is BackgroundJob job)
            {
                await ExecuteAsync(job, cancellationToken);
                count++;
            }
            return count;
        }

        // Pending και έτοιμη, ή Running αλλά το lease έληξε (το process έπεσε όσο έτρεχε)
        private async Task<BackgroundJob?> ClaimNextAsync(CancellationToken cancellationToken)
        {
            var now = DateTime.UtcNow;
            var leaseUntil = now.AddMinutes(_options.LeaseMinutes);
            var (pending, running) = (nameof(BackgroundJobStatus.Pending), nameof(BackgroundJobStatus.Running));

            var claimed = await _db.BackgroundJobs.FromSqlInterpolated($"""
                UPDATE "BackgroundJobs" SET "Status" = {running}, "Attempts" = "Attempts" + 1, "LockedUntil" = {leaseUntil}
                WHERE "Id" = (
                    SELECT "Id" FROM "BackgroundJobs"
                    WHERE ("Status" = {pending} AND "RunAt" <= {now}) OR ("Status" = {running} AND "LockedUntil" < {now})
                    ORDER BY "RunAt"
                    LIMIT 1
                    FOR UPDATE SKIP LOCKED)
                RETURNING *
                """).AsNoTracking().ToListAsync(cancellationToken);
            return claimed.FirstOrDefault();
        }

        private async Task ExecuteAsync(BackgroundJob job, CancellationToken cancellationToken)
        {
            if (job.Attempts > job.MaxAttempts)
            {
                await FinishAsync(job, BackgroundJobStatus.Failed, $"Stopped after {job.MaxAttempts} attempts.", cancellationToken);
                return;
            }

            // Κάθε δουλειά σε δικό της scope (καθαρό DbContext) και με δική της "υπογραφή" στο audit log
            using var audit = AuditContext.Use($"jobs:{job.Type}");
            using var scope = _scopes.CreateScope();
            var handler = scope.ServiceProvider.GetServices<IJobHandler>().FirstOrDefault(h => h.Type == job.Type);
            if (handler == null)
            {
                await FinishAsync(job, BackgroundJobStatus.Failed, $"No handler is registered for job type '{job.Type}'.", cancellationToken);
                return;
            }

            try
            {
                await handler.RunAsync(new JobContext(job.Id, job.IdempotencyKey, job.Attempts, job.Payload), cancellationToken);
                await FinishAsync(job, BackgroundJobStatus.Succeeded, null, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                var error = Truncate($"{e.GetType().Name}: {e.Message}");
                if (job.Attempts >= job.MaxAttempts)
                {
                    _logger.LogError(e, "Job {JobId} ({Type}) failed after {Attempts} attempts", job.Id, job.Type, job.Attempts);
                    await FinishAsync(job, BackgroundJobStatus.Failed, error, cancellationToken);
                }
                else
                {
                    _logger.LogWarning(e, "Job {JobId} ({Type}) failed, attempt {Attempt} of {Max}", job.Id, job.Type, job.Attempts, job.MaxAttempts);
                    var delay = TimeSpan.FromSeconds(Math.Min(3600, _options.RetryDelaySeconds * Math.Pow(2, job.Attempts - 1)));
                    await _db.BackgroundJobs.Where(j => j.Id == job.Id && j.Status == BackgroundJobStatus.Running)
                        .ExecuteUpdateAsync(s => s
                            .SetProperty(j => j.Status, BackgroundJobStatus.Pending)
                            .SetProperty(j => j.RunAt, DateTime.UtcNow + delay)
                            .SetProperty(j => j.LockedUntil, (DateTime?)null)
                            .SetProperty(j => j.LastError, error), CancellationToken.None);
                }
            }
        }

        private Task FinishAsync(BackgroundJob job, BackgroundJobStatus status, string? error, CancellationToken cancellationToken) =>
            _db.BackgroundJobs.Where(j => j.Id == job.Id && j.Status == BackgroundJobStatus.Running)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, status)
                    .SetProperty(j => j.CompletedAt, DateTime.UtcNow)
                    .SetProperty(j => j.LockedUntil, (DateTime?)null)
                    .SetProperty(j => j.LastError, error), CancellationToken.None);

        private static string Truncate(string value) => value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
    }

    // Ψάχνει δουλειές κάθε Jobs:PollSeconds (0 = σταματημένος)
    public class JobRunnerService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly JobOptions _options;
        private readonly ILogger<JobRunnerService> _logger;

        public JobRunnerService(IServiceScopeFactory scopes, IOptions<JobOptions> options, ILogger<JobRunnerService> logger)
        {
            _scopes = scopes;
            _options = options.Value;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (_options.PollSeconds <= 0) return;

            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.PollSeconds));
                do
                {
                    try
                    {
                        using var scope = _scopes.CreateScope();
                        await scope.ServiceProvider.GetRequiredService<JobProcessor>().RunDueAsync(cancellationToken: stoppingToken);
                    }
                    catch (Exception e) when (e is not OperationCanceledException)
                    {
                        _logger.LogError(e, "Job runner failed");
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
