namespace Martis.Domain.Parameters;

/// <summary>
/// One loaded parameter. Classification, scope and sources describe this value,
/// not every value that shares <see cref="Field"/>.
/// A derived entry points at the native entry with <see cref="DerivedFrom"/>.
/// The derived value is never <see cref="ParameterClassification.ScientificMeasured"/>.
/// It may be <see cref="ParameterClassification.ScientificEstimated"/> only when the
/// native value is scientific. <see cref="Conversion"/> records the factor and the
/// assumptions; the loader does not multiply them into <see cref="Value"/>.
/// </summary>
public sealed class ParameterDefinition
{
    public ParameterDefinition(
        string id,
        string field,
        ParameterLiteral value,
        string unit,
        ParameterClassification classification,
        ParameterScope scope,
        decimal version,
        string status,
        bool? usedInV0,
        IReadOnlyList<ParameterSource> sources,
        ParameterRange? range,
        string? uncertainty,
        string? note,
        string? rationale,
        string? derivedFrom,
        ParameterConversion? conversion)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("A parameter needs an id.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("A parameter needs a field.", nameof(field));
        }

        if (string.IsNullOrWhiteSpace(unit))
        {
            throw new ArgumentException("A parameter needs a unit.", nameof(unit));
        }

        if (version <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(version), version, "Version must be greater than 0.");
        }

        if (string.IsNullOrWhiteSpace(status))
        {
            throw new ArgumentException("A parameter needs a status.", nameof(status));
        }

        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(sources);

        Id = id;
        Field = field;
        Value = value;
        Unit = unit;
        Classification = classification;
        Scope = scope;
        Version = version;
        Status = status;
        UsedInV0 = usedInV0;
        Sources = sources;
        Range = range;
        Uncertainty = uncertainty;
        Note = note;
        Rationale = rationale;
        DerivedFrom = string.IsNullOrWhiteSpace(derivedFrom) ? null : derivedFrom;
        Conversion = conversion;
    }

    public string Id { get; }

    public string Field { get; }

    public ParameterLiteral Value { get; }

    public string Unit { get; }

    public ParameterClassification Classification { get; }

    public ParameterScope Scope { get; }

    public decimal Version { get; }

    /// <summary>Status text as written. PENDING, HISTORICAL and SUPERADO are not delivered.</summary>
    public string Status { get; }

    /// <summary>Null when the file omits the flag. False means no v0 system may read the value.</summary>
    public bool? UsedInV0 { get; }

    public IReadOnlyList<ParameterSource> Sources { get; }

    public ParameterRange? Range { get; }

    public string? Uncertainty { get; }

    public string? Note { get; }

    public string? Rationale { get; }

    /// <summary>Id of the native value. Null when this entry is not a derived value.</summary>
    public string? DerivedFrom { get; }

    /// <summary>Present exactly when <see cref="DerivedFrom"/> is set. The factor is not applied.</summary>
    public ParameterConversion? Conversion { get; }
}
