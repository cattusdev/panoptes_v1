using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

public class UsersPageTests : E2ETest
{
    [Test]
    public async Task Search_FiltersTheList_AndIsKeptInTheUrl()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users");
        await Expect(Row(TestData.Admin)).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Searchbox, new() { Name = "Search by email…" }).FillAsync("rolemgr");

        await Expect(Row(TestData.RoleManager)).ToBeVisibleAsync();
        await Expect(Row(TestData.Admin)).ToHaveCountAsync(0);
        await Expect(Page).ToHaveURLAsync(new Regex("search=rolemgr"));
    }

    [Test]
    public async Task StatusFilter_ShowsLockedAccounts()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users");

        await SelectOptionAsync(Page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by status" }), "Locked");

        await Expect(Row(TestData.Locked)).ToBeVisibleAsync();
        await Expect(Row(TestData.Admin)).ToHaveCountAsync(0);
    }

    [Test]
    public async Task Admin_CanCreateAUser_FromTheDialog()
    {
        const string email = "created@e2e.test";
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users");

        await Page.GetByRole(AriaRole.Button, new() { Name = "New user" }).ClickAsync();
        await Page.Locator("#new-user-email").FillAsync(email);
        await Page.Locator("#new-user-password").FillAsync(TestData.Password);
        await SelectOptionAsync(Page.Locator("#new-user-role"), "Viewer");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();

        await ExpectToastAsync($"{email} was created");
        await Expect(Row(email)).ToBeVisibleAsync();
        Assert.That(await TestData.IsInRoleAsync(email, "Viewer"), Is.True);
    }

    [Test]
    public async Task CreateUser_WithAWeakPassword_ShowsTheErrorOnTheField()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users");

        await Page.GetByRole(AriaRole.Button, new() { Name = "New user" }).ClickAsync();
        await Page.Locator("#new-user-email").FillAsync("weak@e2e.test");
        await Page.Locator("#new-user-password").FillAsync("weak");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Dialog)).ToContainTextAsync("Passwords must");
        Assert.That(await TestData.FindUserAsync("weak@e2e.test"), Is.Null);
    }

    [Test]
    public async Task CreateUser_WithEmptyFields_ShowsTheErrorsInline_WithoutCallingTheServer()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users");
        var requests = 0;
        Page.Request += (_, request) => { if (request.Method == "POST" && request.Url.Contains("/api/users")) requests++; };

        await Page.GetByRole(AriaRole.Button, new() { Name = "New user" }).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create user" }).ClickAsync();

        var dialog = Page.GetByRole(AriaRole.Dialog);
        await Expect(dialog.GetByText("This field is required.")).ToHaveCountAsync(2);
        await Expect(Page.Locator("#new-user-email")).ToBeFocusedAsync();

        await Page.Locator("#new-user-email").FillAsync("not-an-email");
        await Page.Locator("#new-user-password").FocusAsync();
        await Expect(dialog.GetByText("Enter a valid email address.")).ToBeVisibleAsync();
        Assert.That(requests, Is.EqualTo(0));
    }

    [Test]
    public async Task List_SortsOnTheServer_ChangesPageSize_AndHidesColumns()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users");
        var table = Page.GetByRole(AriaRole.Table);
        var userHeader = table.GetByRole(AriaRole.Columnheader, new() { Name = "User" });
        var firstRow = table.Locator("tbody tr").First;

        await Expect(userHeader).ToHaveAttributeAsync("aria-sort", "ascending");
        var ascending = await FirstEmailAsync("email");
        await Expect(firstRow).ToHaveAccessibleNameAsync(ascending);

        await userHeader.GetByRole(AriaRole.Button).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("sort=-email"));
        await Expect(userHeader).ToHaveAttributeAsync("aria-sort", "descending");
        var descending = await FirstEmailAsync("-email");
        Assert.That(descending, Is.Not.EqualTo(ascending));
        await Expect(firstRow).ToHaveAccessibleNameAsync(descending);

        // Μόνο τα πεδία της whitelist: ένα άγνωστο πεδίο δίνει την προεπιλεγμένη σειρά
        Assert.That(await FirstEmailAsync("passwordHash"), Is.EqualTo(ascending));

        await SelectOptionAsync(Page.GetByRole(AriaRole.Combobox, new() { Name = "Rows per page" }), "10");
        await Expect(Page).ToHaveURLAsync(new Regex("pageSize=10"));
        await Expect(table.Locator("tbody tr")).ToHaveCountAsync(10);
        await Expect(Page.GetByText(new Regex(@"^1–10 of \d+$"))).ToBeVisibleAsync();

        await Page.GetByRole(AriaRole.Button, new() { Name = "Columns" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Status" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Status" })).ToHaveCountAsync(0);
        await Expect(table.GetByRole(AriaRole.Columnheader, new() { Name = "Role" })).ToBeVisibleAsync();
    }

    private async Task<string> FirstEmailAsync(string sort)
    {
        var response = await ApiAsync("GET", $"/api/users?sort={sort}&pageSize=10");
        Assert.That(response.Status, Is.EqualTo(200));
        return (await response.JsonAsync())!.Value.GetProperty("items")[0].GetProperty("email").GetString()!;
    }

    [Test]
    public async Task Admin_CanUnlockALockedAccount()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync($"/admin/users/{TestData.UserIds[TestData.UnlockMe]}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Unlock" }).ClickAsync();

        await ExpectToastAsync("Account unlocked");
        var user = await TestData.FindUserAsync(TestData.UnlockMe);
        Assert.That(user!.LockoutEnd, Is.Null);
    }

    [Test]
    public async Task Admin_CanDeleteAUser_AfterConfirming()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync($"/admin/users/{TestData.UserIds[TestData.DeleteMe]}");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Delete user" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/users$"));
        await ExpectToastAsync("was deleted");
        Assert.That(await TestData.FindUserAsync(TestData.DeleteMe), Is.Null);
    }
}

public class AuditLogsPageTests : E2ETest
{
    [Test]
    public async Task ClickingAnEntry_ShowsWhatChanged()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/audit-logs");

        await SelectOptionAsync(Page.GetByRole(AriaRole.Combobox, new() { Name = "Filter by action" }), "Created");
        await Page.GetByRole(AriaRole.Searchbox).FillAsync("AspNetUsers");
        await Page.GetByRole(AriaRole.Row, new() { Name = "Create AspNetUsers" }).First.ClickAsync();

        var panel = Page.GetByRole(AriaRole.Dialog);
        await Expect(panel).ToContainTextAsync("Changes");
        await Expect(panel).ToContainTextAsync("Email");
        await Expect(panel.GetByRole(AriaRole.Columnheader, new() { Name = "After" })).ToBeVisibleAsync();
        // Τα μυστικά πεδία δεν καταγράφονται ποτέ
        await Expect(panel).Not.ToContainTextAsync("PasswordHash");
    }
}
