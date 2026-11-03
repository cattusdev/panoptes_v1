using Panoptes_v1.Data;
using Panoptes_v1.Models;

namespace Panoptes_v1.Modules.Crm.Entities
{
    // Ένας άνθρωπος-επαφή. Μπορεί να είναι επαφή σε πολλούς πελάτες (ClientContact), χωρίς ποτέ να ενώνει τα υπόλοιπά τους.
    public class Person : IHasTimestamps
    {
        public Guid Id { get; set; }
        public string FirstName { get; set; } = "";
        public string? LastName { get; set; }
        public List<ContactMethod> ContactMethods { get; set; } = [];
        [AuditIgnore]
        public List<string> MatchEmails { get; set; } = [];
        [AuditIgnore]
        public List<string> MatchPhones { get; set; } = [];
        public string? Notes { get; set; }
        // Χωρίς τηλέφωνο: γιατί (π.χ. "μόνο με email προς το παρόν"). Με τηλέφωνο είναι πάντα null.
        public string? NoPhoneReason { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedById { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedById { get; set; }
        public uint RowVersion { get; set; }

        public List<ClientContact> Clients { get; set; } = [];

        public string FullName => string.IsNullOrWhiteSpace(LastName) ? FirstName : $"{FirstName} {LastName}";
    }
}
