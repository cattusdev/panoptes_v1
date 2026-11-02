using System.Collections.Generic;
using System.Reflection;
using System.Security.Claims;

namespace Panoptes_v1.Constants
{
    public static class Permissions
    {
        // Όλα τα permissions όλων των modules (μέσω reflection στις nested classes)
        public static List<string> GetAll()
        {
            return typeof(Permissions).GetNestedTypes()
                .SelectMany(module => module.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy))
                .Select(field => field.GetValue(null)?.ToString())
                .Where(permission => !string.IsNullOrEmpty(permission))
                .Select(permission => permission!)
                .ToList();
        }

        // Τα permissions ομαδοποιημένα ανά module (για το UI): ("Users", [("View", "Permissions.Users.View"), ...])
        public static List<(string Module, List<(string Action, string Name)> Permissions)> GetModules()
        {
            return typeof(Permissions).GetNestedTypes()
                .Select(module => (
                    module.Name,
                    module.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                        .Select(field => (field.Name, field.GetValue(null)?.ToString() ?? ""))
                        .Where(p => p.Item2 != "")
                        .ToList()))
                .ToList();
        }

        public static List<string> GeneratePermissionsForModule(string module)
        {
            return new List<string>()
            {
                $"Permissions.{module}.Create",
                $"Permissions.{module}.View",
                $"Permissions.{module}.Edit",
                $"Permissions.{module}.Delete",
            };
        }

        // Module: Users
        public static class Users
        {
            public const string View = "Permissions.Users.View";
            public const string Create = "Permissions.Users.Create";
            public const string Edit = "Permissions.Users.Edit";
            public const string Delete = "Permissions.Users.Delete";
        }

        // Module: Roles
        public static class Roles
        {
            public const string View = "Permissions.Roles.View";
            public const string Create = "Permissions.Roles.Create";
            public const string Edit = "Permissions.Roles.Edit";
            public const string Delete = "Permissions.Roles.Delete";
        }

        public static class AuditLogs
        {
            public const string View = "Permissions.AuditLogs.View";
        }

        // Module: Media. Edit/Delete ισχύουν για τα δικά σου αρχεία· με ManageAll και για των άλλων.
        public static class Media
        {
            public const string View = "Permissions.Media.View";
            public const string Upload = "Permissions.Media.Upload";
            public const string Edit = "Permissions.Media.Edit";
            public const string Delete = "Permissions.Media.Delete";
            public const string ManageAll = "Permissions.Media.ManageAll";
        }

        // Module: Settings (ρυθμίσεις εφαρμογής, π.χ. branding)
        public static class Settings
        {
            public const string View = "Permissions.Settings.View";
            public const string Edit = "Permissions.Settings.Edit";
        }

        // Module: Clients (CRM): πελάτες, επαφές και οι σχέσεις τους. Archive χωριστά από το Edit.
        public static class Clients
        {
            public const string View = "Permissions.Clients.View";
            public const string Create = "Permissions.Clients.Create";
            public const string Edit = "Permissions.Clients.Edit";
            public const string Archive = "Permissions.Clients.Archive";
        }

        // Module: Finance. Τα υπόλοιπα (View, Record, Adjust, Expenses) έρχονται με το module των οικονομικών.
        public static class Finance
        {
            public const string Settings = "Permissions.Finance.Settings"; // Settings → Finance: ΦΠΑ και ημερομηνία έναρξης
        }

        // Ίδιος έλεγχος με τα policies (Program.cs): ο Admin έχει τα πάντα
        public static bool Has(ClaimsPrincipal user, string permission) =>
            user.IsInRole(SystemRoles.Admin) || user.HasClaim("Permission", permission);

        // Μελλοντικό Module (π.χ. Customers) - Απλά θα προσθέσεις class Customers {...}
    }
}