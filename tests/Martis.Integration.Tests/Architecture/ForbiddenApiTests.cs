using System.Text.RegularExpressions;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// Fails when Domain or the other engine projects call APIs that break determinism.
/// Comments may name those APIs; call sites may not. The scan covers every
/// <c>.cs</c> file under <c>src</c> (ADR-000 R4).
/// </summary>
[Trait("Category", "Architecture")]
public sealed class ForbiddenApiTests
{
    private static readonly (string Label, Regex Pattern)[] Patterns =
    [
        ("System.Random", Compile(@"\bSystem\.Random\b")),
        ("Random.Shared", Compile(@"\bRandom\.Shared\b")),
        ("new Random", Compile(@"\bnew\s+Random\s*\(")),
        ("GetHashCode", Compile(@"\bGetHashCode\s*\(")),
        ("Parallel.", Compile(@"\bParallel\.")),
        ("AsParallel", Compile(@"\bAsParallel\s*\(")),
        ("Task.Run", Compile(@"\bTask\.Run\s*\(")),
        ("DateTime.Now", Compile(@"\bDateTime\.Now\b")),
        ("DateTime.UtcNow", Compile(@"\bDateTime\.UtcNow\b")),
        ("Guid.NewGuid", Compile(@"\bGuid\.NewGuid\s*\(")),
    ];

    [Fact]
    public void CommentStripperKeepsCodeAndDropsComments()
    {
        const string sample = """
            // System.Random
            /* GetHashCode() */
            var kept = "DateTime.Now";
            var after = Guid.NewGuid();
            var text = $"value {Task.Run(() => 1)}";
            """;

        var stripped = CSharpText.WithoutComments(sample);
        Assert.DoesNotContain("System.Random", stripped, StringComparison.Ordinal);
        Assert.DoesNotContain("GetHashCode", stripped, StringComparison.Ordinal);
        Assert.Contains("DateTime.Now", stripped, StringComparison.Ordinal);
        Assert.Contains("Guid.NewGuid", stripped, StringComparison.Ordinal);
        Assert.Contains("Task.Run", stripped, StringComparison.Ordinal);
    }

    [Fact]
    public void RandomSharedIsDetectedInSampleCodeAndLookalikesAreNot()
    {
        const string usage = """
            var roll = Random.Shared.Next();
            """;

        // Comments may name the API. Identifiers that only look similar are not calls.
        const string lookalikes = """
            // Random.Shared
            /* Random.Shared */
            var joined = RandomShared;
            var prefixed = MyRandom.Shared;
            var longer = Random.SharedState;
            var split = Random . Shared;
            var lower = random.Shared;
            """;

        Assert.Contains("Random.Shared", LabelsIn(usage));
        Assert.DoesNotContain("Random.Shared", LabelsIn(lookalikes));
        Assert.Empty(LabelsIn(lookalikes));
    }

    [Fact]
    public void SourceDoesNotCallNondeterministicApis()
    {
        var root = RepoLayout.Root;
        var files = Directory
            .EnumerateFiles(Path.Combine(root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !IsBuildOutput(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(files);
        Assert.Contains(files, path => path.Contains($"{Path.DirectorySeparatorChar}Martis.Domain{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        Assert.Contains(files, path => path.Contains($"{Path.DirectorySeparatorChar}Martis.Simulation{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        var violations = new List<string>();
        foreach (var file in files)
        {
            foreach (var label in LabelsIn(File.ReadAllText(file)))
            {
                violations.Add($"{Path.GetRelativePath(root, file)}: {label}");
            }
        }

        Assert.True(
            violations.Count == 0,
            "Forbidden API use: " + string.Join("; ", violations));
    }

    private static List<string> LabelsIn(string source)
    {
        var stripped = CSharpText.WithoutComments(source);
        var labels = new List<string>();
        foreach (var (label, pattern) in Patterns)
        {
            if (pattern.IsMatch(stripped))
            {
                labels.Add(label);
            }
        }

        return labels;
    }

    private static Regex Compile(string pattern)
    {
        return new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool IsBuildOutput(string path)
    {
        var parts = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return parts.Contains("bin", StringComparer.OrdinalIgnoreCase)
            || parts.Contains("obj", StringComparer.OrdinalIgnoreCase);
    }
}
