using System.Text.Json;

namespace Panoptes_v1.E2E;

// Audit log (ποιος, από πού, με ποιο reference), ταυτόχρονες αλλαγές (409) και ημερομηνίες της επιχείρησης (Europe/Athens)
public class DataIntegrityTests : MediaTest
{
    [Test]
    public async Task Audit_RequestChanges_RecordTheUserAndReference_ButNotTheRowVersion()
    {
        var editor = await SecondUserAsync(TestData.MediaEditor);
        var name = Unique("audited folder");
        await CreateFolderAsync(editor, name);

        await LoginAsync(TestData.Admin);
        var entry = await AuditEntryAsync("MediaFolders", "Name", name);
        Assert.That(entry.GetProperty("userEmail").GetString(), Is.EqualTo(TestData.MediaEditor));
        Assert.That(entry.GetProperty("source").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(entry.GetProperty("traceId").GetString(), Is.Not.Empty);

        var fields = entry.GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString()).ToList();
        Assert.That(fields, Does.Contain("Name"));
        Assert.That(fields, Has.None.EqualTo("RowVersion"));
    }

    [Test]
    public async Task Audit_JobChanges_AreSignedByTheJob_AndShownAsSystem()
    {
        await LoginAsync(TestData.Admin);
        var name = Unique("job folder");
        Assert.That((await ApiAsync("POST", "/api/test/jobs", new { key = name, createFolder = true })).Status, Is.EqualTo(200));
        Assert.That((await ApiAsync("POST", "/api/test/jobs/run")).Status, Is.EqualTo(200));

        // Η δουλειά έτρεξε μέσα σε request του Admin, αλλά η αλλαγή "υπογράφεται" από το job
        var entry = await AuditEntryAsync("MediaFolders", "Name", name);
        Assert.That(entry.GetProperty("userEmail").ValueKind, Is.EqualTo(JsonValueKind.Null));
        Assert.That(entry.GetProperty("source").GetString(), Is.EqualTo("jobs:test.record"));

        await Page.GotoAsync("/admin/audit-logs");
        await Page.GetByPlaceholder("Search by user or table").FillAsync("MediaFolders");
        await Expect(Page.GetByText("jobs:test.record").First).ToBeVisibleAsync();
    }

    [Test]
    public async Task ConcurrentChange_ReturnsConflict_NotCrash()
    {
        var response = await ApiAsync("POST", "/api/test/concurrency-conflict");
        Assert.That(response.Status, Is.EqualTo(409));
        var problem = (await response.JsonAsync())!.Value;
        Assert.That(problem.GetProperty("detail").GetString(), Is.EqualTo("Someone else changed this at the same time. Reload and try again."));
        Assert.That(problem.GetProperty("traceId").GetString(), Is.Not.Empty);
    }

    [TestCase("2026-03-28T21:59:00Z", "2026-03-28")] // 23:59 Αθήνα (χειμερινή ώρα, UTC+2)
    [TestCase("2026-03-28T22:30:00Z", "2026-03-29")] // 00:30 Αθήνα
    [TestCase("2026-07-15T20:30:00Z", "2026-07-15")] // 23:30 Αθήνα (θερινή ώρα, UTC+3)
    [TestCase("2026-07-15T21:30:00Z", "2026-07-16")] // 00:30 Αθήνα
    public async Task BusinessDates_FollowEuropeAthens_ByDefault(string instant, string expectedDate)
    {
        var result = await GetJsonAsync($"/api/test/business-date?at={Uri.EscapeDataString(instant)}");
        Assert.That(result.GetProperty("date").GetString(), Is.EqualTo(expectedDate));
    }

    [Test]
    public async Task BusinessDay_StartsAtAthensMidnight()
    {
        var result = await GetJsonAsync($"/api/test/business-date?at={Uri.EscapeDataString("2026-07-16T12:00:00Z")}");
        var start = result.GetProperty("startOfDayUtc").GetDateTime().ToUniversalTime();
        Assert.That(start, Is.EqualTo(new DateTime(2026, 7, 15, 21, 0, 0, DateTimeKind.Utc)));
    }

    [Test]
    public async Task TimeZoneSetting_MustBeARealZone()
    {
        await LoginAsync(TestData.Admin);
        var unknown = await ApiAsync("PUT", "/api/settings/general", new { defaultLanguage = "en", dateFormat = "d MMM yyyy", timeFormat = "24h", timeZone = "Mars/Phobos" });
        Assert.That(unknown.Status, Is.EqualTo(400));

        var athens = await ApiAsync("PUT", "/api/settings/general", new { defaultLanguage = "en", dateFormat = "d MMM yyyy", timeFormat = "24h", timeZone = "Europe/Athens" });
        Assert.That(athens.Status, Is.EqualTo(200));
        var reset = await ApiAsync("PUT", "/api/settings/general", new { defaultLanguage = "en", dateFormat = "d MMM yyyy", timeFormat = "24h", timeZone = (string?)null });
        Assert.That(reset.Status, Is.EqualTo(200));
    }

    // Η πιο πρόσφατη εγγραφή του audit log για τον πίνακα όπου το πεδίο πήρε αυτή την τιμή
    private async Task<JsonElement> AuditEntryAsync(string table, string field, string value)
    {
        var page = await GetJsonAsync($"/api/audit-logs?search={table}&pageSize=100");
        return page.GetProperty("items").EnumerateArray().First(e =>
            e.GetProperty("changes").EnumerateArray().Any(c =>
                c.GetProperty("field").GetString() == field && c.GetProperty("newValue").GetString() == value));
    }
}
