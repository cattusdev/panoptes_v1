using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Πλοήγηση του console: registry με ομάδες που κλείνουν, route guard (RequirePermission) και Ctrl+K palette
public class NavigationTests : E2ETest
{
    private ILocator Sidebar => Page.Locator("nav[aria-label='Main navigation']");
    private ILocator GroupHeading(string group) => Sidebar.Locator($"button[data-group='{group}']");
    private ILocator Palette => Page.GetByRole(AriaRole.Dialog, new() { Name = "Command palette" });
    private ILocator Option(string name) => Palette.GetByRole(AriaRole.Option, new() { Name = name, Exact = true });

    // Ctrl+K μόλις φορτώσει το console (το topbar κουμπί του palette φαίνεται)
    private async Task PressCtrlKAsync()
    {
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Search or jump to…" })).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Control+K");
    }

    // Το sidebar ξεκινάει "mini" (μόνο icons, χωρίς τίτλους ομάδων)
    private async Task ExpandSidebarAsync()
    {
        await Page.GetByRole(AriaRole.Button, new() { Name = "Toggle sidebar" }).ClickAsync();
        await Expect(GroupHeading("overview")).ToBeVisibleAsync();
    }

    [Test]
    public async Task Sidebar_ShowsOnlyTheGroupsWithPagesTheUserCanOpen()
    {
        await LoginAsync(TestData.Admin);
        await ExpandSidebarAsync();
        await Expect(GroupHeading("overview")).ToHaveTextAsync("Overview");
        await Expect(GroupHeading("relationships")).ToHaveTextAsync("Relationships");
        await Expect(GroupHeading("workspace")).ToHaveTextAsync("Workspace");
        await Expect(GroupHeading("administration")).ToHaveTextAsync("Administration");
        // Ομάδες χωρίς σελίδες ακόμα (έργα, οικονομικά) δεν εμφανίζονται: όχι κενοί τίτλοι
        await Expect(Sidebar.Locator("button[data-group]")).ToHaveCountAsync(4);
        // Το παλιό ψεύτικο "v2.3" στο κάτω μέρος δεν υπάρχει πια
        await Expect(Sidebar).Not.ToContainTextAsync("v2.3");

        // Ο Auditor βλέπει μόνο Dashboard και Audit Logs: η ομάδα Workspace (Media) λείπει
        await Page.Context.ClearCookiesAsync();
        await LoginAsync(TestData.Auditor);
        await Expect(GroupHeading("administration")).ToBeVisibleAsync();
        await Expect(GroupHeading("workspace")).ToHaveCountAsync(0);
        await Expect(Sidebar.Locator("a[data-title]")).ToHaveCountAsync(2);
        await Expect(Sidebar.Locator("a[data-title='Audit Logs']")).ToBeVisibleAsync();
    }

    [Test]
    public async Task SidebarGroup_Collapses_StaysCollapsedAfterReload_AndOpensWhenYouGoToOneOfItsPages()
    {
        await LoginAsync(TestData.Admin);
        await ExpandSidebarAsync();
        var administration = GroupHeading("administration");
        var users = Sidebar.Locator("a[data-title='Users']");
        await Expect(administration).ToHaveAttributeAsync("aria-expanded", "true");

        await administration.ClickAsync();

        await Expect(administration).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(users).ToBeHiddenAsync();
        await Expect(Sidebar.Locator("a[data-title='Dashboard']")).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Expect(administration).ToHaveAttributeAsync("aria-expanded", "false");
        await Expect(users).ToBeHiddenAsync();

        // Πας σε σελίδα της κλειστής ομάδας (εδώ από το palette): η ομάδα ανοίγει για να φαίνεται πού είσαι
        await Page.Keyboard.PressAsync("Control+K");
        await Palette.GetByRole(AriaRole.Combobox).FillAsync("users");
        await Option("Users").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await Expect(administration).ToHaveAttributeAsync("aria-expanded", "true");
        await Expect(users).ToHaveAttributeAsync("aria-current", "page");

        // Με το πληκτρολόγιο: Enter στον τίτλο κλείνει την ομάδα
        await administration.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(administration).ToHaveAttributeAsync("aria-expanded", "false");
    }

    [Test]
    public async Task DirectUrl_WithoutThePermission_ShowsNoAccess_InsideTheConsole()
    {
        await LoginAsync(TestData.Auditor);
        var usersApiCalls = new List<string>();
        Page.Request += (_, request) =>
        {
            if (request.Url.Contains("/api/users")) usersApiCalls.Add(request.Url);
        };

        await Page.GotoAsync("/admin/users");

        // Ίδιο URL, καθαρό μήνυμα μέσα στο console (sidebar και topbar μένουν), όχι μισή σελίδα με 403
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "You don't have access to this page" })).ToBeVisibleAsync();
        await Expect(Sidebar).ToBeVisibleAsync();
        Assert.That(usersApiCalls, Is.Empty, "The page must not load when the user lacks the permission");

        // Το ίδιο για κάθε προστατευμένη σελίδα και τις υποσελίδες της
        foreach (var url in new[] { $"/admin/users/{TestData.UserIds[TestData.Target]}", "/admin/roles", "/admin/media", "/admin/settings", "/admin/settings/email" })
        {
            await Page.GotoAsync(url);
            await Expect(Page.GetByTestId("no-access")).ToBeVisibleAsync();
        }

        await Page.GetByRole(AriaRole.Link, new() { Name = "Go to dashboard" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));
        await Expect(Page.GetByTestId("no-access")).ToHaveCountAsync(0);

        // Η σελίδα που επιτρέπεται ανοίγει κανονικά
        await Page.GotoAsync("/admin/audit-logs");
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Audit logs" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("no-access")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task DirectUrl_IsGuardedPerPage_NotPerArea()
    {
        // Ο RoleManager έχει Roles.View: οι ρόλοι ανοίγουν, οι χρήστες όχι (ίδια ομάδα, ίδιο tab bar)
        await LoginAsync(TestData.RoleManager);

        await Page.GotoAsync("/admin/roles");
        await Expect(Page.GetByRole(AriaRole.Navigation, new() { Name = "Access management" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("no-access")).ToHaveCountAsync(0);

        await Page.GotoAsync("/admin/users");
        await Expect(Page.GetByTestId("no-access")).ToBeVisibleAsync();

        // Ο SettingsViewer ανοίγει τις ρυθμίσεις
        await Page.Context.ClearCookiesAsync();
        await LoginAsync(TestData.SettingsViewer);
        await Page.GotoAsync("/admin/settings/branding");
        await Expect(Page.Locator("#settings-section-title")).ToHaveTextAsync("Branding");
        await Expect(Page.GetByTestId("no-access")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task CommandPalette_OpensWithCtrlK_AndJumpsToAPage()
    {
        await LoginAsync(TestData.Admin);

        await PressCtrlKAsync();
        await Expect(Palette).ToBeVisibleAsync();
        await Expect(Palette.GetByRole(AriaRole.Combobox)).ToBeFocusedAsync();

        await Page.Keyboard.TypeAsync("audit");
        await Expect(Option("Audit Logs")).ToHaveAttributeAsync("aria-selected", "true");
        await Page.Keyboard.PressAsync("Enter");

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/audit-logs$"));
        await Expect(Palette).ToBeHiddenAsync();

        // Από το κουμπί του topbar, και οι ενότητες των ρυθμίσεων
        await Page.GetByRole(AriaRole.Button, new() { Name = "Search or jump to…" }).ClickAsync();
        await Palette.GetByRole(AriaRole.Combobox).FillAsync("branding");
        await Option("Branding").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/settings/branding$"));

        // Ctrl+K ξανά κλείνει, όπως και το Esc
        await Page.Keyboard.PressAsync("Control+K");
        await Expect(Palette).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("Control+K");
        await Expect(Palette).ToBeHiddenAsync();
        await Page.Keyboard.PressAsync("Control+K");
        await Page.Keyboard.PressAsync("Escape");
        await Expect(Palette).ToBeHiddenAsync();
    }

    [Test]
    public async Task CommandPalette_ListsOnlyThePagesTheUserCanOpen()
    {
        await LoginAsync(TestData.Auditor);
        await PressCtrlKAsync();
        await Expect(Palette).ToBeVisibleAsync();

        await Expect(Option("Dashboard")).ToBeVisibleAsync();
        await Expect(Option("Audit Logs")).ToBeVisibleAsync();
        await Expect(Option("Security")).ToBeVisibleAsync(); // My account: για όλους
        foreach (var hidden in new[] { "Users", "Roles", "Media", "Settings", "General", "Branding", "Email" })
            await Expect(Option(hidden)).ToHaveCountAsync(0);

        await Palette.GetByRole(AriaRole.Combobox).FillAsync("zzzz");
        await Expect(Palette.GetByText("No results found.")).ToBeVisibleAsync();

        // Και ενέργειες: αποσύνδεση
        await Palette.GetByRole(AriaRole.Combobox).FillAsync("sign out");
        await Option("Sign out").ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
    }

    [Test]
    public async Task CommandPalette_WorksOnMyAccount_ForUsersWithoutAdminAccess()
    {
        // Χωρίς κανένα permission: μόνο οι σελίδες του λογαριασμού
        await LoginAsync(TestData.NoAccess);
        await Expect(Page).ToHaveURLAsync(new Regex("/account$"));

        await PressCtrlKAsync();
        await Expect(Option("Dashboard")).ToHaveCountAsync(0);
        await Option("Data & privacy").ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/account/data$"));
    }

    [Test]
    public async Task CommandPalette_IsTranslated()
    {
        // Επιλογή γλώσσας επισκέπτη (ο χρήστης δεν έχει δική του): δεν αλλάζουμε το προφίλ κοινού test χρήστη
        await Page.AddInitScriptAsync("localStorage.setItem('panoptes-v1.language.choice', 'el')");
        await LoginAsync(TestData.Auditor);
        await Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "el");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Αναζήτηση ή μετάβαση…" }).ClickAsync();

        var palette = Page.GetByRole(AriaRole.Dialog, new() { Name = "Παλέτα εντολών" });
        await Expect(palette.GetByPlaceholder("Αναζήτηση σελίδων και ενεργειών…")).ToBeVisibleAsync();
        await palette.GetByRole(AriaRole.Combobox).FillAsync("αρχείο");
        await palette.GetByRole(AriaRole.Option, new() { Name = "Αρχείο ενεργειών", Exact = true }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/audit-logs$"));
    }
}
