using Microsoft.Extensions.DependencyInjection;
using Panoptes_v1.Services;
using Panoptes_v1.Services.Settings;

namespace Panoptes_v1.Tests.Foundations;

// Η "σήμερα" της επιχείρησης (ARCHITECTURE §2): ζώνη από Settings → General, αλλιώς Europe/Athens
public class BusinessClockTests : DomainTest
{
    private Task<T> WithClockAsync<T>(Func<BusinessClock, Task<T>> action) =>
        WithScopeAsync(services => action(services.GetRequiredService<BusinessClock>()));

    // Η ζώνη της επιχείρησης για όσο τρέχει το test· μετά επιστρέφει η προηγούμενη ρύθμιση
    private async Task WithTimeZoneAsync(string? timeZone, Func<Task> action)
    {
        var original = await WithScopeAsync(services => services.GetRequiredService<SettingsService>().GetAsync<GeneralSettings>());
        await WithScopeAsync(services => services.GetRequiredService<SettingsService>()
            .SaveAsync(new GeneralSettings { TimeZone = timeZone }));
        try
        {
            await action();
        }
        finally
        {
            await WithScopeAsync(services => services.GetRequiredService<SettingsService>().SaveAsync(original));
        }
    }

    // Χειμώνας UTC+2, καλοκαίρι UTC+3
    [TestCase("2026-01-15T21:59:59Z", "2026-01-15")]
    [TestCase("2026-01-15T22:00:00Z", "2026-01-16")]
    [TestCase("2026-07-01T20:59:59Z", "2026-07-01")]
    [TestCase("2026-07-01T21:00:00Z", "2026-07-02")]
    public async Task Today_IsTheAthensDate_ByDefault(string now, string expected)
    {
        Clock.Now = DateTimeOffset.Parse(now);

        await WithTimeZoneAsync(null, async () =>
            Assert.That(await WithClockAsync(clock => clock.TodayAsync()), Is.EqualTo(DateOnly.Parse(expected))));
    }

    [Test]
    public async Task Today_FollowsTheConfiguredTimeZone()
    {
        Clock.Now = DateTimeOffset.Parse("2026-07-02T03:00:00Z"); // 23:00 της 1/7 στη Νέα Υόρκη

        await WithTimeZoneAsync("America/New_York", async () =>
            Assert.That(await WithClockAsync(clock => clock.TodayAsync()), Is.EqualTo(new DateOnly(2026, 7, 1))));
    }

    [Test]
    public async Task UnknownConfiguredZone_FallsBackToAthens()
    {
        await WithTimeZoneAsync("Mars/Olympus_Mons", async () =>
            Assert.That(await WithClockAsync(clock => clock.ToBusinessDateAsync(new DateTime(2026, 1, 15, 22, 30, 0, DateTimeKind.Utc))),
                Is.EqualTo(new DateOnly(2026, 1, 16))));
    }

    [Test]
    public async Task NonUtcInstants_AreRefused()
    {
        await WithScopeAsync(services =>
        {
            var clock = services.GetRequiredService<BusinessClock>();
            Assert.ThrowsAsync<ArgumentException>(() => clock.ToBusinessDateAsync(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Local)));
            Assert.ThrowsAsync<ArgumentException>(() => clock.ToBusinessDateAsync(new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Unspecified)));
            return Task.CompletedTask;
        });
    }

    // Η αρχή μιας ημέρας είναι η πρώτη στιγμή που ανήκει σε αυτήν: ένα δευτερόλεπτο πριν, είναι ακόμα η προηγούμενη.
    // Οι μέρες αλλαγής ώρας της Αθήνας (29/3, 25/10) αλλάζουν στις 03:00/04:00, όχι τα μεσάνυχτα.
    [TestCase("2026-01-15", "2026-01-14T22:00:00Z")]
    [TestCase("2026-03-29", "2026-03-28T22:00:00Z")]
    [TestCase("2026-03-30", "2026-03-29T21:00:00Z")]
    [TestCase("2026-10-25", "2026-10-24T21:00:00Z")]
    [TestCase("2026-10-26", "2026-10-25T22:00:00Z")]
    public async Task StartOfDay_IsAthensMidnight_InUtc(string date, string expected)
    {
        await WithTimeZoneAsync(null, () => AssertStartOfDayAsync(DateOnly.Parse(date), DateTimeOffset.Parse(expected).UtcDateTime));
    }

    // Ζώνες όπου η θερινή ώρα ξεκινά τα μεσάνυχτα: τα 00:00 δεν υπάρχουν, η μέρα ξεκινά στη πρώτη έγκυρη ώρα
    [Test]
    public async Task StartOfDay_SkipsAMissingMidnight()
    {
        var (zoneId, date) = FindMissingMidnight();

        await WithTimeZoneAsync(zoneId, async () =>
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
            var firstValid = date.ToDateTime(TimeOnly.MinValue);
            while (zone.IsInvalidTime(firstValid)) firstValid = firstValid.AddMinutes(15);

            await AssertStartOfDayAsync(date, TimeZoneInfo.ConvertTimeToUtc(firstValid, zone));
        });
    }

    private async Task AssertStartOfDayAsync(DateOnly date, DateTime expectedUtc)
    {
        await WithClockAsync(async clock =>
        {
            var start = await clock.StartOfDayUtcAsync(date);
            var startDate = await clock.ToBusinessDateAsync(start);
            var secondBefore = await clock.ToBusinessDateAsync(start.AddSeconds(-1));
            Assert.Multiple(() =>
            {
                Assert.That(start, Is.EqualTo(expectedUtc));
                Assert.That(start.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(startDate, Is.EqualTo(date));
                Assert.That(secondBefore, Is.EqualTo(date.AddDays(-1)));
            });
            return true;
        });
    }

    private static (string ZoneId, DateOnly Date) FindMissingMidnight()
    {
        foreach (var zoneId in new[] { "America/Santiago", "Asia/Beirut", "America/Havana", "America/Asuncion" })
        {
            if (!BusinessClock.TryFindTimeZone(zoneId, out var zone)) continue;
            for (var date = new DateOnly(2026, 1, 1); date.Year < 2028; date = date.AddDays(1))
                if (zone.IsInvalidTime(date.ToDateTime(TimeOnly.MinValue)))
                    return (zoneId, date);
        }

        Assert.Inconclusive("This machine's time zone data has no zone whose daylight saving time starts at midnight.");
        return default;
    }
}
