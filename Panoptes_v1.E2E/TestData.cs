using System.Security.Claims;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Panoptes_v1.E2E;

// Η βάση των tests: σβήνεται, ξαναστήνεται με τα migrations και γεμίζει με γνωστούς χρήστες/ρόλους σε κάθε run
public static class TestData
{
    public const string Password = "E2e-Passw0rd!";

    // Χρήστες (ένας ανά σενάριο, ώστε τα tests να μην πατάνε το ένα το άλλο)
    public const string Admin = "admin@e2e.test";
    public const string UserManager = "usermgr@e2e.test";
    public const string RoleManager = "rolemgr@e2e.test";
    public const string Auditor = "auditor@e2e.test";
    public const string NoAccess = "noaccess@e2e.test";
    public const string Lockout = "lockout@e2e.test";
    public const string Target = "target@e2e.test";
    public const string Promote = "promote@e2e.test";
    public const string TwoFactor = "twofactor@e2e.test";
    public const string Unconfirmed = "unconfirmed@e2e.test"; // Δεν έχει επιβεβαιώσει το email
    public const string NoSmtpUnconfirmed = "nosmtp@e2e.test"; // Το ίδιο, για το email χωρίς SMTP (App_Data/emails)
    public const string Locked = "locked@e2e.test";           // Ήδη κλειδωμένος (για το φίλτρο Status)
    public const string UnlockMe = "unlockme@e2e.test";       // Ήδη κλειδωμένος (για το Unlock)
    public const string DeleteMe = "deleteme@e2e.test";       // Σβήνεται από τα tests
    // Λογαριασμός (Φάση 3): ένας χρήστης ανά σενάριο
    public const string ChangePw = "changepw@e2e.test";
    public const string ChangeEmail = "changeemail@e2e.test";
    public const string Phone = "phone@e2e.test";
    public const string SelfDelete = "selfdelete@e2e.test";
    public const string TwoFaSetup = "twofa-setup@e2e.test";
    public const string TwoFaLogin = "twofa-login@e2e.test"; // 2FA ήδη ενεργό, γνωστό κλειδί/recovery codes
    // Media
    public const string MediaViewer = "mediaviewer@e2e.test";
    public const string MediaEditor = "mediaeditor@e2e.test";
    public const string MediaEditor2 = "mediaeditor2@e2e.test"; // Δεύτερος editor: για τους ελέγχους ιδιοκτησίας
    public const string MediaManager = "mediamanager@e2e.test";
    // Φωτογραφία προφίλ / branding
    public const string AvatarUser = "avatar@e2e.test";
    public const string AvatarUser2 = "avatar2@e2e.test";
    public const string SettingsViewer = "settingsviewer@e2e.test";
    public const string SettingsEditor = "settingseditor@e2e.test"; // Settings.Edit, χωρίς Finance.Settings
    public const string FinanceSettings = "financesettings@e2e.test";
    // CRM: όλα τα Clients.* / μόνο Clients.View
    public const string ClientsManager = "clientsmanager@e2e.test";
    public const string ClientsViewer = "clientsviewer@e2e.test";

    // Γλώσσες: διαλέγει ελληνικά (δικός του χρήστης, για να μην επηρεάζει τα άλλα tests)
    public const string GreekUser = "greek@e2e.test";
    public const string GreekUnconfirmed = "greek-unconfirmed@e2e.test"; // Έχει ήδη διαλέξει ελληνικά, δεν έχει επιβεβαιώσει το email

    public static string TwoFaKey { get; private set; } = "";
    public static IReadOnlyList<string> TwoFaRecoveryCodes { get; private set; } = [];

    private static readonly Dictionary<string, string[]> SeedRoles = new()
    {
        [SystemRoles.Admin] = [], // Superuser: δεν χρειάζεται permissions
        ["UserManager"] = [Permissions.Users.View, Permissions.Users.Create, Permissions.Users.Edit],
        ["Viewer"] = [Permissions.Users.View],
        ["RoleManager"] = [Permissions.Roles.View, Permissions.Roles.Edit],
        ["Support"] = [Permissions.Roles.View],
        ["Auditor"] = [Permissions.AuditLogs.View],
        ["MediaViewer"] = [Permissions.Media.View],
        ["MediaEditor"] = [Permissions.Media.View, Permissions.Media.Upload, Permissions.Media.Edit, Permissions.Media.Delete],
        ["MediaManager"] = [Permissions.Media.View, Permissions.Media.Upload, Permissions.Media.Edit, Permissions.Media.Delete, Permissions.Media.ManageAll],
        ["SettingsViewer"] = [Permissions.Settings.View],
        ["SettingsEditor"] = [Permissions.Settings.View, Permissions.Settings.Edit],
        ["FinanceSettings"] = [Permissions.Settings.View, Permissions.Finance.Settings],
        ["ClientsManager"] = [Permissions.Clients.View, Permissions.Clients.Create, Permissions.Clients.Edit, Permissions.Clients.Archive],
        ["ClientsViewer"] = [Permissions.Clients.View],
    };

    private static readonly Dictionary<string, string?> SeedUsers = new()
    {
        [Admin] = SystemRoles.Admin,
        [UserManager] = "UserManager",
        [RoleManager] = "RoleManager",
        [Auditor] = "Auditor",
        [NoAccess] = null,
        [Lockout] = null,
        [Target] = null,
        [Promote] = null,
        [TwoFactor] = null,
        [Unconfirmed] = null,
        [NoSmtpUnconfirmed] = null,
        [Locked] = null,
        [UnlockMe] = null,
        [DeleteMe] = null,
        [ChangePw] = null,
        [ChangeEmail] = null,
        [Phone] = null,
        [SelfDelete] = null,
        [TwoFaSetup] = null,
        [TwoFaLogin] = "Viewer",
        [MediaViewer] = "MediaViewer",
        [MediaEditor] = "MediaEditor",
        [MediaEditor2] = "MediaEditor",
        [MediaManager] = "MediaManager",
        [AvatarUser] = null,
        [AvatarUser2] = null,
        [SettingsViewer] = "SettingsViewer",
        [SettingsEditor] = "SettingsEditor",
        [FinanceSettings] = "FinanceSettings",
        [ClientsManager] = "ClientsManager",
        [ClientsViewer] = "ClientsViewer",
        [GreekUser] = null,
        [GreekUnconfirmed] = null,
    };

    private static readonly HashSet<string> UnconfirmedUsers = [Unconfirmed, NoSmtpUnconfirmed, GreekUnconfirmed];

    public static string ConnectionString { get; private set; } = "";
    public static Dictionary<string, string> UserIds { get; } = new();
    public static Dictionary<string, string> RoleIds { get; } = new();

    // Ίδιος server/κωδικός με την εφαρμογή, αλλά ξεχωριστή βάση (ή E2E_CONNECTION_STRING)
    public static void Configure(string webProjectDir)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(webProjectDir)
            .AddJsonFile("appsettings.json")
            .AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true)
            .Build();

        var connectionString = Environment.GetEnvironmentVariable("E2E_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            var builder = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"))
            {
                Database = "Panoptesv1_E2E"
            };
            connectionString = builder.ConnectionString;
        }

        // Ασφάλεια: η βάση σβήνεται σε κάθε run, οπότε ποτέ η κανονική
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? "";
        if (!database.Contains("e2e", StringComparison.OrdinalIgnoreCase) &&
            !database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to reset database '{database}': its name must contain 'e2e' or 'test'.");
        }

        ConnectionString = connectionString;
    }

    public static async Task ResetAsync()
    {
        await WithServicesAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();

            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var (roleName, permissions) in SeedRoles)
            {
                var role = new IdentityRole(roleName);
                Check(await roleManager.CreateAsync(role));
                foreach (var permission in permissions)
                    Check(await roleManager.AddClaimAsync(role, new Claim("Permission", permission)));
                RoleIds[roleName] = role.Id;
            }

            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            foreach (var (email, roleName) in SeedUsers)
            {
                var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = !UnconfirmedUsers.Contains(email) };
                Check(await userManager.CreateAsync(user, Password));
                if (roleName != null)
                    Check(await userManager.AddToRoleAsync(user, roleName));
                UserIds[email] = user.Id;
            }

            db.UserProfiles.Add(new UserProfile { UserId = UserIds[GreekUnconfirmed], Language = "el" });
            await db.SaveChangesAsync();

            // 2FA secret: γράφεται στο AspNetUserTokens μέσα από το δικό μας DbContext (άρα περνάει από το audit)
            var twoFactorUser = await userManager.FindByEmailAsync(TwoFactor);
            Check(await userManager.ResetAuthenticatorKeyAsync(twoFactorUser!));

            foreach (var email in new[] { Locked, UnlockMe })
            {
                var lockedUser = await userManager.FindByEmailAsync(email);
                Check(await userManager.SetLockoutEndDateAsync(lockedUser!, DateTimeOffset.UtcNow.AddHours(1)));
            }

            // 2FA ήδη ενεργό, ώστε τα tests του login να μην εξαρτώνται από το test του setup
            var twoFaUser = (await userManager.FindByEmailAsync(TwoFaLogin))!;
            Check(await userManager.ResetAuthenticatorKeyAsync(twoFaUser));
            Check(await userManager.SetTwoFactorEnabledAsync(twoFaUser, true));
            TwoFaKey = (await userManager.GetAuthenticatorKeyAsync(twoFaUser))!;
            TwoFaRecoveryCodes = (await userManager.GenerateNewTwoFactorRecoveryCodesAsync(twoFaUser, 10))!.ToList();
            return true;
        });
    }

    // Για assertions απευθείας στη βάση
    public static async Task<T> WithServicesAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDataProtection();
        services.AddDbContext<ApplicationDbContext>(options => options.UseNpgsql(ConnectionString));
        services.AddIdentityCore<IdentityUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return await action(scope.ServiceProvider);
    }

    public static Task<int> CountUsersAsync() =>
        WithServicesAsync(services => services.GetRequiredService<UserManager<IdentityUser>>().Users.CountAsync());

    public static Task<IdentityUser?> FindUserAsync(string email) =>
        WithServicesAsync(services => services.GetRequiredService<UserManager<IdentityUser>>().FindByEmailAsync(email));

    public static Task<bool> IsInRoleAsync(string email, string roleName) =>
        WithServicesAsync(async services =>
        {
            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var user = await userManager.FindByEmailAsync(email);
            return await userManager.IsInRoleAsync(user!, roleName);
        });

    public static Task<bool> RoleHasPermissionAsync(string roleName, string permission) =>
        WithServicesAsync(async services =>
        {
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
            var role = await roleManager.FindByNameAsync(roleName);
            var claims = await roleManager.GetClaimsAsync(role!);
            return claims.Any(c => c.Type == "Permission" && c.Value == permission);
        });

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException("Seeding failed: " + string.Join(", ", result.Errors.Select(e => e.Description)));
    }
}
