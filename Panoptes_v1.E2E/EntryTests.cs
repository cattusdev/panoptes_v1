using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Χωρίς δημόσιο site (ARCHITECTURE §16 Q3, §16.1): το "/" πάει στη σύνδεση ή στο dashboard, και δεν υπάρχει
// ούτε εγγραφή ούτε "Ξέχασα τον κωδικό". Λογαριασμούς και νέους κωδικούς δίνει ο διαχειριστής.
public class EntryTests : E2ETest
{
    [Test]
    public async Task Root_ForVisitors_GoesToSignIn()
    {
        await Page.GotoAsync("/");

        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sign in" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task Root_ForAnAdmin_GoesToTheDashboard()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/");

        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));
    }

    [Test]
    public async Task Root_ForAUserWithoutAdminAccess_GoesToTheirAccount()
    {
        await LoginAsync(TestData.NoAccess);
        await Page.GotoAsync("/");

        await Expect(Page).ToHaveURLAsync(new Regex("/account$"));
    }

    [Test]
    public async Task SignIn_HasNoSelfService_AndPointsToTheAdministrator()
    {
        await Page.GotoAsync("/login");

        await Expect(Page.GetByText("Need an account or a new password? Contact your administrator.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Forgot password?" })).ToHaveCountAsync(0);
        await Expect(Page.Locator("a[href^='/register'], a[href^='/forgot-password'], a[href^='/privacy']")).ToHaveCountAsync(0);
    }

    [TestCase("/register")]
    [TestCase("/forgot-password")]
    [TestCase("/reset-password?email=noaccess@e2e.test&code=abc")]
    [TestCase("/privacy")]
    [TestCase("/Home/Privacy")]
    public async Task RemovedPages_AreNotFound(string path)
    {
        await Page.GotoAsync(path);

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Page not found" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task RemovedEndpoints_Return404_AndDoNothing()
    {
        const string newcomer = "selfservice@e2e.test";
        await ApiAsync("GET", "/api/auth/session"); // Το antiforgery cookie

        var register = await ApiAsync("POST", "/api/account/register", new { email = newcomer, password = TestData.Password });
        var forgot = await ApiAsync("POST", "/api/account/forgot-password", new { email = TestData.NoAccess });
        var reset = await ApiAsync("POST", "/api/account/reset-password", new { email = TestData.NoAccess, code = "abc", password = "New-Passw0rd!" });

        Assert.That((register.Status, forgot.Status, reset.Status), Is.EqualTo((404, 404, 404)));
        Assert.That(await TestData.FindUserAsync(newcomer), Is.Null);
        Assert.That(Mailbox.For(TestData.NoAccess), Is.Empty);
    }

    [Test]
    public async Task OldIdentityUrls_ForRemovedPages_GoToSignIn()
    {
        await Page.GotoAsync("/Identity/Account/Register");
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));

        await Page.GotoAsync("/Identity/Account/ForgotPassword");
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
    }

    [Test]
    public async Task UnknownPage_ShowsNotFound()
    {
        await Page.GotoAsync("/this/does/not/exist");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Page not found" })).ToBeVisibleAsync();
    }
}

// Ο GlobalExceptionHandler: κάθε crash καταλήγει σε κάτι κατανοητό για τον χρήστη, με reference για το log
public class ErrorHandlingTests : E2ETest
{
    [Test]
    public async Task ApiCrash_ReturnsProblemDetails_WithAReference()
    {
        var response = await Page.APIRequest.GetAsync("/api/test/crash");

        Assert.That(response.Status, Is.EqualTo(500));
        var body = await response.JsonAsync();
        Assert.That(body?.GetProperty("detail").GetString(), Does.Contain("Reference:"));
        Assert.That(body?.GetProperty("traceId").GetString(), Is.Not.Empty);
        Assert.That(await response.TextAsync(), Does.Not.Contain("Simulated crash"), "Internal details must not leak to the client.");
    }

    [Test]
    public async Task PageCrash_ShowsAFriendlyErrorPage()
    {
        var response = await Page.GotoAsync("/test/crash");

        Assert.That(response!.Status, Is.EqualTo(500));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Something went wrong" })).ToBeVisibleAsync();
        await Expect(Page.Locator("code")).Not.ToBeEmptyAsync();
        await Expect(Page.Locator("body")).Not.ToContainTextAsync("Simulated crash");
    }

    [Test]
    public async Task UnknownApiRoute_Returns404_NotTheApp()
    {
        var response = await Page.APIRequest.GetAsync("/api/does-not-exist");

        Assert.That(response.Status, Is.EqualTo(404));
        Assert.That(await response.TextAsync(), Does.Not.Contain("<div id=\"root\">"));
    }
}
