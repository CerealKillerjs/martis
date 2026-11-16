using Martis.Domain.Common;

namespace Martis.Domain.Environment;

/// <summary>
/// Per-cell ledger of requested resource amounts (TDD §12.3).
/// A population records demand here. It does not subtract from
/// <see cref="ResourcePool"/>, so iteration order cannot grant a larger share.
/// T02 does not reconcile or consume the buffer. Units match the pool:
/// pendiente de H-18. Each entry must be finite and greater than or equal to 0.
/// </summary>
public sealed class AllocationBuffer
{
    /// <summary>Requested carbon dioxide. Unit: pendiente de H-18.</summary>
    public double CarbonDioxide { get; set; }

    /// <summary>Requested water. Unit: pendiente de H-18.</summary>
    public double Water { get; set; }

    /// <summary>Requested oxygen. Unit: pendiente de H-18.</summary>
    public double Oxygen { get; set; }

    /// <summary>Requested nitrogen. Unit: pendiente de H-18.</summary>
    public double Nitrogen { get; set; }

    /// <summary>Requested phosphorus. Unit: pendiente de H-18.</summary>
    public double Phosphorus { get; set; }

    /// <summary>Requested sulfur. Unit: pendiente de H-18.</summary>
    public double Sulfur { get; set; }

    /// <summary>Requested iron. Unit: pendiente de H-18.</summary>
    public double Iron { get; set; }

    /// <summary>Requested organic carbon. Unit: pendiente de H-18.</summary>
    public double OrganicCarbon { get; set; }

    /// <summary>Requested mineral carbon. Unit: pendiente de H-18.</summary>
    public double MineralCarbon { get; set; }

    /// <summary>The nine request slots, in the same order as <see cref="ResourcePool"/>.</summary>
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
    /// Throws <see cref="ArgumentOutOfRangeException"/> for the first request that
    /// is negative or not finite.
    /// </summary>
    public void CheckInvariants()
    {
        foreach (var quantity in Quantities)
        {
            QuantityRules.RequireNonNegativeFinite(quantity.Value, quantity.Name);
        }
    }
}
