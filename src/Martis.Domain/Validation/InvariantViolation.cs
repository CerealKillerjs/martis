using Martis.Domain.Common;

namespace Martis.Domain.Validation;

/// <summary>
/// One broken invariant (H-15). <see cref="CellIndex"/> is the linear index
/// <c>y * width + x</c>, or null when the failure is not attached to a cell.
/// <see cref="Population"/> is null when the failure is not attached to a population.
/// <see cref="Value"/> is the offending number. <see cref="Quantity"/> names the field
/// or, for a gene, the gene id (the text <c>GeneId.Empty</c> when that id is empty).
/// </summary>
public readonly record struct InvariantViolation
{
    public InvariantViolation(
        string invariantId,
        long tick,
        int? cellIndex,
        PopulationId? population,
        double value,
        string quantity)
    {
        ArgumentException.ThrowIfNullOrEmpty(invariantId);
        ArgumentException.ThrowIfNullOrEmpty(quantity);
        InvariantId = invariantId;
        Tick = tick;
        CellIndex = cellIndex;
        Population = population;
        Value = value;
        Quantity = quantity;
    }

    public string InvariantId { get; }

    public long Tick { get; }

    public int? CellIndex { get; }

    public PopulationId? Population { get; }

    public double Value { get; }

    public string Quantity { get; }
}
