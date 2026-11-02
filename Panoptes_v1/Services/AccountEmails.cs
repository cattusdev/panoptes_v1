using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Localization;
using Panoptes_v1.Services.Localization;

namespace Panoptes_v1.Services
{
    // Τα emails του λογαριασμού (επιβεβαίωση, reset, αλλαγή email). Τα links δείχνουν στις React σελίδες.
    // Στέλνονται στη γλώσσα του παραλήπτη (αν έχει λογαριασμό με προτίμηση), αλλιώς σε αυτή του request.
    public class AccountEmails
    {
        private readonly IEmailSender _emailSender;
        private readonly AppUrls _urls;
        private readonly ProfileService _profiles;
        private readonly IStringLocalizer<SharedResource> _t;

        public AccountEmails(IEmailSender emailSender, AppUrls urls, ProfileService profiles,
            IStringLocalizer<SharedResource> localizer)
        {
            _emailSender = emailSender;
            _urls = urls;
            _profiles = profiles;
            _t = localizer;
        }

        // Τα Identity tokens έχουν χαρακτήρες που χαλάνε στα URLs: τα στέλνουμε Base64Url
        public static string EncodeToken(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

        public static string? DecodeToken(string code)
        {
            try
            {
                return Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(code));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        public Task SendConfirmationAsync(string email, string userId, string token) =>
            SendAsync(email, () => new(_t["Confirm your email"],
                _t["Confirm your email address"],
                _t["Thanks for signing up. Confirm your email address to activate your account."],
                _t["Confirm email"],
                Link("/confirm-email", ("userId", userId), ("code", EncodeToken(token)))));

        public Task SendPasswordResetAsync(string email, string token) =>
            SendAsync(email, () => new(_t["Reset your password"],
                _t["Reset your password"],
                _t["We received a request to reset your password. If it wasn't you, you can ignore this email."],
                _t["Reset password"],
                Link("/reset-password", ("email", email), ("code", EncodeToken(token)))));

        public Task SendEmailChangeAsync(string newEmail, string userId, string token) =>
            SendAsync(newEmail, () => new(_t["Confirm your new email"],
                _t["Confirm your new email address"],
                _t["Confirm this address to start using it to sign in."],
                _t["Confirm new email"],
                Link("/confirm-email-change", ("userId", userId), ("email", newEmail), ("code", EncodeToken(token)))));

        private string Link(string path, params (string Key, string Value)[] query) => _urls.Link(path, query);

        // Όπως το HtmlEncoder.Default, αλλά τα ελληνικά μένουν ως έχουν (όχι &#x...;)
        private static readonly HtmlEncoder TextEncoder = HtmlEncoder.Create(UnicodeRanges.All);

        private record EmailContent(string Subject, string Heading, string Text, string ButtonLabel, string Url);

        // Το περιεχόμενο φτιάχνεται αφού αλλάξει η γλώσσα (γι' αυτό Func)
        private async Task SendAsync(string to, Func<EmailContent> build)
        {
            string subject, html;
            using (AppLanguages.Use(await _profiles.LanguageForEmailAsync(to)))
            {
                var content = build();
                subject = content.Subject;
                var href = HtmlEncoder.Default.Encode(content.Url);
                html = $"""
                    <div style="font-family:Arial,sans-serif;max-width:480px;margin:0 auto;padding:24px;color:#111214">
                      <h2 style="margin:0 0 12px;font-weight:600">{TextEncoder.Encode(content.Heading)}</h2>
                      <p style="color:#6b7076;line-height:1.5">{TextEncoder.Encode(content.Text)}</p>
                      <p style="margin:24px 0">
                        <a href="{href}" style="background:#0e0e10;color:#fff;padding:12px 20px;border-radius:8px;text-decoration:none;font-weight:600">{TextEncoder.Encode(content.ButtonLabel)}</a>
                      </p>
                      <p style="color:#9a9ea5;font-size:12px">{_t["If the button doesn't work, copy this link: {0}", href]}</p>
                    </div>
                    """;
            }
            await _emailSender.SendEmailAsync(to, subject, html);
        }
    }
}
