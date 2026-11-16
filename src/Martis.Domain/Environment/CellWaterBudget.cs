namespace Martis.Domain.Environment;

/// <summary>
/// Accounted water mass of one cell (ADR-011).
/// The three compartments are <see cref="ResourcePool.Water"/> (accessible liquid),
/// <see cref="EnvironmentState.WaterIce"/> and <see cref="EnvironmentState.WaterVapor"/>.
/// <see cref="RecordedTotalKg"/> is not a fourth stock. It must equal the sum of
/// those three, added in that order. Moving mass between them is a later process
/// (T03/T04) and is not implemented here. The same kilograms cannot be stored in
/// more than one compartment: a copy that leaves the total unchanged makes the
/// sum disagree with <see cref="RecordedTotalKg"/>.
/// </summary>
public sealed class CellWaterBudget
{
    /// <summary>
    /// Recorded total, in kg per cell. Default 0 matches three empty compartments.
    /// </summary>
    public double RecordedTotalKg { get; set; }

    /// <summary>
    /// Accessible liquid, then ice, then vapor. The order is part of the check.
    /// </summary>
    public static double ComponentSum(double accessibleLiquidKg, double iceKg, double vaporKg)
        => (accessibleLiquidKg + iceKg) + vaporKg;

    /// <summary>
    /// True when the absolute gap between the component sum and the recorded total
    /// is within the TDD §10.1 allowance. The allowance is
    /// <see cref="MassBalanceTolerances.MassBalanceAbsoluteToleranceKg"/>
    /// plus the relative tolerance times the absolute recorded total.
    /// </summary>
    public static bool WithinTolerance(double componentSum, double recordedTotalKg)
    {
        if (!double.IsFinite(componentSum) || !double.IsFinite(recordedTotalKg))
        {
            return false;
        }

        var error = Math.Abs(componentSum - recordedTotalKg);
        var allowance = MassBalanceTolerances.MassBalanceAbsoluteToleranceKg
            + (MassBalanceTolerances.MassBalanceRelativeTolerance * Math.Abs(recordedTotalKg));
        return error <= allowance;
    }
}
