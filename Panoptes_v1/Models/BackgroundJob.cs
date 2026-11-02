using System.Text.Json.Serialization;
using Panoptes_v1.Models.Media;

namespace Panoptes_v1.Models
{
    [JsonConverter(typeof(JsonStringEnumConverter<BackgroundJobStatus>))]
    public enum BackgroundJobStatus { Pending, Running, Succeeded, Failed, Cancelled }

    // Μια δουλειά στο παρασκήνιο (email, υπενθύμιση, ανανέωση...). Ζει στη βάση, άρα αντέχει restart.
    // IdempotencyKey: μοναδικό, ώστε η ίδια δουλειά να μπαίνει μία φορά όσες φορές κι αν ζητηθεί.
    public class BackgroundJob : INotAudited
    {
        public Guid Id { get; set; }
        public string Type { get; set; } = "";
        public string Payload { get; set; } = "{}"; // JSON
        public string IdempotencyKey { get; set; } = "";
        public BackgroundJobStatus Status { get; set; }

        public DateTime RunAt { get; set; }          // Όχι νωρίτερα από αυτή τη στιγμή (UTC)
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTime? LockedUntil { get; set; }  // Όσο τρέχει· αν το process πέσει, μετά από αυτό ξαναπαίρνεται
        public string? LastError { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? CreatedById { get; set; }
    }
}
