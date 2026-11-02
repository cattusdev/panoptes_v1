using System.Text.Json.Serialization;
using Panoptes_v1.Data;
using Panoptes_v1.Models;

namespace Panoptes_v1.Modules.Crm.Entities
{
    [JsonConverter(typeof(JsonStringEnumConverter<ClientKind>))]
    public enum ClientKind
    {
        Organization,
        Individual,
    }

    // Active/Inactive τα ορίζει ο χρήστης· η αρχειοθέτηση είναι ξεχωριστή (ArchivedAt) και δεν αλλάζει το Status,
    // ώστε το Unarchive να γυρίζει τον πελάτη όπως ήταν.
    [JsonConverter(typeof(JsonStringEnumConverter<ClientStatus>))]
    public enum ClientStatus
    {
        Active,
        Inactive,
    }

    // Ο πελάτης = η νομική οντότητα που χρεώνεται (ARCHITECTURE §4.1, CRM-01/02). Κάθε υπόλοιπο ανήκει σε ακριβώς έναν
    // πελάτη: δύο εταιρείες του ίδιου ανθρώπου είναι δύο πελάτες με δύο υπόλοιπα. Οι άνθρωποι συνδέονται μέσω ClientContact.
    public class Client : IHasTimestamps
    {
        public Guid Id { get; set; }
        public string Ref { get; set; } = "";              // CL-0042
        public ClientKind Kind { get; set; }
        public string DisplayName { get; set; } = "";
        public string? LegalName { get; set; }
        public string? TaxId { get; set; }                 // ΑΦΜ όπως γράφτηκε
        [AuditIgnore]
        public string? TaxIdNormalized { get; set; }       // για τα διπλότυπα (παράγεται από το TaxId)
        public string? TaxOffice { get; set; }             // ΔΟΥ
        public ClientStatus Status { get; set; }
        public string DefaultCurrency { get; set; } = "EUR";
        public List<string> Tags { get; set; } = [];
        public List<PostalAddress> Addresses { get; set; } = [];
        public List<ContactMethod> ContactMethods { get; set; } = [];
        [AuditIgnore]
        public List<string> MatchEmails { get; set; } = [];  // κανονικοποιημένα, για τα διπλότυπα (από τα ContactMethods)
        [AuditIgnore]
        public List<string> MatchPhones { get; set; } = [];
        public string? Notes { get; set; }

        public RecordOrigin Origin { get; set; }
        public string? SourceRef { get; set; }             // π.χ. onboarding:{draftId}
        public DateTime? ArchivedAt { get; set; }
        public string? ArchivedById { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedById { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedById { get; set; }
        public uint RowVersion { get; set; }

        public List<ClientContact> Contacts { get; set; } = [];
    }
}
