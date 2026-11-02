using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Data;

namespace Panoptes_v1.Services
{
    // Μορφή ενός κωδικού: "CL-0042" (Yearly = false) ή "CHG-2026-0153" (Yearly = true, ξεκινά από 1 κάθε χρόνο).
    // Digits = ελάχιστα ψηφία· ο αριθμός μεγαλώνει όσο χρειάζεται (CL-10000).
    public sealed record ReferenceFormat(string Prefix, int Digits, bool Yearly)
    {
        public string Format(long number, int? year) =>
            year is int y
                ? $"{Prefix}-{y}-{number.ToString($"D{Digits}", CultureInfo.InvariantCulture)}"
                : $"{Prefix}-{number.ToString($"D{Digits}", CultureInfo.InvariantCulture)}";
    }

    // Οι κωδικοί των business εγγραφών (ARCHITECTURE §16 Q2). Κάθε module παίρνει από εδώ τη δική του μορφή.
    public static class ReferenceFormats
    {
        public static readonly ReferenceFormat Client = new("CL", 4, Yearly: false);
        public static readonly ReferenceFormat Project = new("PRJ", 3, Yearly: true);
        public static readonly ReferenceFormat Task = new("T", 4, Yearly: false);
        public static readonly ReferenceFormat Ticket = new("TCK", 4, Yearly: false);
        public static readonly ReferenceFormat Charge = new("CHG", 4, Yearly: true);
        public static readonly ReferenceFormat Payment = new("PAY", 4, Yearly: true);
        public static readonly ReferenceFormat Subscription = new("SUB", 4, Yearly: false);
    }

    // Ο επόμενος κωδικός μιας σειράς, από τον πίνακα Sequences. Το UPDATE κλειδώνει τη γραμμή της σειράς μέχρι το τέλος
    // του transaction: δύο ταυτόχρονες δημιουργίες παίρνουν διαδοχικούς αριθμούς, και αν το save αποτύχει (rollback)
    // ο αριθμός ξαναδίνεται, οπότε δεν μένουν κενά. Γι' αυτό καλείται μόνο μέσα στο transaction που σώζει την εγγραφή.
    // Η χρονιά των ετήσιων σειρών είναι η "σήμερα" της επιχείρησης (BusinessClock).
    public class ReferenceGenerator
    {
        private readonly ApplicationDbContext _db;
        private readonly BusinessClock _clock;

        public ReferenceGenerator(ApplicationDbContext db, BusinessClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<string> NextAsync(ReferenceFormat format, CancellationToken cancellationToken = default)
        {
            if (_db.Database.CurrentTransaction == null)
                throw new InvalidOperationException("Take a reference inside the transaction that saves the record, or a failed save leaves a gap.");

            int? year = format.Yearly ? (await _clock.TodayAsync()).Year : null;
            var name = year is int y ? $"{format.Prefix}-{y}" : format.Prefix;

            // Χωρίς compose (ToListAsync, όχι Single): το EF δεν μπορεί να τυλίξει INSERT ... RETURNING σε SELECT
            var values = await _db.Database.SqlQuery<long>($"""
                INSERT INTO "Sequences" ("Name", "NextValue") VALUES ({name}, 2)
                ON CONFLICT ("Name") DO UPDATE SET "NextValue" = "Sequences"."NextValue" + 1
                RETURNING "NextValue" - 1 AS "Value"
                """).ToListAsync(cancellationToken);
            return format.Format(values.Single(), year);
        }
    }
}
