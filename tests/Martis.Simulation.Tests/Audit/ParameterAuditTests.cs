using Martis.Simulation.Audit;

namespace Martis.Simulation.Tests.Audit;

[Trait("Category", "ParameterAudit")]
public sealed class ParameterAuditTests
{
    [Fact]
    public void EveryDataFileHasAUsableClassification()
    {
        var directory = FindData();
        var errors = ParameterAudit.InspectDirectory(directory);
        Assert.Empty(errors);
    }

    [Fact]
    public void AMissingClassificationIsReported()
    {
        var errors = ParameterAudit.Inspect(Document("""
            { "id": "sample", "value": 1, "unit": "kg" }
            """));
        Assert.Contains(errors, error => error.Contains("sample", StringComparison.Ordinal)
            && error.Contains("classification", StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnknownClassificationIsReported()
    {
        var errors = ParameterAudit.Inspect(Document("""
            { "id": "sample", "classification": "MADE_UP", "value": 1 }
            """));
        Assert.Contains(errors, error => error.Contains("MADE_UP", StringComparison.Ordinal));
    }

    [Fact]
    public void ScientificSourceNullIsReported()
    {
        var errors = ParameterAudit.Inspect(Document("""
            { "id": "sample", "classification": "SCIENTIFIC_MEASURED", "source": null, "value": 1 }
            """));
        Assert.Contains(errors, error => error.Contains("sample", StringComparison.Ordinal)
            && error.Contains("null", StringComparison.Ordinal));
    }

    [Fact]
    public void ScientificSourceWithoutALocatorIsReported()
    {
        var errors = ParameterAudit.Inspect(Document("""
            { "id": "sample", "classification": "SCIENTIFIC_ESTIMATED", "source": [{ "citation": "Fixture", "accessed": "2026-11-16" }], "value": 1 }
            """));
        Assert.Contains(errors, error => error.Contains("sample", StringComparison.Ordinal)
            && error.Contains("DOI", StringComparison.Ordinal));
    }

    private static string Document(string parameter)
        => "{\"parameters\":[" + parameter + "]}";

    private static string FindData()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var data = Path.Combine(directory.FullName, "data");
            if (File.Exists(Path.Combine(directory.FullName, "MARTIS.sln")))
            {
                return data;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("MARTIS.sln was not found above the test output directory.");
    }
}
