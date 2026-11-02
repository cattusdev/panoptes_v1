using Microsoft.EntityFrameworkCore;
using Panoptes_v1.Api;
using Panoptes_v1.Models;

namespace Panoptes_v1.Tests.Foundations;

// Ο κοινός list helper (ARCHITECTURE §1.4): whitelist ταξινόμησης με tie-breaker, σελίδες με όρια, αναζήτηση χωρίς wildcards.
// Δοκιμάζεται πάνω στον πίνακα Sequences (απλός, χωρίς audit), με δικό του πρόθεμα σε κάθε test.
public class ListQueryTests : DomainTest
{
    private static readonly SortMap<Sequence> Sort = new SortMap<Sequence>("name", s => s.Name)
        .Add("name", s => s.Name)
        .Add("next", s => s.NextValue);

    private async Task<string> SeedAsync(params (string Suffix, long NextValue)[] rows)
    {
        var prefix = Unique("LQ");
        await WithDbAsync(db =>
        {
            db.Sequences.AddRange(rows.Select(r => new Sequence { Name = $"{prefix}{r.Suffix}", NextValue = r.NextValue }));
            return db.SaveChangesAsync();
        });
        return prefix;
    }

    private Task<PagedResult<string>> PageAsync(string prefix, string? sort, int page, int pageSize) =>
        WithDbAsync(async db => (await Sort.Apply(db.Sequences.AsNoTracking().Where(s => s.Name.StartsWith(prefix)), sort)
            .ToPagedAsync(new ListRequest { Sort = sort, Page = page, PageSize = pageSize })).Map(s => s.Name[prefix.Length..]));

    [Test]
    public async Task Sort_UsesOnlyWhitelistedFields_AndFallsBackToTheDefault()
    {
        var prefix = await SeedAsync(("-b", 1), ("-a", 3), ("-c", 2));

        Assert.That((await PageAsync(prefix, "next", 1, 10)).Items, Is.EqualTo(new[] { "-b", "-c", "-a" }));
        Assert.That((await PageAsync(prefix, "-next", 1, 10)).Items, Is.EqualTo(new[] { "-a", "-c", "-b" }));
        Assert.That((await PageAsync(prefix, "NEXT", 1, 10)).Items, Is.EqualTo(new[] { "-b", "-c", "-a" }));
        foreach (var unknown in new[] { null, "", "-", "Name; DROP TABLE", "RowVersion" })
            Assert.That((await PageAsync(prefix, unknown, 1, 10)).Items, Is.EqualTo(new[] { "-a", "-b", "-c" }), unknown);
    }

    // Ίδιες τιμές ταξινόμησης: ο tie-breaker δίνει σταθερή σειρά, άρα κάθε γραμμή εμφανίζεται σε ακριβώς μία σελίδα
    [Test]
    public async Task EqualSortValues_PageWithoutRepeatsOrGaps()
    {
        var suffixes = Enumerable.Range(0, 11).Select(i => $"-{i:D2}").ToArray();
        var prefix = await SeedAsync(suffixes.Select(s => (s, 7L)).ToArray());

        var seen = new List<string>();
        for (var page = 1; page <= 4; page++)
            seen.AddRange((await PageAsync(prefix, "-next", page, 3)).Items);

        Assert.That(seen, Is.EqualTo(suffixes));
    }

    [TestCase(0, 10, 1, 10)]
    [TestCase(-5, 10, 1, 10)]
    [TestCase(1, 0, 1, 1)]
    [TestCase(1, 1000, 1, Paging.MaxPageSize)]
    [TestCase(9, 10, 9, 10)]
    public async Task Paging_IsClamped(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var prefix = await SeedAsync(("-a", 1), ("-b", 1), ("-c", 1));

        var result = await PageAsync(prefix, null, page, pageSize);

        Assert.That((result.Page, result.PageSize, result.Total), Is.EqualTo((expectedPage, expectedPageSize, 3)));
        Assert.That(result.Items, page > 1 ? Is.Empty : Is.Not.Empty);
    }

    [TestCase("0%", new[] { "-50%off" })]
    [TestCase("a_b", new[] { "-a_b" })]
    [TestCase(@"x\y", new[] { @"-x\y" })]
    [TestCase("A_B", new[] { "-a_b" })]
    [TestCase("0", new[] { "-50%off", "-500ff" })]
    public async Task Search_TreatsWildcardsAsText(string term, string[] expected)
    {
        var prefix = await SeedAsync(("-50%off", 1), ("-500ff", 1), ("-a_b", 1), ("-axb", 1), (@"-x\y", 1), ("-xzy", 1));

        var found = await WithDbAsync(db => db.Sequences.AsNoTracking()
            .Where(s => s.Name.StartsWith(prefix) && EF.Functions.ILike(s.Name.Substring(prefix.Length), Like.Contains(term), Like.EscapeCharacter))
            .Select(s => s.Name.Substring(prefix.Length)).ToListAsync());

        Assert.That(found, Is.EquivalentTo(expected));
    }

    [TestCase(null, null)]
    [TestCase("   ", null)]
    [TestCase("  acme ", "acme")]
    public void SearchTerm_IsTrimmed_AndEmptyMeansNone(string? search, string? expected) =>
        Assert.That(new ListRequest { Search = search }.SearchTerm, Is.EqualTo(expected));
}
