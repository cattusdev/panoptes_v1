namespace Panoptes_v1.Data
{
    // Ποιος κάνει τις αλλαγές όταν δεν υπάρχει HTTP request (jobs, CLI, καθαρισμοί).
    // using (AuditContext.Use("jobs:email.send")) { ... } -> το audit log γράφει Source = "jobs:email.send".
    public static class AuditContext
    {
        public const string DefaultSource = "system";

        private static readonly AsyncLocal<string?> CurrentSource = new();

        public static string? Source => CurrentSource.Value;

        public static IDisposable Use(string source)
        {
            var previous = CurrentSource.Value;
            CurrentSource.Value = source;
            return new Restore(previous);
        }

        private sealed class Restore(string? previous) : IDisposable
        {
            public void Dispose() => CurrentSource.Value = previous;
        }
    }

    // Ιδιότητα που δεν γράφεται ποτέ στο audit log (μυστικά, tokens, ευαίσθητα δεδομένα)
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class AuditIgnoreAttribute : Attribute { }
}
