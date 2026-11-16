using Martis.Domain.Common;

namespace Martis.Domain.Biology;

/// <summary>
/// Derived traits of a population (TDD §2.4). This value is calculated. It is not
/// stored on <see cref="Population"/> and it is not a source of truth.
/// Each component must be finite and greater than or equal to 0. No upper bound
/// is declared: the biological scale is not fixed.
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

    public double MaintenanceCost
    {
        get => _maintenanceCost;
        init => _maintenanceCost = QuantityRules.ReturnNonNegativeFinite(value, nameof(MaintenanceCost));
    }

    public double ReproductionRate
    {
        get => _reproductionRate;
        init => _reproductionRate = QuantityRules.ReturnNonNegativeFinite(value, nameof(ReproductionRate));
    }

    public double BiofilmProduction
    {
        get => _biofilmProduction;
        init => _biofilmProduction = QuantityRules.ReturnNonNegativeFinite(value, nameof(BiofilmProduction));
    }
}
