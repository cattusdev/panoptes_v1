using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Panoptes_v1.Services
{
    // Εντολές εγκατάστασης (τρέχουν και τελειώνουν, χωρίς web server)
    //   dotnet run -- db:migrate              εφαρμόζει τα migrations (φτιάχνει τη βάση αν λείπει)
    //   dotnet run -- admin:create <email>    ο πρώτος Admin: κωδικός από το ADMIN_PASSWORD ή κρυφή ερώτηση
    public static class SetupCommands
    {
        public const string Migrate = "db:migrate";
        public const string CreateAdmin = "admin:create";
        public const string PasswordVariable = "ADMIN_PASSWORD";

        public static bool TryGet(string[] args, out string command)
        {
            command = args.FirstOrDefault(a => a is Migrate or CreateAdmin) ?? "";
            return command != "";
        }

        public static async Task<int> RunAsync(string command, string[] args, IServiceProvider services)
        {
            using var scope = services.CreateScope();
            try
            {
                return command == Migrate
                    ? await MigrateAsync(scope.ServiceProvider)
                    : await CreateAdminAsync(scope.ServiceProvider, args.SkipWhile(a => a != CreateAdmin).Skip(1).FirstOrDefault());
            }
            catch (Exception e) when (e is Npgsql.NpgsqlException or InvalidOperationException)
            {
                Console.Error.WriteLine($"{command} failed: {e.Message}");
                return 1;
            }
        }

        private static async Task<int> MigrateAsync(IServiceProvider services)
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            await db.Database.MigrateAsync();
            Console.WriteLine(pending.Count == 0
                ? "The database is up to date."
                : $"Database ready: {pending.Count} migration{(pending.Count == 1 ? "" : "s")} applied.");
            return 0;
        }

        // Υπάρχει ήδη ο χρήστης: μόνο μπαίνει στον ρόλο Admin (ο κωδικός του δεν αλλάζει)
        private static async Task<int> CreateAdminAsync(IServiceProvider services, string? email)
        {
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                Console.Error.WriteLine($"Usage: dotnet run -- {CreateAdmin} you@example.com (password from {PasswordVariable}, or asked for)");
                return 1;
            }

            var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
            var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

            if (!await roleManager.RoleExistsAsync(SystemRoles.Admin))
                Check(await roleManager.CreateAsync(new IdentityRole(SystemRoles.Admin)));

            var user = await userManager.FindByEmailAsync(email);
            if (user == null)
            {
                var password = Environment.GetEnvironmentVariable(PasswordVariable);
                if (string.IsNullOrEmpty(password))
                {
                    if (Console.IsInputRedirected)
                    {
                        Console.Error.WriteLine($"Set {PasswordVariable} to the new admin's password.");
                        return 1;
                    }
                    password = ReadHidden("Password: ");
                    if (ReadHidden("Repeat password: ") != password)
                    {
                        Console.Error.WriteLine("The passwords don't match.");
                        return 1;
                    }
                }

                user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
                var created = await userManager.CreateAsync(user, password);
                if (!created.Succeeded)
                {
                    Console.Error.WriteLine("Couldn't create the admin: " + string.Join(" ", created.Errors.Select(e => e.Description)));
                    return 1;
                }
            }

            if (!await userManager.IsInRoleAsync(user, SystemRoles.Admin))
                Check(await userManager.AddToRoleAsync(user, SystemRoles.Admin));

            Console.WriteLine($"{email} is an Admin. Sign in at /login.");
            return 0;
        }

        private static string ReadHidden(string prompt)
        {
            Console.Write(prompt);
            var value = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0) value.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    value.Append(key.KeyChar);
                }
            }
            Console.WriteLine();
            return value.ToString();
        }

        private static void Check(IdentityResult result)
        {
            if (!result.Succeeded)
                throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
