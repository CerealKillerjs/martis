using Martis.Domain.Common;
using Martis.Domain.Validation;

namespace Martis.Domain.Biology;

/// <summary>
/// Derived traits of a population (TDD §2.4). This value is calculated. It is not
/// stored on <see cref="Population"/> and it is not a source of truth.
/// Each component must be finite and at least 0.
/// <see cref="MaintenanceCost"/> is the fraction of <see cref="Population.EnergyReserveJ"/>
/// removed per sol, in [0, 1] (DOM-VAL-014). Because it is a fraction of the reserve,
/// the cost tends to 0 as the reserve is exhausted, and there is no starvation (MODEL_ASSUMPTION).
/// <see cref="ReproductionRate"/> is a per-capita rate per tick in [0, 1] (DOM-VAL-015).
/// Zero means the population does not reproduce. It is not a multiplicative factor.
/// Both scales are MODEL_ASSUMPTION and may later be classified GAME_BALANCE (ADR-011).
/// The other components still have no unit.
/// TODO: pendiente MARTIS Ciencia.
/// </summary>
public sealed record Phenotype
{
    private readonly double _uvResistance;
    private readonly double _thermalTolerance;
    private readonly double _desiccationTolerance;
    private readonly double _photosyntheticEfficiency;
    private readonly double _maintenanceCost;
    private readonly double _reproductionRate;
    private readonly double _biofilmProduction;

    public Phenotype(
        double uvResistance,
        double thermalTolerance,
        double desiccationTolerance,
        double photosyntheticEfficiency,
        double maintenanceCost,
        double reproductionRate,
        double biofilmProduction)
    {
        UvResistance = uvResistance;
        ThermalTolerance = thermalTolerance;
        DesiccationTolerance = desiccationTolerance;
        PhotosyntheticEfficiency = photosyntheticEfficiency;
        MaintenanceCost = maintenanceCost;
        ReproductionRate = reproductionRate;
        BiofilmProduction = biofilmProduction;
    }

    public double UvResistance
    {
        get => _uvResistance;
        init => _uvResistance = QuantityRules.ReturnNonNegativeFinite(value, nameof(UvResistance));
    }

    public double ThermalTolerance
    {
        get => _thermalTolerance;
        init => _thermalTolerance = QuantityRules.ReturnNonNegativeFinite(value, nameof(ThermalTolerance));
    }

    public double DesiccationTolerance
    {
        get => _desiccationTolerance;
        init => _desiccationTolerance = QuantityRules.ReturnNonNegativeFinite(value, nameof(DesiccationTolerance));
    }

    public double PhotosyntheticEfficiency
    {
        get => _photosyntheticEfficiency;
        init => _photosyntheticEfficiency = QuantityRules.ReturnNonNegativeFinite(value, nameof(PhotosyntheticEfficiency));
    }

    /// <summary>
    /// Fraction of the energy reserve removed per sol. Finite and inside [0, 1] (DOM-VAL-014).
    /// Classification of the scale: MODEL_ASSUMPTION (ADR-011).
    /// </summary>
    public double MaintenanceCost
    {
        get => _maintenanceCost;
        init => _maintenanceCost = QuantityRules.ReturnUnitInterval(
            value,
            nameof(MaintenanceCost),
            InvariantIds.MaintenanceCostFraction);
    }

    /// <summary>
    /// Per-capita reproduction rate per tick, in [0, 1] (DOM-VAL-015).
    /// Zero means no reproduction. Classification of the scale: MODEL_ASSUMPTION (ADR-011).
    /// </summary>
    public double ReproductionRate
    {
        get => _reproductionRate;
        init => _reproductionRate = QuantityRules.ReturnUnitInterval(
            value,
            nameof(ReproductionRate),
            InvariantIds.ReproductionRatePerCapita);
    }

    public double BiofilmProduction
    {
        get => _biofilmProduction;
        init => _biofilmProduction = QuantityRules.ReturnNonNegativeFinite(value, nameof(BiofilmProduction));
    }
}
