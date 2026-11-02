namespace Panoptes_v1.Constants
{
    // Οι αρχικοί ρόλοι του Hankatt Operations (ARCHITECTURE §16 Q6). Τους φτιάχνει το `dotnet run -- roles:seed`
    // μόνο αν λείπουν· μετά αλλάζουν ελεύθερα από τους Ρόλους. Κάθε νέο module προσθέτει εδώ τα δικά του permissions.
    public static class DefaultRoles
    {
        public const string Manager = "Manager";
        public const string Staff = "Staff";
        public const string Finance = "Finance";
        public const string Support = "Support";

        // Αρχεία: View/Upload, και Edit/Delete μόνο για τα δικά τους
        private static readonly string[] OwnFiles =
        [
            Permissions.Media.View,
            Permissions.Media.Upload,
            Permissions.Media.Edit,
            Permissions.Media.Delete,
        ];

        // Ο Admin δεν χρειάζεται permissions (περνάει όλα τα policies)
        public static readonly IReadOnlyList<(string Name, IReadOnlyList<string> Permissions)> All =
        [
            (SystemRoles.Admin, []),
            // Τα πάντα εκτός από ρυθμίσεις, διαχείριση χρηστών/ρόλων και (αργότερα) οικονομικές διορθώσεις
            (Manager, [.. OwnFiles, Permissions.Media.ManageAll, Permissions.Users.View, Permissions.AuditLogs.View]),
            (Staff, OwnFiles),
            (Finance, [.. OwnFiles, Permissions.Settings.View, Permissions.Finance.Settings]),
            (Support, OwnFiles),
        ];
    }
}
