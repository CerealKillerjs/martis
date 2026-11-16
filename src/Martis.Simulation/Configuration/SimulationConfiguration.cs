namespace Martis.Simulation.Engine;

/// <summary>
/// Run configuration (TDD §12.1, ADR-011, ADR-006).
/// The time step is <see cref="DeltaTimeSols"/>. There is no <c>DeltaTimeDays</c>:
/// one tick is one sol. The variant cap is per species and cell, not per cell.
/// 32 is the performance default from ADR-006 (<c>GAME_BALANCE</c>), not a Mars measurement.
/// </summary>
public sealed record SimulationConfiguration(
    int MapWidth,
    int MapHeight,
    double DeltaTimeSols,
    ulong WorldSeed,
    int MaxVariantsPerSpeciesPerCell,
    bool EnableValidation)
{
    /// <summary>
    /// Performance cap suggested by TDD §4.4 and fixed as the default name in ADR-006.
    /// It is <c>GAME_BALANCE</c>, not a biological or planetary constant.
    /// </summary>
    public const int DefaultMaxVariantsPerSpeciesPerCell = 32;

    /// <summary>
    /// Rejects non-positive dimensions, a time step that is not finite and positive,
    /// and a non-positive variant cap. NaN fails here because a comparison with zero
    /// does not reject it (H-13).
    /// </summary>
    public void Validate()
    {
        if (MapWidth <= 0 || MapHeight <= 0)
        {
            throw new InvalidOperationException("Map dimensions must be positive.");
        }

        if (!double.IsFinite(DeltaTimeSols) || DeltaTimeSols <= 0.0)
        {
            throw new InvalidOperationException("DeltaTimeSols must be finite and positive.");
        }

        if (MaxVariantsPerSpeciesPerCell <= 0)
        {
            throw new InvalidOperationException("MaxVariantsPerSpeciesPerCell must be positive.");
        }
    }
}
