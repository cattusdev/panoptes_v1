using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace Panoptes_v1.E2E;

// Επιβεβαίωση email (χωρίς login): για λογαριασμούς που δεν έχουν επιβεβαιωθεί ακόμα
public class EmailConfirmationTests : E2ETest
{
    [Test]
    public async Task ResendConfirmation_SendsANewLink()
    {
        await Page.GotoAsync("/resend-confirmation");
        await Page.Locator("#email").FillAsync(TestData.Unconfirmed);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send link" }).ClickAsync();

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();
        var mail = await Mailbox.WaitForAsync(TestData.Unconfirmed, "Confirm your email");
        Assert.That(mail.Link, Does.Contain("/confirm-email?"));
    }

    [Test]
    public async Task OldIdentityUrls_RedirectToTheNewPages()
    {
        await Page.GotoAsync("/Identity/Account/Login");
        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));

        await Page.GotoAsync("/Identity/Account/ResendEmailConfirmation");
        await Expect(Page).ToHaveURLAsync(new Regex("/resend-confirmation$"));
    }
}

public class TwoFactorTests : E2ETest
{
    [Test]
    public async Task SetUp_WithTheAuthenticatorApp_ShowsRecoveryCodes()
    {
        await LoginAsync(TestData.TwoFaSetup);
        await Page.GotoAsync("/account/security");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Set up authenticator app" }).ClickAsync();
        var key = Page.GetByTestId("authenticator-key");
        await Expect(key).Not.ToHaveTextAsync("…");

        await Page.GetByTestId("code-input").FillAsync(Totp.Code(await key.InnerTextAsync()));

        await Expect(Page.GetByTestId("recovery-codes").Locator("li")).ToHaveCountAsync(10);
        await Page.GetByRole(AriaRole.Button, new() { Name = "I've saved them" }).ClickAsync();
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Turn off" })).ToBeVisibleAsync();

        var user = await TestData.FindUserAsync(TestData.TwoFaSetup);
        Assert.That(user!.TwoFactorEnabled, Is.True);

        // Και απενεργοποίηση: η συνεδρία πρέπει να συνεχίζει (νέο security stamp -> νέο cookie)
        await Page.GetByRole(AriaRole.Button, new() { Name = "Turn off" }).ClickAsync();
        await Page.GetByRole(AriaRole.Alertdialog).GetByRole(AriaRole.Button, new() { Name = "Turn off" }).ClickAsync();
        await ExpectToastAsync("Two-factor authentication is off");
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Set up authenticator app" })).ToBeVisibleAsync();

        await Page.ReloadAsync();
        await Expect(Page).ToHaveURLAsync(new Regex("/account/security$"));
        Assert.That((await TestData.FindUserAsync(TestData.TwoFaSetup))!.TwoFactorEnabled, Is.False);
    }

    [Test]
    public async Task SignIn_AsksForTheCode()
    {
        await LoginAsync(TestData.TwoFaLogin);
        await Expect(Page).ToHaveURLAsync(new Regex("/login/2fa"));

        await Page.GetByTestId("code-input").FillAsync(Totp.Code(TestData.TwoFaKey));

        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));
    }

    [Test]
    public async Task SignIn_WithARecoveryCode()
    {
        await LoginAsync(TestData.TwoFaLogin);
        await Expect(Page).ToHaveURLAsync(new Regex("/login/2fa"));

        await Page.GetByRole(AriaRole.Button, new() { Name = "Use a recovery code instead" }).ClickAsync();
        await Page.Locator("#recovery-code").FillAsync(TestData.TwoFaRecoveryCodes[0]);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/admin$"));
    }

    [Test]
    public async Task WrongCode_ShowsAnError_AndStaysOnTheStep()
    {
        await LoginAsync(TestData.TwoFaLogin);
        await Page.GetByTestId("code-input").FillAsync("000000");

        await Expect(AppAlert).ToContainTextAsync("didn't work");
        await Expect(Page).ToHaveURLAsync(new Regex("/login/2fa"));
    }

    [Test]
    public async Task TwoFactorPage_WithoutASignInInProgress_GoesBackToLogin()
    {
        await Page.GotoAsync("/login/2fa");

        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
    }
}

public class MyAccountTests : E2ETest
{
    [Test]
    public async Task UserWithoutAdminAccess_CanStillManageTheirAccount()
    {
        await LoginAsync(TestData.NoAccess);
        await Page.GotoAsync("/account");

        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "My account" })).ToBeVisibleAsync();
        await Expect(Page.GetByTestId("account-email")).ToHaveTextAsync(TestData.NoAccess);
        await Expect(Page.Locator("a[data-title='Dashboard']")).ToHaveCountAsync(0);
    }

    [Test]
    public async Task UpdatePhoneNumber()
    {
        await LoginAsync(TestData.Phone);
        await Page.GotoAsync("/account");

        // Ελλάδα προεπιλογή: ο αριθμός χωρίς κωδικό, αποθηκεύεται σε E.164
        await Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Country code: Greece (+30)" })).ToBeVisibleAsync();
        await Page.Locator("#phone").FillAsync("690 1234");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();
        await Expect(Page.GetByText("Enter a valid phone number. Check the country code.")).ToBeVisibleAsync();

        await Page.Locator("#phone").FillAsync("6900000000");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Save changes" }).ClickAsync();

        await ExpectToastAsync("Profile updated");
        Assert.That((await TestData.FindUserAsync(TestData.Phone))!.PhoneNumber, Is.EqualTo("+306900000000"));

        // Ο server ελέγχει κι αυτός (όχι μόνο το UI)
        var refused = await ApiAsync("PUT", "/api/account/profile", new { phoneNumber = "+30 12" });
        Assert.That(refused.Status, Is.EqualTo(400));
    }

    [Test]
    public async Task ChangeEmail_TakesEffectAfterConfirmingTheNewAddress()
    {
        const string newEmail = "changed@e2e.test";
        await LoginAsync(TestData.ChangeEmail);
        await Page.GotoAsync("/account");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Change email" }).ClickAsync();
        await Page.Locator("#new-email").FillAsync(newEmail);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Send confirmation link" }).ClickAsync();
        await ExpectToastAsync("Check your inbox");

        // Μέχρι να πατηθεί το link, τίποτα δεν αλλάζει
        Assert.That(await TestData.FindUserAsync(TestData.ChangeEmail), Is.Not.Null);

        var mail = await Mailbox.WaitForAsync(newEmail, "Confirm your new email");
        await Page.GotoAsync(mail.Link);
        await Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Email updated" })).ToBeVisibleAsync();

        var user = await TestData.FindUserAsync(newEmail);
        Assert.That(user, Is.Not.Null);
        Assert.That(user!.UserName, Is.EqualTo(newEmail));
    }

    [Test]
    public async Task ChangePassword()
    {
        const string newPassword = "Changed-Passw0rd!";
        await LoginAsync(TestData.ChangePw);
        await Page.GotoAsync("/account/security");

        await Page.Locator("#current-password").FillAsync(TestData.Password);
        await Page.Locator("#new-password").FillAsync(newPassword);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Update password" }).ClickAsync();

        await ExpectToastAsync("Password updated");
        var passwordChanged = await TestData.WithServicesAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            return await userManager.CheckPasswordAsync((await userManager.FindByEmailAsync(TestData.ChangePw))!, newPassword);
        });
        Assert.That(passwordChanged, Is.True);
    }

    [Test]
    public async Task ChangePassword_WithAWrongCurrentPassword_ShowsTheError()
    {
        await LoginAsync(TestData.Phone);
        await Page.GotoAsync("/account/security");

        await Page.Locator("#current-password").FillAsync("Not-My-Passw0rd!");
        await Page.Locator("#new-password").FillAsync("Another-Passw0rd!");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Update password" }).ClickAsync();

        await Expect(Page.GetByText("Your current password is incorrect")).ToBeVisibleAsync();
    }

    [Test]
    public async Task DownloadPersonalData_ContainsMyEmail()
    {
        await LoginAsync(TestData.Phone);

        var response = await ApiAsync("GET", "/api/account/personal-data");

        Assert.That(response.Status, Is.EqualTo(200));
        Assert.That(await response.TextAsync(), Does.Contain(TestData.Phone));
    }

    [Test]
    public async Task DeleteAccount_AfterConfirmingThePassword()
    {
        await LoginAsync(TestData.SelfDelete);
        await Page.GotoAsync("/account/data");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete account" }).ClickAsync();
        await Page.Locator("#delete-password").FillAsync(TestData.Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Delete my account" }).ClickAsync();

        await Expect(Page).ToHaveURLAsync(new Regex("/login$"));
        Assert.That(await TestData.FindUserAsync(TestData.SelfDelete), Is.Null);
    }
}
