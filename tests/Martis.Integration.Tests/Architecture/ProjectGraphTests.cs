using System.Xml.Linq;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// The project-reference graph of TDD §1.2. Reflection cannot see an unused
/// reference, so this test reads the project files with the BCL XML reader.
/// Simulation may reference Domain and Contracts. Contracts may reference only Domain.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ProjectGraphTests
{
    private static readonly string[] TestPackages =
    [
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.runner.visualstudio",
        "coverlet.collector",
    ];

    private static readonly string[] SimulationTestPackages =
    [
        "Microsoft.NET.Test.Sdk",
        "xunit",
        "xunit.runner.visualstudio",
        "coverlet.collector",
        "CsCheck",
    ];

    private static readonly Dictionary<string, string[]> ExpectedProjectReferences = new(StringComparer.Ordinal)
    {
        ["Martis.Domain"] = [],
        ["Martis.Simulation"] = ["Martis.Domain", "Martis.Contracts"],
        ["Martis.Contracts"] = ["Martis.Domain"],
        ["Martis.Persistence"] = ["Martis.Domain", "Martis.Contracts"],
        ["Martis.Cli"] = ["Martis.Simulation", "Martis.Persistence"],
        ["Martis.Domain.Tests"] = ["Martis.Domain"],
        ["Martis.Simulation.Tests"] = ["Martis.Simulation"],
        ["Martis.Persistence.Tests"] = ["Martis.Persistence"],
        ["Martis.Integration.Tests"] = ["Martis.Simulation", "Martis.Persistence", "Martis.Contracts"],

        // T08: the Godot game sits on the same layer as the CLI (ADR-017).
        ["Martis.Game"] = ["Martis.Application", "Martis.Simulation", "Martis.Persistence"],

        // V1 (Documento Maestro v2.0 §16): orchestration between the game and the engine.
        ["Martis.Application"] = ["Martis.Simulation"],
        ["Martis.Application.Tests"] = ["Martis.Application"],

        // T10 / S1-35: BenchmarkDotNet suite, in the solution under benchmarks/.
        ["Martis.Benchmarks"] = ["Martis.Simulation", "Martis.Contracts"],
    };

    [Fact]
    public void ProjectReferencesMatchTheCanonicalTable()
    {
        var projects = LoadProjects();
        AssertSameSet(
            ExpectedProjectReferences.Keys,
            projects.Keys,
            "The set of projects changed.");

        foreach (var (name, document) in projects)
        {
            var edges = ProjectEdges(document).ToArray();
            var compileReferences = edges.Where(edge => !edge.BuildOnly).Select(edge => edge.Name);
            var buildOnly = edges.Where(edge => edge.BuildOnly).Select(edge => edge.Name).ToArray();

            AssertSameSet(ExpectedProjectReferences[name], compileReferences, $"{name} compile project references");

            // The CLI is not a compile reference of the integration tests. The edge only
            // forces `dotnet test` to build the executable that the smoke test launches.
            if (name == "Martis.Integration.Tests")
            {
                AssertSameSet(["Martis.Cli"], buildOnly, $"{name} build-only project references");
            }
            else
            {
                Assert.Empty(buildOnly);
            }
        }
    }

    [Fact]
    public void PackageReferencesStayInsideTheAllowedSet()
    {
        var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Martis.Domain"] = [],
            ["Martis.Simulation"] = [],
            ["Martis.Contracts"] = [],
            ["Martis.Persistence"] = ["Microsoft.Data.Sqlite"],
            ["Martis.Cli"] = [],
            ["Martis.Application"] = [],
            ["Martis.Application.Tests"] = TestPackages,
            ["Martis.Domain.Tests"] = TestPackages,
            ["Martis.Simulation.Tests"] = SimulationTestPackages,
            ["Martis.Persistence.Tests"] = TestPackages,
            ["Martis.Integration.Tests"] = TestPackages,
            ["Martis.Game"] = [],
            ["Martis.Benchmarks"] = ["BenchmarkDotNet"],
        };

        foreach (var (name, document) in LoadProjects())
        {
            var packages = document
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .Select(element => element.Attribute("Include")?.Value ?? string.Empty)
                .ToArray();

            AssertSameSet(expected[name], packages, $"{name} package references");

            foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "PackageReference"))
            {
                Assert.Null(element.Attribute("Version"));
                Assert.DoesNotContain(element.Elements(), child => child.Name.LocalName == "Version");
            }

            var frameworkReferences = document
                .Descendants()
                .Where(element => element.Name.LocalName == "FrameworkReference")
                .Select(element => element.Attribute("Include")?.Value)
                .ToArray();
            Assert.Empty(frameworkReferences);
        }
    }

    [Fact]
    public void TheGodotGameStaysOutOfTheSolution()
    {
        var solution = File.ReadAllText(Path.Combine(RepoLayout.Root, "MARTIS.sln"));
        Assert.DoesNotContain("Martis.Game", solution, StringComparison.OrdinalIgnoreCase);

        // T08 (ADR-017): the game is a Godot .NET project of its own, built by Godot.NET.Sdk.
        var game = Path.Combine(RepoLayout.Root, "src", "Martis.Game");
        Assert.True(File.Exists(Path.Combine(game, "project.godot")), "src/Martis.Game/project.godot is missing.");
        var project = XDocument.Load(Path.Combine(game, "Martis.Game.csproj"));
        Assert.StartsWith("Godot.NET.Sdk/", project.Root!.Attribute("Sdk")!.Value, StringComparison.Ordinal);
    }

    private static Dictionary<string, XDocument> LoadProjects()
    {
        var projects = new Dictionary<string, XDocument>(StringComparer.Ordinal);
        foreach (var path in Directory.EnumerateFiles(RepoLayout.Root, "*.csproj", SearchOption.AllDirectories))
        {
            if (IsBuildOutput(path))
            {
                continue;
            }

            var name = Path.GetFileNameWithoutExtension(path);
            projects.Add(name, XDocument.Load(path));
        }

        return projects;
    }

    private readonly record struct ProjectEdge(string Name, bool BuildOnly);

    private static IEnumerable<ProjectEdge> ProjectEdges(XDocument document)
    {
        foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "ProjectReference"))
        {
            var include = element.Attribute("Include")?.Value ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(include.Replace('\\', '/'));
            var flag = element.Elements().FirstOrDefault(child => child.Name.LocalName == "ReferenceOutputAssembly")?.Value
                ?? element.Attribute("ReferenceOutputAssembly")?.Value
                ?? "true";
            var buildOnly = string.Equals(flag.Trim(), "false", StringComparison.OrdinalIgnoreCase);
            yield return new ProjectEdge(name, buildOnly);
        }
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || parts.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertSameSet(IEnumerable<string> expected, IEnumerable<string> actual, string message)
    {
        var expectedList = expected.ToArray();
        var actualList = actual.ToArray();
        if (actualList.Length != actualList.Distinct(StringComparer.Ordinal).Count())
        {
            Assert.Fail($"{message} contains duplicate entries: [{string.Join(", ", actualList)}].");
        }

        var expectedSorted = expectedList.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var actualSorted = actualList.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        if (!expectedSorted.SequenceEqual(actualSorted, StringComparer.Ordinal))
        {
            Assert.Fail(
                $"{message}. Expected [{string.Join(", ", expectedSorted)}] but was [{string.Join(", ", actualSorted)}].");
        }
    }
}
