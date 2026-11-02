using System.Text.Json.Serialization;

namespace Panoptes_v1.Modules.Crm.Entities
{
    [JsonConverter(typeof(JsonStringEnumConverter<AddressKind>))]
    public enum AddressKind
    {
        Billing,
        Office,
        Shipping,
        Other,
    }

    // Διεύθυνση πελάτη. Αποθηκεύεται ως jsonb στη γραμμή του πελάτη (λίγες, πάντα μαζί του, και στο audit ως μία τιμή).
    public record PostalAddress
    {
        public AddressKind Kind { get; init; }
        public string Line1 { get; init; } = "";
        public string? Line2 { get; init; }
        public string? City { get; init; }
        public string? PostalCode { get; init; }
        public string? Region { get; init; }
        public string? Country { get; init; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter<ContactMethodKind>))]
    public enum ContactMethodKind
    {
        Email,
        Phone,
        Mobile,
        Website,
    }

    // Email, τηλέφωνο ή site (πελάτη ή προσώπου), jsonb όπως οι διευθύνσεις. Label: π.χ. "Λογιστήριο".
    public record ContactMethod
    {
        public ContactMethodKind Kind { get; init; }
        public string Value { get; init; } = "";
        public string? Label { get; init; }
    }
}
