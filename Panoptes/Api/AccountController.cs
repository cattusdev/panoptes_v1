using Microsoft.Extensions.Localization;
using Panoptes.Services.Localization;
using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Panoptes.Constants;
using Panoptes.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Panoptes.Api
{
    public record AvatarDto(string? AvatarUrl);

    public class LanguageRequest
    {
        public string? Language { get; set; } // null = η προεπιλογή της εφαρμογής
    }

    public class SetAvatarRequest
    {
        public Guid MediaId { get; set; }
    }

    public record PasswordPolicyDto(int RequiredLength, bool RequireUppercase, bool RequireLowercase, bool RequireDigit, bool RequireNonAlphanumeric);

    public record AccountDto(
        string Id,
        string Email,
        bool EmailConfirmed,
        string? PhoneNumber,
        bool TwoFactorEnabled,
        bool HasAuthenticator,
        int RecoveryCodesLeft,
        bool IsMachineRemembered);

    public record AuthenticatorSetupDto(string SharedKey, string AuthenticatorUri);

    public record RecoveryCodesDto(IReadOnlyList<string> RecoveryCodes);

    public class EmailRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";
    }

    public class RegisterRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";
    }

    public class ConfirmEmailRequest
    {
        [Required] public string UserId { get; set; } = "";
        [Required] public string Code { get; set; } = "";
    }

    public class ConfirmEmailChangeRequest
    {
        [Required] public string UserId { get; set; } = "";
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Code { get; set; } = "";
    }

    public class ResetPasswordWithCodeRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = "";
        [Required] public string Code { get; set; } = "";
        [Required] public string Password { get; set; } = "";
    }

    public class ProfileRequest
    {
        [Phone, StringLength(32)]
        public string? PhoneNumber { get; set; }
    }

    public class ChangeEmailRequest
    {
        [Required, EmailAddress]
        public string NewEmail { get; set; } = "";
    }

    public class ChangePasswordRequest
    {
        [Required] public string CurrentPassword { get; set; } = "";
        [Required] public string NewPassword { get; set; } = "";
    }

    public class CodeRequest
    {
        [Required, StringLength(7, MinimumLength = 6)]
        public string Code { get; set; } = "";
    }

    public class PasswordConfirmRequest
    {
        [Required] public string Password { get; set; } = "";
    }

    [ApiController]
    [Route("api/account")]
    [Authorize]
    public class AccountController : ControllerBase
    {
        private readonly UserManager<IdentityUser> _userManager;
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly AccountEmails _emails;
        private readonly IdentityOptions _identityOptions;
        private readonly ILogger<AccountController> _logger;
        private readonly ProfileService _profiles;

        private readonly IStringLocalizer<SharedResource> _t;

        public AccountController(
            UserManager<IdentityUser> userManager,
            SignInManager<IdentityUser> signInManager,
            AccountEmails emails,
            IOptions<IdentityOptions> identityOptions,
            ILogger<AccountController> logger,
            ProfileService profiles,
            IStringLocalizer<SharedResource> localizer)
        {
            _t = localizer;
            _profiles = profiles;
            _userManager = userManager;
            _signInManager = signInManager;
            _emails = emails;
            _identityOptions = identityOptions.Value;
            _logger = logger;
        }

        // ---------- Χωρίς login ----------

        // Οι κανόνες κωδικού (για το live checklist στο UI): μία πηγή αλήθειας, ο server
        [HttpGet("password-policy")]
        [AllowAnonymous]
        public PasswordPolicyDto PasswordPolicy()
        {
            var p = _identityOptions.Password;
            return new PasswordPolicyDto(p.RequiredLength, p.RequireUppercase, p.RequireLowercase, p.RequireDigit, p.RequireNonAlphanumeric);
        }

        [HttpPost("register")]
        [AllowAnonymous]
        public async Task<IActionResult> Register(RegisterRequest request)
        {
            var user = new IdentityUser { UserName = request.Email, Email = request.Email };
            var result = await _userManager.CreateAsync(user, request.Password);
            if (!result.Succeeded) return IdentityValidationProblem(result);

            _logger.LogInformation("User created a new account with password.");
            await _emails.SendConfirmationAsync(user.Email!, user.Id, await _userManager.GenerateEmailConfirmationTokenAsync(user));
            return NoContent();
        }

        // Πάντα 204: δεν αποκαλύπτουμε αν υπάρχει λογαριασμός με αυτό το email
        [HttpPost("resend-confirmation")]
        [AllowAnonymous]
        public async Task<IActionResult> ResendConfirmation(EmailRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user != null && !user.EmailConfirmed)
                await _emails.SendConfirmationAsync(user.Email!, user.Id, await _userManager.GenerateEmailConfirmationTokenAsync(user));
            return NoContent();
        }

        [HttpPost("confirm-email")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            var token = AccountEmails.DecodeToken(request.Code);
            if (user == null || token == null) return InvalidLink();

            var result = await _userManager.ConfirmEmailAsync(user, token);
            return result.Succeeded ? NoContent() : InvalidLink();
        }

        // Πάντα 204: δεν αποκαλύπτουμε αν υπάρχει λογαριασμός με αυτό το email
        [HttpPost("forgot-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ForgotPassword(EmailRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            if (user != null && await _userManager.IsEmailConfirmedAsync(user))
                await _emails.SendPasswordResetAsync(user.Email!, await _userManager.GeneratePasswordResetTokenAsync(user));
            return NoContent();
        }

        [HttpPost("reset-password")]
        [AllowAnonymous]
        public async Task<IActionResult> ResetPassword(ResetPasswordWithCodeRequest request)
        {
            var user = await _userManager.FindByEmailAsync(request.Email);
            var token = AccountEmails.DecodeToken(request.Code);
            if (user == null || token == null) return InvalidLink();

            var result = await _userManager.ResetPasswordAsync(user, token, request.Password);
            if (result.Succeeded) return NoContent();
            if (result.Errors.Any(e => e.Code == "InvalidToken")) return InvalidLink();
            return IdentityValidationProblem(result);
        }

        // Το link έρχεται στο νέο email: δουλεύει και χωρίς να είσαι συνδεδεμένος
        [HttpPost("confirm-email-change")]
        [AllowAnonymous]
        public async Task<IActionResult> ConfirmEmailChange(ConfirmEmailChangeRequest request)
        {
            var user = await _userManager.FindByIdAsync(request.UserId);
            var token = AccountEmails.DecodeToken(request.Code);
            if (user == null || token == null) return InvalidLink();

            var result = await _userManager.ChangeEmailAsync(user, request.Email, token);
            if (!result.Succeeded) return InvalidLink();

            // Στην εφαρμογή το username είναι το email
            var userNameResult = await _userManager.SetUserNameAsync(user, request.Email);
            if (!userNameResult.Succeeded) return IdentityValidationProblem(userNameResult);

            if (_userManager.GetUserId(User) == user.Id)
                await _signInManager.RefreshSignInAsync(user);
            return NoContent();
        }

        // ---------- Ο λογαριασμός μου ----------

        [HttpGet]
        public async Task<ActionResult<AccountDto>> Get()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();
            return await ToDtoAsync(user);
        }

        [HttpPut("profile")]
        public async Task<ActionResult<AccountDto>> UpdateProfile(ProfileRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var phone = string.IsNullOrWhiteSpace(request.PhoneNumber) ? null : request.PhoneNumber.Trim();
            if (phone != user.PhoneNumber)
            {
                var result = await _userManager.SetPhoneNumberAsync(user, phone);
                if (!result.Succeeded) return IdentityValidationProblem(result, "PhoneNumber");
            }

            await _signInManager.RefreshSignInAsync(user);
            return await ToDtoAsync(user);
        }

        [HttpPost("change-email")]
        public async Task<IActionResult> ChangeEmail(ChangeEmailRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (string.Equals(request.NewEmail, user.Email, StringComparison.OrdinalIgnoreCase))
                return FieldProblem(nameof(ChangeEmailRequest.NewEmail), _t["This is already your email address."]);

            if (await _userManager.FindByEmailAsync(request.NewEmail) != null)
                return FieldProblem(nameof(ChangeEmailRequest.NewEmail), _t["This email address is already in use."]);

            var token = await _userManager.GenerateChangeEmailTokenAsync(user, request.NewEmail);
            await _emails.SendEmailChangeAsync(request.NewEmail, user.Id, token);
            return NoContent();
        }

        [HttpPost("send-verification-email")]
        public async Task<IActionResult> SendVerificationEmail()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!user.EmailConfirmed)
                await _emails.SendConfirmationAsync(user.Email!, user.Id, await _userManager.GenerateEmailConfirmationTokenAsync(user));
            return NoContent();
        }

        [HttpPost("change-password")]
        public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var result = await _userManager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            if (!result.Succeeded)
            {
                if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
                    return FieldProblem(nameof(ChangePasswordRequest.CurrentPassword), _t["Your current password is incorrect."]);
                return IdentityValidationProblem(result, nameof(ChangePasswordRequest.NewPassword));
            }

            // Οι άλλες συνεδρίες λήγουν (νέο security stamp), η τρέχουσα συνεχίζει
            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("User changed their password successfully.");
            return NoContent();
        }

        // ---------- Two-factor authentication ----------

        // Το κλειδί για το authenticator app (δημιουργείται αν δεν υπάρχει)
        [HttpGet("2fa/authenticator")]
        public async Task<ActionResult<AuthenticatorSetupDto>> Authenticator()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                key = await _userManager.GetAuthenticatorKeyAsync(user);
                // Το reset αλλάζει το security stamp: νέο cookie, αλλιώς η τρέχουσα συνεδρία λήγει
                await _signInManager.RefreshSignInAsync(user);
            }

            var email = await _userManager.GetEmailAsync(user) ?? "";
            var uri = $"otpauth://totp/{UrlEncoder.Default.Encode("Panoptes")}:{UrlEncoder.Default.Encode(email)}?secret={key}&issuer={UrlEncoder.Default.Encode("Panoptes")}&digits=6";

            return new AuthenticatorSetupDto(FormatKey(key!), uri);
        }

        [HttpPost("2fa/enable")]
        public async Task<ActionResult<RecoveryCodesDto>> EnableTwoFactor(CodeRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var code = request.Code.Replace(" ", "").Replace("-", "");
            var valid = await _userManager.VerifyTwoFactorTokenAsync(user, _userManager.Options.Tokens.AuthenticatorTokenProvider, code);
            if (!valid) return FieldProblem(nameof(CodeRequest.Code), _t["That code isn't valid. Check your app and try again."]);

            await _userManager.SetTwoFactorEnabledAsync(user, true);
            await _signInManager.RefreshSignInAsync(user); // νέο security stamp
            _logger.LogInformation("User has enabled 2FA with an authenticator app.");

            var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            return new RecoveryCodesDto(codes?.ToList() ?? new List<string>());
        }

        [HttpPost("2fa/recovery-codes")]
        public async Task<ActionResult<RecoveryCodesDto>> RegenerateRecoveryCodes()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!user.TwoFactorEnabled)
                return Problem(_t["Turn on two-factor authentication first."], statusCode: StatusCodes.Status400BadRequest);

            var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);
            return new RecoveryCodesDto(codes?.ToList() ?? new List<string>());
        }

        [HttpPost("2fa/disable")]
        public async Task<ActionResult<AccountDto>> DisableTwoFactor()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
            if (!result.Succeeded) return Problem(_t["Couldn't turn off two-factor authentication."], statusCode: StatusCodes.Status400BadRequest);

            await _signInManager.RefreshSignInAsync(user); // νέο security stamp
            _logger.LogInformation("User has disabled 2FA.");
            return await ToDtoAsync(user);
        }

        // Νέο κλειδί: το παλιό app σταματάει να δουλεύει, το 2FA κλείνει μέχρι να ξαναστηθεί
        [HttpPost("2fa/reset-authenticator")]
        public async Task<ActionResult<AccountDto>> ResetAuthenticator()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            await _userManager.SetTwoFactorEnabledAsync(user, false);
            await _userManager.ResetAuthenticatorKeyAsync(user);
            await _signInManager.RefreshSignInAsync(user);
            _logger.LogInformation("User has reset their authentication app key.");
            return await ToDtoAsync(user);
        }

        [HttpPost("2fa/forget-browser")]
        public async Task<ActionResult<AccountDto>> ForgetBrowser()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            await _signInManager.ForgetTwoFactorClientAsync();
            return await ToDtoAsync(user) with { IsMachineRemembered = false };
        }

        // ---------- Προσωπικά δεδομένα (GDPR) ----------

        [HttpGet("personal-data")]
        public async Task<IActionResult> DownloadPersonalData()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            _logger.LogInformation("User asked for their personal data.");

            // Μόνο όσα πεδία είναι σημειωμένα [PersonalData] στο IdentityUser
            var data = typeof(IdentityUser).GetProperties()
                .Where(p => Attribute.IsDefined(p, typeof(PersonalDataAttribute)))
                .ToDictionary(p => p.Name, p => p.GetValue(user)?.ToString() ?? "null");

            foreach (var login in await _userManager.GetLoginsAsync(user))
                data[$"{login.LoginProvider} external login provider key"] = login.ProviderKey;

            data["Authenticator Key"] = await _userManager.GetAuthenticatorKeyAsync(user) ?? "null";
            data["Roles"] = string.Join(", ", await _userManager.GetRolesAsync(user));

            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            return File(bytes, "application/json", "PersonalData.json");
        }

        // ---------- Φωτογραφία προφίλ ----------
        // Η εικόνα ανεβαίνει πρώτα στο /api/media/uploads (Purpose = Avatar) και μετά ορίζεται εδώ

        // ---------- Γλώσσα ----------

        [HttpPut("language")]
        public async Task<IActionResult> SetLanguage(LanguageRequest request)
        {
            if (request.Language != null && !AppLanguages.IsSupported(request.Language))
            {
                ModelState.AddModelError(nameof(request.Language), _t["Choose a language from the list."]);
                return ValidationProblem(ModelState);
            }
            await _profiles.SetLanguageAsync(_userManager.GetUserId(User)!, request.Language);
            return NoContent();
        }

        [HttpPut("avatar")]
        public async Task<ActionResult<AvatarDto>> SetAvatar(SetAvatarRequest request)
        {
            var userId = _userManager.GetUserId(User)!;
            if (!await _profiles.SetAvatarAsync(userId, request.MediaId))
                return Problem(_t["That photo isn't available. Upload it again."], statusCode: StatusCodes.Status400BadRequest);
            return new AvatarDto(await _profiles.AvatarUrlAsync(userId));
        }

        [HttpDelete("avatar")]
        public async Task<IActionResult> RemoveAvatar()
        {
            await _profiles.RemoveAvatarAsync(_userManager.GetUserId(User)!);
            return NoContent();
        }

        [HttpPost("delete")]
        public async Task<IActionResult> DeleteAccount(PasswordConfirmRequest request)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Unauthorized();

            if (!await _userManager.CheckPasswordAsync(user, request.Password))
                return FieldProblem(nameof(PasswordConfirmRequest.Password), _t["Incorrect password."]);

            // Δεν αφήνουμε το σύστημα χωρίς διαχειριστή
            if (await _userManager.IsInRoleAsync(user, SystemRoles.Admin) &&
                (await _userManager.GetUsersInRoleAsync(SystemRoles.Admin)).Count <= 1)
            {
                return Problem(_t["You're the only administrator. Make someone else an administrator before deleting your account."],
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var result = await _userManager.DeleteAsync(user);
            if (!result.Succeeded) return Problem(_t["Couldn't delete your account."], statusCode: StatusCodes.Status400BadRequest);
            await _profiles.DeleteAllForUserAsync(user.Id);

            await _signInManager.SignOutAsync();
            _logger.LogInformation("User deleted themselves.");
            return NoContent();
        }

        // ---------- helpers ----------

        private async Task<AccountDto> ToDtoAsync(IdentityUser user) => new(
            user.Id,
            user.Email ?? "",
            user.EmailConfirmed,
            user.PhoneNumber,
            user.TwoFactorEnabled,
            !string.IsNullOrEmpty(await _userManager.GetAuthenticatorKeyAsync(user)),
            await _userManager.CountRecoveryCodesAsync(user),
            await _signInManager.IsTwoFactorClientRememberedAsync(user));

        // "abcdefgh..." -> "abcd efgh ..." (πιο εύκολο να το πληκτρολογήσεις)
        private static string FormatKey(string key)
        {
            var result = new StringBuilder();
            for (var i = 0; i < key.Length; i += 4)
                result.Append(key.AsSpan(i, Math.Min(4, key.Length - i))).Append(' ');
            return result.ToString().TrimEnd().ToLowerInvariant();
        }

        private ActionResult InvalidLink() =>
            Problem(_t["This link is invalid or has expired. Request a new one."], statusCode: StatusCodes.Status400BadRequest);

        private ActionResult FieldProblem(string field, string message)
        {
            ModelState.AddModelError(field, message);
            return ValidationProblem(ModelState);
        }

        private ActionResult IdentityValidationProblem(IdentityResult result, string passwordField = "Password")
        {
            foreach (var error in result.Errors)
            {
                var field = error.Code.StartsWith("Password") ? passwordField
                    : error.Code.Contains("Email") || error.Code.Contains("UserName") ? "Email"
                    : passwordField == "PhoneNumber" ? "PhoneNumber"
                    : "";
                ModelState.AddModelError(field, error.Description);
            }
            return ValidationProblem(ModelState);
        }
    }
}
