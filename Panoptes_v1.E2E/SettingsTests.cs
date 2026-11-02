using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using Panoptes_v1.Data;

namespace Panoptes_v1.E2E;

// Settings: ενότητες (General / Branding / Email) σε ένα layout, μορφή ημερομηνίας παντού, SMTP με κρυπτογραφημένο password
public class SettingsTests : MediaTest
{
    private const string SmtpPassword = "Smtp-S3cret-Value!";
    private static readonly Regex IsoTwelveHour = new(@"^\d{4}-\d{2}-\d{2} \d{1,2}:\d{2} (AM|PM)$");

    private ILocator SectionsNav => Page.GetByRole(AriaRole.Navigation, new() { Name = "Settings sections" });

    [TearDown]
    public async Task ResetSettingsAsync()
    {
        var admin = await SecondUserAsync(TestData.Admin);
        var general = await ApiAsync(admin, "PUT", "/api/settings/general", new { defaultLanguage = "en", dateFormat = "d MMM yyyy", timeFormat = "24h", timeZone = (string?)null });
        var email = await ApiAsync(admin, "PUT", "/api/settings/email", new
        {
            enabled = false, host = "", port = 587, security = "StartTls", username = "", senderName = "", senderEmail = "",
            password = (string?)null, clearPassword = true,
        });
        Assert.That((general.Status, email.Status), Is.EqualTo((200, 200)), "Settings reset");
    }

    [Test]
    public async Task Settings_SectionsShareOneLayout()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin");
        var sidebarLink = Page.Locator("nav[aria-label='Main navigation'] a[data-title='Settings']");
        await sidebarLink.ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/settings$"));
        await Expect(SectionsNav.GetByRole(AriaRole.Link, new() { Name = "General" })).ToHaveAttributeAsync("aria-current", "page");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "General", Level = 2 })).ToBeVisibleAsync();

        // Το παλιό URL του branding είναι πλέον ενότητα του Settings
        await SectionsNav.GetByRole(AriaRole.Link, new() { Name = "Branding" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/settings/branding$"));
        await Expect(Page.Locator("#app-name")).ToBeVisibleAsync();
        await Expect(sidebarLink).ToHaveAttributeAsync("aria-current", "page");

        await SectionsNav.GetByRole(AriaRole.Link, new() { Name = "Email" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Email", Level = 2 })).ToBeVisibleAsync();
        // Στα tests τα emails γράφονται σε φάκελο (Email:PickupDirectory)
        await Expect(Page.GetByTestId("email-status")).ToContainTextAsync("Saved as files");

        // Κινητό: οι ενότητες γίνονται μία οριζόντια σειρά
        await Page.SetViewportSizeAsync(390, 844);
        var general = await SectionsNav.GetByRole(AriaRole.Link, new() { Name = "General" }).BoundingBoxAsync();
        var email = await SectionsNav.GetByRole(AriaRole.Link, new() { Name = "Email" }).BoundingBoxAsync();
        Assert.That(email!.Y, Is.EqualTo(general!.Y).Within(2));
        Assert.That(email.X, Is.GreaterThan(general.X));
    }

    [Test]
    public async Task DateFormat_AppliesAcrossTheApp()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings");

        await Page.Locator("#date-format").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex("ISO 8601") }).ClickAsync();
        await Page.GetByRole(AriaRole.Radio, new() { Name = "12-hour" }).ClickAsync();
        await Page.Locator("#time-zone").ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { NameRegex = new Regex("^UTC ·") }).ClickAsync();

        // Ζωντανή προεπισκόπηση πριν την αποθήκευση
        await Expect(Page.GetByTestId("date-preview")).ToContainTextAsync(new Regex(@"\d{4}-\d{2}-\d{2} \d{1,2}:\d{2} (AM|PM)"));
        // Κάθε κάρτα έχει τη δική της αποθήκευση (Language, Date & time)
        await Page.Locator("form", new() { Has = Page.Locator("#date-format") }).GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Date & time settings saved");

        var saved = await GetJsonAsync("/api/settings/general");
        Assert.That(saved.GetProperty("dateFormat").GetString(), Is.EqualTo("yyyy-MM-dd"));
        Assert.That(saved.GetProperty("timeFormat").GetString(), Is.EqualTo("12h"));
        Assert.That(saved.GetProperty("timeZone").GetString(), Is.EqualTo("UTC"));

        // Οι ημερομηνίες στο audit log (σχετικές, με την ακριβή ώρα στο tooltip) ακολουθούν τη ρύθμιση
        await Page.GotoAsync("/admin/audit-logs");
        var time = Page.Locator("table time").First;
        await Expect(time).ToHaveAttributeAsync("title", IsoTwelveHour);
        var dateTime = await time.GetAttributeAsync("datetime");
        var expected = DateTime.Parse(dateTime!, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal)
            .ToString("yyyy-MM-dd h:mm tt", System.Globalization.CultureInfo.InvariantCulture);
        await Expect(time).ToHaveAttributeAsync("title", expected);

        // Μόνο οι διαθέσιμες μορφές και έγκυρες ζώνες ώρας
        var badFormat = await ApiAsync("PUT", "/api/settings/general", new { defaultLanguage = "en", dateFormat = "yyyy", timeFormat = "24h", timeZone = (string?)null });
        Assert.That(badFormat.Status, Is.EqualTo(400));
        var badZone = await ApiAsync("PUT", "/api/settings/general", new { defaultLanguage = "en", dateFormat = "yyyy-MM-dd", timeFormat = "24h", timeZone = "../etc" });
        Assert.That(badZone.Status, Is.EqualTo(400));
    }

    [Test]
    public async Task Email_PasswordIsWriteOnly_AndEncrypted()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings/email");
        await FillSmtpFormAsync();
        await Page.FillAsync("#smtp-password", SmtpPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Email settings saved");
        await Expect(Page.GetByText("A password is saved")).ToBeVisibleAsync();
        await Expect(Page.Locator("#smtp-password")).ToHaveValueAsync("");

        // Το password δεν επιστρέφει ποτέ στο browser
        var response = await ApiAsync("GET", "/api/settings/email");
        var body = await response.TextAsync();
        Assert.That(body, Does.Not.Contain(SmtpPassword));
        var json = (await response.JsonAsync())!.Value;
        Assert.That(json.GetProperty("hasPassword").GetBoolean(), Is.True);
        Assert.That(json.TryGetProperty("password", out _), Is.False);
        Assert.That(json.GetProperty("host").GetString(), Is.EqualTo("localhost"));

        // Στη βάση: κρυπτογραφημένο, και ποτέ στο audit log
        var (secret, audits) = await ReadEmailRowAsync();
        Assert.That(secret, Is.Not.Null.And.Not.Contains(SmtpPassword));
        Assert.That(audits, Is.Not.Empty);
        Assert.That(audits, Has.None.Contains(SmtpPassword));
        Assert.That(audits, Has.None.Contains(secret!));

        // Αποθήκευση χωρίς password = κρατάμε το υπάρχον
        await Page.FillAsync("#smtp-sender-name", "Panoptes_v1 Mailer");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Email settings saved");
        Assert.That((await ReadEmailRowAsync()).Secret, Is.EqualTo(secret));

        // "Remove it" σβήνει το password στην επόμενη αποθήκευση
        await Page.GetByRole(AriaRole.Button, new() { Name = "Remove it" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Email settings saved");
        Assert.That((await GetJsonAsync("/api/settings/email")).GetProperty("hasPassword").GetBoolean(), Is.False);
    }

    [Test]
    public async Task Email_TestEmail_UsesTheFormValues()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/settings/email");
        await FillSmtpFormAsync();

        // Χωρίς αποθήκευση: η δοκιμή στέλνει με τις τιμές της φόρμας
        await Expect(Page.Locator("#test-email-to")).ToHaveValueAsync(TestData.Admin);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send test email" }).ClickAsync();
        await ExpectToastAsync($"Test email sent to {TestData.Admin}");
        var mail = await Mailbox.WaitForAsync(TestData.Admin, "Test email from");
        Assert.That(mail.Html, Does.Contain("localhost:1025"));

        // Χωρίς host: λάθος στο πεδίο
        var invalid = await ApiAsync("POST", "/api/settings/email/test", new { enabled = true, host = "", port = 25, security = "None", senderEmail = "noreply@e2e.test" });
        Assert.That(invalid.Status, Is.EqualTo(400));
        Assert.That(await invalid.TextAsync(), Does.Contain("Enter the SMTP host"));
    }

    [Test]
    public async Task Settings_ReadOnlyWithoutEditPermission()
    {
        var anonymous = await AnonymousAsync();
        Assert.That((await anonymous.GetAsync("/api/settings/general")).Status, Is.EqualTo(200));
        Assert.That((await anonymous.GetAsync("/api/settings/email")).Status, Is.EqualTo(401));

        await LoginAsync(TestData.SettingsViewer);
        Assert.That((await ApiAsync("GET", "/api/settings/email")).Status, Is.EqualTo(200));
        Assert.That((await ApiAsync("PUT", "/api/settings/general", new { dateFormat = "yyyy-MM-dd", timeFormat = "24h" })).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("PUT", "/api/settings/email", new { enabled = false })).Status, Is.EqualTo(403));
        Assert.That((await ApiAsync("POST", "/api/settings/email/test", new { host = "localhost", senderEmail = "a@e2e.test" })).Status, Is.EqualTo(403));

        await Page.GotoAsync("/admin/settings");
        await Expect(Page.GetByText("only people with the Settings · Edit permission can change them")).ToBeVisibleAsync();
        await Expect(Page.Locator("#date-format")).ToBeDisabledAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveCountAsync(0);

        await SectionsNav.GetByRole(AriaRole.Link, new() { Name = "Email" }).ClickAsync();
        await Expect(Page.Locator("#smtp-enabled")).ToBeDisabledAsync();

        var noAccess = await SecondUserAsync(TestData.NoAccess);
        Assert.That((await ApiAsync(noAccess, "GET", "/api/settings/email")).Status, Is.EqualTo(403));
    }

    private async Task FillSmtpFormAsync()
    {
        await Page.GetByRole(AriaRole.Switch, new() { Name = "Use a custom SMTP server" }).ClickAsync();
        await Page.FillAsync("#smtp-sender-email", "noreply@e2e.test");
        await Page.FillAsync("#smtp-sender-name", "Panoptes_v1 E2E");
        await Page.FillAsync("#smtp-host", "localhost");
        await Page.FillAsync("#smtp-port", "1025");
        await SelectOptionAsync(Page.Locator("#smtp-security"), "None (local servers only)");
        await Page.FillAsync("#smtp-username", "mailer");
    }

    private static Task<(string? Secret, List<string> Audits)> ReadEmailRowAsync() =>
        TestData.WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var row = await db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == "email");
            var audits = await db.AuditLogs.AsNoTracking()
                .Where(a => a.TableName == "AppSettings")
                .Select(a => (a.OldValues ?? "") + (a.NewValues ?? ""))
                .ToListAsync();
            return (row.Secret, audits);
        });
}
