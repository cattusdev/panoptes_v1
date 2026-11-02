using System.Text.RegularExpressions;
using Panoptes_v1.Constants;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// RoleManager έχει Roles.View/Edit: διαχειρίζεται μόνο ρόλους "κάτω" από τον ίδιο (π.χ. Support)
public class RoleHierarchyTests : E2ETest
{
    [Test]
    public async Task RoleManager_CannotChangeTheirOwnRolesPermissions()
    {
        await LoginAsync(TestData.RoleManager);
        await Page.GotoAsync($"/admin/roles/{TestData.RoleIds["RoleManager"]}");

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "can't change a role you belong to" })).ToBeVisibleAsync();
        await Expect(PermissionSwitch(Permissions.Roles.View)).ToBeDisabledAsync();

        var response = await ApiAsync("PUT", $"/api/roles/{TestData.RoleIds["RoleManager"]}/permissions",
            new { permissions = new[] { Permissions.Roles.View, Permissions.Roles.Edit, Permissions.Roles.Delete } });
        Assert.That(response.Status, Is.EqualTo(403));
    }

    [Test]
    public async Task AdminRole_IsLocked_EvenForAdmins()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync($"/admin/roles/{TestData.RoleIds[SystemRoles.Admin]}");

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "protected system role" })).ToBeVisibleAsync();
        await Expect(PermissionSwitch(Permissions.Users.Delete)).ToBeDisabledAsync();
        await Expect(PermissionSwitch(Permissions.Users.Delete)).ToBeCheckedAsync();

        var response = await ApiAsync("PUT", $"/api/roles/{TestData.RoleIds[SystemRoles.Admin]}/permissions", new { permissions = Array.Empty<string>() });
        Assert.That(response.Status, Is.EqualTo(403));
    }

    [Test]
    public async Task RoleManager_CannotGrantPermissionsTheyDontHave()
    {
        await LoginAsync(TestData.RoleManager);
        await Page.GotoAsync($"/admin/roles/{TestData.RoleIds["Support"]}");

        // Στο UI το switch είναι κλειδωμένο...
        await Expect(PermissionSwitch(Permissions.Users.Delete)).ToBeDisabledAsync();

        // ...και ο server αρνείται αν το στείλεις απευθείας
        var response = await ApiAsync("PUT", $"/api/roles/{TestData.RoleIds["Support"]}/permissions",
            new { permissions = new[] { Permissions.Roles.View, Permissions.Users.Delete } });

        Assert.That(response.Status, Is.EqualTo(403));
        Assert.That(await TestData.RoleHasPermissionAsync("Support", Permissions.Users.Delete), Is.False);
    }

    [Test]
    public async Task RoleManager_CanGrantPermissionsTheyHave_ToALowerRole()
    {
        await LoginAsync(TestData.RoleManager);
        await Page.GotoAsync($"/admin/roles/{TestData.RoleIds["Support"]}");

        await PermissionSwitch(Permissions.Roles.Edit).ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await ExpectToastAsync("Permissions updated for Support");
        Assert.That(await TestData.RoleHasPermissionAsync("Support", Permissions.Roles.Edit), Is.True);
    }

    [Test]
    public async Task Admin_CanCreateRenameAndDeleteARole()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/roles");

        // Create -> πάει κατευθείαν στη σελίδα του ρόλου για να διαλέξεις permissions
        await Page.GetByRole(AriaRole.Button, new() { Name = "New role" }).ClickAsync();
        await Page.Locator("#role-name").FillAsync("Temp Role");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create role" }).ClickAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/admin/roles/[0-9a-f-]+$"));
        await ExpectToastAsync("created");

        // Rename
        await Page.GetByRole(AriaRole.Button, new() { Name = "Rename" }).ClickAsync();
        await Page.Locator("#role-name").FillAsync("Temporary");
        await Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = "Rename" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Temporary" })).ToBeVisibleAsync();

        // Delete (Danger zone + επιβεβαίωση)
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete role" }).ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Delete role" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin/roles$"));
        await Expect(Row("Temporary")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task DuplicateRoleName_ShowsAnInlineError()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/roles");

        await Page.GetByRole(AriaRole.Button, new() { Name = "New role" }).ClickAsync();
        await Page.Locator("#role-name").FillAsync("Support");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create role" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Dialog)).ToContainTextAsync("already exists");
    }

    private ILocator PermissionSwitch(string permission) => Page.Locator($"[data-permission='{permission}']");
}
