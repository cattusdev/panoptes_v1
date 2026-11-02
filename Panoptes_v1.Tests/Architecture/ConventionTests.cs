using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Panoptes_v1.Api;
using Panoptes_v1.Constants;
using Panoptes_v1.Data;
using Panoptes_v1.Models;

namespace Panoptes_v1.Tests.Architecture;

// Κανόνες που ισχύουν για όλο τον κώδικα (CLAUDE.md, ARCHITECTURE §1-§3), ελεγμένοι με reflection
public class ConventionTests
{
    private static readonly Assembly App = typeof(ApplicationDbContext).Assembly;

    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new ApplicationDbContext(options, new HttpContextAccessor());
        return db.GetService<IDesignTimeModel>().Model;
    }

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        App.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes().OfType<IActionHttpMethodProvider>().Any())
                .Select(m => (t, m)));

    private static string Name((Type Controller, MethodInfo Action) a) => $"{a.Controller.Name}.{a.Action.Name}";

    // Optimistic concurrency: RowVersion = το xmin της Postgres, ποτέ μια κανονική στήλη που κανείς δεν ενημερώνει
    [Test]
    public void RowVersions_AreMappedToXmin()
    {
        var rowVersions = Model().GetEntityTypes()
            .Select(e => e.FindProperty("RowVersion"))
            .OfType<IProperty>()
            .ToList();

        Assert.That(rowVersions, Is.Not.Empty);
        Assert.Multiple(() =>
        {
            foreach (var property in rowVersions)
            {
                var entity = property.DeclaringType.DisplayName();
                Assert.That(property.IsConcurrencyToken, entity);
                Assert.That(property.GetColumnName(), Is.EqualTo("xmin"), entity);
                Assert.That(property.ValueGenerated, Is.EqualTo(ValueGenerated.OnAddOrUpdate), entity);
            }
        });
    }

    // Ό,τι κρατάει Updated* αλλάζει, άρα χρειάζεται και έλεγχο ταυτόχρονων αλλαγών
    [Test]
    public void TimestampedEntities_HaveARowVersion()
    {
        var missing = Model().GetEntityTypes()
            .Where(e => typeof(IHasTimestamps).IsAssignableFrom(e.ClrType) && e.FindProperty("RowVersion")?.IsConcurrencyToken != true)
            .Select(e => e.DisplayName());

        Assert.That(missing, Is.Empty);
    }

    // Κάθε endpoint δηλώνει ρητά ποιος το βλέπει: [Authorize...] ή [AllowAnonymous], στο ίδιο ή στον controller
    [Test]
    public void EveryEndpoint_DeclaresItsAccess()
    {
        static bool Declares(MemberInfo member) =>
            member.GetCustomAttributes(inherit: true).Any(a => a is IAuthorizeData or IAllowAnonymous);

        var undeclared = Actions().Where(a => !Declares(a.Action) && !Declares(a.Controller)).Select(Name);

        Assert.That(undeclared, Is.Empty);
    }

    // Policies μόνο από τα permissions (ή το AdminArea), ρόλος μόνο ο Admin: οι άλλοι ρόλοι αλλάζουν από το UI
    [Test]
    public void Endpoints_AuthorizeOnlyWithKnownPermissionsOrTheAdminRole()
    {
        var policies = Permissions.GetAll().Append(SystemRoles.AdminAreaPolicy).ToHashSet();
        var authorizations = Actions()
            .SelectMany(a => a.Action.GetCustomAttributes<AuthorizeAttribute>().Concat(a.Controller.GetCustomAttributes<AuthorizeAttribute>())
                .Select(attribute => (Endpoint: Name(a), attribute.Policy, attribute.Roles)))
            .ToList();

        Assert.Multiple(() =>
        {
            foreach (var (endpoint, policy, roles) in authorizations)
            {
                if (policy != null) Assert.That(policies, Does.Contain(policy), endpoint);
                if (roles != null) Assert.That(roles.Split(',', StringSplitOptions.TrimEntries), Is.All.EqualTo(SystemRoles.Admin), endpoint);
            }
        });
    }

    // Χωρίς ρητό escape character το Npgsql γράφει ESCAPE '' και τα escaped %/_ του Like.Contains δεν ταιριάζουν ποτέ
    [Test]
    public void LikeSearches_PassTheEscapeCharacter()
    {
        var sourceDir = Path.Combine(TestDatabase.SolutionDir, "Panoptes_v1");
        var calls = Directory.EnumerateFiles(sourceDir, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .SelectMany(file => File.ReadLines(file).Select((line, index) => (Where: $"{Path.GetRelativePath(sourceDir, file)}:{index + 1}", Line: line.Trim())))
            .Where(l => !l.Line.StartsWith("//") && (l.Line.Contains("EF.Functions.ILike(") || l.Line.Contains("EF.Functions.Like(")))
            .ToList();

        Assert.That(calls, Is.Not.Empty);
        Assert.That(calls.Where(c => !c.Line.Contains("Like.EscapeCharacter")).Select(c => c.Where), Is.Empty);
    }

    // Κάθε λίστα με σελίδες περνάει από τον κοινό helper: [FromQuery] υποκλάση του ListRequest (typed φίλτρα -> 400)
    [Test]
    public void PagedEndpoints_TakeAListRequest()
    {
        static Type Unwrap(Type type) =>
            type.IsGenericType && (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ActionResult<>))
                ? Unwrap(type.GetGenericArguments()[0])
                : type;

        var paged = Actions()
            .Where(a => Unwrap(a.Action.ReturnType) is { IsGenericType: true } t && t.GetGenericTypeDefinition() == typeof(PagedResult<>))
            .ToList();
        var withoutRequest = paged
            .Where(a => !a.Action.GetParameters().Any(p => typeof(ListRequest).IsAssignableFrom(p.ParameterType) && p.IsDefined(typeof(FromQueryAttribute))))
            .Select(Name);

        Assert.That(paged, Is.Not.Empty);
        Assert.That(withoutRequest, Is.Empty);
    }
}
