using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Api;
using Panoptes_v1.Data;
using Panoptes_v1.Modules.Collaboration.Entities;
using Panoptes_v1.Services.Access;

namespace Panoptes_v1.Modules.Collaboration.Services
{
    // Τι έγινε σε μια εγγραφή, για το ιστορικό. Data = οι τιμές που χρειάζεται το κείμενο (ονόματα, αιτιολογία...).
    public record ActivityEntry(string EntityType, Guid EntityId, string Verb, object? Data = null, Guid? ClientId = null, Guid? ProjectId = null);

    public record ActivityActor(string Id, string Email);

    public record ActivityItem(Guid Id, string EntityType, Guid EntityId, string Verb, JsonElement Data, ActivityActor? Actor, string? Source, DateTime OccurredAt);

    public class ActivityListRequest : ListRequest
    {
        public ActivityListRequest() => PageSize = 20;
    }

    // Το ιστορικό δραστηριότητας (ARCHITECTURE §9.2). Add: μπαίνει στο ίδιο SaveChanges με την αλλαγή, άρα
    // ή σώζονται και τα δύο ή κανένα. Οι αναγνώσεις γίνονται αφού το module της εγγραφής έχει ελέγξει την πρόσβαση.
    public interface IActivityLog
    {
        void Add(ActivityEntry entry);

        // Όλο το ιστορικό ενός πελάτη (και των εγγραφών του), το πιο πρόσφατο πρώτο. Ο καλών έχει ελέγξει ότι βλέπει τον πελάτη.
        Task<PagedResult<ActivityItem>> ForClientAsync(Guid clientId, ClaimsPrincipal user, ListRequest request, CancellationToken cancellationToken = default);
    }

    public class ActivityLog : IActivityLog
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ApplicationDbContext _db;
        private readonly IHttpContextAccessor _http;
        private readonly IProjectAccess _projects;

        public ActivityLog(ApplicationDbContext db, IHttpContextAccessor http, IProjectAccess projects)
        {
            _db = db;
            _http = http;
            _projects = projects;
        }

        public void Add(ActivityEntry entry)
        {
            // Όπως το audit log: το κομμάτι του συστήματος που το δήλωσε (job, CLI), αλλιώς ο χρήστης του request
            var source = AuditContext.Source;
            _db.Set<ActivityEvent>().Add(new ActivityEvent
            {
                Id = Guid.NewGuid(),
                EntityType = entry.EntityType,
                EntityId = entry.EntityId,
                ClientId = entry.ClientId,
                ProjectId = entry.ProjectId,
                Verb = entry.Verb,
                Data = JsonSerializer.Serialize(entry.Data ?? new { }, Json),
                ActorId = source == null ? _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier) : null,
                Source = source ?? (_http.HttpContext == null ? AuditContext.DefaultSource : null),
                OccurredAt = DateTime.UtcNow,
            });
        }

        private static readonly SortMap<ActivityEvent> Sort = new SortMap<ActivityEvent>("-occurred", e => e.Id, tieBreakerDescending: true)
            .Add("occurred", e => e.OccurredAt);

        public async Task<PagedResult<ActivityItem>> ForClientAsync(Guid clientId, ClaimsPrincipal user, ListRequest request, CancellationToken cancellationToken = default)
        {
            var query = VisibleTo(_db.Set<ActivityEvent>().AsNoTracking().Where(e => e.ClientId == clientId), user);
            var page = await Sort.Apply(query, request.Sort).ToPagedAsync(request, cancellationToken);

            var actorIds = page.Items.Select(e => e.ActorId).OfType<string>().Distinct().ToList();
            var emails = await _db.Users.AsNoTracking().Where(u => actorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? "", cancellationToken);

            return page.Map(e => new ActivityItem(
                e.Id,
                e.EntityType,
                e.EntityId,
                e.Verb,
                JsonDocument.Parse(e.Data).RootElement.Clone(),
                e.ActorId is string id ? new ActivityActor(id, emails.GetValueOrDefault(id, "")) : null,
                e.Source,
                DateTime.SpecifyKind(e.OccurredAt, DateTimeKind.Utc)));
        }

        // Γραμμές εγγραφών μέσα σε project: μόνο αν ο χρήστης βλέπει το project (restricted = μόνο τα μέλη)
        private IQueryable<ActivityEvent> VisibleTo(IQueryable<ActivityEvent> events, ClaimsPrincipal user)
        {
            var scope = _projects.VisibleProjects(user);
            if (scope.All) return events;
            if (scope.Ids is IQueryable<Guid> projectIds)
                return events.Where(e => e.ProjectId == null || projectIds.Contains(e.ProjectId.Value));
            return events.Where(e => e.ProjectId == null);
        }
    }
}
