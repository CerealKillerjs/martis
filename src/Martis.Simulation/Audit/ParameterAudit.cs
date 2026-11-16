using System.Text.Json;

namespace Martis.Simulation.Audit;

/// <summary>
/// Checks parameter documents for ADR-000 R5 labels (S1-30).
/// It reads the JSON itself. It does not call <c>ParameterCatalog</c>, so a
/// document can be audited without being delivered to a system.
/// A scientific value needs a source with a DOI or a URL. <c>source: null</c>
/// is rejected for those values. Other classifications may omit the source.
/// </summary>
public static class ParameterAudit
{
    private static readonly string[] Classifications =
    [
        "SCIENTIFIC_MEASURED",
        "SCIENTIFIC_ESTIMATED",
        "MODEL_ASSUMPTION",
        "GAME_BALANCE",
        "SPECULATIVE",
        "UNCLASSIFIED_PENDING",
    ];

    public static IReadOnlyList<string> InspectDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var errors = new List<string>();
        var files = Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (files.Length == 0)
        {
            errors.Add($"No JSON files were found under '{directory}'.");
            return errors;
        }

        foreach (var file in files)
        {
            foreach (var error in Inspect(File.ReadAllText(file)))
            {
                errors.Add(Path.GetFileName(file) + ": " + error);
            }
        }

        return errors;
    }

    public static IReadOnlyList<string> Inspect(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("parameters", out var parameters)
                || parameters.ValueKind != JsonValueKind.Array)
            {
                return ["The document has no parameters array."];
            }

            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var index = 0;
            foreach (var entry in parameters.EnumerateArray())
            {
                InspectEntry(entry, index, seen, errors);
                index++;
            }

            return errors;
        }
        catch (JsonException error)
        {
            return ["The document is not valid JSON. " + error.Message];
        }
    }

    private static void InspectEntry(JsonElement entry, int index, HashSet<string> seen, List<string> errors)
    {
        var label = $"parameters[{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}]";
        if (entry.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{label} is not an object.");
            return;
        }

        if (entry.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            var id = idElement.GetString() ?? string.Empty;
            if (id.Length > 0)
            {
                label = id;
                if (!seen.Add(id))
                {
                    errors.Add($"Duplicate parameter id '{id}'.");
                }
            }
        }

        if (!entry.TryGetProperty("classification", out var classificationElement)
            || classificationElement.ValueKind != JsonValueKind.String)
        {
            errors.Add($"{label} is missing classification.");
            return;
        }

        var classification = classificationElement.GetString() ?? string.Empty;
        var known = false;
        foreach (var candidate in Classifications)
        {
            if (string.Equals(candidate, classification, StringComparison.Ordinal))
            {
                known = true;
                break;
            }
        }

        if (!known)
        {
            errors.Add($"{label} has unknown classification '{classification}'.");
            return;
        }

        if (classification is not ("SCIENTIFIC_MEASURED" or "SCIENTIFIC_ESTIMATED"))
        {
            return;
        }

        if (!entry.TryGetProperty("source", out var source) || source.ValueKind == JsonValueKind.Null)
        {
            errors.Add($"{label} is {classification} and source is null.");
            return;
        }

        if (source.ValueKind != JsonValueKind.Array || !HasLocator(source))
        {
            errors.Add($"{label} is {classification} and needs a source with a DOI or a URL.");
        }
    }

    private static bool HasLocator(JsonElement source)
    {
        foreach (var item in source.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (HasText(item, "doi") || HasText(item, "url"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasText(JsonElement item, string name)
    {
        return item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString());
    }
}
