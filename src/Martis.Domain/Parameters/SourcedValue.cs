namespace Martis.Domain.Parameters;

/// <summary>
/// A value delivered to a system, still carrying the classification, scope and
/// sources of that value (ADR-000 R5). This is not a bare double.
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
        IReadOnlyList<ParameterSource> sources)
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
}
