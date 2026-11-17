using System.Diagnostics;
using Martis.Contracts.Commands;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;

namespace Martis.Benchmarks;

/// <summary>The worlds that the benchmarks and the profile run (T10, S1-35).</summary>
internal static class World
{
    public const ulong Seed = 5UL;

    private static readonly Lazy<string> RootPath = new(FindRoot);
    private static readonly Lazy<MarsBaselineScenario> ScenarioValue = new(() => MarsBaselineScenario.LoadPublished(RootPath.Value));
    private static readonly Lazy<BiologyCatalog> CatalogValue = new(() => BiologyCatalog.LoadPublished(RootPath.Value));

    public static MarsBaselineScenario Scenario => ScenarioValue.Value;

    public static BiologyCatalog Catalog => CatalogValue.Value;

    /// <summary>pipeline-v0 world without life (the Sprint 1 prototype of §19).</summary>
    public static SimulationEngine Empty() => MarsBaselineWorld.Create(Scenario, Seed);

    /// <summary>The FIRST COLONY start: one designed phototroph and one decomposer.</summary>
    public static SimulationEngine Colony(out MetabolismWorkspace workspace)
    {
        var (engine, space) = MarsBaselineWorld.CreateWithBiology(Scenario, Catalog, Seed);
        workspace = space;
        engine.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", 2080, 0.5, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.85 }));
        engine.ApplyCommand(new IntroducePopulationCommand("decomposer_d01", 2080, 0.05));

        return engine;
    }

    /// <summary>
    /// The §7.4 load: 4096 cells, three organisms and 10 000 populations at the start
    /// (phototroph and decomposer in every cell, extremophile in the first 1808).
    /// </summary>
    public static SimulationEngine Stress(out MetabolismWorkspace workspace)
    {
        workspace = new MetabolismWorkspace(Catalog);
        return Stress(workspace, BiologyPipeline.Systems(workspace));
    }

    /// <summary>The same load with given systems that share <paramref name="workspace"/> (profile mode).</summary>
    public static SimulationEngine Stress(MetabolismWorkspace workspace, IReadOnlyList<ISimulationSystem> systems)
    {
        var state = new SimulationState(MarsBaselineWorld.Width, MarsBaselineWorld.Height);
        Scenario.Apply(state);
        var engine = new SimulationEngine(
            state,
            new SimulationConfiguration(MarsBaselineWorld.Width, MarsBaselineWorld.Height, 1.0, Seed, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell, true),
            RngStreams.Create(Seed),
            systems,
            BiologyPipeline.Handlers(workspace),
            SimulationPipeline.BiologyVersion);
        var count = 0;
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            engine.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", cell, 0.05));
            engine.ApplyCommand(new IntroducePopulationCommand("decomposer_d01", cell, 0.005));
            count += 2;
        }

        for (var cell = 0; count < StressPopulations; cell++, count++)
        {
            engine.ApplyCommand(new IntroducePopulationCommand("extremophile_e01", cell, 0.02));
        }

        return engine;
    }

    public const int StressPopulations = 10_000;

    private static string FindRoot()
    {
        var configured = System.Environment.GetEnvironmentVariable("MARTIS_DATA_ROOT");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MARTIS.sln")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("MARTIS.sln not found above the benchmark; set MARTIS_DATA_ROOT.");
    }
}

/// <summary>Wraps a pipeline step and adds up its wall time (profile mode only).</summary>
internal sealed class TimedSystem(ISimulationSystem inner) : ISimulationSystem
{
    public string Name => inner.Name;

    public TimeSpan Elapsed { get; private set; }

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        var start = Stopwatch.GetTimestamp();
        inner.Execute(context, deltaTimeSols);
        Elapsed += Stopwatch.GetElapsedTime(start);
    }
}
