using System.Reflection;
using Martis.Contracts;
using Martis.Persistence.SQLite;
using Martis.Simulation.Engine;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// Architecture checks that read <see cref="Assembly.GetReferencedAssemblies"/>.
/// Project references that no instruction uses are omitted by the compiler,
/// so <see cref="ProjectGraphTests"/> also reads the project files.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ReferencedAssemblyTests
{
    private static readonly string[] ForbiddenHostPrefixes = ["Godot", "Microsoft.Data.Sqlite", "SQLitePCL"];

    [Fact]
    public void DomainDoesNotReferenceGodotSqliteOrOtherMartisAssemblies()
    {
        var names = ReferencedNames(LoadOutputAssembly("Martis.Domain"));
        AssertNoHostAssemblies(names, "Martis.Domain");
        Assert.DoesNotContain(names, static name => name.StartsWith("Martis.", StringComparison.Ordinal));
        AssertOnlyFramework(names, [], "Martis.Domain");
    }

    [Fact]
    public void SimulationReferencesDomainAndNotTheHostOrOtherMartisProjects()
    {
        var names = ReferencedNames(typeof(EngineAssembly).Assembly);
        Assert.Contains("Martis.Domain", names);
        AssertNoHostAssemblies(names, "Martis.Simulation");
        Assert.DoesNotContain("Martis.Persistence", names);
        Assert.DoesNotContain("Martis.Cli", names);
        Assert.DoesNotContain("Martis.Contracts", names);
        Assert.DoesNotContain("Martis.Game", names);
        AssertOnlyFramework(names, ["Martis.Domain"], "Martis.Simulation");
    }

    [Fact]
    public void ContractsReferencesDomainAndNotSimulationSqliteOrGodot()
    {
        var names = ReferencedNames(typeof(ContractsAssembly).Assembly);
        Assert.Contains("Martis.Domain", names);
        AssertNoHostAssemblies(names, "Martis.Contracts");
        Assert.DoesNotContain("Martis.Simulation", names);
        Assert.DoesNotContain("Martis.Persistence", names);
        Assert.DoesNotContain("Martis.Cli", names);
        AssertOnlyFramework(names, ["Martis.Domain"], "Martis.Contracts");
    }

    [Fact]
    public void PersistenceReferencesDomainContractsAndSqlite()
    {
        var names = ReferencedNames(typeof(SqliteProbe).Assembly);
        Assert.Contains("Martis.Domain", names);
        Assert.Contains("Martis.Contracts", names);
        Assert.Contains(names, static name => name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal));
        Assert.DoesNotContain(names, static name => name.StartsWith("Godot", StringComparison.Ordinal));
        Assert.DoesNotContain("Martis.Simulation", names);
        Assert.DoesNotContain("Martis.Cli", names);

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "Martis.Domain",
            "Martis.Contracts",
        };
        AssertOnlyFramework(names, allowed, "Martis.Persistence", AllowSqlite);
    }

    [Fact]
    public void CliReferencesSimulationAndPersistenceOnly()
    {
        var names = ReferencedNames(Assembly.LoadFrom(RepoLayout.FindCliDll()));
        Assert.Contains("Martis.Simulation", names);
        Assert.Contains("Martis.Persistence", names);
        Assert.DoesNotContain("Martis.Domain", names);
        Assert.DoesNotContain("Martis.Contracts", names);
        Assert.DoesNotContain("Martis.Game", names);
        AssertNoHostAssemblies(names, "Martis.Cli");
        AssertOnlyFramework(names, ["Martis.Simulation", "Martis.Persistence"], "Martis.Cli");
    }

    [Fact]
    public void EngineAssembliesTargetNet10()
    {
        AssertTargetFramework(LoadOutputAssembly("Martis.Domain"));
        AssertTargetFramework(typeof(EngineAssembly).Assembly);
        AssertTargetFramework(typeof(ContractsAssembly).Assembly);
        AssertTargetFramework(typeof(SqliteProbe).Assembly);
        AssertTargetFramework(Assembly.LoadFrom(RepoLayout.FindCliDll()));
    }

    private static void AssertTargetFramework(Assembly assembly)
    {
        var framework = assembly
            .GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()
            ?.FrameworkName;
        Assert.Equal(".NETCoreApp,Version=v10.0", framework);
    }

    private static Assembly LoadOutputAssembly(string assemblyName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, assemblyName + ".dll");
        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"'{assemblyName}' was not copied next to the integration tests.",
                path);
        }

        return Assembly.LoadFrom(path);
    }

    private static List<string> ReferencedNames(Assembly assembly)
    {
        return assembly
            .GetReferencedAssemblies()
            .Select(name => name.Name ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static void AssertNoHostAssemblies(IEnumerable<string> names, string owner)
    {
        foreach (var name in names)
        {
            foreach (var prefix in ForbiddenHostPrefixes)
            {
                Assert.False(
                    name.StartsWith(prefix, StringComparison.Ordinal),
                    $"{owner} references '{name}', which is forbidden (prefix '{prefix}').");
            }
        }
    }

    private static void AssertOnlyFramework(
        IReadOnlyCollection<string> names,
        IEnumerable<string> allowedExtras,
        string owner,
        Func<string, bool>? extraAllowed = null)
    {
        var allowed = new HashSet<string>(allowedExtras, StringComparer.Ordinal);
        var unexpected = names
            .Where(name => !IsFramework(name) && !allowed.Contains(name) && extraAllowed?.Invoke(name) != true)
            .ToArray();
        Assert.True(
            unexpected.Length == 0,
            $"{owner} has unexpected assembly references: {string.Join(", ", unexpected)}. Full set: {string.Join(", ", names)}.");
    }

    private static bool AllowSqlite(string name)
    {
        return name.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal)
            || name.StartsWith("SQLitePCL", StringComparison.Ordinal)
            || name.StartsWith("SQLite", StringComparison.Ordinal);
    }

    private static bool IsFramework(string name)
    {
        if (name is "mscorlib" or "netstandard" or "Microsoft.CSharp" or "Microsoft.VisualBasic" or "Microsoft.Win32")
        {
            return true;
        }

        if (name.StartsWith("Microsoft.Win32.", StringComparison.Ordinal))
        {
            return true;
        }

        return name.StartsWith("System", StringComparison.Ordinal)
            && (name.Length == "System".Length || name["System".Length] == '.');
    }
}
