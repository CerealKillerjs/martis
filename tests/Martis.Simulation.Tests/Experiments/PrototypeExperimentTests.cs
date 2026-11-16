using System.Text;
using System.Text.Json;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Experiments;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Experiments;

/// <summary>
/// S1-32 / S1-33 in process: the TDD §19 flow on the published MarsBaseline files.
/// </summary>
[Trait("Category", "Integration")]
public sealed class PrototypeExperimentTests
{
    private static readonly HostInfo Host = new("test-runtime", "test-sdk", "test-os");

    [Fact]
    public void ThousandTicksOfMarsBaselineCompleteAndWriteTheFiveFiles()
    {
        var result = PrototypeExperiment.Run(Request(12345UL, 1000), Published());

        Assert.True(result.Succeeded, result.FailureSummary);
        Assert.Equal(1000, result.FinalTick);
        Assert.Equal(PrototypeExperiment.FileNames, result.Files.Select(file => file.Name));

        var snapshot = StateSnapshotJson.Read(File(result, PrototypeExperiment.SnapshotFile));
        Assert.Equal(1000, snapshot.Tick);
        Assert.Equal(64 * 64, snapshot.Cells.Count);
        Assert.Equal("mars_baseline", snapshot.Scenario!.Id);

        using var metadata = JsonDocument.Parse(File(result, PrototypeExperiment.MetadataFile));
        var root = metadata.RootElement;
        Assert.Equal("completed", root.GetProperty("status").GetString());
        Assert.Equal("12345", root.GetProperty("seed").GetString());
        Assert.Equal(Pcg32.AlgorithmId, root.GetProperty("prng_algorithm").GetString());
        Assert.Equal("test-sdk", root.GetProperty("build_sdk").GetString());
        Assert.Equal(
            StateSnapshotJson.Sha256Hex(snapshot),
            root.GetProperty("final_snapshot_sha256").GetString());

        Assert.EndsWith("status completed\n", Text(result, PrototypeExperiment.LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public void HistoriesHaveARowAtTickZeroEveryIntervalAndTheLastTick()
    {
        var result = PrototypeExperiment.Run(Request(1UL, 100), Published());

        var rows = Text(result, PrototypeExperiment.ResourceHistoryFile).TrimEnd('\n').Split('\n');
        Assert.StartsWith("tick,CarbonDioxide_kg,", rows[0], StringComparison.Ordinal);
        Assert.Equal(["0", "30", "60", "90", "100"], rows.Skip(1).Select(row => row.Split(',')[0]));

        var populations = Text(result, PrototypeExperiment.PopulationHistoryFile).TrimEnd('\n').Split('\n');
        Assert.Equal("tick,population_count,total_abundance,total_biomass_kg", populations[0]);
        Assert.Equal(rows.Length, populations.Length);
    }

    [Fact]
    public void SameRequestGivesTheSameBytes()
    {
        var a = PrototypeExperiment.Run(Request(777UL, 200), Published());
        var b = PrototypeExperiment.Run(Request(777UL, 200), Published());

        foreach (var name in PrototypeExperiment.FileNames)
        {
            Assert.Equal(File(a, name), File(b, name));
        }
    }

    [Fact]
    public void AnotherSeedChangesTheSnapshot()
    {
        var a = PrototypeExperiment.Run(Request(777UL, 10), Published());
        var b = PrototypeExperiment.Run(Request(778UL, 10), Published());

        Assert.NotEqual(File(a, PrototypeExperiment.SnapshotFile), File(b, PrototypeExperiment.SnapshotFile));
    }

    [Fact]
    public void AnInjectedInvalidStateFailsAtThatTickAndLogsSeedTickCellAndSystem()
    {
        var result = PrototypeExperiment.Run(
            Request(4242UL, 1000),
            Published(),
            (scenario, seed) => WithExtraSystem(scenario, seed, new NegativeWater(armAtTick: 500, cell: 130)));

        Assert.False(result.Succeeded);
        Assert.Equal(500, result.FinalTick);
        var log = Text(result, PrototypeExperiment.LogFile);
        Assert.Contains(
            "VIOLATION VAL-001 seed=4242 tick=500 cell=130 (x=2, y=2) population=- system=validate-state quantity=Water value=-1",
            log,
            StringComparison.Ordinal);
        Assert.Contains("status failed: tick 500 failed validation in validate-state", log, StringComparison.Ordinal);

        // The snapshot is the last committed tick, not the half-applied one.
        var snapshot = StateSnapshotJson.Read(File(result, PrototypeExperiment.SnapshotFile));
        Assert.Equal(500, snapshot.Tick);
        Assert.True(snapshot.Cells[130].Deposits[ResourceColumns.Names.ToList().IndexOf("Water")] >= 0.0);

        using var metadata = JsonDocument.Parse(File(result, PrototypeExperiment.MetadataFile));
        Assert.Equal("failed", metadata.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void AThrowingSystemIsNamedInTheLog()
    {
        var result = PrototypeExperiment.Run(
            Request(5UL, 50),
            Published(),
            (scenario, seed) => WithExtraSystem(scenario, seed, new Throwing(armAtTick: 20)));

        Assert.False(result.Succeeded);
        Assert.Equal(20, result.FinalTick);
        Assert.Contains(
            "ERROR seed=5 tick=20 system=test-throwing error=InvalidOperationException",
            Text(result, PrototypeExperiment.LogFile),
            StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownScenarioIdIsRejected()
    {
        var request = Request(1UL, 1) with { ScenarioId = "olympus" };

        Assert.Throws<ArgumentException>(() => PrototypeExperiment.Run(request, Published()));
    }

    private static ExperimentRequest Request(ulong seed, int ticks)
        => new(PrototypeExperiment.MarsBaselineId, seed, ticks, PrototypeExperiment.DefaultHistoryIntervalTicks, Host);

    private static MarsBaselineScenario Published() => MarsBaselineScenario.LoadPublished(Repo.Root());

    private static byte[] File(ExperimentResult result, string name)
        => result.Files.Single(file => file.Name == name).Content;

    private static string Text(ExperimentResult result, string name) => Encoding.UTF8.GetString(File(result, name));

    private static SimulationEngine WithExtraSystem(MarsBaselineScenario scenario, ulong seed, ISimulationSystem extra)
    {
        var state = new SimulationState(MarsBaselineWorld.Width, MarsBaselineWorld.Height);
        scenario.Apply(state);
        var configuration = new SimulationConfiguration(
            MarsBaselineWorld.Width,
            MarsBaselineWorld.Height,
            1.0,
            seed,
            SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell,
            true);
        return new SimulationEngine(
            state,
            configuration,
            RngStreams.Create(seed),
            SimulationPipeline.CreateV0().Append(extra).ToArray());
    }

    /// <summary>Test-only fault: makes one water deposit negative from a given tick on.</summary>
    private sealed class NegativeWater(long armAtTick, int cell) : ISimulationSystem
    {
        public string Name => "test-negative-water";

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            if (context.State.Tick >= armAtTick)
            {
                context.State.Resources[cell].Water = -1.0;
            }
        }
    }

    private sealed class Throwing(long armAtTick) : ISimulationSystem
    {
        public string Name => "test-throwing";

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            if (context.State.Tick >= armAtTick)
            {
                throw new InvalidOperationException("injected");
            }
        }
    }
}
