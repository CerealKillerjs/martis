namespace Martis.Domain.Parameters;

/// <summary>
/// One citation attached to a parameter value. Provenance stays with the value.
/// <see cref="Doi"/> and <see cref="Url"/> are optional on their own; a
/// <see cref="ParameterClassification.ScientificMeasured"/> or
/// <see cref="ParameterClassification.ScientificEstimated"/> value needs at least
/// one source that has one of them (ADR-000 R5).
/// </summary>
public sealed record ParameterSource
{
    public ParameterSource(string citation, string accessed, string? doi, string? url, string? where)
    {
        if (string.IsNullOrWhiteSpace(citation))
        {
            throw new ArgumentException("A source needs a citation.", nameof(citation));
        }

        if (string.IsNullOrWhiteSpace(accessed))
        {
            throw new ArgumentException("A source needs an access date.", nameof(accessed));
        }

        Citation = citation;
        Accessed = accessed;
        Doi = string.IsNullOrWhiteSpace(doi) ? null : doi;
        Url = string.IsNullOrWhiteSpace(url) ? null : url;
        Where = string.IsNullOrWhiteSpace(where) ? null : where;
    }

    public string Citation { get; }

    public string Accessed { get; }

    public string? Doi { get; }

    public string? Url { get; }

    public string? Where { get; }

    /// <summary>True when this source can support a SCIENTIFIC_* classification.</summary>
    public bool HasLocator => Doi is not null || Url is not null;
}
