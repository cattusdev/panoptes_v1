using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Panoptes_v1.Services.Jobs;

namespace Panoptes_v1.Tests.Jobs;

// Η ουρά δουλειών (ARCHITECTURE §9): μία φορά ανά key, outbox με το transaction, επαναλήψεις με backoff,
// ανάκτηση μετά από "πτώση" του process, και ποτέ η ίδια δουλειά σε δύο runners μαζί.
// Ο runner παίρνει ό,τι είναι έτοιμο, οπότε κάθε test ξεκινά με άδεια ουρά.
public class JobTests : DomainTest
{
    [SetUp]
    public Task EmptyQueueAsync() => WithDbAsync(db => db.BackgroundJobs.ExecuteDeleteAsync()); // Πίνακας χωρίς audit

    private Task<bool> EnqueueAsync(string key, int failTimes = 0, bool createFolder = false, DateTime? runAtUtc = null, int? maxAttempts = null) =>
        WithScopeAsync(services => services.GetRequiredService<JobQueue>()
            .EnqueueAsync(TestJob.JobType, new TestJobRequest(key, failTimes, createFolder), key, runAtUtc, maxAttempts));

    private Task<int> RunDueAsync() =>
        WithScopeAsync(services => services.GetRequiredService<JobProcessor>().RunDueAsync());

    // Runner με δικές του ρυθμίσεις (π.χ. πραγματική καθυστέρηση επανάληψης)
    private Task<int> RunDueAsync(JobOptions options) =>
        WithScopeAsync(services => new JobProcessor(services.GetRequiredService<ApplicationDbContext>(),
            services.GetRequiredService<IServiceScopeFactory>(), Options.Create(options), NullLogger<JobProcessor>.Instance).RunDueAsync());

    private Task<BackgroundJob> JobAsync(string key) =>
        WithDbAsync(db => db.BackgroundJobs.AsNoTracking().SingleAsync(j => j.IdempotencyKey == key));

    // Σαν να έπεσε το process στη μέση της δουλειάς: Running, με lease που λήγει (ή έληξε) τότε
    private Task SimulateCrashAsync(string key, int attempts, DateTime lockedUntil) =>
        WithDbAsync(db => db.BackgroundJobs.Where(j => j.IdempotencyKey == key).ExecuteUpdateAsync(s => s
            .SetProperty(j => j.Status, BackgroundJobStatus.Running)
            .SetProperty(j => j.Attempts, attempts)
            .SetProperty(j => j.LockedUntil, lockedUntil)));

    private Task MakeDueAsync(string key) =>
        WithDbAsync(db => db.BackgroundJobs.Where(j => j.IdempotencyKey == key)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.RunAt, DateTime.UtcNow.AddSeconds(-1))));

    [Test]
    public async Task SameKey_IsQueuedOnce_AndRunsOnce()
    {
        var key = Unique("job");
        SignIn("user-1");

        Assert.That(await EnqueueAsync(key), Is.True);
        Assert.That(await EnqueueAsync(key, failTimes: 3), Is.False);
        Assert.That(await RunDueAsync(), Is.EqualTo(1));
        Assert.That(await RunDueAsync(), Is.EqualTo(0));

        var job = await JobAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That(job.Status, Is.EqualTo(BackgroundJobStatus.Succeeded));
            Assert.That(job.Payload, Does.Contain("\"failTimes\": 0"), "the first request's payload is kept");
            Assert.That(job.CreatedById, Is.EqualTo("user-1"));
            Assert.That(job.CompletedAt, Is.Not.Null);
            Assert.That(TestJob.Runs(key), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Enqueue_JoinsTheOpenTransaction()
    {
        var rolledBack = Unique("job");
        var committed = Unique("job");

        await WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var queue = services.GetRequiredService<JobQueue>();

            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await queue.EnqueueAsync(TestJob.JobType, new TestJobRequest(rolledBack), rolledBack);
                await transaction.RollbackAsync();
            }
            await using (var transaction = await db.Database.BeginTransactionAsync())
            {
                await queue.EnqueueAsync(TestJob.JobType, new TestJobRequest(committed), committed);
                await transaction.CommitAsync();
            }
        });

        var keys = await WithDbAsync(db => db.BackgroundJobs.Select(j => j.IdempotencyKey).ToListAsync());
        Assert.That(keys, Is.EqualTo(new[] { committed }));
    }

    [Test]
    public void RunAt_MustBeUtc()
    {
        Assert.ThrowsAsync<ArgumentException>(() => EnqueueAsync(Unique("job"), runAtUtc: DateTime.Now));
    }

    [Test]
    public async Task FutureJobs_WaitUntilDue()
    {
        var key = Unique("job");
        await EnqueueAsync(key, runAtUtc: DateTime.UtcNow.AddHours(1));

        Assert.That(await RunDueAsync(), Is.EqualTo(0));
        await MakeDueAsync(key);
        Assert.That(await RunDueAsync(), Is.EqualTo(1));
    }

    [Test]
    public async Task FailingJob_IsRetried_UntilItSucceeds()
    {
        var key = Unique("job");
        await EnqueueAsync(key, failTimes: 1, maxAttempts: 3);

        await RunDueAsync();

        var job = await JobAsync(key);
        Assert.That((job.Status, job.Attempts, job.LastError), Is.EqualTo((BackgroundJobStatus.Succeeded, 2, (string?)null)));
    }

    [Test]
    public async Task FailingJob_StopsAfterMaxAttempts_WithTheLastError()
    {
        var key = Unique("job");
        await EnqueueAsync(key, failTimes: 5, maxAttempts: 2);

        await RunDueAsync();

        var job = await JobAsync(key);
        Assert.Multiple(() =>
        {
            Assert.That((job.Status, job.Attempts), Is.EqualTo((BackgroundJobStatus.Failed, 2)));
            Assert.That(job.LastError, Is.EqualTo("InvalidOperationException: Simulated failure 2 of 5"));
            Assert.That(job.CompletedAt, Is.Not.Null);
            Assert.That(TestJob.Runs(key), Is.EqualTo(2));
        });
    }

    // Καθυστέρηση = RetryDelaySeconds × 2^(προσπάθεια - 1), έως μία ώρα
    [Test]
    public async Task Retries_BackOffExponentially_UpToAnHour()
    {
        var key = Unique("job");
        await EnqueueAsync(key, failTimes: 10, maxAttempts: 10);

        foreach (var (delaySeconds, expected) in new[] { (30, 30), (30, 60), (30, 120), (3000, 3600) })
        {
            await MakeDueAsync(key);
            var before = DateTime.UtcNow;
            await RunDueAsync(new JobOptions { RetryDelaySeconds = delaySeconds });

            var job = await JobAsync(key);
            Assert.That(job.Status, Is.EqualTo(BackgroundJobStatus.Pending));
            Assert.That(job.LockedUntil, Is.Null);
            Assert.That(job.RunAt, Is.EqualTo(before.AddSeconds(expected)).Within(TimeSpan.FromSeconds(5)), $"attempt {job.Attempts}");
        }
    }

    [Test]
    public async Task CrashedJob_IsTakenAgain_OnlyAfterItsLeaseExpires()
    {
        var key = Unique("job");
        await EnqueueAsync(key);

        await SimulateCrashAsync(key, attempts: 1, lockedUntil: DateTime.UtcNow.AddMinutes(5));
        Assert.That(await RunDueAsync(), Is.EqualTo(0), "still leased by the other runner");

        await SimulateCrashAsync(key, attempts: 1, lockedUntil: DateTime.UtcNow.AddSeconds(-1));
        Assert.That(await RunDueAsync(), Is.EqualTo(1));

        var job = await JobAsync(key);
        Assert.That((job.Status, job.Attempts, TestJob.Runs(key)), Is.EqualTo((BackgroundJobStatus.Succeeded, 2, 1)));
    }

    [Test]
    public async Task CrashedJob_OnItsLastAttempt_FailsWithoutRunningAgain()
    {
        var key = Unique("job");
        await EnqueueAsync(key, maxAttempts: 2);
        await SimulateCrashAsync(key, attempts: 2, lockedUntil: DateTime.UtcNow.AddSeconds(-1));

        await RunDueAsync();

        var job = await JobAsync(key);
        Assert.That((job.Status, job.LastError, TestJob.Runs(key)), Is.EqualTo((BackgroundJobStatus.Failed, "Stopped after 2 attempts.", 0)));
    }

    [Test]
    public async Task UnknownJobType_Fails_WithAClearError()
    {
        var key = Unique("job");
        await WithScopeAsync(services => services.GetRequiredService<JobQueue>().EnqueueAsync("tests.nobody", new { }, key));

        await RunDueAsync();

        var job = await JobAsync(key);
        Assert.That((job.Status, job.LastError), Is.EqualTo((BackgroundJobStatus.Failed, "No handler is registered for job type 'tests.nobody'.")));
    }

    [Test]
    public async Task ParallelRunners_NeverRunTheSameJobTwice()
    {
        var keys = Enumerable.Range(0, 40).Select(_ => Unique("job")).ToList();
        foreach (var key in keys)
            await EnqueueAsync(key);

        var counts = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => RunDueAsync())));

        Assert.That(counts.Sum(), Is.EqualTo(keys.Count));
        Assert.That(keys.Select(TestJob.Runs), Is.All.EqualTo(1));
        Assert.That(await WithDbAsync(db => db.BackgroundJobs.CountAsync(j => j.Status != BackgroundJobStatus.Succeeded)), Is.Zero);
    }

    [Test]
    public async Task JobChanges_AreSignedByTheJob_NotByAUser()
    {
        var key = Unique("job");
        SignIn("user-1"); // Ο χρήστης που έβαλε τη δουλειά δεν είναι αυτός που την εκτελεί
        await EnqueueAsync(key, createFolder: true);
        SignOut();

        await RunDueAsync();

        var log = await WithDbAsync(db => db.AuditLogs.AsNoTracking()
            .SingleAsync(l => l.TableName == "MediaFolders" && l.NewValues!.Contains(key)));
        Assert.That((log.Source, log.UserId, log.Type), Is.EqualTo(($"jobs:{TestJob.JobType}", (string?)null, AuditType.Create)));
    }
}
