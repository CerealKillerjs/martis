using Martis.Domain.World;
using Martis.Simulation.Random;

namespace Martis.Simulation.Engine;

/// <summary>
/// Empty world used by the headless CLI before a scenario exists.
/// 64 by 64 is the prototype grid size (TDD §7), not a physical cell size.
/// The time step is one sol (ADR-011). No scenario file is read.
/// </summary>
public static class PrototypeWorld
{
    public const int Width = 64;

    public const int Height = 64;

    public static SimulationEngine Create(ulong worldSeed)
    {
        var configuration = new SimulationConfiguration(
            Width,
            Height,
            1.0,
            worldSeed,
            SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell,
            true);
        configuration.Validate();
        var state = new SimulationState(Width, Height);
        return new SimulationEngine(
            state,
            configuration,
            RngStreams.Create(worldSeed),
            SimulationPipeline.CreateStubs());
    }
}
