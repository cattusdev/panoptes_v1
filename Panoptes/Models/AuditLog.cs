using System;

namespace Panoptes.Models
{
    // Οι τύποι των ενεργειών που καταγράφουμε
    public enum AuditType
    {
        None = 0,
        Create = 1,
        Update = 2,
        Delete = 3,
        Manual = 4 // Για login, exports, κλπ
    }

    public class AuditLog
    {
        public int Id { get; set; }

        // Ποιος το έκανε (Μπορεί να είναι null αν π.χ. είναι σύστημα ή ανώνυμος)
        public string? UserId { get; set; }

        // Τι έκανε
        public AuditType Type { get; set; }

        // Σε ποιον πίνακα το έκανε
        public string? TableName { get; set; }

        // Πότε το έκανε (ΠΑΝΤΑ UTC)
        public DateTime DateTime { get; set; }

        // Τα παλιά δεδομένα σε JSON
        public string? OldValues { get; set; }

        // Τα νέα δεδομένα σε JSON
        public string? NewValues { get; set; }

        // Ποιες στήλες άλλαξαν (χρήσιμο για να μην ψάχνεις όλο το JSON)
        public string? AffectedColumns { get; set; }

        // Το ID της εγγραφής που πειράχτηκε (π.χ. το UserID ή το RoleID)
        public string? PrimaryKey { get; set; }

        // Για τα Manual Logs (π.χ. "User Login", "Exported PDF")
        public string? ActionMessage { get; set; }
    }
}