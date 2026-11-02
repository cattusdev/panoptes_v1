using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;

namespace Panoptes_v1.E2E;

// Βάση για όλα τα browser tests: κάθε test παίρνει καθαρό browser context (χωρίς cookies)
public abstract class E2ETest : PageTest
{
    public override BrowserNewContextOptions ContextOptions() => new() { BaseURL = AppFixture.BaseUrl };

    protected Task LoginAsync(string email, string password = TestData.Password) => LoginAsync(Page, email, password);

    // Login μέσα από τη React σελίδα /login (POST /api/auth/login)
    protected static async Task LoginAsync(IPage page, string email, string password = TestData.Password, string? returnUrl = null)
    {
        if (returnUrl == null || !page.Url.Contains("/login"))
            await page.GotoAsync(returnUrl == null ? "/login" : $"/login?returnUrl={Uri.EscapeDataString(returnUrl)}");

        await page.FillAsync("#email", email);
        await page.FillAsync("#password", password);

        var loginResponse = page.WaitForResponseAsync(r => r.Request.Method == "POST" && r.Url.Contains("/api/auth/login"));
        await page.ClickAsync("#login-submit");
        await loginResponse;
        await page.WaitForLoadStateAsync();
    }

    // Η React σελίδα δείχνει τα λάθη σε shadcn Alert (role="alert")
    protected ILocator AppAlert => Page.GetByRole(AriaRole.Alert);

    // "Χειροποίητα" API calls (όπως θα έκανε κάποιος που παρακάμπτει το UI) με τα cookies του τρέχοντος χρήστη.
    // withCsrf: στέλνει το X-XSRF-TOKEN header όπως το React app.
    protected Task<IAPIResponse> ApiAsync(string method, string url, object? body = null, bool withCsrf = true) =>
        ApiAsync(Page, method, url, body, withCsrf);

    protected static async Task<IAPIResponse> ApiAsync(IPage page, string method, string url, object? body = null, bool withCsrf = true)
    {
        var headers = withCsrf ? await CsrfHeadersAsync(page) : new Dictionary<string, string>();
        return await page.APIRequest.FetchAsync(url, new() { Method = method, DataObject = body, Headers = headers });
    }

    protected static async Task<Dictionary<string, string>> CsrfHeadersAsync(IPage page)
    {
        var headers = new Dictionary<string, string>();
        var token = (await page.Context.CookiesAsync()).FirstOrDefault(c => c.Name == "XSRF-TOKEN")?.Value;
        if (token != null) headers["X-XSRF-TOKEN"] = Uri.UnescapeDataString(token);
        return headers;
    }

    // Ειδοποιήσεις (sonner toasts)
    protected Task ExpectToastAsync(string text) =>
        Expect(Page.Locator("[data-sonner-toast]", new() { HasText = text }).First).ToBeVisibleAsync();

    protected ILocator Row(string name) => Page.GetByRole(AriaRole.Row, new() { Name = name, Exact = true });

    // Επιλογή σε shadcn (Radix) Select
    protected async Task SelectOptionAsync(ILocator trigger, string option)
    {
        await trigger.ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = option, Exact = true }).ClickAsync();
    }
}
