namespace Panoptes_v1.E2E;

// Οπτικός έλεγχος: τρέχει μόνο όταν ζητηθεί ρητά
//   dotnet test Panoptes_v1.E2E --filter "Category=Screenshots"
// Οι εικόνες γράφονται στο Panoptes_v1.E2E/TestResults/screenshots
[Explicit, Category("Screenshots")]
public class ScreenshotTests : E2ETest
{
    private static string OutputDir => Path.Combine(AppFixture.SolutionDir, "Panoptes_v1.E2E", "TestResults", "screenshots");

    public override Microsoft.Playwright.BrowserNewContextOptions ContextOptions() => new()
    {
        BaseURL = AppFixture.BaseUrl,
        ViewportSize = new() { Width = 1440, Height = 900 },
    };

    [Test]
    public async Task AuthPages()
    {
        await Capture("/login", "02-login");

        // Login με λάθος κωδικό για να φανεί το μήνυμα λάθους
        await LoginAsync(TestData.NoAccess, "Wrong-Passw0rd!");
        await Shot("03-login-error");

        await Capture("/lockout", "06-lockout");
    }

    [Test]
    public async Task AdminPages()
    {
        await LoginAsync(TestData.Admin);
        await Capture("/admin", "10-dashboard", fullPage: true);
        await Capture("/admin/users", "11-users");
        await Capture("/admin/roles", "12-roles");
        await Capture($"/admin/users/{TestData.UserIds[TestData.Target]}", "13-user-edit");
        await Capture($"/admin/roles/{TestData.RoleIds["Support"]}", "14-permissions");
        await Capture("/admin/audit-logs", "15-audit-logs");
        await Capture("/account", "16-profile");
        await Capture("/account/security", "18-security");
        await Capture("/admin/settings", "19-settings-general");
        await Capture("/admin/settings/email", "19-settings-email");
        await Page.GetByRole(Microsoft.Playwright.AriaRole.Switch, new() { Name = "Use a custom SMTP server" }).ClickAsync();
        await Shot("19-settings-email-custom", fullPage: true);

        await Page.SetViewportSizeAsync(390, 844);
        await Capture("/admin", "17-dashboard-mobile", fullPage: true);
    }

    // Τα ίδια στα ελληνικά (η γλώσσα του admin, μετά επιστρέφει στην προεπιλογή)
    [Test]
    public async Task GreekPages()
    {
        await LoginAsync(TestData.Admin);
        await ApiAsync("PUT", "/api/account/language", new { language = "el" });
        try
        {
            await Capture("/admin", "30-el-dashboard", fullPage: true);
            await Capture("/admin/users", "31-el-users");
            await Capture($"/admin/roles/{TestData.RoleIds["Support"]}", "32-el-permissions");
            await Capture("/admin/media", "33-el-media");
            await Capture("/admin/settings", "34-el-settings-general");
            await Capture("/account", "35-el-profile");
            await Capture("/account/security", "36-el-security");
        }
        finally
        {
            await ApiAsync("PUT", "/api/account/language", new { language = (string?)null });
        }

        var visitor = await (await Browser.NewContextAsync(ContextOptions())).NewPageAsync();
        await visitor.GotoAsync("/login");
        await visitor.EvaluateAsync("() => localStorage.setItem('panoptes-v1.language.choice', 'el')");
        await visitor.GotoAsync("/login");
        await visitor.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
        Directory.CreateDirectory(OutputDir);
        await visitor.ScreenshotAsync(new() { Path = Path.Combine(OutputDir, "37-el-login.png") });
    }

    [Test]
    public async Task AccessDenied()
    {
        await LoginAsync(TestData.NoAccess);
        await Capture("/admin", "20-access-denied");
    }

    private async Task Capture(string url, string name, bool fullPage = false)
    {
        await Page.GotoAsync(url);
        await Page.WaitForLoadStateAsync(Microsoft.Playwright.LoadState.NetworkIdle);
        await Shot(name, fullPage);
    }

    private async Task Shot(string name, bool fullPage = false)
    {
        Directory.CreateDirectory(OutputDir);
        await Page.ScreenshotAsync(new() { Path = Path.Combine(OutputDir, $"{name}.png"), FullPage = fullPage });
    }
}
