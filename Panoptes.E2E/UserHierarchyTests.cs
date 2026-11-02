using System.Text.RegularExpressions;
using Panoptes.Constants;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Panoptes.E2E;

// UserManager έχει Users.View/Create/Edit: διαχειρίζεται απλούς χρήστες, όχι Admins
public class UserHierarchyTests : E2ETest
{
    [Test]
    public async Task UserManager_CannotPromoteThemselvesToAdmin()
    {
        await LoginAsync(TestData.UserManager);
        await Page.GotoAsync($"/admin/users/{TestData.UserIds[TestData.UserManager]}");

        // Το UI δεν προσφέρει καν τον ρόλο Admin
        await Page.Locator("#user-role").ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Option, new() { Name = SystemRoles.Admin, Exact = true })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");

        // ...και ο server το αρνείται αν το στείλεις απευθείας
        var response = await ApiAsync("PUT", $"/api/users/{TestData.UserIds[TestData.UserManager]}",
            new { email = TestData.UserManager, role = SystemRoles.Admin });

        Assert.That(response.Status, Is.EqualTo(403));
        Assert.That(await TestData.IsInRoleAsync(TestData.UserManager, SystemRoles.Admin), Is.False);
    }

    [Test]
    public async Task UserManager_CannotUsePromoteToAdmin()
    {
        await LoginAsync(TestData.UserManager);
        await Page.GotoAsync("/admin/users?search=target%40");

        await Page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {TestData.Target}" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Menuitem, new() { Name = "Promote to Admin" })).ToHaveCountAsync(0);
        await Page.Keyboard.PressAsync("Escape");

        var response = await ApiAsync("POST", $"/api/users/{TestData.UserIds[TestData.UserManager]}/promote");

        Assert.That(response.Status, Is.EqualTo(403));
        Assert.That(await TestData.IsInRoleAsync(TestData.UserManager, SystemRoles.Admin), Is.False);
    }

    [Test]
    public async Task UserManager_SeesAnAdminAccount_AsViewOnly()
    {
        await LoginAsync(TestData.UserManager);
        await Page.GotoAsync($"/admin/users/{TestData.UserIds[TestData.Admin]}");

        await Expect(Page.GetByRole(AriaRole.Alert).Filter(new() { HasText = "View only" })).ToBeVisibleAsync();
        await Expect(Page.Locator("#user-email")).ToBeDisabledAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" })).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UserManager_CannotResetAnAdminsPassword()
    {
        await LoginAsync(TestData.UserManager);

        var response = await ApiAsync("POST", $"/api/users/{TestData.UserIds[TestData.Admin]}/reset-password",
            new { newPassword = "Hacked-Passw0rd!" });

        Assert.That(response.Status, Is.EqualTo(403));
        var adminPasswordUnchanged = await TestData.WithServicesAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var admin = await userManager.FindByEmailAsync(TestData.Admin);
            return await userManager.CheckPasswordAsync(admin!, TestData.Password);
        });
        Assert.That(adminPasswordUnchanged, Is.True);
    }

    [Test]
    public async Task UserManager_CanAssignARoleWithinTheirOwnPermissions()
    {
        await LoginAsync(TestData.UserManager);
        await Page.GotoAsync($"/admin/users/{TestData.UserIds[TestData.Target]}");

        await SelectOptionAsync(Page.Locator("#user-role"), "Viewer");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await ExpectToastAsync("User updated");
        Assert.That(await TestData.IsInRoleAsync(TestData.Target, "Viewer"), Is.True);
    }

    [Test]
    public async Task Admin_CanPromoteAUserToAdmin()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users?search=promote%40");

        await Page.GetByRole(AriaRole.Button, new() { Name = $"Actions for {TestData.Promote}" }).ClickAsync();
        await Page.GetByRole(AriaRole.Menuitem, new() { Name = "Promote to Admin" }).ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Promote" }).ClickAsync();

        await ExpectToastAsync("is now an administrator");
        await Expect(Row(TestData.Promote)).ToContainTextAsync("Administrator");
        Assert.That(await TestData.IsInRoleAsync(TestData.Promote, SystemRoles.Admin), Is.True);
    }

    [Test]
    public async Task UserLink_FromTheList_OpensTheDetailPage()
    {
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync("/admin/users?search=target%40");

        await Row(TestData.Target).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex($"/admin/users/{TestData.UserIds[TestData.Target]}$"));
        await Expect(Page.Locator("#user-email")).ToHaveValueAsync(TestData.Target);
    }
}
