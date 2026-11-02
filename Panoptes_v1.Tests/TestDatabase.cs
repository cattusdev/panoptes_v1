using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Panoptes_v1.Data;

namespace Panoptes_v1.Tests;

// Μία φορά για όλα τα tests: η βάση Panoptesv1_Test σβήνεται και ξαναστήνεται από τα migrations
[SetUpFixture]
public class TestDatabase
{
    public static string ConnectionString { get; private set; } = "";
    public static string SolutionDir { get; } = FindSolutionDir();

    [OneTimeSetUp]
    public async Task ResetAsync()
    {
        ConnectionString = ResolveConnectionString(Path.Combine(SolutionDir, "Panoptes_v1"));

        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(ConnectionString).Options;
        await using var db = new ApplicationDbContext(options, new HttpContextAccessor());
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    // Ίδιος server/κωδικός με την εφαρμογή, αλλά ξεχωριστή βάση (ή TEST_CONNECTION_STRING)
    private static string ResolveConnectionString(string webProjectDir)
    {
        var connectionString = Environment.GetEnvironmentVariable("TEST_CONNECTION_STRING");
        if (string.IsNullOrEmpty(connectionString))
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(webProjectDir)
                .AddJsonFile("appsettings.json")
                .AddUserSecrets(typeof(ApplicationDbContext).Assembly, optional: true)
                .Build();

            connectionString = new NpgsqlConnectionStringBuilder(config.GetConnectionString("DefaultConnection"))
            {
                Database = "Panoptesv1_Test"
            }.ConnectionString;
        }

        // Ασφάλεια: η βάση σβήνεται σε κάθε run, οπότε ποτέ η κανονική
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database ?? "";
        if (!database.Contains("e2e", StringComparison.OrdinalIgnoreCase) &&
            !database.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to reset database '{database}': its name must contain 'e2e' or 'test'.");
        }

        return connectionString;
    }

    private static string FindSolutionDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Panoptes_v1.sln")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not find Panoptes_v1.sln above the test output folder.");
    }
}
