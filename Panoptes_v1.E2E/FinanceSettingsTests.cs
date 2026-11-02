using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Settings → Finance: ΦΠΑ 24% και έναρξη καταγραφής 2026-12-01 (ARCHITECTURE Q4/Q5), αλλάζουν μόνο με Finance.Settings,
// και η ημερομηνία έναρξης μόνο με αιτιολογία που μένει στο audit log
public class FinanceSettingsTests : MediaTest
{
    private ILocator VatForm => Page.Locator("form", new() { Has = Page.Locator("#default-vat-rate") });
    private ILocator TrackingForm => Page.Locator("form", new() { Has = Page.Locator("#tracking-start-date") });

    [TearDown]
    public async Task ResetFinanceSettingsAsync()
    {
        var admin = await SecondUserAsync(TestData.Admin);
        var reset = await ApiAsync(admin, "PUT", "/api/settings/finance", new { defaultVatRate = 24, trackingStartDate = "2026-12-01", reason = "E2E reset" });
        Assert.That(reset.Status, Is.EqualTo(200), "Finance settings reset");
    }

    [Test]
    public async Task FinanceSection_ShowsTheApprovedDefaults()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings");
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings sections" }).GetByRole(AriaRole.Link, new() { Name = "Finance" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/settings/finance$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Finance", Level = 2 })).ToBeVisibleAsync();
        await Expect(Page.Locator("#default-vat-rate")).ToHaveValueAsync("24");
        await Expect(Page.Locator("#tracking-start-date")).ToHaveTextAsync("1 Dec 2026");
        // Χωρίς αλλαγές δεν υπάρχει τίποτα να αποθηκευτεί
        await Expect(VatForm.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToBeDisabledAsync();

        var saved = await GetJsonAsync("/api/settings/finance");
        Assert.That(saved.GetProperty("defaultVatRate").GetDecimal(), Is.EqualTo(24m));
        Assert.That(saved.GetProperty("trackingStartDate").GetString(), Is.EqualTo("2026-12-01"));
    }

    [Test]
    public async Task VatRate_SavesFromTheForm_AndRejectsInvalidRates()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings/finance");
        var rate = Page.Locator("#default-vat-rate");
        var save = VatForm.GetByRole(AriaRole.Button, new() { Name = "Save changes" });

        await rate.FillAsync("abc");
        await rate.BlurAsync();
        await Expect(VatForm.GetByText("Enter a rate, e.g. 24 or 13.5.")).ToBeVisibleAsync();

        await rate.FillAsync("150");
        await save.ClickAsync();
        await Expect(VatForm.GetByText("Enter 100 or less.")).ToBeVisibleAsync();

        await rate.FillAsync("13.5");
        await save.ClickAsync();
        await ExpectToastAsync("VAT rate saved");
        await Expect(Page.GetByText("New charge lines start at 13.5%.")).ToBeVisibleAsync();
        Assert.That((await GetJsonAsync("/api/settings/finance")).GetProperty("defaultVatRate").GetDecimal(), Is.EqualTo(13.5m));

        // Ο server ελέγχει ούτως ή άλλως (όρια, έως δύο δεκαδικά)
        var tooHigh = await ApiAsync("PUT", "/api/settings/finance", new { defaultVatRate = 150, trackingStartDate = "2026-12-01" });
        Assert.That(await ErrorAsync(tooHigh, "DefaultVatRate"), Is.EqualTo("Enter a VAT rate from 0 to 100."));
        var tooPrecise = await ApiAsync("PUT", "/api/settings/finance", new { defaultVatRate = 24.555m, trackingStartDate = "2026-12-01" });
        Assert.That(await ErrorAsync(tooPrecise, "DefaultVatRate"), Is.EqualTo("Use at most two decimals."));
    }

    [Test]
    public async Task TrackingStart_ChangesOnlyWithAReason_KeptInTheAuditLog()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings/finance");
        await Expect(Page.Locator("#tracking-start-reason")).ToHaveCountAsync(0);

        await Page.Locator("#tracking-start-date").ClickAsync();
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Today", Exact = true }).ClickAsync();

        // Μόλις αλλάξει η ημερομηνία: προειδοποίηση για τις συνέπειες και υποχρεωτική αιτιολογία
        await Expect(TrackingForm.GetByText("This moves the line between history and live records")).ToBeVisibleAsync();
        await TrackingForm.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(TrackingForm.GetByText("Say why the tracking start date changes.")).ToBeVisibleAsync();
        await Expect(Page.Locator("#tracking-start-reason")).ToBeFocusedAsync();

        var reason = $"Started recording earlier {Guid.NewGuid():N}"[..36];
        await Page.Locator("#tracking-start-reason").FillAsync(reason);
        await TrackingForm.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Tracking start date saved");
        // Μετά την αποθήκευση η φόρμα ξεκινά από τη νέα τιμή: χωρίς προειδοποίηση και αιτιολογία
        await Expect(Page.Locator("#tracking-start-reason")).ToHaveCountAsync(0);

        var today = DateTime.Today.ToString("yyyy-MM-dd");
        Assert.That((await GetJsonAsync("/api/settings/finance")).GetProperty("trackingStartDate").GetString(), Is.EqualTo(today));

        var logs = await GetJsonAsync("/api/audit-logs?search=AppSettings&pageSize=100");
        var entry = logs.GetProperty("items").EnumerateArray().First(e =>
            e.GetProperty("changes").EnumerateArray().Any(c => c.GetProperty("newValue").GetString()?.Contains(reason) == true));
        Assert.That(entry.GetProperty("userEmail").GetString(), Is.EqualTo(TestData.Admin));

        // Χωρίς αιτιολογία ο server αρνείται· ίδια ημερομηνία (μόνο ΦΠΑ) δεν χρειάζεται
        var noReason = await ApiAsync("PUT", "/api/settings/finance", new { defaultVatRate = 24, trackingStartDate = "2027-01-01", reason = "  " });
        Assert.That(await ErrorAsync(noReason, "Reason"), Is.EqualTo("Say why the tracking start date changes."));
        var sameDate = await ApiAsync("PUT", "/api/settings/finance", new { defaultVatRate = 23, trackingStartDate = today });
        Assert.That(sameDate.Status, Is.EqualTo(200));
    }

    [Test]
    public async Task FinanceSettings_ChangeOnlyWithFinanceSettingsPermission()
    {
        var body = new { defaultVatRate = 22, trackingStartDate = "2026-12-01" };

        // Settings · View: βλέπει, δεν αλλάζει
        await LoginAsync(TestData.SettingsViewer);
        await Page.GotoAsync("/admin/settings/finance");
        await Expect(Page.GetByText("only people with the Finance · Settings permission can change them")).ToBeVisibleAsync();
        await Expect(Page.Locator("#default-vat-rate")).ToBeDisabledAsync();
        await Expect(Page.Locator("#tracking-start-date")).ToBeDisabledAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveCountAsync(0);
        Assert.That((await ApiAsync("PUT", "/api/settings/finance", body)).Status, Is.EqualTo(403));

        // Settings · Edit δεν αρκεί για τα οικονομικά
        var editor = await SecondUserAsync(TestData.SettingsEditor);
        Assert.That((await ApiAsync(editor, "PUT", "/api/settings/finance", body)).Status, Is.EqualTo(403));

        // Χωρίς κανένα από τα δύο: ούτε ανάγνωση
        var outsider = await SecondUserAsync(TestData.NoAccess);
        Assert.That((await ApiAsync(outsider, "GET", "/api/settings/finance")).Status, Is.EqualTo(403));

        // Finance · Settings: αλλάζει από τη φόρμα
        var finance = await SecondUserAsync(TestData.FinanceSettings);
        await finance.GotoAsync("/admin/settings/finance");
        await finance.Locator("#default-vat-rate").FillAsync("22");
        await finance.Locator("form", new() { Has = finance.Locator("#default-vat-rate") }).GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(finance.Locator("[data-sonner-toast]", new() { HasText = "VAT rate saved" })).ToBeVisibleAsync();
        Assert.That((await GetJsonAsync("/api/settings/finance")).GetProperty("defaultVatRate").GetDecimal(), Is.EqualTo(22m));
    }

    [Test]
    public async Task FinanceSection_IsTranslated()
    {
        await LoginAsync(TestData.Admin);
        await ApiAsync("PUT", "/api/account/language", new { language = "el" });
        try
        {
            await Page.GotoAsync("/admin/settings/finance");
            await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Οικονομικά", Level = 2 })).ToBeVisibleAsync();
            await Expect(Page.GetByText("Προεπιλεγμένος συντελεστής ΦΠΑ")).ToBeVisibleAsync();

            // Τα μηνύματα του server στη γλώσσα του request (ο client στέλνει Accept-Language)
            var headers = await CsrfHeadersAsync(Page);
            headers["Accept-Language"] = "el";
            var invalid = await Page.APIRequest.PutAsync("/api/settings/finance", new()
            {
                DataObject = new { defaultVatRate = 150, trackingStartDate = "2026-12-01" },
                Headers = headers,
            });
            Assert.That(await ErrorAsync(invalid, "DefaultVatRate"), Is.EqualTo("Εισαγάγετε συντελεστή ΦΠΑ από 0 έως 100."));
        }
        finally
        {
            await ApiAsync("PUT", "/api/account/language", new { language = (string?)null });
        }
    }

    private static async Task<string?> ErrorAsync(IAPIResponse response, string field)
    {
        Assert.That(response.Status, Is.EqualTo(400), await response.TextAsync());
        var problem = (await response.JsonAsync())!.Value;
        return problem.GetProperty("errors").TryGetProperty(field, out var messages) ? messages[0].GetString() : null;
    }
}
