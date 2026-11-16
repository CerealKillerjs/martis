using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Environmental scalars of one cell (TDD §2.2).
/// Defaults are 0, which means the quantity has not been set. They are not a
/// measured Martian state. Every field must be finite and greater than or equal
/// to 0 (VAL-001, H-13).
/// </summary>
public sealed class EnvironmentState
{
    /// <summary>
    /// Regional temperature. Unit: kelvin (K). Finite and greater than or equal to 0.
    /// </summary>
    public double TemperatureKelvin { get; set; }

    /// <summary>
    /// Regional pressure. Unit: pascal (Pa). Finite and greater than or equal to 0.
    /// </summary>
    public double PressurePascal { get; set; }

    /// <summary>
    /// Ultraviolet radiation at the cell.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double UvRadiation { get; set; }

    /// <summary>
    /// Ionizing radiation at the cell.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double IonizingRadiation { get; set; }

    /// <summary>
    /// Solar irradiance at the cell.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double SolarIrradiance { get; set; }

    /// <summary>
    /// Water held as ice.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double WaterIce { get; set; }

    /// <summary>
    /// Water held as vapor.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double WaterVapor { get; set; }

    /// <summary>
    /// Soil porosity.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// An upper bound is not declared here.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double SoilPorosity { get; set; }

    /// <summary>
    /// Perchlorate concentration.
    /// Unit: pendiente de MARTIS Ciencia. Finite and greater than or equal to 0.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double PerchlorateConcentration { get; set; }

    /// <summary>
    /// The nine fields in TDD declaration order.
    /// </summary>
    public IReadOnlyList<NamedQuantity> Quantities =>
    [
        new(nameof(TemperatureKelvin), TemperatureKelvin),
        new(nameof(PressurePascal), PressurePascal),
        new(nameof(UvRadiation), UvRadiation),
        new(nameof(IonizingRadiation), IonizingRadiation),
        new(nameof(SolarIrradiance), SolarIrradiance),
        new(nameof(WaterIce), WaterIce),
        new(nameof(WaterVapor), WaterVapor),
        new(nameof(SoilPorosity), SoilPorosity),
        new(nameof(PerchlorateConcentration), PerchlorateConcentration),
    ];

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> for the first field that
    /// is negative or not finite. The parameter name is the field name.
    /// </summary>
    public void CheckInvariants()
    {
        foreach (var quantity in Quantities)
        {
            QuantityRules.RequireNonNegativeFinite(quantity.Value, quantity.Name);
        }
    }
}
