using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Accessible resource deposits of one cell (TDD §2.3, ADR-011).
/// Every amount is a mass in kg, finite and at least 0 (VAL-001).
/// Molar masses live in <see cref="ElementalCompositionMatrix"/> and are not numbers yet.
/// Defaults are an assigned zero, not an unset measurement.
/// Populations must not subtract from this pool. Demand is recorded on
/// <see cref="AllocationBuffer"/> and reconciled later.
/// </summary>
public sealed class ResourcePool
{
    /// <summary>Carbon dioxide deposit. Unit: kg per cell.</summary>
    public double CarbonDioxide { get; set; }

    /// <summary>
    /// Liquid water the biology can use, in kg per cell.
    /// It is not ice and it is not vapor. Those physical reserves are
    /// <see cref="EnvironmentState.WaterIce"/> and <see cref="EnvironmentState.WaterVapor"/>.
    /// The same mass is not stored in both places (ADR-011, DOM-VAL-012).
    /// </summary>
    public double Water { get; set; }

    /// <summary>Oxygen deposit. Unit: kg per cell.</summary>
    public double Oxygen { get; set; }

    /// <summary>Nitrogen deposit. Unit: kg per cell.</summary>
    public double Nitrogen { get; set; }

    /// <summary>Phosphorus deposit. Unit: kg per cell.</summary>
    public double Phosphorus { get; set; }

    /// <summary>Sulfur deposit. Unit: kg per cell.</summary>
    public double Sulfur { get; set; }

    /// <summary>Iron deposit. Unit: kg per cell.</summary>
    public double Iron { get; set; }

    /// <summary>Organic carbon deposit. Unit: kg per cell.</summary>
    public double OrganicCarbon { get; set; }

    /// <summary>Mineral carbon deposit. Unit: kg per cell.</summary>
    public double MineralCarbon { get; set; }

    /// <summary>The nine deposits in TDD declaration order.</summary>
    public IReadOnlyList<NamedQuantity> Quantities =>
    [
        new(nameof(CarbonDioxide), CarbonDioxide),
        new(nameof(Water), Water),
        new(nameof(Oxygen), Oxygen),
        new(nameof(Nitrogen), Nitrogen),
        new(nameof(Phosphorus), Phosphorus),
        new(nameof(Sulfur), Sulfur),
        new(nameof(Iron), Iron),
        new(nameof(OrganicCarbon), OrganicCarbon),
        new(nameof(MineralCarbon), MineralCarbon),
    ];

    /// <summary>Number of deposits, the length of <see cref="Quantities"/>.</summary>
    public const int SlotCount = 9;

    /// <summary>
    /// The deposit at a <see cref="Quantities"/> position, without building the list (H3:
    /// per-cell loops on 256×256 maps). Same order as <see cref="Quantities"/>.
    /// </summary>
    public double ValueAt(int slot) => slot switch
    {
        0 => CarbonDioxide,
        1 => Water,
        2 => Oxygen,
        3 => Nitrogen,
        4 => Phosphorus,
        5 => Sulfur,
        6 => Iron,
        7 => OrganicCarbon,
        8 => MineralCarbon,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "A pool has nine deposits."),
    };

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> for the first deposit that
    /// is negative or not finite. The parameter name is the deposit name.
    /// </summary>
    public void CheckInvariants()
    {
        foreach (var quantity in Quantities)
        {
            QuantityRules.RequireNonNegativeFinite(quantity.Value, quantity.Name);
        }
    }
}
