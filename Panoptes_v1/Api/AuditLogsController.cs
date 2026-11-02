using System.Text.Json;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Panoptes_v1.Api
{
    public record AuditChangeDto(string Field, string? OldValue, string? NewValue);

    public record AuditLogDto(
        int Id,
        string Type,
        string? TableName,
        string? RecordId,
        DateTime DateTime,
        string? UserEmail,
        string? Source,   // Χωρίς χρήστη: ποιο κομμάτι του συστήματος (π.χ. "jobs:email.send")
        string? TraceId,  // Το reference του request/job (ίδιο με αυτό των μηνυμάτων λάθους)
        IReadOnlyList<AuditChangeDto> Changes);

    public class AuditLogListRequest : ListRequest
    {
        public AuditType? Type { get; set; }
    }

    [ApiController]
    [Route("api/audit-logs")]
    [Authorize(Policy = Permissions.AuditLogs.View)]
    public class AuditLogsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public AuditLogsController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET /api/audit-logs?search=&type=Create|Update|Delete&sort=&page=&pageSize=
        // search: email του χρήστη ή όνομα πίνακα
        // Ταξινόμηση: time (προεπιλογή: η πιο πρόσφατη πρώτη) ή table
        private static readonly SortMap<AuditLog> LogSort = new SortMap<AuditLog>("-time", a => a.Id, tieBreakerDescending: true)
            .Add("time", a => a.DateTime)
            .Add("table", a => a.TableName);

        [HttpGet]
        public async Task<PagedResult<AuditLogDto>> List([FromQuery] AuditLogListRequest request)
        {
            var query = _context.AuditLogs.AsQueryable();

            if (request.Type is AuditType auditType)
                query = query.Where(a => a.Type == auditType);

            if (request.SearchTerm is string term)
            {
                var pattern = Like.Contains(term);
                var userIds = _context.Users
                    .Where(u => u.Email != null && EF.Functions.ILike(u.Email, pattern))
                    .Select(u => u.Id);

                query = query.Where(a =>
                    (a.UserId != null && userIds.Contains(a.UserId)) ||
                    (a.TableName != null && EF.Functions.ILike(a.TableName, pattern)));
            }

            var page = await LogSort.Apply(query, request.Sort).ToPagedAsync(request);
            var logs = page.Items;

            var ids = logs.Where(l => l.UserId != null).Select(l => l.UserId).Distinct().ToList();
            var emails = await _context.Users
                .Where(u => ids.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email);

            return page.Map(l => new AuditLogDto(
                l.Id,
                l.Type.ToString(),
                l.TableName,
                FormatKey(l.PrimaryKey),
                DateTime.SpecifyKind(l.DateTime, DateTimeKind.Utc),
                l.UserId != null && emails.TryGetValue(l.UserId, out var email) ? email : null,
                l.Source,
                l.TraceId,
                BuildChanges(l)));
        }

        // {"Id":"abc"} -> "abc", {"UserId":"a","RoleId":"b"} -> "a, b"
        private static string? FormatKey(string? json)
        {
            var values = ParseObject(json);
            return values.Count == 0 ? null : string.Join(", ", values.Values.Where(v => v != null));
        }

        // Ένα "diff" ανά πεδίο: παλιά τιμή -> νέα τιμή
        private static List<AuditChangeDto> BuildChanges(AuditLog log)
        {
            var oldValues = ParseObject(log.OldValues);
            var newValues = ParseObject(log.NewValues);

            return oldValues.Keys.Union(newValues.Keys)
                .Select(field => new AuditChangeDto(
                    field,
                    oldValues.GetValueOrDefault(field),
                    newValues.GetValueOrDefault(field)))
                .ToList();
        }

        private static Dictionary<string, string?> ParseObject(string? json)
        {
            var result = new Dictionary<string, string?>();
            if (string.IsNullOrWhiteSpace(json)) return result;

            try
            {
                using var document = JsonDocument.Parse(json);
                if (document.RootElement.ValueKind != JsonValueKind.Object) return result;

                foreach (var property in document.RootElement.EnumerateObject())
                {
                    result[property.Name] = property.Value.ValueKind switch
                    {
                        JsonValueKind.Null => null,
                        JsonValueKind.String => property.Value.GetString(),
                        _ => property.Value.GetRawText()
                    };
                }
            }
            catch (JsonException)
            {
                // Χαλασμένο JSON: δείχνουμε απλά ότι δεν υπάρχουν λεπτομέρειες
            }

            return result;
        }
    }
}
