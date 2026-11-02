using System.Diagnostics;
using System.Text.Json;
using Panoptes_v1.Services;
using Microsoft.Playwright;
using Npgsql;

namespace Panoptes_v1.E2E;

// Η εγκατάσταση ενός νέου project: dotnet run -- db:migrate / admin:create, και emails χωρίς SMTP
public class SetupCommandTests : E2ETest
{
    private const string Owner = "owner@e2e.test";
    private const string Teammate = "teammate@e2e.test";

    // Άδεια βάση, όπως σε ένα καινούριο project (το όνομα περιέχει E2E: σβήνεται χωρίς φόβο)
    private static string FreshDatabase =>
        new NpgsqlConnectionStringBuilder(TestData.ConnectionString) { Database = "Panoptesv1_E2E_Setup" }.ConnectionString;

    [OneTimeSetUp]
    [OneTimeTearDown]
    public async Task DropFreshDatabaseAsync()
    {
        NpgsqlConnection.ClearAllPools();
        var server = new NpgsqlConnectionStringBuilder(FreshDatabase) { Database = "postgres" };
        await using var connection = new NpgsqlConnection(server.ConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand("DROP DATABASE IF EXISTS \"Panoptesv1_E2E_Setup\" WITH (FORCE)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task NewDatabase_MigrateThenCreateAdmin_AndTheAdminSignsIn()
    {
        var (exitCode, output) = await RunAsync(SetupCommands.Migrate);
        Assert.That(exitCode, Is.EqualTo(0), output);
        Assert.That(output, Does.Contain("Database ready"));
        (exitCode, output) = await RunAsync(SetupCommands.Migrate);
        Assert.That(output, Does.Contain("The database is up to date."));

        // Λάθη: χωρίς email, αδύναμος κωδικός, κανένας κωδικός (χωρίς κονσόλα για να τον ρωτήσει)
        await ExpectFailureAsync(SetupCommands.CreateAdmin, null, "Usage: dotnet run -- admin:create");
        await ExpectFailureAsync($"{SetupCommands.CreateAdmin} {Owner}", "short", "Couldn't create the admin");
        await ExpectFailureAsync($"{SetupCommands.CreateAdmin} {Owner}", null, $"Set {SetupCommands.PasswordVariable}");

        (exitCode, output) = await RunAsync($"{SetupCommands.CreateAdmin} {Owner}", password: TestData.Password);
        Assert.That(exitCode, Is.EqualTo(0), output);
        Assert.That(output, Does.Contain($"{Owner} is an Admin"));

        // Η εφαρμογή πάνω στη νέα βάση: ο Admin μπαίνει στην κονσόλα και φτιάχνει έναν συνάδελφο
        using var app = await AppProcess.StartAsync(new Dictionary<string, string>(AppFixture.DefaultEnvironment())
        {
            ["ConnectionStrings__DefaultConnection"] = FreshDatabase,
        });
        var page = await (await Browser.NewContextAsync(new() { BaseURL = app.BaseUrl })).NewPageAsync();
        await LoginAsync(page, Owner);
        Assert.That(await IsAdminAsync(page), Is.True);
        await page.GotoAsync("/admin/users");
        await Expect(page.GetByRole(AriaRole.Main)).ToContainTextAsync(Owner);
        var created = await ApiAsync(page, "POST", "/api/users", new { email = Teammate, password = TestData.Password });
        Assert.That(created.Status, Is.EqualTo(201), await created.TextAsync());

        // Υπάρχων χρήστης: γίνεται Admin χωρίς κωδικό και ο κωδικός του δεν αλλάζει
        (exitCode, output) = await RunAsync($"{SetupCommands.CreateAdmin} {Teammate}");
        Assert.That(exitCode, Is.EqualTo(0), output);
        var teammate = await (await Browser.NewContextAsync(new() { BaseURL = app.BaseUrl })).NewPageAsync();
        await LoginAsync(teammate, Teammate);
        Assert.That(await IsAdminAsync(teammate), Is.True);
    }

    [Test]
    public async Task WithoutSmtp_DevelopmentWritesEmailsToAppData()
    {
        var email = $"nosmtp-{Guid.NewGuid():N}@e2e.test";
        var environment = new Dictionary<string, string>(AppFixture.DefaultEnvironment()) { ["SmtpSettings__Server"] = "" };
        environment.Remove("Email__PickupDirectory");
        using var app = await AppProcess.StartAsync(environment);

        var page = await (await Browser.NewContextAsync(new() { BaseURL = app.BaseUrl })).NewPageAsync();
        await page.GotoAsync("/register");
        await page.Locator("#email").FillAsync(email);
        await page.Locator("#password").FillAsync(TestData.Password);
        await page.Locator("#register-submit").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Check your email" })).ToBeVisibleAsync();

        var folder = Path.Combine(AppFixture.WebProjectDir, "App_Data", "emails");
        var files = Directory.GetFiles(folder, "*.json").Where(f => File.ReadAllText(f).Contains(email)).ToList();
        try
        {
            Assert.That(files, Has.Count.EqualTo(1));
            var message = JsonSerializer.Deserialize<Mailbox.Email>(File.ReadAllText(files[0]))!;
            Assert.That(message.Subject, Does.Contain("Confirm your email"));
        }
        finally
        {
            files.ForEach(File.Delete);
        }
    }

    private static async Task<bool> IsAdminAsync(IPage page) =>
        (await (await page.APIRequest.GetAsync("/api/auth/session")).JsonAsync())!.Value
            .GetProperty("user").GetProperty("isAdmin").GetBoolean();

    private static async Task ExpectFailureAsync(string arguments, string? password, string message)
    {
        var (exitCode, output) = await RunAsync(arguments, password);
        Assert.That(exitCode, Is.EqualTo(1), output);
        Assert.That(output, Does.Contain(message));
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(string arguments, string? password = null)
    {
        var environment = new Dictionary<string, string>(AppFixture.DefaultEnvironment())
        {
            ["ConnectionStrings__DefaultConnection"] = FreshDatabase,
        };
        var startInfo = AppFixture.DotnetRun(environment, arguments);
        startInfo.RedirectStandardInput = true; // Όπως σε script: καμία κονσόλα για ερωτήσεις
        if (password != null) startInfo.Environment[SetupCommands.PasswordVariable] = password;

        using var process = Process.Start(startInfo)!;
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, await output + await errors);
    }
}
