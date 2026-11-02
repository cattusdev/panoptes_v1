using System.Text.Json.Serialization;

namespace Panoptes.Services.Settings
{
    // Γενικές ρυθμίσεις: προεπιλεγμένη γλώσσα και πώς δείχνουν οι ημερομηνίες σε όλη την εφαρμογή
    public class GeneralSettings : ISettingsSection
    {
        public static string Key => "general";

        // Έτοιμες μορφές (όχι ελεύθερο pattern): ο client ξέρει να τις εμφανίσει
        public static readonly IReadOnlyList<string> DateFormats = ["d MMM yyyy", "MMM d, yyyy", "dd/MM/yyyy", "MM/dd/yyyy", "yyyy-MM-dd"];
        public static readonly IReadOnlyList<string> TimeFormats = ["24h", "12h"];

        public string DefaultLanguage { get; set; } = Localization.AppLanguages.Default; // Για επισκέπτες και όσους δεν έχουν διαλέξει γλώσσα
        public string DateFormat { get; set; } = "d MMM yyyy";
        public string TimeFormat { get; set; } = "24h";
        public string? TimeZone { get; set; } // null = η τοπική ώρα κάθε χρήστη, αλλιώς IANA (π.χ. "Europe/Athens")
    }

    [JsonConverter(typeof(JsonStringEnumConverter<SmtpSecurity>))]
    public enum SmtpSecurity
    {
        Auto,         // Το MailKit διαλέγει από το port (465 = SSL, αλλιώς STARTTLS αν υπάρχει)
        None,         // Χωρίς κρυπτογράφηση (π.χ. Mailpit τοπικά)
        StartTls,     // Συνήθως port 587
        SslOnConnect, // Συνήθως port 465
    }

    // Δικός μας SMTP server από το UI. Το password είναι στο Secret της γραμμής (κρυπτογραφημένο).
    public class EmailSettings : ISettingsSection
    {
        public static string Key => "email";

        public bool Enabled { get; set; }
        public string Host { get; set; } = "";
        public int Port { get; set; } = 587;
        public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
        public string Username { get; set; } = "";
        public string SenderName { get; set; } = "";
        public string SenderEmail { get; set; } = "";
    }
}
