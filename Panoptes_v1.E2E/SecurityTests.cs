using System.Text.RegularExpressions;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

public class SecurityTests : E2ETest
{
    [Test]
    public async Task DeleteUser_WithoutCsrfToken_IsRejected()
    {
        await LoginAsync(TestData.Admin);

        // Αυτό που θα έστελνε μια κακόβουλη σελίδα μέσω του browser του Admin (έχει το cookie, όχι το token)
        var response = await ApiAsync("DELETE", $"/api/users/{TestData.UserIds[TestData.Target]}", withCsrf: false);

        Assert.That(response.Status, Is.EqualTo(400));
        Assert.That(await TestData.FindUserAsync(TestData.Target), Is.Not.Null);
    }

    [Test]
    public async Task ApiPost_WithoutCsrfHeader_IsRejected()
    {
        await LoginAsync(TestData.Admin);

        // Το cookie του χρήστη υπάρχει, αλλά χωρίς X-XSRF-TOKEN header το request απορρίπτεται
        var response = await Page.APIRequest.PostAsync("/api/auth/logout");

        Assert.That(response.Status, Is.EqualTo(400));
    }

    [Test]
    public async Task Api_WithoutLogin_Returns401NotARedirect()
    {
        var response = await Page.APIRequest.GetAsync("/api/dashboard", new() { MaxRedirects = 0 });

        Assert.That(response.Status, Is.EqualTo(401));
    }

    [Test]
    public async Task RemovedPermission_AppliesImmediately_WithoutLogout()
    {
        // Ο Auditor είναι ήδη μέσα, σε δικό του browser
        await using var auditorContext = await Browser.NewContextAsync(ContextOptions());
        var auditorPage = await auditorContext.NewPageAsync();
        await LoginAsync(auditorPage, TestData.Auditor);
        await auditorPage.GotoAsync("/admin/audit-logs");
        await Expect(auditorPage.GetByRole(AriaRole.Heading, new() { Name = "Audit logs" })).ToBeVisibleAsync();

        // Ο Admin του αφαιρεί το AuditLogs.View
        await LoginAsync(TestData.Admin);
        await Page.GotoAsync($"/admin/roles/{TestData.RoleIds["Auditor"]}");
        await Page.Locator($"[data-permission='{Permissions.AuditLogs.View}']").ClickAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await ExpectToastAsync("Permissions updated for Auditor");

        // Στο επόμενο request του Auditor το παλιό cookie δεν ισχύει πια
        await auditorPage.GotoAsync("/admin/audit-logs");
        await Expect(auditorPage).ToHaveURLAsync(new Regex("/login"));
    }
}

// Δεν χρειάζεται browser: ελέγχουμε απευθείας τι γράφτηκε στη βάση
public class AuditLogTests
{
    [Test]
    public async Task AuthenticatorKey_IsNeverWrittenToTheAuditLog()
    {
        var (authenticatorKey, tokenLogs) = await TestData.WithServicesAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var user = await userManager.FindByEmailAsync(TestData.TwoFactor);
            var key = await userManager.GetAuthenticatorKeyAsync(user!);

            var db = services.GetRequiredService<ApplicationDbContext>();
            var logs = await db.AuditLogs.Where(l => l.TableName == "AspNetUserTokens").ToListAsync();
            return (key, logs);
        });

        Assert.That(authenticatorKey, Is.Not.Null.And.Not.Empty);
        Assert.That(tokenLogs, Is.Not.Empty, "The token change should still be audited (without its value).");

        foreach (var log in tokenLogs)
        {
            var values = $"{log.OldValues} {log.NewValues}";
            Assert.That(values, Does.Not.Contain(authenticatorKey!));
            Assert.That(values, Does.Not.Contain("\"Value\""));
        }
    }
}
