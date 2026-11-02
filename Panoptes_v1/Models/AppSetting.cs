namespace Panoptes_v1.Models
{
    // Ρυθμίσεις της εφαρμογής ως key/value: μία γραμμή ανά ενότητα (π.χ. "general", "email").
    // Value = JSON της ενότητας, Secret = κρυπτογραφημένο μυστικό (π.χ. SMTP password), ποτέ στο audit log.
    public class AppSetting : IHasTimestamps
    {
        public string Key { get; set; } = "";
        public string Value { get; set; } = "{}";
        public string? Secret { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedById { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? UpdatedById { get; set; }
        public uint RowVersion { get; set; } // Έλεγχος ταυτόχρονων αλλαγών (xmin της Postgres)
    }
}
