using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Accessible resource deposits of one cell (TDD §2.3). These are model deposits,
/// not necessarily total chemical concentrations.
/// Every amount is finite and greater than or equal to 0 (VAL-001, H-13).
/// The unit of every amount is pendiente de H-18. No unit is invented here.
/// Defaults are 0, meaning the deposit has not been set, not a measured stock.
/// Populations must not subtract from this pool. Demand is recorded on
/// <see cref="AllocationBuffer"/> and reconciled later.
/// </summary>
public sealed class ResourcePool
{
    /// <summary>Carbon dioxide deposit. Unit: pendiente de H-18.</summary>
    public double CarbonDioxide { get; set; }

    /// <summary>Water deposit. Unit: pendiente de H-18.</summary>
    public double Water { get; set; }

    /// <summary>Oxygen deposit. Unit: pendiente de H-18.</summary>
    public double Oxygen { get; set; }

    /// <summary>Nitrogen deposit. Unit: pendiente de H-18.</summary>
    public double Nitrogen { get; set; }

    /// <summary>Phosphorus deposit. Unit: pendiente de H-18.</summary>
    public double Phosphorus { get; set; }

    /// <summary>Sulfur deposit. Unit: pendiente de H-18.</summary>
    public double Sulfur { get; set; }

    /// <summary>Iron deposit. Unit: pendiente de H-18.</summary>
    public double Iron { get; set; }

    /// <summary>Organic carbon deposit. Unit: pendiente de H-18.</summary>
    public double OrganicCarbon { get; set; }

    /// <summary>Mineral carbon deposit. Unit: pendiente de H-18.</summary>
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
