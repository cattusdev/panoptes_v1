using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panoptes_v1.Data;
using Panoptes_v1.Models;

namespace Panoptes_v1.E2E;

// Τα κοινά θεμέλια των business modules (ARCHITECTURE §2, §1.4): κωδικοί από τον πίνακα Sequences,
// Created*/Updated* από το DbContext, και ο κοινός list helper (σελίδες, ταξινόμηση, typed φίλτρα, αναζήτηση)
public class FoundationTests : MediaTest
{
    private static string Prefix() => $"Q{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";

    private async Task<List<string?>> ReferencesAsync(string query)
    {
        var response = await ApiAsync("POST", $"/api/test/references?{query}");
        Assert.That(response.Status, Is.EqualTo(200), await response.TextAsync());
        return (await response.JsonAsync())!.Value.GetProperty("refs").EnumerateArray().Select(r => r.GetString()).ToList();
    }

    [Test]
    public async Task References_AreGaplessAndUnique_UnderConcurrency()
    {
        var prefix = Prefix();
        var refs = await ReferencesAsync($"prefix={prefix}&count=20");

        var expected = Enumerable.Range(1, 20).Select(n => $"{prefix}-{n:D4}").ToList();
        Assert.That(refs, Is.EquivalentTo(expected));
    }

    [Test]
    public async Task References_FromARolledBackSave_AreGivenAgain()
    {
        var prefix = Prefix();
        Assert.That(await ReferencesAsync($"prefix={prefix}&count=3&mode=rollback"), Is.EqualTo(new[] { $"{prefix}-0001", $"{prefix}-0001", $"{prefix}-0001" }));
        Assert.That(await ReferencesAsync($"prefix={prefix}"), Is.EqualTo(new[] { $"{prefix}-0001" }));
        Assert.That(await ReferencesAsync($"prefix={prefix}"), Is.EqualTo(new[] { $"{prefix}-0002" }));
    }

    [Test]
    public async Task References_OutsideATransaction_AreRefused()
    {
        var prefix = Prefix();
        Assert.That(await ReferencesAsync($"prefix={prefix}&mode=none"), Is.EqualTo(new string?[] { null }));
        // Η άρνηση δεν "έφαγε" αριθμό
        Assert.That(await ReferencesAsync($"prefix={prefix}"), Is.EqualTo(new[] { $"{prefix}-0001" }));
    }

    [Test]
    public async Task YearlyReferences_CarryTheBusinessYear_AndRestartEachYear()
    {
        var prefix = Prefix();
        var year = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/Athens").Year;

        Assert.That(await ReferencesAsync($"prefix={prefix}&yearly=true&count=2"), Is.EquivalentTo(new[] { $"{prefix}-{year}-0001", $"{prefix}-{year}-0002" }));
        // Η ετήσια σειρά είναι ξεχωριστή από τη χωρίς χρονιά
        Assert.That(await ReferencesAsync($"prefix={prefix}"), Is.EqualTo(new[] { $"{prefix}-0001" }));

        var sequences = await TestData.WithServicesAsync(services =>
            services.GetRequiredService<ApplicationDbContext>().Sequences.AsNoTracking()
                .Where(s => s.Name.StartsWith(prefix)).ToDictionaryAsync(s => s.Name, s => s.NextValue));
        Assert.That(sequences, Is.EqualTo(new Dictionary<string, long> { [$"{prefix}-{year}"] = 3, [prefix] = 2 }));
    }

    [Test]
    public async Task Timestamps_AreSetOnSave_AndCreatedOnesNeverChange()
    {
        var before = await FinanceRowAsync();

        await LoginAsync(TestData.Admin);
        var first = await ApiAsync("PUT", "/api/settings/finance", new { defaultVatRate = 21, trackingStartDate = "2026-12-01" });
        Assert.That(first.Status, Is.EqualTo(200));
        var afterAdmin = (await FinanceRowAsync())!;
        Assert.That(afterAdmin.UpdatedById, Is.EqualTo(TestData.UserIds[TestData.Admin]));
        Assert.That(afterAdmin.UpdatedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
        if (before == null)
            Assert.That((afterAdmin.CreatedById, afterAdmin.CreatedAt), Is.EqualTo((afterAdmin.UpdatedById, afterAdmin.UpdatedAt)));
        else
            Assert.That((afterAdmin.CreatedById, afterAdmin.CreatedAt), Is.EqualTo((before.CreatedById, before.CreatedAt)));

        var finance = await SecondUserAsync(TestData.FinanceSettings);
        var second = await ApiAsync(finance, "PUT", "/api/settings/finance", new { defaultVatRate = 24, trackingStartDate = "2026-12-01" });
        Assert.That(second.Status, Is.EqualTo(200));
        var afterFinance = (await FinanceRowAsync())!;
        Assert.That(afterFinance.UpdatedById, Is.EqualTo(TestData.UserIds[TestData.FinanceSettings]));
        Assert.That(afterFinance.UpdatedAt, Is.GreaterThan(afterAdmin.UpdatedAt));
        Assert.That((afterFinance.CreatedById, afterFinance.CreatedAt), Is.EqualTo((afterAdmin.CreatedById, afterAdmin.CreatedAt)));

        // Το audit log έχει ήδη ποιος και πότε: τα timestamps δεν γράφονται ξανά ως αλλαγές
        var logs = await GetJsonAsync("/api/audit-logs?search=AppSettings&type=Update&pageSize=1");
        var fields = logs.GetProperty("items")[0].GetProperty("changes").EnumerateArray().Select(c => c.GetProperty("field").GetString()).ToList();
        Assert.That(fields, Is.EqualTo(new[] { "Value" }));
    }

    private static Task<AppSetting?> FinanceRowAsync() =>
        TestData.WithServicesAsync(services =>
            services.GetRequiredService<ApplicationDbContext>().AppSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == "finance"));

    [Test]
    public async Task Lists_ClampPaging_AndFallBackToTheDefaultSort()
    {
        await LoginAsync(TestData.Admin);

        var clamped = await GetJsonAsync("/api/audit-logs?page=0&pageSize=1000");
        Assert.That((clamped.GetProperty("page").GetInt32(), clamped.GetProperty("pageSize").GetInt32()), Is.EqualTo((1, 100)));

        var unknownSort = await GetJsonAsync("/api/audit-logs?sort=passwordHash&pageSize=10");
        var defaultSort = await GetJsonAsync("/api/audit-logs?pageSize=10");
        Assert.That(Ids(unknownSort), Is.EqualTo(Ids(defaultSort)));

        // Πολλές γραμμές με τον ίδιο πίνακα: χάρη στο tie-breaker οι σελίδες δεν επικαλύπτονται ούτε χάνουν γραμμές
        var firstPage = await GetJsonAsync("/api/audit-logs?sort=table&page=1&pageSize=20");
        var secondPage = await GetJsonAsync("/api/audit-logs?sort=table&page=2&pageSize=20");
        var both = await GetJsonAsync("/api/audit-logs?sort=table&page=1&pageSize=40");
        Assert.That(Ids(firstPage).Concat(Ids(secondPage)), Is.EqualTo(Ids(both)));
    }

    [Test]
    public async Task Lists_SearchWildcardsLiterally()
    {
        await LoginAsync(TestData.Admin);

        // Κανένα email ή όνομα πίνακα δεν έχει % ή _: ως wildcards θα ταίριαζαν σε όλα
        Assert.That((await GetJsonAsync("/api/audit-logs?search=%25")).GetProperty("total").GetInt32(), Is.Zero);
        Assert.That((await GetJsonAsync("/api/audit-logs?search=_")).GetProperty("total").GetInt32(), Is.Zero);
        Assert.That((await GetJsonAsync("/api/users?search=_")).GetProperty("total").GetInt32(), Is.Zero);

        // Χωρίς διάκριση πεζών/κεφαλαίων
        var users = await GetJsonAsync("/api/users?search=SETTINGSEDITOR");
        Assert.That(users.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()), Is.EqualTo(new[] { TestData.SettingsEditor }));
        Assert.That((await GetJsonAsync("/api/audit-logs?search=aspnetusers")).GetProperty("total").GetInt32(), Is.Positive);
    }

    // Το αντίστροφο: όποιος ψάχνει "50%_off" βρίσκει αυτό ακριβώς (με wildcards θα ταίριαζε και το "50x off")
    [Test]
    public async Task Lists_SearchFindsWildcardCharacters()
    {
        await LoginAsync(TestData.Admin);
        var id = Guid.NewGuid().ToString("N")[..6];
        foreach (var key in new[] { $"{id} 50%_off\\now", $"{id} 50x off\\now" })
            Assert.That((await ApiAsync("POST", "/api/test/jobs", new { key })).Status, Is.EqualTo(200));

        var found = await GetJsonAsync($"/api/jobs?search={Uri.EscapeDataString("50%_off\\now")}&pageSize=100");

        Assert.That(found.GetProperty("items").EnumerateArray().Select(j => j.GetProperty("idempotencyKey").GetString()),
            Is.EqualTo(new[] { $"test:{id} 50%_off\\now" }));
    }

    [Test]
    public async Task Lists_TypedFilters_RejectUnknownValues()
    {
        await LoginAsync(TestData.Admin);

        var created = await GetJsonAsync("/api/audit-logs?type=create&pageSize=100");
        Assert.That(created.GetProperty("items").EnumerateArray().Select(l => l.GetProperty("type").GetString()), Is.All.EqualTo("Create"));
        Assert.That((await ApiAsync("GET", "/api/audit-logs?type=Bogus")).Status, Is.EqualTo(400));

        var locked = await GetJsonAsync("/api/users?status=locked&pageSize=100");
        Assert.That(locked.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("email").GetString()), Does.Contain(TestData.Locked));
        Assert.That(locked.GetProperty("items").EnumerateArray().Select(u => u.GetProperty("isLockedOut").GetBoolean()), Is.All.True);
        Assert.That((await ApiAsync("GET", "/api/users?status=sleeping")).Status, Is.EqualTo(400));

        Assert.That((await GetJsonAsync("/api/jobs?status=Failed")).GetProperty("pageSize").GetInt32(), Is.EqualTo(25));
        Assert.That((await ApiAsync("GET", "/api/jobs?status=Exploded")).Status, Is.EqualTo(400));
    }

    private static List<int> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32()).ToList();
}
