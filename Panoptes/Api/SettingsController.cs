using Microsoft.Extensions.Localization;
using Panoptes.Services.Localization;
using System.ComponentModel.DataAnnotations;
using System.Net.Sockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Panoptes.Constants;
using Panoptes.Data;
using Panoptes.Models;
using Panoptes.Services;
using Panoptes.Services.Settings;

namespace Panoptes.Api
{
    public record GeneralSettingsDto(string DefaultLanguage, string DateFormat, string TimeFormat, string? TimeZone);

    public class GeneralSettingsRequest
    {
        [Required]
        public string DefaultLanguage { get; set; } = "";

        [Required]
        public string DateFormat { get; set; } = "";

        [Required]
        public string TimeFormat { get; set; } = "";

        [StringLength(64)]
        public string? TimeZone { get; set; }
    }

    public record EmailSettingsDto(
        bool Enabled, string Host, int Port, SmtpSecurity Security, string Username, string SenderName, string SenderEmail,
        bool HasPassword, EmailTransport Status);

    public class EmailSettingsRequest
    {
        public bool Enabled { get; set; }

        [StringLength(255)]
        public string Host { get; set; } = "";

        [Range(1, 65535)]
        public int Port { get; set; } = 587;

        public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

        [StringLength(255)]
        public string Username { get; set; } = "";

        // null = κρατάμε το αποθηκευμένο password (δεν το στέλνουμε ποτέ πίσω στο browser)
        [StringLength(500)]
        public string? Password { get; set; }

        public bool ClearPassword { get; set; }

        [StringLength(100)]
        public string SenderName { get; set; } = "";

        // Κενό επιτρέπεται όταν ο δικός μας server είναι κλειστός (ελέγχεται στο ValidateServer)
        [StringLength(255)]
        public string SenderEmail { get; set; } = "";
    }

    public class TestEmailRequest : EmailSettingsRequest
    {
        [StringLength(255), EmailAddress]
        public string? To { get; set; }
    }

    // Ρυθμίσεις της εφαρμογής (Settings στο console). Το branding έχει δικό του controller (/api/branding).
    [ApiController]
    [Route("api/settings")]
    [Authorize]
    public class SettingsController : ControllerBase
    {
        private static readonly Regex TimeZonePattern = new(@"^[A-Za-z][A-Za-z0-9_+\-]*(/[A-Za-z0-9_+\-]+)*$");

        private readonly SettingsService _settings;
        private readonly EmailSender _emailSender;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<SettingsController> _logger;
        private readonly IStringLocalizer<SharedResource> _t;
        private readonly ProfileService _profiles;

        public SettingsController(SettingsService settings, EmailSender emailSender, ApplicationDbContext db, ILogger<SettingsController> logger,
            IStringLocalizer<SharedResource> localizer, ProfileService profiles)
        {
            _t = localizer;
            _profiles = profiles;
            _settings = settings;
            _emailSender = emailSender;
            _db = db;
            _logger = logger;
        }

        // Η μορφή ημερομηνίας χρειάζεται παντού (και χωρίς login)· δεν έχει τίποτα μυστικό
        [HttpGet("general")]
        [AllowAnonymous]
        public async Task<GeneralSettingsDto> General()
        {
            Response.Headers.CacheControl = "no-cache";
            var general = await _settings.GetAsync<GeneralSettings>();
            return new GeneralSettingsDto(general.DefaultLanguage, general.DateFormat, general.TimeFormat, general.TimeZone);
        }

        [HttpPut("general")]
        [Authorize(Policy = Permissions.Settings.Edit)]
        public async Task<ActionResult<GeneralSettingsDto>> UpdateGeneral(GeneralSettingsRequest request)
        {
            if (!AppLanguages.IsSupported(request.DefaultLanguage))
                ModelState.AddModelError(nameof(request.DefaultLanguage), _t["Choose a language from the list."]);
            if (!GeneralSettings.DateFormats.Contains(request.DateFormat))
                ModelState.AddModelError(nameof(request.DateFormat), _t["Choose one of the available date formats."]);
            if (!GeneralSettings.TimeFormats.Contains(request.TimeFormat))
                ModelState.AddModelError(nameof(request.TimeFormat), _t["Choose 24-hour or 12-hour."]);
            var timeZone = string.IsNullOrWhiteSpace(request.TimeZone) ? null : request.TimeZone.Trim();
            if (timeZone != null && !TimeZonePattern.IsMatch(timeZone))
                ModelState.AddModelError(nameof(request.TimeZone), _t["Choose a time zone from the list."]);
            if (!ModelState.IsValid)
                return ValidationProblem(ModelState);

            await _settings.SaveAsync(new GeneralSettings { DefaultLanguage = request.DefaultLanguage, DateFormat = request.DateFormat, TimeFormat = request.TimeFormat, TimeZone = timeZone });
            return await General();
        }

        [HttpGet("email")]
        [Authorize(Policy = Permissions.Settings.View)]
        public async Task<EmailSettingsDto> Email()
        {
            var email = await _settings.GetAsync<EmailSettings>();
            return new EmailSettingsDto(email.Enabled, email.Host, email.Port, email.Security, email.Username, email.SenderName, email.SenderEmail,
                await _settings.HasSecretAsync<EmailSettings>(), await _emailSender.DescribeTransportAsync());
        }

        [HttpPut("email")]
        [Authorize(Policy = Permissions.Settings.Edit)]
        public async Task<ActionResult<EmailSettingsDto>> UpdateEmail(EmailSettingsRequest request)
        {
            if (request.Enabled && !ValidateServer(request))
                return ValidationProblem(ModelState);

            await _settings.SaveAsync(new EmailSettings
            {
                Enabled = request.Enabled,
                Host = request.Host.Trim(),
                Port = request.Port,
                Security = request.Security,
                Username = request.Username.Trim(),
                SenderName = request.SenderName.Trim(),
                SenderEmail = request.SenderEmail.Trim(),
            });
            if (request.ClearPassword)
                await _settings.SetSecretAsync<EmailSettings>(null);
            else if (!string.IsNullOrEmpty(request.Password))
                await _settings.SetSecretAsync<EmailSettings>(request.Password);

            return await Email();
        }

        // Δοκιμή με τις τιμές της φόρμας (και πριν αποθηκευτούν). Κενό password = το αποθηκευμένο.
        [HttpPost("email/test")]
        [Authorize(Policy = Permissions.Settings.Edit)]
        public async Task<IActionResult> SendTestEmail(TestEmailRequest request)
        {
            if (!ValidateServer(request))
                return ValidationProblem(ModelState);

            var to = string.IsNullOrWhiteSpace(request.To) ? User.FindFirstValue(ClaimTypes.Email) : request.To.Trim();
            if (string.IsNullOrEmpty(to))
            {
                ModelState.AddModelError(nameof(request.To), _t["Enter the address to send the test email to."]);
                return ValidationProblem(ModelState);
            }

            var password = request.ClearPassword ? null
                : string.IsNullOrEmpty(request.Password) ? await _settings.GetSecretAsync<EmailSettings>() : request.Password;
            var connection = new SmtpConnection(request.Host.Trim(), request.Port, request.Security, request.Username.Trim(), password,
                request.SenderName.Trim(), request.SenderEmail.Trim());

            var appName = (await _db.Branding.AsNoTracking().Select(b => b.AppName).FirstOrDefaultAsync()) ?? BrandingSettings.DefaultAppName;

            // Το email στη γλώσσα του παραλήπτη (αν έχει λογαριασμό με προτίμηση)· τα μηνύματα λάθους στη γλώσσα του request.
            // Οι μεταφράσεις είναι δικό μας κείμενο (χωρίς HTML)· encode μόνο οι τιμές.
            string subject, html;
            using (AppLanguages.Use(await _profiles.LanguageForEmailAsync(to)))
            {
                subject = _t["Test email from {0}", appName];
                html = $"""
                    <div style="font-family:Arial,sans-serif;max-width:480px;margin:0 auto;padding:24px;color:#111214">
                      <h2 style="margin:0 0 12px;font-weight:600">{_t["Your email settings work"]}</h2>
                      <p style="color:#6b7076;line-height:1.5">{_t["This test email was sent from {0} through {1}.", HtmlEncoder.Default.Encode(appName), $"{HtmlEncoder.Default.Encode(connection.Host)}:{connection.Port}"]}</p>
                    </div>
                    """;
            }

            try
            {
                await _emailSender.SendTestAsync(connection, to, subject, html);
            }
            catch (Exception error) when (SmtpFailure(error, connection) is { } message)
            {
                _logger.LogWarning(error, "Test email through {Host}:{Port} failed", connection.Host, connection.Port);
                return Problem(message, statusCode: StatusCodes.Status400BadRequest, title: _t["The test email wasn't sent"]);
            }

            return Ok(new { to });
        }

        private bool ValidateServer(EmailSettingsRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Host))
                ModelState.AddModelError(nameof(request.Host), _t["Enter the SMTP host."]);
            if (string.IsNullOrWhiteSpace(request.SenderEmail))
                ModelState.AddModelError(nameof(request.SenderEmail), _t["Enter the address emails are sent from."]);
            else if (!new EmailAddressAttribute().IsValid(request.SenderEmail.Trim()))
                ModelState.AddModelError(nameof(request.SenderEmail), _t["Enter a valid email address."]);
            return ModelState.IsValid;
        }

        // Σύντομο, κατανοητό μήνυμα για το UI (χωρίς stack traces / εσωτερικά). null = άγνωστο λάθος, το πιάνει ο GlobalExceptionHandler.
        private string? SmtpFailure(Exception error, SmtpConnection connection)
        {
            var server = $"{connection.Host}:{connection.Port}";
            return error switch
            {
                AuthenticationException => _t["The SMTP server rejected the username or password."],
                SslHandshakeException => _t["Couldn't set up a secure connection with {0}. Check the security setting and the port.", server],
                SocketException => _t["Couldn't connect to {0}. Check the host and the port.", server],
                TimeoutException or OperationCanceledException => _t["{0} didn't answer in time.", server],
                SmtpCommandException command => _t["The SMTP server refused the email: {0}", command.Message],
                SmtpProtocolException or ServiceNotConnectedException => _t["{0} didn't respond like an SMTP server.", server],
                NotSupportedException => _t["{0} doesn't support this security setting. Try another one.", server],
                FormatException or ParseException => _t["One of the email addresses isn't valid."],
                IOException => _t["The connection to {0} was closed unexpectedly.", server],
                _ => (string?)null,
            };
        }
    }
}
