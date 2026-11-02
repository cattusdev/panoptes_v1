using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Panoptes.Constants;
using Panoptes.Services;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Panoptes.Api
{
    public record SessionUserDto(string Id, string Email, bool IsAdmin, IReadOnlyList<string> Permissions, string? AvatarUrl, string? Language);

    public record SessionDto(SessionUserDto? User);

    public class LoginRequest
    {
        [Required, EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        public string Password { get; set; } = "";

        public bool RememberMe { get; set; }
    }

    public class TwoFactorLoginRequest
    {
        [Required, StringLength(7, MinimumLength = 6)]
        public string Code { get; set; } = "";

        public bool RememberMe { get; set; }

        // "Να μη ζητηθεί ξανά κωδικός σε αυτή τη συσκευή"
        public bool RememberMachine { get; set; }
    }

    public class RecoveryLoginRequest
    {
        [Required]
        public string RecoveryCode { get; set; } = "";
    }

    // result: success | invalid | lockedOut | requiresTwoFactor | notAllowed | expired (το 2FA βήμα έληξε)
    public record LoginResultDto(string Result, SessionUserDto? User = null);

    [ApiController]
    [Route("api/auth")]
    public class AuthController : ControllerBase
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IAuthorizationService _authorizationService;
        private readonly IAntiforgery _antiforgery;
        private readonly ILogger<AuthController> _logger;
        private readonly ProfileService _profiles;

        public AuthController(
            SignInManager<IdentityUser> signInManager,
            UserManager<IdentityUser> userManager,
            IAuthorizationService authorizationService,
            IAntiforgery antiforgery,
            ILogger<AuthController> logger,
            ProfileService profiles)
        {
            _profiles = profiles;
            _signInManager = signInManager;
            _userManager = userManager;
            _authorizationService = authorizationService;
            _antiforgery = antiforgery;
            _logger = logger;
        }

        // Ποιος είμαι (null αν δεν είμαι συνδεδεμένος). Δίνει και το XSRF-TOKEN cookie στο React app.
        [HttpGet("session")]
        [AllowAnonymous]
        public async Task<SessionDto> Session()
        {
            IssueAntiforgeryCookie();
            return new SessionDto(await BuildUserAsync(User));
        }

        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<LoginResultDto> Login(LoginRequest request)
        {
            // Τα αποτυχημένα logins μετράνε για lockout (5 προσπάθειες -> 5 λεπτά)
            var result = await _signInManager.PasswordSignInAsync(request.Email, request.Password, request.RememberMe, lockoutOnFailure: true);

            if (result.RequiresTwoFactor) return new LoginResultDto("requiresTwoFactor");
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return new LoginResultDto("lockedOut");
            }
            if (result.IsNotAllowed) return new LoginResultDto("notAllowed");
            if (!result.Succeeded) return new LoginResultDto("invalid");

            _logger.LogInformation("User logged in.");

            // Το token του antiforgery είναι δεμένο με τον χρήστη: νέο token για τον συνδεδεμένο χρήστη
            var user = await _userManager.FindByEmailAsync(request.Email);
            HttpContext.User = await _signInManager.CreateUserPrincipalAsync(user!);
            IssueAntiforgeryCookie();

            return new LoginResultDto("success", await BuildUserAsync(HttpContext.User));
        }

        // Υπάρχει login σε εξέλιξη που περιμένει 2FA κωδικό; (αλλιώς η σελίδα 2FA γυρνάει στο /login)
        [HttpGet("two-factor")]
        [AllowAnonymous]
        public async Task<IActionResult> TwoFactorPending()
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            return user == null ? NotFound() : NoContent();
        }

        [HttpPost("login-2fa")]
        [AllowAnonymous]
        public async Task<LoginResultDto> LoginWithTwoFactor(TwoFactorLoginRequest request)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return new LoginResultDto("expired");

            var code = request.Code.Replace(" ", "").Replace("-", "");
            var result = await _signInManager.TwoFactorAuthenticatorSignInAsync(code, request.RememberMe, request.RememberMachine);
            return await TwoFactorResultAsync(result, user);
        }

        [HttpPost("login-recovery")]
        [AllowAnonymous]
        public async Task<LoginResultDto> LoginWithRecoveryCode(RecoveryLoginRequest request)
        {
            var user = await _signInManager.GetTwoFactorAuthenticationUserAsync();
            if (user == null) return new LoginResultDto("expired");

            var result = await _signInManager.TwoFactorRecoveryCodeSignInAsync(request.RecoveryCode.Replace(" ", ""));
            return await TwoFactorResultAsync(result, user);
        }

        private async Task<LoginResultDto> TwoFactorResultAsync(Microsoft.AspNetCore.Identity.SignInResult result, IdentityUser user)
        {
            if (result.IsLockedOut)
            {
                _logger.LogWarning("User account locked out.");
                return new LoginResultDto("lockedOut");
            }
            if (!result.Succeeded) return new LoginResultDto("invalid");

            _logger.LogInformation("User logged in with 2fa.");
            HttpContext.User = await _signInManager.CreateUserPrincipalAsync(user);
            IssueAntiforgeryCookie();
            return new LoginResultDto("success", await BuildUserAsync(HttpContext.User));
        }

        [HttpPost("logout")]
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await _signInManager.SignOutAsync();
            _logger.LogInformation("User logged out.");

            HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity());
            IssueAntiforgeryCookie();
            return NoContent();
        }

        private void IssueAntiforgeryCookie()
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            Response.Cookies.Append("XSRF-TOKEN", tokens.RequestToken!, new CookieOptions
            {
                HttpOnly = false, // Το διαβάζει το JavaScript και το στέλνει ως header
                SameSite = SameSiteMode.Strict,
                Secure = Request.IsHttps,
                Path = "/"
            });
        }

        // Τα permissions υπολογίζονται με τα ίδια policies που ελέγχει ο server (Admin = όλα)
        private async Task<SessionUserDto?> BuildUserAsync(ClaimsPrincipal principal)
        {
            if (principal.Identity?.IsAuthenticated != true) return null;

            var permissions = new List<string>();
            foreach (var permission in Permissions.GetAll())
            {
                if ((await _authorizationService.AuthorizeAsync(principal, permission)).Succeeded)
                    permissions.Add(permission);
            }

            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
            return new SessionUserDto(
                userId,
                principal.Identity.Name ?? "",
                principal.IsInRole(SystemRoles.Admin),
                permissions,
                await _profiles.AvatarUrlAsync(userId),
                await _profiles.LanguageAsync(userId));
        }
    }
}
