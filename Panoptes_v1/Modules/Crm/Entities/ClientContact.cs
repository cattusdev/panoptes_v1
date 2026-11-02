using Panoptes_v1.Models;
using Panoptes_v1.Services.Access;

namespace Panoptes_v1.Modules.Crm.Entities
{
    // Η σχέση ανθρώπου-πελάτη: ρόλος, κύρια επαφή, επαφή τιμολόγησης. Ένας πελάτης έχει το πολύ μία κύρια επαφή
    // (unique partial index). Η αφαίρεση σβήνει μόνο τη σχέση· το πρόσωπο μένει.
    [AccessThrough(typeof(Client))]
    public class ClientContact : IHasTimestamps
    {
        public Guid Id { get; set; }
        public Guid ClientId { get; set; }
        public Guid PersonId { get; set; }
        public string? Role { get; set; }                  // ελεύθερο κείμενο (με προτάσεις), π.χ. "CEO"
        public bool IsPrimary { get; set; }
        public bool IsBillingContact { get; set; }
        public DateOnly? StartedOn { get; set; }
        public DateOnly? EndedOn { get; set; }

        public DateTime CreatedAt { get; set; }
        public string? CreatedById { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedById { get; set; }
        public uint RowVersion { get; set; }

        public Client Client { get; set; } = null!;
        public Person Person { get; set; } = null!;
    }
}
