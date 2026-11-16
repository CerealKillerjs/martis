namespace Martis.Domain.Parameters;

/// <summary>
/// Classification of one parameter value (ADR-000 R5, TDD §10.2).
/// The label belongs to the value, not to the field name: two entries may share
/// a field and still carry different classifications.
/// <see cref="UnclassifiedPending"/> is a file marker for a null value whose
/// status is PENDING. It is not a sixth classification that a system can read.
/// ADR-000 R5 still allows only the five classifications on a delivered value.
/// </summary>
public enum ParameterClassification
{
    /// <summary>Published measurement. A source with a DOI or URL is required.</summary>
    ScientificMeasured,

    /// <summary>Estimate based on evidence. A source with a DOI or URL is required.</summary>
    ScientificEstimated,

    /// <summary>Modelling choice, including a conversion from a native source value.</summary>
    ModelAssumption,

    /// <summary>Value chosen for play. It is not a measurement.</summary>
    GameBalance,

    /// <summary>Speculative hypothesis. It is not a measurement.</summary>
    Speculative,

    /// <summary>
    /// Accepted only when the entry has status PENDING and a null value.
    /// <see cref="ParameterCatalog.RequireNumber"/> never returns it.
    /// </summary>
    UnclassifiedPending,
}
