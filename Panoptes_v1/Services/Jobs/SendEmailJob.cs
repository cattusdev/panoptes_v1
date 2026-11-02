namespace Panoptes_v1.Services.Jobs
{
    // Email στο παρασκήνιο, με επαναλήψεις αν ο SMTP δεν απαντά. Το περιεχόμενο φτιάχνεται πριν μπει στην ουρά
    // (στη γλώσσα του παραλήπτη, με links από το AppUrls). Ποτέ μυστικά στο payload: φαίνεται στη σελίδα των jobs.
    public record EmailMessage(string To, string Subject, string Html);

    public class SendEmailJob : IJobHandler
    {
        public const string JobType = "email.send";

        private readonly IEmailSender _emailSender;

        public SendEmailJob(IEmailSender emailSender)
        {
            _emailSender = emailSender;
        }

        public string Type => JobType;

        public Task RunAsync(JobContext context, CancellationToken cancellationToken)
        {
            var message = context.Read<EmailMessage>();
            return _emailSender.SendEmailAsync(message.To, message.Subject, message.Html);
        }
    }

    public static class EmailJobs
    {
        // idempotencyKey: π.χ. $"renewal-reminder:{subscriptionId}:{periodEnd:yyyy-MM-dd}" ώστε να μη σταλεί δύο φορές
        public static Task<bool> EnqueueEmailAsync(this JobQueue queue, EmailMessage message, string idempotencyKey,
            DateTime? sendAtUtc = null, CancellationToken cancellationToken = default) =>
            queue.EnqueueAsync(SendEmailJob.JobType, message, $"email:{idempotencyKey}", sendAtUtc, cancellationToken: cancellationToken);
    }
}
