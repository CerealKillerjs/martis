using System.Text;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Genetics;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Snapshots;

/// <summary>
/// S1-31: full state snapshot, canonical JSON and restore (VAL-009, VAL-010).
/// The engines here draw from every PRNG stream each tick, so a restore that
/// forgot one stream would make the continuation diverge.
/// </summary>
[Trait("Category", "Determinism")]
public sealed class StateSnapshotTests
{
    private const ulong Seed = 20261116UL;

    [Fact]
    public void JsonRoundTripIsByteIdentical()
    {
        var engine = Build(Seed);
        engine.Advance(7);
        var snapshot = StateSnapshot.Capture(engine, Identity());

        var first = StateSnapshotJson.Write(snapshot);
        var second = StateSnapshotJson.Write(StateSnapshotJson.Read(first));

        Assert.Equal(first, second);
        Assert.Contains("\"tick\": 7,", Encoding.UTF8.GetString(first), StringComparison.Ordinal);
    }

    [Fact]
    public void RestoreOfACaptureGivesTheSameState()
    {
        var original = Build(Seed);
        original.Advance(5);
        var bytes = StateSnapshotJson.Write(StateSnapshot.Capture(original, Identity()));

        var restored = Build(Seed);
        StateSnapshotJson.Read(bytes).Restore(restored);

        Assert.Equal(bytes, StateSnapshotJson.Write(StateSnapshot.Capture(restored, Identity())));
        Assert.Equal(5, restored.CurrentTick);
        Assert.Single(restored.State.Populations);
    }

    [Fact]
    public void ContinuingFromARestoreMatchesContinuingTheOriginal()
    {
        var original = Build(Seed);
        original.Advance(5);
        var bytes = StateSnapshotJson.Write(StateSnapshot.Capture(original, Identity()));
        var restored = Build(Seed);
        StateSnapshotJson.Read(bytes).Restore(restored);

        original.Advance(100);
        restored.Advance(100);

        Assert.Equal(
            StateSnapshotJson.Write(StateSnapshot.Capture(original, Identity())),
            StateSnapshotJson.Write(StateSnapshot.Capture(restored, Identity())));
    }

    [Fact]
    public void AnotherSeedGivesAnotherState()
    {
        var a = Build(Seed);
        var b = Build(Seed + 1);
        a.Advance(3);
        b.Advance(3);

        Assert.NotEqual(
            StateSnapshotJson.Sha256Hex(StateSnapshot.Capture(a, Identity())),
            StateSnapshotJson.Sha256Hex(StateSnapshot.Capture(b, Identity())));
    }

    [Fact]
    public void SnapshotRecordsVersionsAndPrng()
    {
        var engine = Build(Seed);
        var snapshot = StateSnapshot.Capture(engine, Identity());

        Assert.Equal(StateSnapshot.CurrentSchemaVersion, snapshot.SchemaVersion);
        Assert.Equal(Pcg32.AlgorithmId, snapshot.PrngAlgorithm);
        Assert.Equal(SimulationPipeline.Version, snapshot.PipelineVersion);
        Assert.Equal(EngineAssembly.Version, snapshot.EngineVersion);
        Assert.Equal(engine.Random.ExportState(), snapshot.Random);
        Assert.Equal(engine.State.CellCount, snapshot.Cells.Count);
    }

    [Fact]
    public void SnapshotDoesNotFollowLaterEdits()
    {
        var engine = Build(Seed);
        var snapshot = StateSnapshot.Capture(engine, Identity());
        var before = StateSnapshotJson.Write(snapshot);

        engine.State.Resources[0].Water += 1.0;
        engine.State.Populations[0].Abundance += 1.0;

        Assert.Equal(before, StateSnapshotJson.Write(snapshot));
    }

    [Fact]
    public void ExtremeDoublesKeepTheirBits()
    {
        double[] values = [0.1, 1.0 / 3.0, double.Epsilon, double.MaxValue, 1e-300, 123456789.123456789, 0.0];
        foreach (var value in values)
        {
            var engine = Build(Seed);
            engine.State.Populations[0].EnergyReserveJ = value;
            var read = StateSnapshotJson.Read(StateSnapshotJson.Write(StateSnapshot.Capture(engine, Identity())));

            Assert.Equal(
                BitConverter.DoubleToInt64Bits(value),
                BitConverter.DoubleToInt64Bits(read.Populations[0].EnergyReserveJ));
        }
    }

    [Fact]
    public void CaptureRefusesQueuedCommands()
    {
        var engine = Build(Seed);
        engine.ApplyCommand(new QueuedCommand());

        Assert.Throws<InvalidOperationException>(() => StateSnapshot.Capture(engine, Identity()));
    }

    [Fact]
    public void RestoreRejectsAnotherSeed()
    {
        var snapshot = StateSnapshot.Capture(Build(Seed), Identity());
        var other = Build(Seed + 1);

        Assert.Throws<InvalidOperationException>(() => snapshot.Restore(other));
    }

    [Fact]
    public void RestoreRejectsAnotherGrid()
    {
        var snapshot = StateSnapshot.Capture(Build(Seed), Identity());
        var other = Build(Seed, width: 4, height: 4);

        Assert.Throws<InvalidOperationException>(() => snapshot.Restore(other));
    }

    [Fact]
    public void RestoreOfAnInvalidStateThrowsAndLeavesTheEngineUntouched()
    {
        var source = Build(Seed);
        source.Advance(2);
        var good = StateSnapshot.Capture(source, Identity());
        var cells = good.Cells.ToArray();
        cells[17] = cells[17] with { TemperatureKelvin = double.NaN };
        var bad = good with { Cells = cells };

        var target = Build(Seed);
        target.Advance(1);
        var before = StateSnapshotJson.Write(StateSnapshot.Capture(target, Identity()));

        var error = Assert.Throws<SimulationInvariantException>(() => bad.Restore(target));

        Assert.Contains(error.Violations, violation => violation.CellIndex == 17);
        Assert.Equal(before, StateSnapshotJson.Write(StateSnapshot.Capture(target, Identity())));
    }

    [Fact]
    public void ReadRejectsAnUnknownSchema()
    {
        var text = Encoding.UTF8.GetString(StateSnapshotJson.Write(StateSnapshot.Capture(Build(Seed), Identity())));
        var changed = text.Replace(StateSnapshot.CurrentSchemaVersion, "martis-state-v999", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => StateSnapshotJson.Read(Encoding.UTF8.GetBytes(changed)));
    }

    [Fact]
    public void ReadRejectsAnExtraProperty()
    {
        var text = Encoding.UTF8.GetString(StateSnapshotJson.Write(StateSnapshot.Capture(Build(Seed), Identity())));
        var changed = text.Replace("\"tick\": ", "\"extra\": 1,\n  \"tick\": ", StringComparison.Ordinal);

        Assert.Throws<FormatException>(() => StateSnapshotJson.Read(Encoding.UTF8.GetBytes(changed)));
    }

    [Fact]
    public void ReadRejectsAShortPrngWord()
    {
        var snapshot = StateSnapshot.Capture(Build(Seed), Identity());
        var text = Encoding.UTF8.GetString(StateSnapshotJson.Write(snapshot));
        var word = snapshot.Random.Environment.State.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        var changed = text.Replace($"\"state\":\"{word}\"", "\"state\":\"abc\"", StringComparison.Ordinal);

        Assert.NotEqual(text, changed);
        Assert.Throws<FormatException>(() => StateSnapshotJson.Read(Encoding.UTF8.GetBytes(changed)));
    }

    private static ScenarioIdentity Identity() => new("fixture", "fixture-baseline", "0.3", false);

    private static SimulationEngine Build(ulong seed, int width = 8, int height = 8)
    {
        var scenario = MarsBaselineScenario.Load(ScenarioFixtures.Science, ScenarioFixtures.Deposits);
        var state = new SimulationState(width, height);
        scenario.Apply(state);
        state.Populations.Add(new Population(
            new PopulationId(1),
            new OrganismId("fixture.phototroph"),
            3,
            new Genome(new Dictionary<GeneId, double>
            {
                [new GeneId("UV_PROTECTION")] = 0.25,
                [new GeneId("REPAIR_CAPACITY")] = 0.5,
            }))
        {
            Abundance = 10.0,
            BiomassKg = 0.001,
            EnergyReserveJ = 1.0,
            AgeDistributionMeanSols = 2.0,
            Viability = 1.0,
        });
        var configuration = new SimulationConfiguration(
            width,
            height,
            1.0,
            seed,
            SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell,
            true);
        var systems = SimulationPipeline.CreateV0().Append(new AllStreamsProbe()).ToArray();
        return new SimulationEngine(state, configuration, RngStreams.Create(seed), systems);
    }

    /// <summary>
    /// Test-only step: draws from each stream and writes the draws into state, so
    /// every stream position is visible in the snapshot. Not a model of anything.
    /// </summary>
    private sealed class AllStreamsProbe : ISimulationSystem
    {
        public string Name => "test-all-streams";

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            var state = context.State;
            var cell = context.Random.Events.NextInt(0, state.CellCount);
            state.AllocationBuffers[cell].Water = context.Random.Environment.NextDouble();
            var population = state.Populations[0];
            population.EnergyReserveJ = context.Random.Reproduction.NextDouble();
            population.Viability = context.Random.Mutation.NextDouble();
            population.AgeDistributionMeanSols = context.Random.GeneticDrift.NextDouble();
        }
    }

    private sealed record QueuedCommand : Martis.Contracts.Commands.ISimulationCommand
    {
        public string Kind => "test-queued";
    }
}
