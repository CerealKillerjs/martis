using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Scenarios;

/// <summary>
/// Builds a world from a loaded MarsBaseline scenario.
/// 64 by 64 is the prototype grid (TDD §7), not a physical cell size.
/// The time step is one sol, the same engine default as the empty prototype.
/// It is not a value read from the science file.
/// </summary>
public static class MarsBaselineWorld
{
    public const int Width = 64;

    public const int Height = 64;

    public static SimulationEngine Create(
        MarsBaselineScenario scenario,
        ulong worldSeed,
        int width = Width,
        int height = Height)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        var state = new SimulationState(width, height);
        scenario.Apply(state);
        var configuration = new SimulationConfiguration(
            width,
            height,
            1.0,
            worldSeed,
            SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell,
            true);
        return new SimulationEngine(
            state,
            configuration,
            RngStreams.Create(worldSeed),
            SimulationPipeline.CreateV0());
    }
}
