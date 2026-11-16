using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Environmental scalars of one cell (TDD §2.2, ADR-011).
/// Temperature and pressure use <see cref="AssignedDouble"/>: the default is
/// unset, which is not the number 0. A loaded scenario must assign both and
/// must set <see cref="Scope"/>. Other magnitudes default to 0, an assigned
/// zero, not a missing measurement.
/// </summary>
public sealed class EnvironmentState
{
    /// <summary>
    /// Temperature. Unit: kelvin (K). When set, it must be finite and strictly
    /// greater than 0. Unset is not 0 K (DOM-VAL-001, DOM-VAL-009).
    /// </summary>
    public AssignedDouble TemperatureKelvin { get; set; }

    /// <summary>
    /// Pressure. Unit: pascal (Pa). When set, it must be finite and greater
    /// than or equal to 0. An assigned 0 Pa is a real value and is not unset
    /// (DOM-VAL-002, DOM-VAL-009).
    /// </summary>
    public AssignedDouble PressurePascal { get; set; }

    /// <summary>
    /// Ultraviolet radiation at the surface.
    /// Unit: W/m², classified MODEL_ASSUMPTION (ADR-011). Finite and at least 0
    /// (DOM-VAL-004).
    /// </summary>
    public double UvRadiation { get; set; }

    /// <summary>
    /// Ionizing radiation at the surface, as an energy flux.
    /// Unit: W/m², classified MODEL_ASSUMPTION (ADR-011). A dosimetric unit is
    /// not chosen here. Finite and at least 0 (DOM-VAL-004).
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double IonizingRadiation { get; set; }

    /// <summary>
    /// Solar irradiance at the surface, not at the top of the atmosphere.
    /// Unit: W/m², classified MODEL_ASSUMPTION (ADR-011). Finite and at least 0
    /// (DOM-VAL-004).
    /// </summary>
    public double SolarIrradiance { get; set; }

    /// <summary>
    /// Physical ice reserve in this cell. Biology does not consume it.
    /// Unit: kg per cell, classified MODEL_ASSUMPTION (ADR-011). It is not
    /// <see cref="ResourcePool.Water"/>. Finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public double WaterIce { get; set; }

    /// <summary>
    /// Physical vapor reserve in this cell. Biology does not consume it.
    /// Unit: kg per cell, classified MODEL_ASSUMPTION (ADR-011). It is not
    /// <see cref="ResourcePool.Water"/>. Finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public double WaterVapor { get; set; }

    /// <summary>
    /// Soil porosity as a volume fraction.
    /// Unit: dimensionless, classified MODEL_ASSUMPTION (ADR-011).
    /// Finite and inside [0, 1] (DOM-VAL-003).
    /// </summary>
    public double SoilPorosity { get; set; }

    /// <summary>
    /// Mass of perchlorate in the cell, not a molar concentration.
    /// Unit: kg per cell, classified MODEL_ASSUMPTION (ADR-011).
    /// Finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public double PerchlorateConcentration { get; set; }

    /// <summary>
    /// Null until a scenario assigns where this cell applies.
    /// A loaded scenario must set it (DOM-VAL-010).
    /// </summary>
    public EnvironmentScope? Scope { get; set; }

    /// <summary>
    /// Double fields in declaration order. Temperature and pressure are not
    /// here: they are <see cref="AssignedDouble"/> values.
    /// </summary>
    public IReadOnlyList<NamedQuantity> Quantities =>
    [
        new(nameof(UvRadiation), UvRadiation),
        new(nameof(IonizingRadiation), IonizingRadiation),
        new(nameof(SolarIrradiance), SolarIrradiance),
        new(nameof(WaterIce), WaterIce),
        new(nameof(WaterVapor), WaterVapor),
        new(nameof(SoilPorosity), SoilPorosity),
        new(nameof(PerchlorateConcentration), PerchlorateConcentration),
    ];

    /// <summary>
    /// Checks assigned numbers. Unset temperature or pressure is allowed here;
    /// <see cref="Validation.SimulationValidator"/> rejects them when a scenario
    /// is loaded. Soil porosity uses the closed unit interval. The other
    /// doubles must be finite and at least 0. The first failure throws
    /// <see cref="ArgumentOutOfRangeException"/> with the field name.
    /// </summary>
    public void CheckInvariants()
    {
        if (TemperatureKelvin.IsSet)
        {
            QuantityRules.RequireStrictlyPositiveFinite(TemperatureKelvin.Value, nameof(TemperatureKelvin));
        }

        if (PressurePascal.IsSet)
        {
            QuantityRules.RequireNonNegativeFinite(PressurePascal.Value, nameof(PressurePascal));
        }

        foreach (var quantity in Quantities)
        {
            if (quantity.Name == nameof(SoilPorosity))
            {
                QuantityRules.RequireUnitInterval(quantity.Value, quantity.Name);
            }
            else
            {
                QuantityRules.RequireNonNegativeFinite(quantity.Value, quantity.Name);
            }
        }
    }
}
