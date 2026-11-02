using System.Text.Json;
using System.Text.Json.Serialization;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Panoptes.Services.Settings;

namespace Panoptes.Services
{
    public interface IEmailSender
    {
        Task SendEmailAsync(string email, string subject, string htmlMessage);
    }

    // Ένας SMTP server (από το UI ή από τα SmtpSettings)
    public record SmtpConnection(string Host, int Port, SmtpSecurity Security, string? Username, string? Password, string? SenderName, string SenderEmail);

    [JsonConverter(typeof(JsonStringEnumConverter<EmailTransportSource>))]
    public enum EmailTransportSource { Pickup, Custom, Configuration, DevelopmentFolder, NotConfigured }

    public record EmailTransport(EmailTransportSource Source, string Detail);

    // Πού πάνε τα emails, με σειρά προτεραιότητας:
    // 1. Email:PickupDirectory (π.χ. στα E2E tests): γράφονται εκεί ως JSON αντί να σταλούν.
    // 2. Ο SMTP server από τις ρυθμίσεις (Settings → Email), αν είναι ενεργός.
    // 3. Τα SmtpSettings (user secrets / env vars, όχι στο appsettings.json).
    // 4. Στο Development χωρίς τίποτα από τα παραπάνω: γράφονται στο App_Data/emails.
    public class EmailSender : IEmailSender
    {
        private const string DevelopmentPickupDirectory = "App_Data/emails";
        private const int TimeoutMs = 15_000;

        private readonly IConfiguration _configuration;
        private readonly IHostEnvironment _environment;
        private readonly SettingsService _settings;
        private readonly ILogger<EmailSender> _logger;

        public EmailSender(IConfiguration configuration, IHostEnvironment environment, SettingsService settings, ILogger<EmailSender> logger)
        {
            _configuration = configuration;
            _environment = environment;
            _settings = settings;
            _logger = logger;
        }

        public async Task SendEmailAsync(string email, string subject, string htmlMessage)
        {
            var pickupDirectory = PickupDirectory;
            if (!string.IsNullOrEmpty(pickupDirectory))
            {
                await WriteToFolderAsync(pickupDirectory, email, subject, htmlMessage);
                return;
            }

            var connection = await CustomConnectionAsync() ?? ConfiguredConnection();
            if (connection != null)
            {
                await SendViaSmtpAsync(connection, email, subject, htmlMessage);
                return;
            }

            if (!_environment.IsDevelopment())
                throw new InvalidOperationException("No SMTP server is configured (Settings → Email or SmtpSettings:Server), so emails can't be sent.");

            pickupDirectory = Path.Combine(_environment.ContentRootPath, DevelopmentPickupDirectory);
            _logger.LogWarning("No SMTP server configured: email \"{Subject}\" to {Email} was written to {Directory}", subject, email, pickupDirectory);
            await WriteToFolderAsync(pickupDirectory, email, subject, htmlMessage);
        }

        // Δοκιμαστικό email με συγκεκριμένο server (και μη αποθηκευμένες τιμές από τη φόρμα).
        // Με Email:PickupDirectory (tests) γράφεται εκεί, όπως όλα τα emails.
        public Task SendTestAsync(SmtpConnection connection, string to, string subject, string htmlMessage)
        {
            var pickupDirectory = PickupDirectory;
            return string.IsNullOrEmpty(pickupDirectory)
                ? SendViaSmtpAsync(connection, to, subject, htmlMessage)
                : WriteToFolderAsync(pickupDirectory, to, subject, htmlMessage);
        }

        // Για το UI: ποιος δρόμος θα χρησιμοποιηθεί αυτή τη στιγμή
        public async Task<EmailTransport> DescribeTransportAsync()
        {
            if (!string.IsNullOrEmpty(PickupDirectory))
                return new(EmailTransportSource.Pickup, "Emails are saved as files (Email:PickupDirectory) instead of being sent.");

            var custom = await CustomConnectionAsync();
            if (custom != null)
                return new(EmailTransportSource.Custom, $"{custom.Host}:{custom.Port}");

            try
            {
                var configured = ConfiguredConnection();
                if (configured != null)
                    return new(EmailTransportSource.Configuration, $"{configured.Host}:{configured.Port}");
            }
            catch (InvalidOperationException error)
            {
                return new(EmailTransportSource.NotConfigured, error.Message);
            }

            return _environment.IsDevelopment()
                ? new(EmailTransportSource.DevelopmentFolder, $"Emails are saved to {DevelopmentPickupDirectory} instead of being sent.")
                : new(EmailTransportSource.NotConfigured, "Emails can't be sent until an SMTP server is set up.");
        }

        public static async Task SendViaSmtpAsync(SmtpConnection connection, string to, string subject, string htmlMessage)
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(connection.SenderName ?? "", connection.SenderEmail));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;
            message.Body = new BodyBuilder { HtmlBody = htmlMessage }.ToMessageBody(); // HTML για να παίζουν τα links (π.χ. Reset Password)

            using var client = new SmtpClient { Timeout = TimeoutMs };
            await client.ConnectAsync(connection.Host, connection.Port, connection.Security switch
            {
                SmtpSecurity.None => SecureSocketOptions.None,
                SmtpSecurity.StartTls => SecureSocketOptions.StartTls,
                SmtpSecurity.SslOnConnect => SecureSocketOptions.SslOnConnect,
                _ => SecureSocketOptions.Auto,
            });
            if (!string.IsNullOrEmpty(connection.Username))
                await client.AuthenticateAsync(connection.Username, connection.Password ?? "");
            await client.SendAsync(message);
            await client.DisconnectAsync(true);
        }

        private string? PickupDirectory => _configuration["Email:PickupDirectory"];

        private async Task<SmtpConnection?> CustomConnectionAsync()
        {
            var settings = await _settings.GetAsync<EmailSettings>();
            if (!settings.Enabled || string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.SenderEmail))
                return null;
            return new SmtpConnection(settings.Host, settings.Port, settings.Security, settings.Username,
                await _settings.GetSecretAsync<EmailSettings>(), settings.SenderName, settings.SenderEmail);
        }

        // SmtpSettings:Security λείπει = STARTTLS (όπως πριν)· διαπιστευτήρια μόνο αν υπάρχει Username
        private SmtpConnection? ConfiguredConnection()
        {
            var section = _configuration.GetSection("SmtpSettings");
            var server = section["Server"];
            if (string.IsNullOrEmpty(server))
                return null;

            var senderEmail = section["SenderEmail"];
            if (string.IsNullOrEmpty(senderEmail))
                throw new InvalidOperationException("SmtpSettings:SenderEmail is missing.");

            var security = Enum.TryParse<SmtpSecurity>(section["Security"], ignoreCase: true, out var parsed) ? parsed : SmtpSecurity.StartTls;
            return new SmtpConnection(server, section.GetValue("Port", 587), security, section["Username"], section["Password"], section["SenderName"], senderEmail);
        }

        private static async Task WriteToFolderAsync(string directory, string to, string subject, string htmlMessage)
        {
            Directory.CreateDirectory(directory);
            var file = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(new { To = to, Subject = subject, Html = htmlMessage }));
        }
    }
}
