using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panoptes_v1.Data;
using Panoptes_v1.Models;

namespace Panoptes_v1.Tests.Foundations;

// Το audit log του ApplicationDbContext (ARCHITECTURE §2): ποιος, τι άλλαξε, στο ίδιο transaction με την αλλαγή,
// χωρίς μυστικά, concurrency tokens και Created*/Updated*. Δοκιμάζεται πάνω στο AppSetting (έχει Secret, IHasTimestamps, RowVersion).
public class AuditTests : DomainTest
{
    private Task<List<AuditLog>> LogsAsync(string traceId) =>
        WithDbAsync(db => db.AuditLogs.AsNoTracking().Where(l => l.TraceId == traceId).OrderBy(l => l.Id).ToListAsync());

    private Task<List<AuditLog>> SettingLogsAsync(string key)
    {
        var primaryKey = JsonSerializer.Serialize(new Dictionary<string, object?> { ["Key"] = key });
        return WithDbAsync(db => db.AuditLogs.AsNoTracking()
            .Where(l => l.TableName == "AppSettings" && l.PrimaryKey == primaryKey)
            .OrderBy(l => l.Id).ToListAsync());
    }

    private static Dictionary<string, JsonElement> Values(string? json) =>
        json == null ? [] : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    private Task AddSettingAsync(string key, string? secret = null) =>
        WithDbAsync(db =>
        {
            db.AppSettings.Add(new AppSetting { Key = key, Value = """{"a":1}""", Secret = secret });
            return db.SaveChangesAsync();
        });

    [Test]
    public async Task RequestChanges_RecordTheUserAndTrace_WithoutSecretsOrBookkeepingColumns()
    {
        var key = Unique("tests.audit");
        var traceId = SignIn("user-1");

        await AddSettingAsync(key, secret: "s3cr3t");
        await WithDbAsync(async db =>
        {
            var setting = await db.AppSettings.SingleAsync(s => s.Key == key);
            setting.Value = """{"a":2}""";
            setting.Secret = "an0ther";
            return await db.SaveChangesAsync();
        });

        var logs = await LogsAsync(traceId);
        Assert.That(logs.Select(l => l.Type), Is.EqualTo(new[] { AuditType.Create, AuditType.Update }));
        Assert.Multiple(() =>
        {
            Assert.That(logs.Select(l => l.UserId), Is.All.EqualTo("user-1"));
            Assert.That(logs.Select(l => l.Source), Is.All.Null);
            Assert.That(logs.Select(l => l.TableName), Is.All.EqualTo("AppSettings"));

            // Ούτε το μυστικό, ούτε το xmin, ούτε τα Created*/Updated* (τα έχει ήδη κάθε γραμμή του log)
            Assert.That(Values(logs[0].NewValues).Keys, Is.EquivalentTo(new[] { "Key", "Value" }));
            Assert.That(logs[1].AffectedColumns, Is.EqualTo("""["Value"]"""));
            Assert.That(Values(logs[1].OldValues)["Value"].GetString(), Is.EqualTo("""{"a": 1}"""));
            Assert.That(Values(logs[1].NewValues)["Value"].GetString(), Is.EqualTo("""{"a":2}"""));
            Assert.That(logs.Select(l => l.NewValues + l.OldValues), Has.None.Contains("s3cr3t").And.None.Contains("an0ther"));
        });
    }

    [Test]
    public async Task ChangesWithoutARequest_AreSignedBySourceOrSystem()
    {
        var bySource = Unique("tests.audit");
        var bySystem = Unique("tests.audit");

        using (AuditContext.Use("cli:tests"))
        {
            // Το Source υπερισχύει του χρήστη: ένα job που τρέχει μέσα σε request δεν "γράφεται" στον χρήστη
            SignIn("user-1");
            await AddSettingAsync(bySource);
            SignOut();
        }
        await AddSettingAsync(bySystem);

        var source = (await SettingLogsAsync(bySource)).Single();
        var system = (await SettingLogsAsync(bySystem)).Single();
        Assert.Multiple(() =>
        {
            Assert.That((source.Source, source.UserId), Is.EqualTo(("cli:tests", (string?)null)));
            Assert.That((system.Source, system.UserId), Is.EqualTo((AuditContext.DefaultSource, (string?)null)));
        });
    }

    [Test]
    public async Task SyncSaveChanges_IsAuditedToo()
    {
        var key = Unique("tests.audit");
        var traceId = SignIn("user-1");

        await WithDbAsync(db =>
        {
            db.AppSettings.Add(new AppSetting { Key = key });
            return Task.FromResult(db.SaveChanges());
        });

        Assert.That((await LogsAsync(traceId)).Select(l => (l.Type, l.TableName)), Is.EqualTo(new[] { (AuditType.Create, "AppSettings") }));
    }

    [Test]
    public async Task FailedSave_LeavesNoAuditRow()
    {
        var key = Unique("tests.audit");
        await AddSettingAsync(key);
        var traceId = SignIn("user-1");

        // Ίδιο κλειδί: η βάση αρνείται την εισαγωγή, και το log της δεν πρέπει να μείνει πίσω
        Assert.ThrowsAsync<DbUpdateException>(() => AddSettingAsync(key));

        Assert.That(await LogsAsync(traceId), Is.Empty);
    }

    [Test]
    public async Task DatabaseGeneratedKeys_AreRecordedAfterTheSave()
    {
        var traceId = SignIn("user-1");
        var claimId = await WithDbAsync(async db =>
        {
            var role = new IdentityRole(Unique("tests-role"));
            role.NormalizedName = role.Name!.ToUpperInvariant();
            db.Roles.Add(role);
            var claim = new IdentityRoleClaim<string> { RoleId = role.Id, ClaimType = "Permission", ClaimValue = "Permissions.Tests.View" };
            db.RoleClaims.Add(claim);
            await db.SaveChangesAsync();
            return claim.Id;
        });

        var claimLog = (await LogsAsync(traceId)).Single(l => l.TableName == "AspNetRoleClaims");
        Assert.That(Values(claimLog.PrimaryKey)["Id"].GetInt32(), Is.EqualTo(claimId).And.GreaterThan(0));
    }

    [Test]
    public async Task AccountSecrets_AreNeverLogged()
    {
        var traceId = SignIn("user-1");
        await WithScopeAsync(async services =>
        {
            var users = services.GetRequiredService<UserManager<IdentityUser>>();
            var email = $"{Unique("audit")}@tests.test";
            var user = new IdentityUser { UserName = email, Email = email, PhoneNumber = "+30 210 0000000" };
            Assert.That((await users.CreateAsync(user, "Tests-Passw0rd!")).Succeeded);
        });

        var log = (await LogsAsync(traceId)).Single(l => l.TableName == "AspNetUsers");
        Assert.That(Values(log.NewValues).Keys, Has.None.AnyOf("PasswordHash", "SecurityStamp", "PhoneNumber", "ConcurrencyStamp"));
        Assert.That(Values(log.NewValues).Keys, Does.Contain("Email"));
    }

    [Test]
    public async Task Timestamps_AreStampedOnSave_AndCreatedOnesNeverChange()
    {
        var key = Unique("tests.timestamps");
        SignIn("creator");
        await AddSettingAsync(key);
        var created = await WithDbAsync(db => db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == key));
        SignOut();

        // Αλλαγή από job: χωρίς χρήστη. Ο κώδικας που "πειράζει" τα Created* αγνοείται.
        using (AuditContext.Use("jobs:tests"))
        {
            await WithDbAsync(async db =>
            {
                var setting = await db.AppSettings.SingleAsync(s => s.Key == key);
                setting.Value = """{"a":2}""";
                setting.CreatedAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                setting.CreatedById = "someone-else";
                return await db.SaveChangesAsync();
            });
        }
        var updated = await WithDbAsync(db => db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == key));

        Assert.Multiple(() =>
        {
            Assert.That(created.CreatedAt, Is.EqualTo(DateTime.UtcNow).Within(TimeSpan.FromMinutes(1)));
            Assert.That((created.CreatedById, created.UpdatedById), Is.EqualTo(("creator", "creator")));
            Assert.That(created.UpdatedAt, Is.EqualTo(created.CreatedAt));

            Assert.That(updated.CreatedAt, Is.EqualTo(created.CreatedAt));
            Assert.That(updated.CreatedById, Is.EqualTo("creator"));
            Assert.That(updated.UpdatedAt, Is.GreaterThan(created.UpdatedAt));
            Assert.That(updated.UpdatedById, Is.Null);
        });
    }

    [Test]
    public async Task StaleWrite_IsRejected_AndNotLogged()
    {
        var key = Unique("tests.concurrency");
        await AddSettingAsync(key);
        var traceId = SignIn("user-1");

        await using var first = Scope();
        await using var second = Scope();
        var firstDb = first.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var mine = await firstDb.AppSettings.SingleAsync(s => s.Key == key);
        var theirs = await secondDb.AppSettings.SingleAsync(s => s.Key == key);

        theirs.Value = """{"who":"theirs"}""";
        await secondDb.SaveChangesAsync();
        mine.Value = """{"who":"mine"}""";

        Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstDb.SaveChangesAsync());
        var stored = await WithDbAsync(db => db.AppSettings.AsNoTracking().SingleAsync(s => s.Key == key));
        Assert.That(stored.Value, Does.Contain("theirs"));
        Assert.That((await LogsAsync(traceId)).Select(l => l.NewValues), Has.One.Contains("theirs").And.None.Contains("mine"));
    }
}
