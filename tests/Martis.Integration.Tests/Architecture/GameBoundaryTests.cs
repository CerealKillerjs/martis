using System.Text.RegularExpressions;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// T08 (ROADMAP, ADR-017): the Godot game reads copies (<c>WorldView</c>, <c>CellReport</c>)
/// and sends commands through <c>GameSession</c>. It never touches the mutable engine state.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class GameBoundaryTests
{
    private static readonly (string Label, Regex Pattern)[] Patterns =
    [
        ("GameSession.Engine", new Regex(@"(?<!Martis\.Simulation)\.Engine\b", RegexOptions.CultureInvariant)),
        ("GameSession.Workspace", new Regex(@"\.Workspace\b", RegexOptions.CultureInvariant)),
        ("SimulationState", new Regex(@"\bSimulationState\b", RegexOptions.CultureInvariant)),
        ("SimulationEngine", new Regex(@"\bSimulationEngine\b", RegexOptions.CultureInvariant)),
        ("MetabolismWorkspace", new Regex(@"\bMetabolismWorkspace\b", RegexOptions.CultureInvariant)),
        ("Martis.Domain", new Regex(@"\bMartis\.Domain\b", RegexOptions.CultureInvariant)),
    ];

    [Fact]
    public void TheGameOnlyUsesTheSessionApi()
    {
        var game = Path.Combine(RepoLayout.Root, "src", "Martis.Game");
        var files = Directory.EnumerateFiles(game, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part is "bin" or "obj" or ".godot"))
            .ToArray();
        Assert.NotEmpty(files);

        var violations = new List<string>();
        foreach (var file in files)
        {
            var code = CSharpText.WithoutComments(File.ReadAllText(file));
            violations.AddRange(Patterns
                .Where(pattern => pattern.Pattern.IsMatch(code))
                .Select(pattern => $"{Path.GetRelativePath(RepoLayout.Root, file)}: {pattern.Label}"));
        }

        Assert.True(violations.Count == 0, "The game reaches into the engine: " + string.Join("; ", violations));
    }

    [Fact]
    public void ThePatternsCatchEngineAccessButNotTheVersionHelper()
    {
        Assert.Contains(Patterns, pattern => pattern.Pattern.IsMatch("var p = session.Engine.State;"));
        Assert.DoesNotContain(Patterns, pattern => pattern.Pattern.IsMatch("var v = EngineAssembly.Version;"));
        Assert.DoesNotContain(Patterns, pattern => pattern.Pattern.IsMatch("using Martis.Simulation.Engine;"));
    }
}
