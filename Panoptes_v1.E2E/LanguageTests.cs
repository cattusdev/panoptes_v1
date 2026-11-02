using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Γλώσσες: προτίμηση χρήστη (My account), προεπιλογή εφαρμογής (Settings → General), επιλογή επισκέπτη (footer),
// μηνύματα του server και emails στη γλώσσα του παραλήπτη
public class LanguageTests : MediaTest
{
    private static readonly Regex Greek = new("[Α-Ωα-ωά-ώ]");

    [TearDown]
    public async Task ResetLanguagesAsync()
    {
        var admin = await SecondUserAsync(TestData.Admin);
        var settings = await GetJsonAsync(admin, "/api/settings/general");
        var reset = await ApiAsync(admin, "PUT", "/api/settings/general", new
        {
            defaultLanguage = "en",
            dateFormat = settings.GetProperty("dateFormat").GetString(),
            timeFormat = settings.GetProperty("timeFormat").GetString(),
            timeZone = (string?)null,
        });
        var adminLanguage = await ApiAsync(admin, "PUT", "/api/account/language", new { language = (string?)null });
        var greek = await SecondUserAsync(TestData.GreekUser);
        var greekLanguage = await ApiAsync(greek, "PUT", "/api/account/language", new { language = (string?)null });
        Assert.That((reset.Status, adminLanguage.Status, greekLanguage.Status), Is.EqualTo((200, 204, 204)), "Language reset");
    }

    [Test]
    public async Task UserLanguage_AppliesToTheUi_TheServer_AndEmails()
    {
        await LoginAsync(TestData.GreekUser);
        await Page.GotoAsync("/account");
        await SelectOptionAsync(Page.Locator("#account-language"), "Ελληνικά");

        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "el");
        await Page.GetByTestId("user-menu").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Αποσύνδεση" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Escape");

        // Αποθηκεύεται στο προφίλ: ισχύει και μετά από reload
        Assert.That((await GetJsonAsync("/api/auth/session")).GetProperty("user").GetProperty("language").GetString(), Is.EqualTo("el"));
        await Page.ReloadAsync();
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "el");

        // Τα μηνύματα του server έρχονται στη γλώσσα του request (ο client στέλνει Accept-Language)
        var headers = await CsrfHeadersAsync(Page);
        headers["Accept-Language"] = "el";
        var invalid = await Page.APIRequest.FetchAsync("/api/account/language", new() { Method = "PUT", DataObject = new { language = "xx" }, Headers = headers });
        Assert.That(invalid.Status, Is.EqualTo(400));
        Assert.That(await invalid.TextAsync(), Does.Contain("Επιλέξτε μια γλώσσα από τη λίστα."));

        // Τα emails πάνε στη γλώσσα του παραλήπτη, ακόμα κι αν το ζήτησε κάποιος που βλέπει αγγλικά
        var visitor = await (await Browser.NewContextAsync(ContextOptions())).NewPageAsync();
        await ApiAsync(visitor, "GET", "/api/auth/session"); // Το antiforgery cookie
        var resend = await ApiAsync(visitor, "POST", "/api/account/resend-confirmation", new { email = TestData.GreekUnconfirmed });
        Assert.That(resend.Ok, Is.True, $"resend-confirmation: {resend.Status} {await resend.TextAsync()}");
        var mail = await Mailbox.WaitForAsync(TestData.GreekUnconfirmed, "");
        Assert.That(mail.Subject, Does.Match(Greek), $"Subject: {mail.Subject}");
    }

    [Test]
    public async Task AppDefaultLanguage_AppliesToVisitors_WhoCanSwitch()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings");
        await SelectOptionAsync(Page.Locator("#default-language"), "Ελληνικά");
        await Page.Locator("form", new() { Has = Page.Locator("#default-language") }).GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Language settings saved");

        // Ο admin δεν έχει δική του προτίμηση: βλέπει πλέον την προεπιλογή, και οι μήνες γράφονται ελληνικά
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Ρυθμίσεις", Level = 1 })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("date-preview")).ToContainTextAsync(new Regex("[Α-Ω][α-ω]{2}"));

        // Νέος επισκέπτης: ελληνικά
        var visitor = await (await Browser.NewContextAsync(ContextOptions())).NewPageAsync();
        await visitor.GotoAsync("/login");
        await Expect(visitor.Locator("html")).ToHaveAttributeAsync("lang", "el");
        await Expect(visitor.GetByText("Μία κονσόλα. Πλήρης έλεγχος.")).ToBeVisibleAsync();

        // ...που μπορεί να διαλέξει αγγλικά από το footer (μένει και μετά από reload)
        await visitor.GetByTestId("language-switcher").ClickAsync();
        await visitor.GetByRole(AriaRole.Option, new() { Name = "English", Exact = true }).ClickAsync();
        await Expect(visitor.GetByText("One console. Full control.")).ToBeVisibleAsync();
        await visitor.ReloadAsync();
        await Expect(visitor.GetByText("One console. Full control.")).ToBeVisibleAsync();
        await Expect(visitor.Locator("html")).ToHaveAttributeAsync("lang", "en");
    }

    private static async Task<System.Text.Json.JsonElement> GetJsonAsync(IPage page, string url)
    {
        var response = await ApiAsync(page, "GET", url);
        Assert.That(response.Status, Is.EqualTo(200), $"GET {url}");
        return (await response.JsonAsync())!.Value;
    }
}
