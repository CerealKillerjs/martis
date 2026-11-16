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
    /// Ultraviolet irradiance at local noon on a horizontal surface, band 200 to 400 nm.
    /// Unit: W/m². This is not a sol mean and it is not summed as a fluence.
    /// Daily fluence is pending. Finite and at least 0 (DOM-VAL-004). See ADR-011.
    /// </summary>
    public double UvRadiation { get; set; }

    /// <summary>
    /// Absorbed dose from galactic cosmic rays at the surface, per sol.
    /// Unit: gray per sol (Gy = J/kg), in tissue or water, not in silicon.
    /// It is not added to <see cref="IonizingEquivalentDoseSvPerSol"/>.
    /// Solar particle events are outside v0. Finite and at least 0 (DOM-VAL-013).
    /// Reference values stay out of this type; they belong to the scenario (T03).
    /// </summary>
    public double IonizingAbsorbedDoseGyPerSol { get; set; }

    /// <summary>
    /// Equivalent dose at the surface, per sol.
    /// Unit: sievert per sol (Sv = Gy times a dimensionless quality factor).
    /// It is never added to or compared with <see cref="IonizingAbsorbedDoseGyPerSol"/>.
    /// Using sievert for microorganisms is MODEL_ASSUMPTION: the quality factor
    /// is defined for human tissue (ADR-011). Finite and at least 0 (DOM-VAL-013).
    /// </summary>
    public double IonizingEquivalentDoseSvPerSol { get; set; }

    /// <summary>
    /// Surface solar irradiance averaged over one sol, not the top of the atmosphere.
    /// Unit: W/m². Energy over the tick is this value times <see cref="MartianTime.SecondsPerSol"/>,
    /// in J/m². Finite and at least 0 (DOM-VAL-004). See ADR-011.
    /// </summary>
    public double SolarIrradiance { get; set; }

    /// <summary>
    /// Physical ice reserve in this cell. Biology does not consume it.
    /// Unit: kg per cell. Unit choice: MODEL_ASSUMPTION (ADR-011). It is not
    /// <see cref="ResourcePool.Water"/>. Finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public double WaterIce { get; set; }

    /// <summary>
    /// Physical vapor reserve in this cell. Biology does not consume it.
    /// Unit: kg per cell. Unit choice: MODEL_ASSUMPTION (ADR-011). It is not
    /// <see cref="ResourcePool.Water"/>. Finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public double WaterVapor { get; set; }

    /// <summary>
    /// Soil porosity as a volume fraction.
    /// [0, 1] is the definition of a volume fraction.
    /// Finite and inside that interval (DOM-VAL-003).
    /// </summary>
    public double SoilPorosity { get; set; }

    /// <summary>
    /// Mass of perchlorate in the cell after the scenario loader converts a mass fraction.
    /// The chemical species (perchlorate ion or a calcium or magnesium salt) is not chosen here.
    /// Unit: kg per cell. Finite and at least 0 (DOM-VAL-004).
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double PerchlorateMassKg { get; set; }

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
        new(nameof(IonizingAbsorbedDoseGyPerSol), IonizingAbsorbedDoseGyPerSol),
        new(nameof(IonizingEquivalentDoseSvPerSol), IonizingEquivalentDoseSvPerSol),
        new(nameof(SolarIrradiance), SolarIrradiance),
        new(nameof(WaterIce), WaterIce),
        new(nameof(WaterVapor), WaterVapor),
        new(nameof(SoilPorosity), SoilPorosity),
        new(nameof(PerchlorateMassKg), PerchlorateMassKg),
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
