using Panoptes_v1.Services.Settings;

namespace Panoptes_v1.Services
{
    // Η "ημερολογιακή" ώρα της επιχείρησης για υπολογισμούς στον server (λήξεις, ανανεώσεις, προθεσμίες, "σήμερα").
    // Στιγμές αποθηκεύονται πάντα σε UTC· ολόκληρες μέρες (π.χ. περίοδος συνδρομής) ως DateOnly.
    // Ζώνη: Settings → General (αν έχει οριστεί), αλλιώς Europe/Athens.
    public class BusinessClock
    {
        public const string DefaultTimeZone = "Europe/Athens";

        private readonly SettingsService _settings;
        private readonly TimeProvider _time;

        public BusinessClock(SettingsService settings, TimeProvider time)
        {
            _settings = settings;
            _time = time;
        }

        public async Task<TimeZoneInfo> TimeZoneAsync()
        {
            var configured = (await _settings.GetAsync<GeneralSettings>()).TimeZone;
            return configured != null && TryFindTimeZone(configured, out var zone) ? zone : TimeZoneInfo.FindSystemTimeZoneById(DefaultTimeZone);
        }

        public async Task<DateOnly> TodayAsync() => await ToBusinessDateAsync(_time.GetUtcNow().UtcDateTime);

        // Σε ποια ημέρα της επιχείρησης πέφτει μια στιγμή (π.χ. 22:30 UTC = 00:30 της επομένης στην Αθήνα)
        public async Task<DateOnly> ToBusinessDateAsync(DateTime utc)
        {
            if (utc.Kind != DateTimeKind.Utc)
                throw new ArgumentException("Expected a UTC DateTime.", nameof(utc));
            return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, await TimeZoneAsync()));
        }

        // Η στιγμή (UTC) που ξεκινά μια ημέρα της επιχείρησης. Αν τα μεσάνυχτα "λείπουν" λόγω θερινής ώρας, η πρώτη έγκυρη ώρα.
        public async Task<DateTime> StartOfDayUtcAsync(DateOnly date)
        {
            var zone = await TimeZoneAsync();
            var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local))
                local = local.AddMinutes(15);
            return TimeZoneInfo.ConvertTimeToUtc(local, zone);
        }

        // IANA ονόματα (π.χ. "Europe/Athens"). Στα Windows τα μετατρέπει το .NET μέσω ICU.
        public static bool TryFindTimeZone(string id, out TimeZoneInfo zone) =>
            TimeZoneInfo.TryFindSystemTimeZoneById(id, out zone!);
    }
}
