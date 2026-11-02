using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

public class PublicSiteTests : E2ETest
{
    private ILocator Header => Page.GetByRole(AriaRole.Banner);

    [Test]
    public async Task Home_ForVisitors_LeadsToSignUp()
    {
        await Page.GotoAsync("/");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Manage your enterprise" })).ToBeVisibleAsync();
        await Expect(Header.GetByRole(AriaRole.Link, new() { Name = "Sign in" })).ToBeVisibleAsync();

        await Header.GetByRole(AriaRole.Link, new() { Name = "Get started" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/register$"));
    }

    [Test]
    public async Task Home_ForAnAdmin_LinksToTheDashboard()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/");

        await Header.GetByRole(AriaRole.Link, new() { Name = "Dashboard" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));
    }

    [Test]
    public async Task Home_ForAUserWithoutAdminAccess_LinksToTheirAccount()
    {
        await LoginAsync(TestData.NoAccess);
        await Page.GotoAsync("/");

        await Expect(Header.GetByRole(AriaRole.Link, new() { Name = "Dashboard" })).ToHaveCountAsync(0);
        await Header.GetByRole(AriaRole.Link, new() { Name = "My account" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/account$"));
    }

    [Test]
    public async Task SignOut_FromTheSiteHeader()
    {
        await LoginAsync(TestData.NoAccess);
        await Page.GotoAsync("/");

        await Header.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();

        await Expect(Header.GetByRole(AriaRole.Link, new() { Name = "Sign in" })).ToBeVisibleAsync();
        var session = await (await ApiAsync("GET", "/api/auth/session")).JsonAsync();
        Assert.That(session?.GetProperty("user").ValueKind, Is.EqualTo(System.Text.Json.JsonValueKind.Null));
    }

    [Test]
    public async Task PrivacyPage_AndItsOldUrl()
    {
        await Page.GotoAsync("/Home/Privacy");

        await Expect(Page).ToHaveURLAsync(new Regex("/privacy$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Privacy policy" })).ToBeVisibleAsync();
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
