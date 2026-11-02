using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

public class AccessTests : E2ETest
{
    [Test]
    public async Task AnonymousUser_IsRedirectedToLogin()
    {
        await Page.GotoAsync("/admin");
        await Expect(Page).ToHaveURLAsync(new Regex("/login\\?returnUrl=%2Fadmin$"));

        await Page.GotoAsync("/admin/users");
        await Expect(Page).ToHaveURLAsync(new Regex("/login\\?returnUrl=%2Fadmin%2Fusers$"));

        // Το παλιό URL του προφίλ -> /account -> login
        await Page.GotoAsync("/Identity/Account/Manage");
        await Expect(Page).ToHaveURLAsync(new Regex("/login\\?returnUrl=%2Faccount$"));
    }

    [Test]
    public async Task Login_ReturnsToTheRequestedPage()
    {
        await Page.GotoAsync("/admin/roles");
        await Expect(Page).ToHaveURLAsync(new Regex("/login"));

        await LoginAsync(Page, TestData.Admin, returnUrl: "/admin/roles");

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/roles$"));
    }

    [Test]
    public async Task WrongPassword_ShowsGenericError()
    {
        await LoginAsync(TestData.NoAccess, "Wrong-Passw0rd!");

        await Expect(AppAlert).ToContainTextAsync("Invalid email or password");
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
    }

    [Test]
    public async Task UnconfirmedEmail_IsToldToConfirmFirst()
    {
        await LoginAsync(TestData.Unconfirmed);

        await Expect(AppAlert).ToContainTextAsync("confirm your email");
    }

    [Test]
    public async Task UserWithoutPermissions_IsDeniedTheAdminArea()
    {
        await LoginAsync(TestData.NoAccess);
        await Page.GotoAsync("/admin");

        await Expect(Page).ToHaveURLAsync(new Regex("/access-denied$"));
    }

    [Test]
    public async Task Admin_IsSuperuser_AndSeesEverySection()
    {
        // Ο seeded Admin ρόλος δεν έχει κανένα permission claim: όλα έρχονται από το ότι είναι Admin
        await LoginAsync(TestData.Admin);
        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));

        await Expect(Page.Locator("a[data-title='Users']")).ToBeVisibleAsync();
        await Expect(Page.Locator("a[data-title='Audit Logs']")).ToBeVisibleAsync();

        await Page.Locator("a[data-title='Audit Logs']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/audit-logs$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Audit logs" })).ToBeVisibleAsync();
    }

    [Test]
    public async Task AuditLog_SortsByTime_OldestFirst_WhenAsked()
    {
        await LoginAsync(TestData.Auditor);
        await Page.GotoAsync("/admin/audit-logs");
        var timeHeader = Page.GetByRole(AriaRole.Columnheader, new() { Name = "Time" });
        await Expect(timeHeader).ToHaveAttributeAsync("aria-sort", "descending");

        await timeHeader.GetByRole(AriaRole.Button).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("sort=time"));
        await Expect(timeHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        var oldest = (await (await ApiAsync("GET", "/api/audit-logs?sort=time&pageSize=2")).JsonAsync())!.Value.GetProperty("items");
        var newest = (await (await ApiAsync("GET", "/api/audit-logs?pageSize=1")).JsonAsync())!.Value.GetProperty("items");
        Assert.That(oldest[0].GetProperty("dateTime").GetDateTimeOffset(), Is.LessThanOrEqualTo(oldest[1].GetProperty("dateTime").GetDateTimeOffset()));
        Assert.That(oldest[0].GetProperty("dateTime").GetDateTimeOffset(), Is.LessThan(newest[0].GetProperty("dateTime").GetDateTimeOffset()));
    }

    [Test]
    public async Task Sidebar_HasNoDuplicates_OfTheUsersTabs_OrTheUserMenu()
    {
        await LoginAsync(TestData.Admin);
        var sidebar = Page.Locator("nav[aria-label='Main navigation']");
        await Expect(sidebar.Locator("a[data-title='Users']")).ToBeVisibleAsync();

        // Roles είναι tab του Users, το My account είναι στο μενού χρήστη
        await Expect(sidebar.Locator("a[data-title='Roles'], a[data-title='My account']")).ToHaveCountAsync(0);

        // Το Users μένει ενεργό και στο tab Roles
        await sidebar.Locator("a[data-title='Users']").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await Page.GetByRole(AriaRole.Navigation, new() { Name = "Access management" }).GetByRole(AriaRole.Link, new() { Name = "Roles" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/roles$"));
        await Expect(sidebar.Locator("a[data-title='Users']")).ToHaveAttributeAsync("aria-current", "page");

        // Μενού χρήστη: μόνο το avatar στο κουμπί, τα links μέσα στο μενού
        var menu = Page.GetByTestId("user-menu");
        await Expect(menu).ToHaveAccessibleNameAsync("Account menu");
        await Expect(menu).Not.ToContainTextAsync(TestData.Admin);
        await menu.ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menu)).ToContainTextAsync(TestData.Admin);
        // Δεν υπάρχει δημόσιο site για να το "δεις"
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "View site" })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "My account" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/account$"));
    }

    [Test]
    public async Task Sidebar_Users_LeadsToRoles_WhenOnlyRolesAreAllowed()
    {
        // Ο RoleManager βλέπει ρόλους αλλά όχι χρήστες
        await LoginAsync(TestData.RoleManager);
        var users = Page.Locator("nav[aria-label='Main navigation'] a[data-title='Users']");
        await Expect(users).ToHaveAttributeAsync("href", "/admin/roles");
        await users.ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/roles$"));
        await Expect(users).ToHaveAttributeAsync("aria-current", "page");
    }

    [Test]
    public async Task Dashboard_ShowsLiveNumbers()
    {
        await LoginAsync(TestData.Admin);

        await Expect(Page.GetByTestId("stat-users")).ToHaveTextAsync((await TestData.CountUsersAsync()).ToString());

        // Κατάσταση συστήματος δίπλα στα νούμερα (βάση, ασφάλεια, 2FA)
        await Expect(Page.Locator("dl[aria-label='System status']")).ToContainTextAsync("Healthy");
        await Expect(Page.Locator("dl[aria-label='System status']")).ToContainTextAsync("Two-factor");
        await Page.Locator("dl[aria-label='System status']").GetByRole(AriaRole.Link).HoverAsync();
        await Expect(Page.GetByRole(AriaRole.Tooltip)).ToContainTextAsync("Locked accounts");

        // Κάθε νούμερο οδηγεί στη σχετική λίστα
        await Page.GetByRole(AriaRole.List, new() { Name = "Key metrics" }).GetByRole(AriaRole.Link).Filter(new() { HasText = "Admin" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex(@"/admin/users\?role=Admin$"));
    }

    [Test]
    public async Task SignOut_EndsTheSession()
    {
        await LoginAsync(TestData.Admin);
        await Page.GetByTestId("user-menu").ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Sign out" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));

        await Page.GotoAsync("/admin");
        await Expect(Page).ToHaveURLAsync(new Regex("/login\\?returnUrl="));
    }

    [Test]
    public async Task FiveFailedLogins_LockTheAccount()
    {
        for (var attempt = 0; attempt < 5; attempt++)
            await LoginAsync(TestData.Lockout, "Wrong-Passw0rd!");

        // Ακόμα και με τον σωστό κωδικό, ο λογαριασμός είναι κλειδωμένος
        await LoginAsync(TestData.Lockout);

        await Expect(Page).ToHaveURLAsync(new Regex("/lockout$"));
        // Χωρίς self-service reset: ο διαχειριστής ξεκλειδώνει ή αλλάζει τον κωδικό
        await Expect(Page.GetByText("Try again later, or contact your administrator.")).ToBeVisibleAsync();
        await Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Reset password" })).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Link, new() { Name = "Back to sign in" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
    }
}
