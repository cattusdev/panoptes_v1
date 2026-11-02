using System.Text.RegularExpressions;
using Panoptes_v1.Constants;

namespace Panoptes_v1.Tests.Architecture;

// Τα permissions του server (Constants/Permissions.cs) και του client (src/lib/permissions.ts) είναι η ίδια λίστα
public partial class PermissionTests
{
    // Γραμμές της μορφής "  Users: {" και "    View: 'Permissions.Users.View',"
    [GeneratedRegex(@"^ {2}(\w+): \{$")]
    private static partial Regex ClientModule();

    [GeneratedRegex(@"^ {4}(\w+): '([^']+)',?$")]
    private static partial Regex ClientPermission();

    private static List<(string Module, string Action, string Name)> ClientPermissions()
    {
        var path = Path.Combine(TestDatabase.SolutionDir, "Panoptes_v1.Client", "src", "lib", "permissions.ts");
        var permissions = new List<(string, string, string)>();
        string? module = null;
        foreach (var line in File.ReadAllLines(path))
        {
            if (ClientModule().Match(line) is { Success: true } moduleMatch)
                module = moduleMatch.Groups[1].Value;
            else if (ClientPermission().Match(line) is { Success: true } permission)
                permissions.Add((module ?? throw new InvalidOperationException($"Permission outside a module: {line}"), permission.Groups[1].Value, permission.Groups[2].Value));
        }
        return permissions;
    }

    private static List<(string Module, string Action, string Name)> ServerPermissions() =>
        Permissions.GetModules().SelectMany(m => m.Permissions.Select(p => (m.Module, p.Action, p.Name))).ToList();

    [Test]
    public void ClientAndServer_HaveTheSamePermissions()
    {
        var client = ClientPermissions();

        Assert.That(client, Is.Not.Empty, "permissions.ts could not be read");
        Assert.That(client, Is.EquivalentTo(ServerPermissions()));
    }

    [Test]
    public void PermissionNames_FollowTheirModuleAndAction_AndAreUnique()
    {
        var server = ServerPermissions();

        Assert.That(server.Select(p => p.Name), Is.EqualTo(server.Select(p => $"Permissions.{p.Module}.{p.Action}")));
        Assert.That(server.Select(p => p.Name), Is.Unique);
    }

    [Test]
    public void DefaultRoles_UseOnlyKnownPermissions()
    {
        var known = Permissions.GetAll();

        Assert.Multiple(() =>
        {
            foreach (var (role, permissions) in DefaultRoles.All)
            {
                Assert.That(known, Is.SupersetOf(permissions), role);
                Assert.That(permissions, Is.Unique, role);
            }
        });
        Assert.That(DefaultRoles.All.Select(r => r.Name), Is.Unique.And.Contains(SystemRoles.Admin));
    }
}
