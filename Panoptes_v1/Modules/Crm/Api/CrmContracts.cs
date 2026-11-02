using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Panoptes_v1.Api;
using Panoptes_v1.Models;
using Panoptes_v1.Modules.Crm.Entities;

namespace Panoptes_v1.Modules.Crm.Api
{
    // --- Αιτήματα ---

    public class AddressRequest
    {
        [Required]
        public AddressKind? Kind { get; set; }

        [Required, StringLength(200)]
        public string Line1 { get; set; } = "";

        [StringLength(200)]
        public string? Line2 { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(20)]
        public string? PostalCode { get; set; }

        [StringLength(100)]
        public string? Region { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }
    }

    public class ContactMethodRequest
    {
        [Required]
        public ContactMethodKind? Kind { get; set; }

        [Required, StringLength(200)]
        public string Value { get; set; } = "";

        [StringLength(50)]
        public string? Label { get; set; }
    }

    // Ίδια στοιχεία για δημιουργία και αλλαγή πελάτη. Ο ΑΦΜ άλλου πελάτη σταματά την αποθήκευση (409 crm.duplicateTaxId)
    // εκτός αν ο χρήστης το επιβεβαιώσει με αιτιολογία (μένει στο ιστορικό).
    public class ClientRequest
    {
        [Required]
        public ClientKind? Kind { get; set; }

        [Required, StringLength(200)]
        public string DisplayName { get; set; } = "";

        [StringLength(300)]
        public string? LegalName { get; set; }

        [StringLength(30)]
        public string? TaxId { get; set; }

        [StringLength(100)]
        public string? TaxOffice { get; set; }

        public ClientStatus Status { get; set; } = ClientStatus.Active;

        [StringLength(3, MinimumLength = 3)]
        public string DefaultCurrency { get; set; } = "EUR";

        [MaxLength(20)]
        public List<string> Tags { get; set; } = [];

        [MaxLength(10)]
        public List<AddressRequest> Addresses { get; set; } = [];

        [MaxLength(20)]
        public List<ContactMethodRequest> ContactMethods { get; set; } = [];

        [StringLength(4000)]
        public string? Notes { get; set; }

        // Αλλαγή: το version που διάβασε ο χρήστης (stale -> 409)
        public uint? Version { get; set; }

        [StringLength(500)]
        public string? DuplicateReason { get; set; }
    }

    // Έλεγχος διπλοτύπων πριν από την αποθήκευση (καθώς γράφει ο χρήστης)
    public class ClientDuplicateCheckRequest
    {
        public Guid? ExcludeId { get; set; }

        [StringLength(200)]
        public string? DisplayName { get; set; }

        [StringLength(300)]
        public string? LegalName { get; set; }

        [StringLength(30)]
        public string? TaxId { get; set; }

        [MaxLength(20)]
        public List<ContactMethodRequest> ContactMethods { get; set; } = [];
    }

    public class VersionRequest
    {
        public uint? Version { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<ClientListStatus>))]
    public enum ClientListStatus
    {
        Active,
        Inactive,
        Archived,
        All,
    }

    // Χωρίς status: όσοι δεν είναι αρχειοθετημένοι
    public class ClientListRequest : ListRequest
    {
        public ClientListStatus? Status { get; set; }
        public ClientKind? Kind { get; set; }

        [StringLength(40)]
        public string? Tag { get; set; }
    }

    public class PersonRequest
    {
        [Required, StringLength(100)]
        public string FirstName { get; set; } = "";

        [StringLength(100)]
        public string? LastName { get; set; }

        [MaxLength(20)]
        public List<ContactMethodRequest> ContactMethods { get; set; } = [];

        [StringLength(4000)]
        public string? Notes { get; set; }

        public uint? Version { get; set; }
    }

    public class PersonListRequest : ListRequest
    {
        public Guid? ClientId { get; set; }
    }

    // Νέα επαφή πελάτη: ένα υπάρχον πρόσωπο (PersonId) ή ένα νέο (Person)
    public class AddContactRequest
    {
        public Guid? PersonId { get; set; }

        public PersonRequest? Person { get; set; }

        [StringLength(100)]
        public string? Role { get; set; }

        public bool IsPrimary { get; set; }
        public bool IsBillingContact { get; set; }
    }

    public class UpdateContactRequest
    {
        [StringLength(100)]
        public string? Role { get; set; }

        public bool IsPrimary { get; set; }
        public bool IsBillingContact { get; set; }

        public uint? Version { get; set; }
    }

    // --- Απαντήσεις ---

    public record ContactMethodDto(ContactMethodKind Kind, string Value, string? Label);

    public record AddressDto(AddressKind Kind, string Line1, string? Line2, string? City, string? PostalCode, string? Region, string? Country);

    public record PrimaryContactDto(Guid PersonId, string Name, string? Role, string? Email, string? Phone);

    // Γραμμή της λίστας πελατών
    public record ClientListItemDto(
        Guid Id,
        string Ref,
        ClientKind Kind,
        string DisplayName,
        string? LegalName,
        string? TaxId,
        ClientStatus Status,
        bool IsArchived,
        IReadOnlyList<string> Tags,
        PrimaryContactDto? PrimaryContact,
        DateTime CreatedAt);

    public record ClientDto(
        Guid Id,
        string Ref,
        ClientKind Kind,
        string DisplayName,
        string? LegalName,
        string? TaxId,
        string? TaxOffice,
        ClientStatus Status,
        string DefaultCurrency,
        IReadOnlyList<string> Tags,
        IReadOnlyList<AddressDto> Addresses,
        IReadOnlyList<ContactMethodDto> ContactMethods,
        string? Notes,
        RecordOrigin Origin,
        DateTime? ArchivedAt,
        DateTime CreatedAt,
        uint Version,
        bool CanEdit,
        bool CanArchive);

    // Ένας άλλος πελάτης όπου το πρόσωπο είναι επίσης επαφή ("Also contact for")
    public record LinkedClientDto(Guid Id, string Ref, string DisplayName, bool IsArchived);

    public record ClientContactDto(
        Guid Id,
        Guid PersonId,
        string FirstName,
        string? LastName,
        string Name,
        string? Role,
        bool IsPrimary,
        bool IsBillingContact,
        IReadOnlyList<ContactMethodDto> ContactMethods,
        IReadOnlyList<LinkedClientDto> AlsoContactFor,
        uint Version);

    public record PersonListItemDto(
        Guid Id,
        string FirstName,
        string? LastName,
        string Name,
        string? Email,
        string? Phone,
        IReadOnlyList<LinkedClientDto> Clients);

    public record PersonClientDto(Guid ContactId, Guid ClientId, string ClientRef, string ClientName, bool ClientArchived, string? Role, bool IsPrimary, bool IsBillingContact);

    public record PersonDto(
        Guid Id,
        string FirstName,
        string? LastName,
        string Name,
        IReadOnlyList<ContactMethodDto> ContactMethods,
        string? Notes,
        IReadOnlyList<PersonClientDto> Clients,
        uint Version,
        bool CanEdit);

    // Πιθανό διπλότυπο. Reasons: taxId (σκληρή προειδοποίηση), name, email, phone (ήπιες)
    public record ClientMatchDto(Guid Id, string Ref, string DisplayName, string? LegalName, string? TaxId, bool IsArchived, IReadOnlyList<string> Reasons);

    public record PersonMatchDto(Guid Id, string Name, string? Email, IReadOnlyList<string> Reasons);

    public record DuplicateCheckDto(IReadOnlyList<ClientMatchDto> Matches);

    internal static class CrmMapping
    {
        public static ContactMethodDto ToDto(this ContactMethod method) => new(method.Kind, method.Value, method.Label);

        public static AddressDto ToDto(this PostalAddress address) =>
            new(address.Kind, address.Line1, address.Line2, address.City, address.PostalCode, address.Region, address.Country);

        public static string? First(this IEnumerable<ContactMethod> methods, params ContactMethodKind[] kinds) =>
            kinds.Select(kind => methods.FirstOrDefault(m => m.Kind == kind)?.Value).FirstOrDefault(value => value != null);

        public static DateTime Utc(this DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);

        public static DateTime? Utc(this DateTime? value) => value is DateTime v ? DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;
    }
}
