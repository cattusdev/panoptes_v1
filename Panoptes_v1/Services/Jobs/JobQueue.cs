using System.Security.Claims;
using System.Text.Json;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Panoptes_v1.Services.Jobs
{
    public class JobOptions
    {
        public const string Section = "Jobs";

        public int PollSeconds { get; set; } = 5;          // Κάθε πόσο ψάχνει δουλειές ο runner (0 = σταματημένος, π.χ. στα tests)
        public int LeaseMinutes { get; set; } = 5;         // Πόσο "κρατάει" μια δουλειά όσο τρέχει (μετά ξαναπαίρνεται)
        public int DefaultMaxAttempts { get; set; } = 5;
        public int RetryDelaySeconds { get; set; } = 30;   // Πρώτη επανάληψη· μετά διπλασιάζεται (έως 1 ώρα)
    }

    // Μια δουλειά ενός τύπου (π.χ. "email.send"). Πρέπει να αντέχει να ξανατρέξει: ελέγχει ξανά αν χρειάζεται ακόμα
    // (π.χ. αν πληρώθηκε στο μεταξύ η χρέωση) και χρησιμοποιεί το IdempotencyKey για ό,τι δημιουργεί.
    public interface IJobHandler
    {
        string Type { get; }
        Task RunAsync(JobContext context, CancellationToken cancellationToken);
    }

    public record JobContext(Guid JobId, string IdempotencyKey, int Attempt, string Payload)
    {
        public T Read<T>() => JsonSerializer.Deserialize<T>(Payload, JobQueue.Json)!;
    }

    // Βάζει δουλειές στην ουρά (πίνακας BackgroundJobs).
    // Outbox: αν υπάρχει ανοιχτό transaction, η δουλειά μπαίνει μαζί με την αλλαγή που την προκάλεσε ή καθόλου.
    // Ίδιο IdempotencyKey: δεν ξαναμπαίνει (π.χ. "renewal-reminder:{subscriptionId}:{periodEnd}").
    public class JobQueue
    {
        public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly JobOptions _options;

        public JobQueue(ApplicationDbContext db, IHttpContextAccessor httpContextAccessor, IOptions<JobOptions> options)
        {
            _db = db;
            _httpContextAccessor = httpContextAccessor;
            _options = options.Value;
        }

        // true: μπήκε τώρα · false: υπήρχε ήδη δουλειά με το ίδιο key
        public async Task<bool> EnqueueAsync(string type, object payload, string idempotencyKey, DateTime? runAtUtc = null,
            int? maxAttempts = null, CancellationToken cancellationToken = default)
        {
            if (runAtUtc is DateTime runAt && runAt.Kind != DateTimeKind.Utc)
                throw new ArgumentException("runAtUtc must be a UTC DateTime.", nameof(runAtUtc));

            var now = DateTime.UtcNow;
            var json = JsonSerializer.Serialize(payload, Json);
            var userId = _httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
            var pending = nameof(BackgroundJobStatus.Pending);

            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "BackgroundJobs" ("Id", "Type", "Payload", "IdempotencyKey", "Status", "RunAt", "Attempts", "MaxAttempts", "CreatedAt", "CreatedById")
                VALUES ({Guid.NewGuid()}, {type}, CAST({json} AS jsonb), {idempotencyKey}, {pending}, {runAtUtc ?? now}, 0, {maxAttempts ?? _options.DefaultMaxAttempts}, {now}, {userId})
                ON CONFLICT ("IdempotencyKey") DO NOTHING
                """, cancellationToken);
            return inserted == 1;
        }
    }
}
