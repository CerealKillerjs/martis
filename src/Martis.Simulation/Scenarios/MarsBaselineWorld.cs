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

    /// <summary>
    /// World with the biology pipeline (<see cref="SimulationPipeline.BiologyVersion"/>).
    /// The workspace is returned so a host can read the tick totals.
    /// </summary>
    public static (SimulationEngine Engine, Biology.MetabolismWorkspace Workspace) CreateWithBiology(
        MarsBaselineScenario scenario,
        Biology.BiologyCatalog catalog,
        ulong worldSeed,
        int width = Width,
        int height = Height,
        int? populationBudget = null,
        EnvironmentGradient? gradient = null,
        Biology.TransitionCatalog? transitions = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(catalog);
        var state = new SimulationState(width, height);
        scenario.Apply(state);
        gradient?.Apply(state);
        var configuration = new SimulationConfiguration(
            width,
            height,
            1.0,
            worldSeed,
            SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell,
            true);
        var workspace = new Biology.MetabolismWorkspace(catalog) { PopulationBudget = populationBudget, Transitions = transitions };
        var engine = new SimulationEngine(
            state,
            configuration,
            RngStreams.Create(worldSeed),
            BiologyPipeline.Systems(workspace),
            BiologyPipeline.Handlers(workspace),
            SimulationPipeline.BiologyVersion);
        return (engine, workspace);
    }
}
