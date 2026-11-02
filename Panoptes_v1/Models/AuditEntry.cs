using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Text.Json;

namespace Panoptes_v1.Models
{
    public class AuditEntry
    {
        public EntityEntry Entry { get; }
        public string TableName { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? Source { get; set; }
        public string? TraceId { get; set; }
        public AuditType AuditType { get; set; }
        public Dictionary<string, object?> KeyValues { get; } = new();
        public Dictionary<string, object?> OldValues { get; } = new();
        public Dictionary<string, object?> NewValues { get; } = new();
        public List<string> ChangedColumns { get; } = new();

        // Τιμές που δίνει η βάση (π.χ. identity): γράφονται στο log μετά το Save
        public List<PropertyEntry> TemporaryProperties { get; } = new();

        public bool HasTemporaryProperties => TemporaryProperties.Count > 0;

        public AuditEntry(EntityEntry entry)
        {
            Entry = entry;
        }

        // Μετατρέπει το "καλαθάκι" στο τελικό AuditLog Model για να σωθεί στη βάση
        public AuditLog ToAuditLog()
        {
            foreach (var property in TemporaryProperties)
            {
                if (property.Metadata.IsPrimaryKey())
                    KeyValues[property.Metadata.Name] = property.CurrentValue;
                else if (AuditType == AuditType.Create)
                    NewValues[property.Metadata.Name] = property.CurrentValue;
            }

            return new AuditLog
            {
                UserId = UserId,
                Source = Source,
                TraceId = TraceId,
                Type = AuditType,
                TableName = TableName,
                DateTime = DateTime.UtcNow,
                PrimaryKey = JsonSerializer.Serialize(KeyValues),
                OldValues = OldValues.Count == 0 ? null : JsonSerializer.Serialize(OldValues),
                NewValues = NewValues.Count == 0 ? null : JsonSerializer.Serialize(NewValues),
                AffectedColumns = ChangedColumns.Count == 0 ? null : JsonSerializer.Serialize(ChangedColumns)
            };
        }
    }
}
