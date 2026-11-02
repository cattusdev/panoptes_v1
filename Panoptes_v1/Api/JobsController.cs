using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

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
        string Payload);

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

        // GET /api/jobs?status=Failed&type=email.send&sort=&page=&pageSize=
        [HttpGet]
        public async Task<PagedResult<BackgroundJobDto>> List([FromQuery] JobListRequest request)
        {
            var query = _db.BackgroundJobs.AsNoTracking();
            if (request.Status != null) query = query.Where(j => j.Status == request.Status);
            if (!string.IsNullOrWhiteSpace(request.Type)) query = query.Where(j => j.Type == request.Type.Trim());

            return (await JobSort.Apply(query, request.Sort).ToPagedAsync(request)).Map(ToDto);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<BackgroundJobDto>> Get(Guid id)
        {
            var job = await _db.BackgroundJobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id);
            return job == null ? NotFound() : ToDto(job);
        }

        // Αποτυχημένη ή ακυρωμένη -> ξανά στην ουρά, με νέες προσπάθειες
        [HttpPost("{id:guid}/retry")]
        public async Task<ActionResult<BackgroundJobDto>> Retry(Guid id)
        {
            var updated = await _db.BackgroundJobs
                .Where(j => j.Id == id && (j.Status == BackgroundJobStatus.Failed || j.Status == BackgroundJobStatus.Cancelled))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(j => j.Status, BackgroundJobStatus.Pending)
                    .SetProperty(j => j.RunAt, DateTime.UtcNow)
                    .SetProperty(j => j.Attempts, 0)
                    .SetProperty(j => j.CompletedAt, (DateTime?)null));
            return updated == 1 ? await Get(id) : await NotChangedAsync(id, "Only failed or cancelled jobs can be retried.");
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

        private async Task<ActionResult<BackgroundJobDto>> NotChangedAsync(Guid id, string message) =>
            await _db.BackgroundJobs.AnyAsync(j => j.Id == id)
                ? Problem(_t[message], statusCode: StatusCodes.Status409Conflict)
                : NotFound();

        private static BackgroundJobDto ToDto(BackgroundJob job) => new(
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
            job.Payload);
    }
}
