using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Environmental scalars of one cell (TDD §2.2, ADR-011, R2-10).
/// Temperature and pressure use <see cref="AssignedDouble"/>: the default is
/// unset, which is not the number 0. A loaded scenario must assign both and
/// must set <see cref="Scope"/>.
/// Ultraviolet irradiance, vapor, porosity and perchlorate mass are also
/// <see cref="AssignedDouble"/>. In MarsBaseline v0 the field map leaves them
/// unassigned, so a zero is not invented for them. The other doubles are
/// numbers the scenario writes explicitly. An unassigned value is omitted from
/// <see cref="Quantities"/> and is not treated as a measured zero.
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
    /// Daily fluence is pending. Unset until a scenario assigns it. When set,
    /// finite and at least 0 (DOM-VAL-004). MarsBaseline v0 leaves it unset.
    /// </summary>
    public AssignedDouble UvRadiation { get; set; }

    /// <summary>
    /// Absorbed dose from galactic cosmic rays at the surface, per sol.
    /// Unit: gray per sol (Gy = J/kg), in tissue or water, not in silicon.
    /// It is not added to <see cref="IonizingEquivalentDoseSvPerSol"/>.
    /// Solar particle events are outside v0. Finite and at least 0 (DOM-VAL-013).
    /// The number comes from the scenario. This type does not store a Gale value.
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
    /// in J/m². Finite and at least 0 (DOM-VAL-004). The scenario supplies the number.
    /// </summary>
    public double SolarIrradiance { get; set; }

    /// <summary>
    /// Physical ice reserve in this cell. Biology does not consume it.
    /// Unit: kg per cell. This is a GAME_BALANCE deposit, not a conversion of
    /// <see cref="WaterIceWtPercent"/>. Finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public double WaterIce { get; set; }

    /// <summary>
    /// Physical vapor reserve in this cell. Biology does not consume it.
    /// Unit: kg per cell. Unset until a scenario assigns kilograms.
    /// When set, finite and at least 0 (DOM-VAL-004). MarsBaseline v0 leaves it unset.
    /// An unset vapor is not a stored zero and is not added as a measurement.
    /// </summary>
    public AssignedDouble WaterVapor { get; set; }

    /// <summary>
    /// Soil porosity as a volume fraction.
    /// [0, 1] is the definition of a volume fraction, checked only when the
    /// value is assigned (DOM-VAL-003). Unset is not 0. MarsBaseline v0 leaves it unset.
    /// </summary>
    public AssignedDouble SoilPorosity { get; set; }

    /// <summary>
    /// Mass of perchlorate in the cell. Unit: kg per cell.
    /// Unset until H-07 gives a cell area. A weight percent must not be
    /// converted into this field. When set, finite and at least 0 (DOM-VAL-004).
    /// </summary>
    public AssignedDouble PerchlorateMassKg { get; set; }

    /// <summary>
    /// Perchlorate as a mass fraction of ClO4, in the closed interval [0, 100].
    /// Unit: wt %. This is not <see cref="PerchlorateMassKg"/> (DOM-VAL-016).
    /// The scenario writes it. The default 0 is the empty-world number, not a Gale measurement.
    /// </summary>
    public double PerchlorateWtPercent { get; set; }

    /// <summary>
    /// Water ice as a mass fraction, in the closed interval [0, 100].
    /// Unit: wt %. This is not <see cref="WaterIce"/> in kilograms (DOM-VAL-016).
    /// The scenario writes it. The loader does not turn this fraction into kilograms.
    /// </summary>
    public double WaterIceWtPercent { get; set; }

    /// <summary>
    /// Null until a scenario assigns where this cell applies.
    /// A loaded scenario must set it (DOM-VAL-010).
    /// MarsBaseline takes it from <c>metadata.scope_id</c>, not from the free-text scope.
    /// </summary>
    public EnvironmentScope? Scope { get; set; }

    /// <summary>
    /// Numeric fields in declaration order. Unset <see cref="AssignedDouble"/>
    /// values are skipped, so an unassigned quantity is not reported as 0.
    /// Temperature and pressure stay out of this list: the validator handles them first.
    /// </summary>
    public IReadOnlyList<NamedQuantity> Quantities
    {
        get
        {
            var list = new List<NamedQuantity>(8);
            AddIfSet(list, nameof(UvRadiation), UvRadiation);
            list.Add(new NamedQuantity(nameof(IonizingAbsorbedDoseGyPerSol), IonizingAbsorbedDoseGyPerSol));
            list.Add(new NamedQuantity(nameof(IonizingEquivalentDoseSvPerSol), IonizingEquivalentDoseSvPerSol));
            list.Add(new NamedQuantity(nameof(SolarIrradiance), SolarIrradiance));
            list.Add(new NamedQuantity(nameof(WaterIce), WaterIce));
            AddIfSet(list, nameof(WaterVapor), WaterVapor);
            AddIfSet(list, nameof(SoilPorosity), SoilPorosity);
            AddIfSet(list, nameof(PerchlorateMassKg), PerchlorateMassKg);
            list.Add(new NamedQuantity(nameof(PerchlorateWtPercent), PerchlorateWtPercent));
            list.Add(new NamedQuantity(nameof(WaterIceWtPercent), WaterIceWtPercent));
            return list;
        }
    }

    /// <summary>
    /// Checks assigned numbers. Unset temperature, pressure, ultraviolet,
    /// vapor, porosity or perchlorate mass is allowed here;
    /// <see cref="Validation.SimulationValidator"/> rejects a missing temperature,
    /// pressure or scope when a scenario is loaded. Soil porosity uses the closed
    /// unit interval. Weight percents use the closed interval [0, 100].
    /// The other doubles must be finite and at least 0. The first failure throws
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
            else if (quantity.Name is nameof(PerchlorateWtPercent) or nameof(WaterIceWtPercent))
            {
                QuantityRules.RequireWeightPercent(quantity.Value, quantity.Name);
            }
            else
            {
                QuantityRules.RequireNonNegativeFinite(quantity.Value, quantity.Name);
            }
        }
    }

    private static void AddIfSet(List<NamedQuantity> list, string name, AssignedDouble value)
    {
        if (value.IsSet)
        {
            list.Add(new NamedQuantity(name, value.Value));
        }
    }
}
