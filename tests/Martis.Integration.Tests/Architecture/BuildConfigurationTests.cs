using System.Text.Json.Nodes;
using System.Xml.Linq;

namespace Martis.Integration.Tests.Architecture;

/// <summary>
/// Locks the SDK pin and the shared build settings required by T01.
/// </summary>
[Trait("Category", "Architecture")]
public sealed class BuildConfigurationTests
{
    [Fact]
    public void GlobalJsonPinsTheNet10Sdk()
    {
        var path = Path.Combine(RepoLayout.Root, "global.json");
        var sdk = JsonNode.Parse(File.ReadAllText(path))!["sdk"]!.AsObject();

        Assert.Equal("10.0.404", sdk["version"]!.GetValue<string>());
        Assert.Equal("latestPatch", sdk["rollForward"]!.GetValue<string>());
        Assert.False(sdk["allowPrerelease"]!.GetValue<bool>());
    }

    [Fact]
    public void DirectoryBuildPropsSetsTheRequiredCompilerSwitches()
    {
        var document = XDocument.Load(Path.Combine(RepoLayout.Root, "Directory.Build.props"));
        var properties = document
            .Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .GroupBy(element => element.Name.LocalName, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last().Value.Trim(), StringComparer.Ordinal);

        Assert.Equal("net10.0", properties["TargetFramework"]);
        Assert.Equal("enable", properties["Nullable"]);
        Assert.Equal("enable", properties["ImplicitUsings"]);
        Assert.Equal("latest", properties["LangVersion"]);
        Assert.Equal("true", properties["TreatWarningsAsErrors"]);
        Assert.Equal("true", properties["Deterministic"]);
        Assert.Equal("latest", properties["AnalysisLevel"]);
        Assert.Equal("true", properties["InvariantGlobalization"]);
        Assert.Equal("0.1.0", properties["Version"]);

        var continuous = document
            .Descendants()
            .Where(element => element.Name.LocalName == "ContinuousIntegrationBuild")
            .ToArray();
        Assert.Contains(continuous, element => element.Value.Trim() == "true");
        Assert.Contains(
            continuous,
            element => (element.Attribute("Condition")?.Value ?? string.Empty).Contains("CI", StringComparison.Ordinal));
    }

    [Fact]
    public void PackageVersionsAreExact()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["coverlet.collector"] = "10.1.1",
            ["Microsoft.Data.Sqlite"] = "11.0.0",
            ["Microsoft.NET.Test.Sdk"] = "18.12.0",
            ["xunit"] = "2.9.3",
            ["xunit.runner.visualstudio"] = "4.0.1",
        };

        var document = XDocument.Load(Path.Combine(RepoLayout.Root, "Directory.Packages.props"));
        var central = document
            .Descendants()
            .Single(element => element.Name.LocalName == "ManagePackageVersionsCentrally");
        Assert.Equal("true", central.Value.Trim());

        var actual = document
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .ToDictionary(
                element => element.Attribute("Include")!.Value,
                element => element.Attribute("Version")!.Value,
                StringComparer.Ordinal);

        AssertSameMap(expected, actual);
        foreach (var version in actual.Values)
        {
            Assert.Matches(@"^\d+\.\d+\.\d+$", version);
        }
    }

    private static void AssertSameMap(Dictionary<string, string> expected, Dictionary<string, string> actual)
    {
        var expectedText = string.Join(", ", expected.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"));
        var actualText = string.Join(", ", actual.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"));
        Assert.Equal(expectedText, actualText);
    }
}
