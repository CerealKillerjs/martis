namespace Martis.Domain.Validation;

/// <summary>
/// Identifiers written into <see cref="InvariantViolation"/>.
/// VAL-001..005 are only the invariants named in TDD §10.
/// DOM-VAL-001..016 are domain rules added here. Each one notes, in
/// docs/DATA_DICTIONARY.md, which TDD invariant it resembles.
/// VAL-004 is declared so the TDD id stays stable. No field in this milestone
/// is a TDD probability, so the validator does not emit it.
/// VAL-006, VAL-007 and VAL-008 are not produced: consumption, elemental
/// balance and energy balance are not implemented.
/// </summary>
public static class InvariantIds
{
    public const string NoNegativeResource = "VAL-001";

    public const string NoNegativePopulation = "VAL-002";

    public const string NoNegativeBiomass = "VAL-003";

    public const string ProbabilityInUnitInterval = "VAL-004";

    public const string GeneInUnitInterval = "VAL-005";

    public const string TemperaturePositive = "DOM-VAL-001";

    public const string PressureNonNegative = "DOM-VAL-002";

    public const string SoilPorosityUnitInterval = "DOM-VAL-003";

    public const string EnvironmentNonNegative = "DOM-VAL-004";

    public const string AllocationNonNegative = "DOM-VAL-005";

    public const string EnergyReserveNonNegative = "DOM-VAL-006";

    public const string AgeNonNegative = "DOM-VAL-007";

    public const string ViabilityUnitInterval = "DOM-VAL-008";

    public const string ScenarioQuantityUnset = "DOM-VAL-009";

    public const string ScenarioScopeMissing = "DOM-VAL-010";

    public const string CellIndexInRange = "DOM-VAL-011";

    public const string WaterPartition = "DOM-VAL-012";

    /// <summary>Absorbed dose and equivalent dose are each finite and at least 0. They are not combined.</summary>
    public const string IonizingDoseNonNegative = "DOM-VAL-013";

    /// <summary>Maintenance cost is a fraction of the energy reserve in the closed interval [0, 1].</summary>
    public const string MaintenanceCostFraction = "DOM-VAL-014";

    /// <summary>Per-capita reproduction rate per tick, in the closed interval [0, 1].</summary>
    public const string ReproductionRatePerCapita = "DOM-VAL-015";

    /// <summary>A weight percent in the closed interval [0, 100]. It is not a kilogram mass.</summary>
    public const string WeightPercentInterval = "DOM-VAL-016";
}
