using PhoneNumbers;

namespace Panoptes_v1.Services
{
    // Τηλέφωνα σε όλη την εφαρμογή: πάντα με κωδικό χώρας, ελεγμένα με το libphonenumber (της Google) και
    // αποθηκευμένα σε E.164 ("+302101234567"). Χωρίς "+" ο αριθμός διαβάζεται ως ελληνικός. Ο client κάνει τον ίδιο
    // έλεγχο με το libphonenumber-js (PhoneInput), αλλά αυτός εδώ αποφασίζει.
    public static class Phones
    {
        public const string DefaultRegion = "GR";

        private static readonly PhoneNumberUtil Util = PhoneNumberUtil.GetInstance();

        // "+30 210 123 4567", "0030 2101234567", "210-1234567" -> "+302101234567"; μη έγκυρος -> null
        public static string? ToE164(string? value, string defaultRegion = DefaultRegion)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            try
            {
                var number = Util.Parse(value.Trim(), defaultRegion);
                return Util.IsValidNumber(number) ? Util.Format(number, PhoneNumberFormat.E164) : null;
            }
            catch (NumberParseException)
            {
                return null;
            }
        }
    }
}
