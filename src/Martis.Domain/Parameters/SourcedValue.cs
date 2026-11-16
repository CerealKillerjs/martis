namespace Martis.Domain.Parameters;

/// <summary>
/// A value delivered to a system, still carrying the classification, scope,
/// sources, derivation, uncertainty and range of that value (ADR-000 R5).
/// This is not a bare double.
/// </summary>
public sealed class SourcedValue<T>
{
    public SourcedValue(
        string id,
        string field,
        T value,
        string unit,
        ParameterClassification classification,
        ParameterScope scope,
        decimal version,
        string status,
        IReadOnlyList<ParameterSource> sources,
        string? derivedFrom,
        ParameterConversion? conversion,
        string? uncertainty,
        ParameterRange? range)
    {
        Id = id;
        Field = field;
        Value = value;
        Unit = unit;
        Classification = classification;
        Scope = scope;
        Version = version;
        Status = status;
        Sources = sources;
        DerivedFrom = derivedFrom;
        Conversion = conversion;
        Uncertainty = uncertainty;
        Range = range;
    }

    public string Id { get; }

    public string Field { get; }

    public T Value { get; }

    public string Unit { get; }

    public ParameterClassification Classification { get; }

    public ParameterScope Scope { get; }

    public decimal Version { get; }

    public string Status { get; }

    public IReadOnlyList<ParameterSource> Sources { get; }

    /// <summary>Id of the native entry. Null when this value is not derived.</summary>
    public string? DerivedFrom { get; }

    /// <summary>Conversion record. Null when this value is not derived. The factor was not applied.</summary>
    public ParameterConversion? Conversion { get; }

    /// <summary>Raw uncertainty text, or the canonical JSON of an uncertainty object.</summary>
    public string? Uncertainty { get; }

    public ParameterRange? Range { get; }
}
