using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Panoptes_v1.Data;

namespace Panoptes_v1.Tests.Architecture;

// ARCHITECTURE §1.1: ένα module δεν αγγίζει τις οντότητες άλλου module. Διαβάζει μέσα από το service/query interface
// εκείνου, και οι αναφορές ανάμεσα σε modules είναι σκέτα IDs (Guid ClientId), όχι navigation properties.
// Modules = namespaces Panoptes_v1.Modules.<Module> (φάκελος Modules/<Module>).
public class ModuleBoundaryTests
{
    private const string ModulesNamespace = "Panoptes_v1.Modules.";

    private static readonly Assembly App = typeof(ApplicationDbContext).Assembly;

    private static string? ModuleOf(Type type)
    {
        var ns = type.Namespace;
        return ns != null && ns.StartsWith(ModulesNamespace) ? ns[ModulesNamespace.Length..].Split('.')[0] : null;
    }

    // Οι οντότητες (του EF model) κάθε module
    private static Dictionary<Type, string> ModuleEntities()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql("Host=unused").Options;
        using var db = new ApplicationDbContext(options, new HttpContextAccessor());
        return db.GetService<IDesignTimeModel>().Model.GetEntityTypes()
            .Where(e => !e.IsOwned() && ModuleOf(e.ClrType) != null)
            .ToDictionary(e => e.ClrType, e => ModuleOf(e.ClrType)!);
    }

    // Οι τύποι που εμφανίζονται στην "υπογραφή" ενός τύπου (πεδία, properties, constructors, μέθοδοι), με τα generic ορίσματα
    private static IEnumerable<Type> SignatureTypes(Type type)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var direct = type.GetFields(all).Select(f => f.FieldType)
            .Concat(type.GetProperties(all).Select(p => p.PropertyType))
            .Concat(type.GetConstructors(all).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType))
            .Concat(type.GetMethods(all).SelectMany(m => m.GetParameters().Select(p => p.ParameterType).Append(m.ReturnType)))
            .Concat(type.GetInterfaces())
            .Append(type.BaseType ?? typeof(object));
        return direct.SelectMany(Expand);

        static IEnumerable<Type> Expand(Type t) =>
            t.HasElementType ? Expand(t.GetElementType()!)
            : t.IsGenericType ? t.GetGenericArguments().SelectMany(Expand).Append(t)
            : [t];
    }

    [Test]
    public void Modules_DoNotReferenceOtherModulesEntities()
    {
        var entities = ModuleEntities();
        var moduleTypes = App.GetTypes().Where(t => ModuleOf(t) != null).ToList();
        if (moduleTypes.Count == 0)
            Assert.Ignore("No business modules yet: this rule starts checking with the first one (Slice 2, CRM).");

        var violations = moduleTypes
            .SelectMany(type => SignatureTypes(type)
                .Where(used => entities.TryGetValue(used, out var owner) && owner != ModuleOf(type))
                .Select(used => $"{type.FullName} -> {used.FullName}"))
            .Distinct();

        Assert.That(violations, Is.Empty);
    }
}
