namespace Martis.Domain.Validation;

/// <summary>
/// Identifiers written into <see cref="InvariantViolation"/>.
/// VAL-006, VAL-007 and VAL-008 are not produced in T02: consumption, elemental
/// balance and energy balance are not implemented.
/// <see cref="CellIndexInRange"/> is a structural check, not a VAL from TDD §10.
/// </summary>
public static class InvariantIds
{
    public const string NoNegativeResource = "VAL-001";

    public const string NoNegativePopulation = "VAL-002";

    public const string NoNegativeBiomass = "VAL-003";

    public const string ProbabilityInUnitInterval = "VAL-004";

    public const string GeneInUnitInterval = "VAL-005";

    public const string CellIndexInRange = "CELL-001";
}
