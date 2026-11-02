using System.Text;
using Panoptes_v1.Modules.Crm.Entities;

namespace Panoptes_v1.Modules.Crm.Services
{
    // Κανονικοποίηση για τα διπλότυπα (ARCHITECTURE §4.1): ίδιος ΑΦΜ, ίδιο email ή τηλέφωνο, όπως κι αν γράφτηκαν.
    public static class CrmMatching
    {
        public const double NameSimilarity = 0.6;

        // "EL 099 999 999", "gr099999999", "099.999.999" -> "099999999". Ξένα VAT κρατούν το πρόθεμα της χώρας.
        public static string? TaxId(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var compact = new string(value.Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
            if (compact.Length == 11 && (compact.StartsWith("EL") || compact.StartsWith("GR")) && compact[2..].All(char.IsAsciiDigit))
                compact = compact[2..];
            return compact.Length == 0 ? null : compact;
        }

        public static string? Email(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

        // Μόνο τα ψηφία, χωρίς το διεθνές πρόθεμα της Ελλάδας: "+30 210 1234567" = "0030 2101234567" = "2101234567"
        public static string? Phone(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var digits = new StringBuilder();
            foreach (var c in value) if (char.IsAsciiDigit(c)) digits.Append(c);
            var phone = digits.ToString();
            if (phone.StartsWith("00")) phone = phone[2..];
            if (phone.Length == 12 && phone.StartsWith("30")) phone = phone[2..];
            return phone.Length < 5 ? null : phone;
        }

        public static List<string> Emails(IEnumerable<ContactMethod> methods) =>
            methods.Where(m => m.Kind == ContactMethodKind.Email).Select(m => Email(m.Value)).OfType<string>().Distinct().ToList();

        public static List<string> Phones(IEnumerable<ContactMethod> methods) =>
            methods.Where(m => m.Kind is ContactMethodKind.Phone or ContactMethodKind.Mobile).Select(m => Phone(m.Value)).OfType<string>().Distinct().ToList();
    }
}
