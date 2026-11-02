using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Panoptes_v1.Data;
using Panoptes_v1.Services;

namespace Panoptes_v1.Tests.Foundations;

// Κωδικοί εγγραφών (ARCHITECTURE §2): χωρίς κενά, χωρίς διπλούς, η χρονιά είναι της επιχείρησης
public class ReferenceTests : DomainTest
{
    private static ReferenceFormat NewFormat(bool yearly = false) =>
        new($"Q{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}", 4, yearly);

    // Ένας κωδικός μέσα σε δικό του transaction, όπως όταν σώζεται μια εγγραφή
    private Task<string> TakeAsync(ReferenceFormat format, bool commit = true) =>
        WithScopeAsync(async services =>
        {
            var db = services.GetRequiredService<ApplicationDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            var reference = await services.GetRequiredService<ReferenceGenerator>().NextAsync(format);
            if (commit) await transaction.CommitAsync();
            else await transaction.RollbackAsync();
            return reference;
        });

    [TestCase(42, null, "CL-0042")]
    [TestCase(10000, null, "CL-10000")]
    [TestCase(153, 2026, "CL-2026-0153")]
    public void Format_PadsToTheMinimumDigits_AndGrowsBeyondThem(long number, int? year, string expected) =>
        Assert.That(new ReferenceFormat("CL", 4, Yearly: year != null).Format(number, year), Is.EqualTo(expected));

    [Test]
    public async Task ConcurrentSaves_GetGaplessUniqueNumbers()
    {
        var format = NewFormat();

        var references = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => TakeAsync(format))));

        Assert.That(references, Is.EquivalentTo(Enumerable.Range(1, 20).Select(n => format.Format(n, null))));
    }

    [Test]
    public async Task RolledBackSave_GivesItsNumberBack()
    {
        var format = NewFormat();

        Assert.That(await TakeAsync(format, commit: false), Is.EqualTo(format.Format(1, null)));
        Assert.That(await TakeAsync(format), Is.EqualTo(format.Format(1, null)));
        Assert.That(await TakeAsync(format), Is.EqualTo(format.Format(2, null)));
    }

    [Test]
    public async Task OutsideATransaction_IsRefused_WithoutUsingANumber()
    {
        var format = NewFormat();

        await WithScopeAsync(services =>
        {
            var generator = services.GetRequiredService<ReferenceGenerator>();
            Assert.ThrowsAsync<InvalidOperationException>(() => generator.NextAsync(format));
            return Task.CompletedTask;
        });

        Assert.That(await TakeAsync(format), Is.EqualTo(format.Format(1, null)));
    }

    [Test]
    public async Task YearlySeries_FollowTheBusinessYear_AndRestartOnNewYear()
    {
        var format = NewFormat(yearly: true);

        // 23:59 στην Αθήνα της 31/12: ακόμα 2026
        Clock.Now = new DateTimeOffset(2026, 12, 31, 21, 59, 0, TimeSpan.Zero);
        Assert.That(await TakeAsync(format), Is.EqualTo(format.Format(1, 2026)));
        Assert.That(await TakeAsync(format), Is.EqualTo(format.Format(2, 2026)));

        // 00:00 στην Αθήνα (22:00 UTC): νέα χρονιά, νέα σειρά από το 1, ενώ σε UTC είναι ακόμα 2026
        Clock.Now = new DateTimeOffset(2026, 12, 31, 22, 0, 0, TimeSpan.Zero);
        Assert.That(await TakeAsync(format), Is.EqualTo(format.Format(1, 2027)));

        var sequences = await WithDbAsync(db => db.Sequences.AsNoTracking()
            .Where(s => s.Name.StartsWith(format.Prefix)).ToDictionaryAsync(s => s.Name, s => s.NextValue));
        Assert.That(sequences, Is.EqualTo(new Dictionary<string, long>
        {
            [$"{format.Prefix}-2026"] = 3,
            [$"{format.Prefix}-2027"] = 2,
        }));
    }
}
