using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using Microsoft.Extensions.Localization;
using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace Panoptes_v1.Api
{
    public record BackgroundJobDto(
        Guid Id,
        string Type,
        string IdempotencyKey,
        BackgroundJobStatus Status,
        DateTime RunAt,
        int Attempts,
        int MaxAttempts,
        string? LastError,
        DateTime CreatedAt,
        DateTime? CompletedAt,
        string? Payload); // Μόνο στη λεπτομέρεια (GET /api/jobs/{id}): ένα email μπορεί να είναι μεγάλο

    // Πόσες δουλειές ανά κατάσταση και ποιοι τύποι υπάρχουν (για τα φίλτρα και την ειδοποίηση αποτυχιών)
    public record JobSummaryDto(IReadOnlyDictionary<BackgroundJobStatus, int> Counts, IReadOnlyList<string> Types);

    public class JobIdsRequest
    {
        [Required, MinLength(1), MaxLength(100)]
        public List<Guid> Ids { get; set; } = [];
    }

    // search: ψάχνει στον τύπο και στο idempotency key (π.χ. "email:" ή το id μιας εγγραφής)
    public class JobListRequest : ListRequest
    {
        public BackgroundJobStatus? Status { get; set; }
        public string? Type { get; set; }
    }

    // Οι δουλειές του παρασκηνίου: τι περιμένει, τι απέτυχε και γιατί. Επανάληψη και ακύρωση. Μόνο ο Admin.
    [ApiController]
    [Route("api/jobs")]
    [Authorize(Roles = SystemRoles.Admin)]
    public class JobsController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IStringLocalizer<SharedResource> _t;

        public JobsController(ApplicationDbContext db, IStringLocalizer<SharedResource> localizer)
        {
            _db = db;
            _t = localizer;
        }

        // Ταξινόμηση: created (προεπιλογή: η πιο πρόσφατη πρώτη) ή runAt
        private static readonly SortMap<BackgroundJob> JobSort = new SortMap<BackgroundJob>("-created", j => j.Id)
            .Add("created", j => j.CreatedAt)
            .Add("runAt", j => j.RunAt);

        // GET /api/jobs?search=&status=Failed&type=email.send&sort=&page=&pageSize=
        [HttpGet]
        public async Task<PagedResult<BackgroundJobDto>> List([FromQuery] JobListRequest request)
        {
            var query = _db.BackgroundJobs.AsNoTracking();
            if (request.Status != null) query = query.Where(j => j.Status == request.Status);
            if (!string.IsNullOrWhiteSpace(request.Type)) query = query.Where(j => j.Type == request.Type.Trim());
            if (request.SearchTerm is string term)
            {
                var pattern = Like.Contains(term);
                query = query.Where(j => EF.Functions.ILike(j.Type, pattern) || EF.Functions.ILike(j.IdempotencyKey, pattern));
            }

            return (await JobSort.Apply(query, request.Sort).ToPagedAsync(request)).Map(job => ToDto(job, withPayload: false));
        }

        [HttpGet("summary")]
        public async Task<JobSummaryDto> Summary()
        {
            var counts = await _db.BackgroundJobs.GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync();
            var types = await _db.BackgroundJobs.Select(j => j.Type).Distinct().OrderBy(t => t).ToListAsync();
            return new JobSummaryDto(
                Enum.GetValues<BackgroundJobStatus>().ToDictionary(status => status, status => counts.FirstOrDefault(c => c.Key == status)?.Count ?? 0),
                types);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BackgroundJobDto>> Get(Guid id)
        {
            var job = await _db.BackgroundJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
            return job == null ? NotFound() : ToDto(job, withPayload: true);
        }

        // Αποτυχημένη ή ακυρωμένη -> ξανά στην ουρά, με νέες προσπάθειες
        [HttpPost("{id:guid}/retry")]
        public async Task<ActionResult<BackgroundJobDto>> Retry(Guid id)
        {
            var updated = await RetryableJobs(_db.BackgroundJobs.Where(j => j.Id == id)).ExecuteUpdateAsync(RequeueJob);
            return updated == 1 ? await Get(id) : await NotChangedAsync(id, "Only failed or cancelled jobs can be retried.");
        }

        // Πολλές μαζί (π.χ. μετά από διακοπή του SMTP): όλες ή καμία. Αν κάποια δεν είναι πια αποτυχημένη/ακυρωμένη, 409.
        [HttpPost("retry")]
        public async Task<IActionResult> RetryMany(JobIdsRequest request)
        {
            var ids = request.Ids.Distinct().ToList();
            await using var transaction = await _db.Database.BeginTransactionAsync();
            var updated = await RetryableJobs(_db.BackgroundJobs.Where(j => ids.Contains(j.Id))).ExecuteUpdateAsync(RequeueJob);
            if (updated != ids.Count)
                return Problem(_t["Some of the selected jobs can't be retried any more. Reload and try again."], statusCode: StatusCodes.Status409Conflict);
            await transaction.CommitAsync();
            return Ok(new { retried = updated });
        }

        // Μόνο όσες δεν έχουν ξεκινήσει (μια δουλειά που τρέχει δεν σταματά στη μέση)
        [HttpPost("{id:guid}/cancel")]
        public async Task<ActionResult<BackgroundJobDto>> Cancel(Guid id)
        {
            var updated = await _db.BackgroundJobs
                .Where(j => j.Id == id && j.Status == BackgroundJobStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, BackgroundJobStatus.Cancelled)
                    .SetProperty(j => j.CompletedAt, DateTime.UtcNow));
            return updated == 1 ? await Get(id) : await NotChangedAsync(id, "Only pending jobs can be cancelled.");
        }

        private static IQueryable<BackgroundJob> RetryableJobs(IQueryable<BackgroundJob> jobs) =>
            jobs.Where(j => j.Status == BackgroundJobStatus.Failed || j.Status == BackgroundJobStatus.Cancelled);

        // Ξανά στην ουρά από την αρχή: τώρα, με όλες τις προσπάθειες. Το LastError μένει μέχρι την επόμενη εκτέλεση.
        private static readonly Expression<Func<SetPropertyCalls<BackgroundJob>, SetPropertyCalls<BackgroundJob>>> RequeueJob = s => s
            .SetProperty(j => j.Status, BackgroundJobStatus.Pending)
            .SetProperty(j => j.RunAt, DateTime.UtcNow)
            .SetProperty(j => j.Attempts, 0)
            .SetProperty(j => j.CompletedAt, (DateTime?)null);

        private async Task<ActionResult<BackgroundJobDto>> NotChangedAsync(Guid id, string message) =>
            await _db.BackgroundJobs.AnyAsync(j => j.Id == id)
                ? Problem(_t[message], statusCode: StatusCodes.Status409Conflict)
                : NotFound();

        private static BackgroundJobDto ToDto(BackgroundJob job, bool withPayload) => new(
            job.Id,
            job.Type,
            job.IdempotencyKey,
            job.Status,
            DateTime.SpecifyKind(job.RunAt, DateTimeKind.Utc),
            job.Attempts,
            job.MaxAttempts,
            job.LastError,
            DateTime.SpecifyKind(job.CreatedAt, DateTimeKind.Utc),
            job.CompletedAt is DateTime completed ? DateTime.SpecifyKind(completed, DateTimeKind.Utc) : null,
            withPayload ? job.Payload : null);
    }
}
