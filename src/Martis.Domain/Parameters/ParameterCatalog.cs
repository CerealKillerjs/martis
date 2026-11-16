using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Martis.Domain.Parameters;

/// <summary>
/// Loads a parameter document and delivers values by id.
/// The document shape is the scenario root
/// (<c>metadata</c>, <c>location</c>, <c>parameters</c>). This type does not read
/// a scenario file and does not fill an <c>EnvironmentState</c>.
/// PENDING, HISTORICAL and <c>used_in_v0: false</c> stay in <see cref="Definitions"/>
/// and are refused by <see cref="RequireNumber"/> with an error that names the id.
/// </summary>
public sealed class ParameterCatalog
{
    private readonly Dictionary<string, ParameterDefinition> _byId;
    private readonly List<ParameterWarning> _warnings = [];

    private ParameterCatalog(
        string metadataJson,
        string locationJson,
        IReadOnlyList<ParameterDefinition> definitions,
        Dictionary<string, ParameterDefinition> byId)
    {
        MetadataJson = metadataJson;
        LocationJson = locationJson;
        Definitions = definitions;
        _byId = byId;
    }

    /// <summary>Raw JSON object for <c>metadata</c>. The loader does not interpret it.</summary>
    public string MetadataJson { get; }

    /// <summary>Raw JSON object for <c>location</c>. The loader does not interpret it.</summary>
    public string LocationJson { get; }

    /// <summary>Entries in file order, including ones that no system may read.</summary>
    public IReadOnlyList<ParameterDefinition> Definitions { get; }

    /// <summary>Warnings produced by <see cref="NoteRegionalCellUse"/>.</summary>
    public IReadOnlyList<ParameterWarning> Warnings => _warnings;

    public static ParameterCatalog Load(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Disallow,
                AllowTrailingCommas = false,
            });
            return LoadRoot(document.RootElement);
        }
        catch (JsonException error)
        {
            throw new ParameterLoadException("The parameter document is not valid JSON. " + error.Message);
        }
    }

    /// <summary>
    /// Records that a regional cell would use <paramref name="parameterId"/>.
    /// A <c>planetary_mean</c> value produces a warning and is not rewritten.
    /// The method does not return the number. A missing id is an error.
    /// </summary>
    public void NoteRegionalCellUse(string parameterId)
    {
        if (!_byId.TryGetValue(parameterId, out var definition))
        {
            throw new ParameterUnavailableException(parameterId, ParameterUnavailableReason.Missing);
        }

        if (definition.Scope is not ParameterScope.PlanetaryMean)
        {
            return;
        }

        if (_warnings.Exists(warning => string.Equals(warning.ParameterId, parameterId, StringComparison.Ordinal)))
        {
            return;
        }

        _warnings.Add(new ParameterWarning(
            parameterId,
            $"Parameter '{parameterId}' has scope planetary_mean and does not replace a regional measurement."));
    }

    public SourcedValue<double> RequireNumber(string id)
    {
        var definition = RequireDeliverable(id);
        if (definition.Value is not ParameterLiteral.Number number)
        {
            throw WrongKind(id, "a number");
        }

        return Deliver(definition, number.Value);
    }

    public SourcedValue<bool> RequireBool(string id)
    {
        var definition = RequireDeliverable(id);
        if (definition.Value is not ParameterLiteral.Boolean boolean)
        {
            throw WrongKind(id, "a boolean");
        }

        return Deliver(definition, boolean.Value);
    }

    public SourcedValue<string> RequireString(string id)
    {
        var definition = RequireDeliverable(id);
        if (definition.Value is not ParameterLiteral.Text text)
        {
            throw WrongKind(id, "a string");
        }

        return Deliver(definition, text.Value);
    }

    public SourcedValue<ParameterLiteral.Interval> RequireRange(string id)
    {
        var definition = RequireDeliverable(id);
        if (definition.Value is not ParameterLiteral.Interval interval)
        {
            throw WrongKind(id, "an array of two numbers");
        }

        return Deliver(definition, interval);
    }

    private ParameterDefinition RequireDeliverable(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A parameter id is required.", nameof(id));
        }

        if (!_byId.TryGetValue(id, out var definition))
        {
            throw new ParameterUnavailableException(id, ParameterUnavailableReason.Missing);
        }

        if (string.Equals(definition.Status, "PENDING", StringComparison.Ordinal)
            || definition.Classification == ParameterClassification.UnclassifiedPending)
        {
            throw new ParameterUnavailableException(id, ParameterUnavailableReason.Pending);
        }

        if (string.Equals(definition.Status, "HISTORICAL", StringComparison.Ordinal))
        {
            throw new ParameterUnavailableException(id, ParameterUnavailableReason.Historical);
        }

        if (string.Equals(definition.Status, "SUPERADO", StringComparison.Ordinal))
        {
            throw new ParameterUnavailableException(id, ParameterUnavailableReason.Superseded);
        }

        if (definition.UsedInV0 == false)
        {
            throw new ParameterUnavailableException(id, ParameterUnavailableReason.NotUsedInV0);
        }

        return definition;
    }

    private static SourcedValue<T> Deliver<T>(ParameterDefinition definition, T value)
    {
        return new SourcedValue<T>(
            definition.Id,
            definition.Field,
            value,
            definition.Unit,
            definition.Classification,
            definition.Scope,
            definition.Version,
            definition.Status,
            definition.Sources,
            definition.DerivedFrom,
            definition.Conversion,
            definition.Uncertainty,
            definition.Range);
    }

    private static ParameterUnavailableException WrongKind(string id, string expected)
    {
        return new ParameterUnavailableException(
            id,
            ParameterUnavailableReason.WrongKind,
            $"Parameter '{id}' is not {expected}. No default was invented.");
    }

    private static ParameterCatalog LoadRoot(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new ParameterLoadException("The parameter document must be a JSON object.");
        }

        var properties = ReadProperties(root, "$");
        var metadata = RequireKind(properties, "metadata", JsonValueKind.Object, "$");
        var location = RequireKind(properties, "location", JsonValueKind.Object, "$");
        var parameters = RequireKind(properties, "parameters", JsonValueKind.Array, "$");

        var drafts = new List<Draft>();
        var index = 0;
        foreach (var entry in parameters.EnumerateArray())
        {
            drafts.Add(ReadDraft(entry, index));
            index++;
        }

        var byId = new Dictionary<string, ParameterDefinition>(drafts.Count, StringComparer.Ordinal);
        var definitions = new List<ParameterDefinition>(drafts.Count);
        foreach (var draft in drafts)
        {
            if (byId.ContainsKey(draft.Id))
            {
                throw new ParameterLoadException($"Duplicate parameter id '{draft.Id}'.", draft.Id);
            }

            var (derivedFrom, conversion) = ResolveDerivation(draft, drafts);
            var definition = new ParameterDefinition(
                draft.Id,
                draft.Field,
                draft.Value,
                draft.Unit,
                draft.Classification,
                draft.Scope,
                draft.Version,
                draft.Status,
                draft.UsedInV0,
                draft.Sources,
                draft.Range,
                draft.Uncertainty,
                draft.Note,
                draft.Rationale,
                derivedFrom,
                conversion);
            definitions.Add(definition);
            byId.Add(draft.Id, definition);
        }

        return new ParameterCatalog(metadata.GetRawText(), location.GetRawText(), definitions, byId);
    }

    private static (string? DerivedFrom, ParameterConversion? Conversion) ResolveDerivation(Draft draft, List<Draft> drafts)
    {
        if (draft.DerivedFrom is null)
        {
            if (draft.Conversion is not null)
            {
                throw new ParameterLoadException(
                    $"Parameter '{draft.Id}' sets conversion without derived_from.",
                    draft.Id);
            }

            return (null, null);
        }

        if (string.Equals(draft.DerivedFrom, draft.Id, StringComparison.Ordinal))
        {
            throw new ParameterLoadException(
                $"Parameter '{draft.Id}' cannot be derived from itself.",
                draft.Id);
        }

        var native = drafts.Find(item => string.Equals(item.Id, draft.DerivedFrom, StringComparison.Ordinal));
        if (native is null)
        {
            throw new ParameterLoadException(
                $"Parameter '{draft.Id}' derives from '{draft.DerivedFrom}', which is not in the document.",
                draft.Id);
        }

        if (native.DerivedFrom is not null)
        {
            throw new ParameterLoadException(
                $"Parameter '{draft.Id}' must derive from a native value, not from '{draft.DerivedFrom}'.",
                draft.Id);
        }

        if (draft.Classification == ParameterClassification.ScientificMeasured)
        {
            throw new ParameterLoadException(
                $"Parameter '{draft.Id}' is derived from '{draft.DerivedFrom}' and cannot be SCIENTIFIC_MEASURED.",
                draft.Id);
        }

        if (IsScientific(draft.Classification) && !IsScientific(native.Classification))
        {
            throw new ParameterLoadException(
                $"Parameter '{draft.Id}' is {draft.Classification} and derives from '{draft.DerivedFrom}', which is not SCIENTIFIC_*.",
                draft.Id);
        }

        if (draft.Conversion is null)
        {
            throw new ParameterLoadException(
                $"Parameter '{draft.Id}' has derived_from and needs a conversion object.",
                draft.Id);
        }

        return (draft.DerivedFrom, draft.Conversion);
    }

    private static Draft ReadDraft(JsonElement entry, int index)
    {
        var path = $"parameters[{index}]";
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new ParameterLoadException($"{path} must be an object.");
        }

        var properties = ReadProperties(entry, path);
        var id = ReadRequiredString(properties, "id", path);
        path = $"parameters[{index}] '{id}'";
        var field = ReadRequiredString(properties, "field", path);
        if (!properties.TryGetValue("value", out var valueElement))
        {
            throw new ParameterLoadException($"Parameter '{id}' is missing value. No default was invented.", id);
        }

        var value = ReadLiteral(valueElement, id);
        var unit = ReadRequiredString(properties, "unit", path);
        var classification = ReadClassification(properties, id, path);
        var scope = ReadScope(properties, id, path);
        var version = ReadVersion(properties, id, path);
        var status = ReadRequiredString(properties, "status", path);
        var usedInV0 = ReadOptionalBool(properties, "used_in_v0", id);
        var sources = ReadSources(properties, id);
        var range = ReadRange(properties, id);
        var uncertainty = ReadUncertainty(properties, id);
        var note = ReadOptionalText(properties, "note", id);
        var rationale = ReadOptionalText(properties, "rationale", id);
        var derivedFrom = ReadOptionalString(properties, "derived_from", id);
        var conversion = ReadConversion(properties, id);

        if (classification == ParameterClassification.UnclassifiedPending
            && (!string.Equals(status, "PENDING", StringComparison.Ordinal) || value is not ParameterLiteral.None))
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' uses UNCLASSIFIED_PENDING, which is allowed only when status is PENDING and value is null.",
                id);
        }

        if (string.Equals(status, "PENDING", StringComparison.Ordinal) && value is not ParameterLiteral.None)
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' has status PENDING, so its value must be null.",
                id);
        }

        if (IsScientific(classification) && !sources.Any(source => source.HasLocator))
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' is {classification} and needs a source with a DOI or a URL.",
                id);
        }

        if (!IsScientific(classification)
            && sources.Count == 0
            && string.IsNullOrWhiteSpace(note)
            && string.IsNullOrWhiteSpace(rationale))
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' is {classification} without a source and needs a rationale or a note.",
                id);
        }

        if (range is ParameterRange.Interval bounds && bounds.Minimum > bounds.Maximum)
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' has a range whose minimum is greater than its maximum.",
                id);
        }

        if (range is ParameterRange.Interval accepted && !LiesInside(value, accepted, id))
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' is outside its range.",
                id);
        }

        return new Draft(
            id,
            field,
            value,
            unit,
            classification,
            scope,
            version,
            status,
            usedInV0,
            sources,
            range,
            uncertainty,
            note,
            rationale,
            derivedFrom,
            conversion);
    }

    private static bool IsScientific(ParameterClassification classification)
    {
        return classification is ParameterClassification.ScientificMeasured
            or ParameterClassification.ScientificEstimated;
    }

    private static ParameterLiteral ReadLiteral(JsonElement element, string id)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (!element.TryGetDouble(out var number) || !double.IsFinite(number))
                {
                    throw new ParameterLoadException($"Parameter '{id}' has a number that is not a finite double.", id);
                }

                return new ParameterLiteral.Number(number, element.GetRawText());
            case JsonValueKind.True:
                return new ParameterLiteral.Boolean(true);
            case JsonValueKind.False:
                return new ParameterLiteral.Boolean(false);
            case JsonValueKind.String:
                return new ParameterLiteral.Text(element.GetString() ?? string.Empty);
            case JsonValueKind.Null:
                return new ParameterLiteral.None();
            case JsonValueKind.Array:
                return ReadInterval(element, id);
            default:
                throw new ParameterLoadException(
                    $"Parameter '{id}' has a value kind that is not a number, boolean, string, array or null.",
                    id);
        }
    }

    private static ParameterLiteral.Interval ReadInterval(JsonElement element, string id)
    {
        var items = element.EnumerateArray().ToArray();
        if (items.Length != 2 || items[0].ValueKind != JsonValueKind.Number || items[1].ValueKind != JsonValueKind.Number)
        {
            throw new ParameterLoadException($"Parameter '{id}' has an array value that is not [min, max].", id);
        }

        if (!items[0].TryGetDouble(out var minimum) || !double.IsFinite(minimum)
            || !items[1].TryGetDouble(out var maximum) || !double.IsFinite(maximum))
        {
            throw new ParameterLoadException($"Parameter '{id}' has a non-finite endpoint.", id);
        }

        return new ParameterLiteral.Interval(minimum, maximum);
    }

    private static ParameterClassification ReadClassification(
        Dictionary<string, JsonElement> properties,
        string id,
        string path)
    {
        if (!properties.TryGetValue("classification", out var element))
        {
            throw new ParameterLoadException($"Parameter '{id}' is missing classification.", id);
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ParameterLoadException($"{path}: classification must be a string.", id);
        }

        var text = element.GetString() ?? string.Empty;
        return text switch
        {
            "SCIENTIFIC_MEASURED" => ParameterClassification.ScientificMeasured,
            "SCIENTIFIC_ESTIMATED" => ParameterClassification.ScientificEstimated,
            "MODEL_ASSUMPTION" => ParameterClassification.ModelAssumption,
            "GAME_BALANCE" => ParameterClassification.GameBalance,
            "SPECULATIVE" => ParameterClassification.Speculative,
            "UNCLASSIFIED_PENDING" => ParameterClassification.UnclassifiedPending,
            _ => throw new ParameterLoadException($"Parameter '{id}' has unknown classification '{text}'.", id),
        };
    }

    private static ParameterScope ReadScope(Dictionary<string, JsonElement> properties, string id, string path)
    {
        var text = ReadRequiredString(properties, "scope", path);
        if (string.Equals(text, "planetary_mean", StringComparison.Ordinal))
        {
            return new ParameterScope.PlanetaryMean();
        }

        if (text.StartsWith("regional:", StringComparison.Ordinal))
        {
            var site = text["regional:".Length..];
            if (string.IsNullOrWhiteSpace(site))
            {
                throw new ParameterLoadException($"Parameter '{id}' has an empty regional site.", id);
            }

            return new ParameterScope.Regional(site);
        }

        if (text.StartsWith("analog:", StringComparison.Ordinal))
        {
            var name = text["analog:".Length..];
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ParameterLoadException($"Parameter '{id}' has an empty analog name.", id);
            }

            return new ParameterScope.Analog(name);
        }

        throw new ParameterLoadException(
            $"Parameter '{id}' has scope '{text}'. Expected regional:<site>, planetary_mean or analog:<name>.",
            id);
    }

    private static decimal ReadVersion(Dictionary<string, JsonElement> properties, string id, string path)
    {
        if (!properties.TryGetValue("version", out var element))
        {
            throw new ParameterLoadException($"Parameter '{id}' is missing version.", id);
        }

        decimal version;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (!element.TryGetDecimal(out version))
                {
                    throw new ParameterLoadException($"Parameter '{id}' has a version that is not a decimal number.", id);
                }

                break;
            case JsonValueKind.String:
                var text = element.GetString() ?? string.Empty;
                if (!TryParseVersion(text, out version))
                {
                    throw new ParameterLoadException($"Parameter '{id}' has version '{text}', which is not a positive number.", id);
                }

                break;
            default:
                throw new ParameterLoadException($"{path}: version must be a number or a numeric string.", id);
        }

        if (version <= 0m)
        {
            throw new ParameterLoadException($"Parameter '{id}' has version {version}, which is not greater than 0.", id);
        }

        return version;
    }

    private static bool TryParseVersion(string text, out decimal version)
    {
        version = 0m;
        if (text.Length == 0 || char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]))
        {
            return false;
        }

        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint;
        return decimal.TryParse(text, styles, CultureInfo.InvariantCulture, out version);
    }

    private static IReadOnlyList<ParameterSource> ReadSources(Dictionary<string, JsonElement> properties, string id)
    {
        if (!properties.TryGetValue("source", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new ParameterLoadException($"Parameter '{id}' has a source that is not an array or null.", id);
        }

        var sources = new List<ParameterSource>();
        var index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                throw new ParameterLoadException($"Parameter '{id}' source[{index}] must be an object.", id);
            }

            var sourceProperties = ReadProperties(item, $"parameter '{id}' source[{index}]");
            var citation = ReadRequiredString(sourceProperties, "citation", $"parameter '{id}' source[{index}]");
            var accessed = ReadRequiredString(sourceProperties, "accessed", $"parameter '{id}' source[{index}]");
            sources.Add(new ParameterSource(
                citation,
                accessed,
                ReadOptionalString(sourceProperties, "doi", id),
                ReadOptionalString(sourceProperties, "url", id),
                ReadOptionalString(sourceProperties, "where", id)));
            index++;
        }

        return sources;
    }

    private static ParameterRange? ReadRange(Dictionary<string, JsonElement> properties, string id)
    {
        if (!properties.TryGetValue("range", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return new ParameterRange.Text(element.GetString() ?? string.Empty);
        }

        if (element.ValueKind == JsonValueKind.Array)
        {
            var interval = ReadInterval(element, id);
            return new ParameterRange.Interval(interval.Minimum, interval.Maximum);
        }

        throw new ParameterLoadException($"Parameter '{id}' has a range that is not a string or [min, max].", id);
    }

    private static bool? ReadOptionalBool(Dictionary<string, JsonElement> properties, string name, string id)
    {
        if (!properties.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new ParameterLoadException($"Parameter '{id}' has {name} that is not a boolean.", id),
        };
    }

    private static string? ReadOptionalText(Dictionary<string, JsonElement> properties, string name, string id)
    {
        if (!properties.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ParameterLoadException($"Parameter '{id}' has {name} that is not a string.", id);
        }

        return element.GetString();
    }

    private static string? ReadOptionalString(Dictionary<string, JsonElement> properties, string name, string id)
    {
        var text = ReadOptionalText(properties, name, id);
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static string ReadRequiredString(Dictionary<string, JsonElement> properties, string name, string path)
    {
        if (!properties.TryGetValue(name, out var element))
        {
            throw new ParameterLoadException($"{path}: missing {name}.");
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            throw new ParameterLoadException($"{path}: {name} must be a string.");
        }

        var text = element.GetString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ParameterLoadException($"{path}: {name} is empty.");
        }

        return text;
    }

    private static JsonElement RequireKind(
        Dictionary<string, JsonElement> properties,
        string name,
        JsonValueKind kind,
        string path)
    {
        if (!properties.TryGetValue(name, out var element))
        {
            throw new ParameterLoadException($"{path}: missing {name}.");
        }

        if (element.ValueKind != kind)
        {
            throw new ParameterLoadException($"{path}: {name} has the wrong JSON kind.");
        }

        return element;
    }

    private static Dictionary<string, JsonElement> ReadProperties(JsonElement element, string path)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new ParameterLoadException($"{path}: duplicate property '{property.Name}'.");
            }
        }

        return properties;
    }

    private sealed record Draft(
        string Id,
        string Field,
        ParameterLiteral Value,
        string Unit,
        ParameterClassification Classification,
        ParameterScope Scope,
        decimal Version,
        string Status,
        bool? UsedInV0,
        IReadOnlyList<ParameterSource> Sources,
        ParameterRange? Range,
        string? Uncertainty,
        string? Note,
        string? Rationale,
        string? DerivedFrom,
        ParameterConversion? Conversion);

    private static bool LiesInside(ParameterLiteral value, ParameterRange.Interval bounds, string id)
    {
        switch (value)
        {
            case ParameterLiteral.Number number:
                return number.Value >= bounds.Minimum && number.Value <= bounds.Maximum;
            case ParameterLiteral.Interval interval:
                if (interval.Minimum > interval.Maximum)
                {
                    throw new ParameterLoadException(
                        $"Parameter '{id}' has an interval value whose minimum is greater than its maximum.",
                        id);
                }

                return interval.Minimum >= bounds.Minimum
                    && interval.Maximum <= bounds.Maximum;
            default:
                throw new ParameterLoadException(
                    $"Parameter '{id}' has a numeric range and a value that is not a number or an interval.",
                    id);
        }
    }

    private static string? ReadUncertainty(Dictionary<string, JsonElement> properties, string id)
    {
        if (!properties.TryGetValue("uncertainty", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.String)
        {
            return element.GetString();
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            return CanonicalJson(element);
        }

        throw new ParameterLoadException(
            $"Parameter '{id}' has uncertainty that is not a string or an object.",
            id);
    }

    /// <summary>
    /// Compact JSON with object keys sorted by ordinal name.
    /// Leaf tokens are copied as written, so a number is not reinterpreted.
    /// </summary>
    private static string CanonicalJson(JsonElement element)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            WriteCanonical(writer, element);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }

                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(writer, item);
                }

                writer.WriteEndArray();
                break;
            default:
                writer.WriteRawValue(element.GetRawText().Trim());
                break;
        }
    }

    private static ParameterConversion? ReadConversion(Dictionary<string, JsonElement> properties, string id)
    {
        if (!properties.TryGetValue("conversion", out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new ParameterLoadException($"Parameter '{id}' has a conversion that is not an object.", id);
        }

        var fields = ReadProperties(element, $"parameter '{id}' conversion");
        var expression = ReadRequiredString(fields, "expression", $"parameter '{id}' conversion");
        if (!fields.TryGetValue("factor", out var factorElement) || factorElement.ValueKind != JsonValueKind.Number)
        {
            throw new ParameterLoadException($"Parameter '{id}' conversion needs a numeric factor.", id);
        }

        if (!factorElement.TryGetDouble(out var factor) || !double.IsFinite(factor) || factor <= 0.0)
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' conversion factor must be finite and greater than 0.",
                id);
        }

        if (!fields.TryGetValue("factor_classification", out var classificationElement))
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' conversion is missing factor_classification.",
                id);
        }

        var factorClassification = classificationElement.ValueKind == JsonValueKind.String
            ? classificationElement.GetString() ?? string.Empty
            : string.Empty;
        if (!string.Equals(factorClassification, "MODEL_ASSUMPTION", StringComparison.Ordinal))
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' conversion factor_classification must be MODEL_ASSUMPTION.",
                id);
        }

        if (!fields.TryGetValue("assumptions", out var assumptionsElement) || assumptionsElement.ValueKind != JsonValueKind.Array)
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' conversion needs an assumptions array.",
                id);
        }

        var assumptions = new List<string>();
        foreach (var item in assumptionsElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                throw new ParameterLoadException(
                    $"Parameter '{id}' conversion assumptions must be non-empty strings.",
                    id);
            }

            assumptions.Add(item.GetString() ?? string.Empty);
        }

        if (assumptions.Count == 0)
        {
            throw new ParameterLoadException(
                $"Parameter '{id}' conversion assumptions must not be empty.",
                id);
        }

        return new ParameterConversion(expression, factor, factorClassification, assumptions);
    }
}
