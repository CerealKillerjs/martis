using System.Security.Cryptography;
using System.Text.Json;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.Parameters;
using Martis.Domain.Validation;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Tests.Scenarios;

/// <summary>
/// The published v0.3 file is the definitive load. Expected numbers are read
/// back from that file. They are not copied into this test as literals.
/// </summary>
public sealed class PublishedMarsBaselineTests
{
    private const string PublishedSha256 = "dd9830437cc9ad0b7fde4512ea21e6ad1de77cf05991111a2016edce7cebe69b";

    [Fact]
    public void PublishedFileMatchesTheV04Hash()
    {
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SciencePath()))).ToLowerInvariant();
        Assert.Equal(PublishedSha256, hash);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public void PublishedV04LoadsOntoEveryCell()
    {
        using var science = JsonDocument.Parse(File.ReadAllText(SciencePath()));
        using var deposits = JsonDocument.Parse(File.ReadAllText(DepositsPath()));
        var scenario = MarsBaselineScenario.LoadPublished(Repo.Root());
        var engine = MarsBaselineWorld.Create(scenario, 12345UL);

        Assert.Equal(4096, engine.State.CellCount);
        Assert.Equal("0.4", scenario.DataVersion);
        Assert.Equal("gale", scenario.CellSite);
        var scopeId = science.RootElement.GetProperty("metadata").GetProperty("scope_id").GetString();
        var freeText = science.RootElement.GetProperty("metadata").GetProperty("scope").GetString();
        Assert.Equal("regional:gale", scopeId);
        Assert.NotEqual(scopeId, freeText);
        Assert.False(scenario.SepEnabled);

        var parameters = Index(science.RootElement.GetProperty("parameters"));
        var map = science.RootElement.GetProperty("metadata").GetProperty("v0_field_map");
        foreach (var field in map.EnumerateObject())
        {
            var id = field.Value.GetProperty("id");
            if (id.ValueKind == JsonValueKind.Null)
            {
                Assert.Equal("UNASSIGNED", field.Value.GetProperty("status").GetString());
                continue;
            }

            var parameter = parameters[id.GetString()!];
            Assert.True(parameter.GetProperty("used_in_v0").GetBoolean());
            if (field.Name == "SepEnabled")
            {
                Assert.Equal(parameter.GetProperty("value").GetBoolean(), scenario.SepEnabled);
                Assert.Equal(ParseClassification(parameter), scenario.SepEnabledValue.Classification);
                continue;
            }

            var delivered = scenario.DeliveredNumbers.Single(value => value.Id == id.GetString());
            Assert.Equal(ParseClassification(parameter), delivered.Classification);
            var regional = Assert.IsType<ParameterScope.Regional>(delivered.Scope);
            Assert.Equal("gale", regional.Site);
            Equal(parameter.GetProperty("value").GetDouble(), delivered.Value);
        }

        var depositParameters = Index(deposits.RootElement.GetProperty("parameters"));
        foreach (var deposit in scenario.DepositValues)
        {
            var parameter = depositParameters[deposit.Id];
            Assert.Equal(ParameterClassification.GameBalance, deposit.Classification);
            Assert.Equal("kg", deposit.Unit);
            Assert.False(string.IsNullOrWhiteSpace(parameter.GetProperty("rationale").GetString()));
            Equal(parameter.GetProperty("value").GetDouble(), deposit.Value);
        }

        var ice = scenario.DepositValues.Single(value => value.Id == "game.gale.deposit.water_ice");
        var icePercent = scenario.DeliveredNumbers.Single(value => value.Field == "water_ice_surface_present"
            || value.Id == "env.gale.water_ice.surface_present");
        Assert.NotEqual(ice.Id, icePercent.Id);

        for (var i = 0; i < engine.State.CellCount; i++)
        {
            var cell = engine.State.Environment[i];
            Equal(scenario.DeliveredNumbers.Single(value => value.Id == "env.gale.air_temperature.reference").Value, cell.TemperatureKelvin.Value);
            Equal(ice.Value, cell.WaterIce);
            Equal(icePercent.Value, cell.WaterIceWtPercent);
            Assert.True(cell.UvRadiation.IsUnset);
            Assert.True(cell.SoilPorosity.IsUnset);
            Assert.True(cell.PerchlorateMassKg.IsUnset);
            Assert.True(cell.WaterVapor.IsUnset);
            Assert.True(engine.State.MassLedgers[i].Matches(engine.State.Resources[i], cell.WaterIce));
            Equal(engine.State.MassLedgers[i].RecordedInitialTotalKg, CellMassLedger.Sum(CellMassLedger.Read(engine.State.Resources[i], cell.WaterIce)));
        }

        Assert.Empty(SimulationValidator.Validate(engine.State, ValidationContext.LoadedScenario));
        Assert.Empty(scenario.Warnings);

        foreach (var id in science.RootElement.GetProperty("metadata").GetProperty("pending").EnumerateArray())
        {
            var parameterId = id.GetString()!;
            var error = Assert.Throws<ParameterUnavailableException>(() => scenario.ReadNumber(parameterId));
            Assert.Equal(parameterId, error.ParameterId);
            Assert.Equal(ParameterUnavailableReason.Pending, error.Reason);
        }

        var historical = Assert.Throws<ParameterUnavailableException>(() => scenario.ReadNumber("env.gale.air_temperature.midrange"));
        Assert.Equal(ParameterUnavailableReason.Historical, historical.Reason);
        var superseded = Assert.Throws<ParameterUnavailableException>(() => scenario.ReadNumber("env.gale.radiation.gcr_dose_equivalent_rate_hassler2014"));
        Assert.Equal(ParameterUnavailableReason.Superseded, superseded.Reason);
    }

    [Fact]
    [Trait("Category", "Determinism")]
    public void TheSameSeedAndScenarioKeepTheSameSnapshotHash()
    {
        var scenario = MarsBaselineScenario.LoadPublished(Repo.Root());
        var first = Run(scenario, 12345UL, 100);
        var second = Run(scenario, 12345UL, 100);
        Assert.Equal(first.Hash, second.Hash);
        Assert.NotEqual(first.Hash, Run(scenario, 99UL, 100).Hash);
        Assert.Equal(first.TemperatureBits, second.TemperatureBits);
        Assert.Equal(first.Random, second.Random);
        Assert.Equal(first.RandomBefore, first.Random);
        Assert.True(first.MassStillMatches);
        Assert.Equal(100, first.Tick);
    }

    [Fact]
    public void SnapshotRoundTripRestoresTheEnvironmentAndTheGenerator()
    {
        var scenario = MarsBaselineScenario.LoadPublished(Repo.Root());
        var engine = MarsBaselineWorld.Create(scenario, 5UL);
        engine.Advance(4);
        var snapshot = ScenarioRunSnapshot.Capture(engine, scenario);
        var random = engine.Random.ExportState();
        var temperature = engine.State.Environment[0].TemperatureKelvin.Value;
        engine.State.Environment[0].TemperatureKelvin = AssignedDouble.Set(10);
        _ = engine.Random.Environment.NextUInt64();

        snapshot.Restore(engine);

        Equal(temperature, engine.State.Environment[0].TemperatureKelvin.Value);
        Assert.Equal(random, engine.Random.ExportState());
        Assert.Equal(4, engine.CurrentTick);

        var twin = MarsBaselineWorld.Create(scenario, 5UL);
        twin.Advance(4);
        engine.Advance(7);
        twin.Advance(7);
        Assert.Equal(
            ScenarioRunSnapshot.Capture(engine, scenario).Sha256Hex(),
            ScenarioRunSnapshot.Capture(twin, scenario).Sha256Hex());
    }

    [Fact]
    public void TheViewSnapshotHasEveryCellAndDoesNotFollowLaterEdits()
    {
        var scenario = MarsBaselineScenario.LoadPublished(Repo.Root());
        var engine = MarsBaselineWorld.Create(scenario, 5UL);
        var genome = new Genome(new Dictionary<GeneId, double> { [new GeneId("a")] = 0.25 });
        var population = new Population(new PopulationId(7), new OrganismId("organism"), 0, genome)
        {
            Abundance = 3,
        };
        engine.State.Populations.Add(population);
        engine.Advance(2);
        var view = SimulationSnapshotFactory.Capture(engine, scenario);

        Assert.Equal(4096, view.Cells.Count);
        Assert.Equal(2, view.Tick);
        Assert.Equal(2, view.Statistics.Tick);
        Assert.Equal(4096, view.Statistics.CellCount);
        Assert.False(view.SepEnabled);
        Assert.Null(view.Cells[0].UvRadiation);
        Assert.Null(view.Cells[0].SoilPorosity);
        Assert.Null(view.Cells[0].WaterVaporKg);
        Assert.Null(view.Cells[0].PerchlorateMassKg);
        var seen = view.Cells[0].TemperatureKelvin;
        var abundance = Assert.Single(view.Populations).Abundance;
        engine.State.Environment[0].TemperatureKelvin = AssignedDouble.Set(10);
        population.Abundance = 9;

        Assert.Equal(seen, view.Cells[0].TemperatureKelvin);
        Equal(3, abundance);
        Assert.NotEqual(10, view.Cells[0].TemperatureKelvin);
    }

    [Fact]
    public void InjectingNanTemperatureFailsWithTheTickAndTheCell()
    {
        var scenario = MarsBaselineScenario.LoadPublished(Repo.Root());
        var engine = MarsBaselineWorld.Create(scenario, 5UL);
        engine.Advance(4);
        engine.State.Environment[5].TemperatureKelvin = AssignedDouble.Set(double.NaN);

        var error = Assert.Throws<SimulationInvariantException>(() => engine.Advance(1));

        Assert.Equal(4, error.Tick);
        Assert.Equal(4, engine.CurrentTick);
        Assert.Contains("DOM-VAL-001", error.Message, StringComparison.Ordinal);
        Assert.Contains("cell 5", error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(EnvironmentState.TemperatureKelvin), error.Message, StringComparison.Ordinal);
    }

    private static RunResult Run(MarsBaselineScenario scenario, ulong seed, int steps)
    {
        var engine = MarsBaselineWorld.Create(scenario, seed);
        var temperature = engine.State.Environment[0].TemperatureKelvin.Value;
        var random = engine.Random.ExportState();
        engine.Advance(steps);
        var unchanged = true;
        for (var i = 0; i < engine.State.CellCount; i++)
        {
            if (BitConverter.DoubleToInt64Bits(engine.State.Environment[i].TemperatureKelvin.Value)
                != BitConverter.DoubleToInt64Bits(temperature))
            {
                unchanged = false;
            }
        }

        Assert.True(unchanged);
        return new RunResult(
            ScenarioRunSnapshot.Capture(engine, scenario).Sha256Hex(),
            BitConverter.DoubleToInt64Bits(engine.State.Environment[0].TemperatureKelvin.Value),
            engine.Random.ExportState(),
            engine.State.MassLedgers[0].Matches(engine.State.Resources[0], engine.State.Environment[0].WaterIce),
            engine.CurrentTick,
            random);
    }

    private static Dictionary<string, JsonElement> Index(JsonElement parameters)
    {
        var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var parameter in parameters.EnumerateArray())
        {
            index.Add(parameter.GetProperty("id").GetString()!, parameter);
        }

        return index;
    }

    private static ParameterClassification ParseClassification(JsonElement parameter)
    {
        return parameter.GetProperty("classification").GetString() switch
        {
            "SCIENTIFIC_MEASURED" => ParameterClassification.ScientificMeasured,
            "SCIENTIFIC_ESTIMATED" => ParameterClassification.ScientificEstimated,
            "MODEL_ASSUMPTION" => ParameterClassification.ModelAssumption,
            "GAME_BALANCE" => ParameterClassification.GameBalance,
            "SPECULATIVE" => ParameterClassification.Speculative,
            "UNCLASSIFIED_PENDING" => ParameterClassification.UnclassifiedPending,
            var other => throw new InvalidOperationException($"Unknown classification '{other}'."),
        };
    }

    private static string SciencePath()
        => Path.Combine(Repo.Root(), "data", "scenarios", "MarsBaseline", MarsBaselineScenario.ScienceFileName);

    private static string DepositsPath()
        => Path.Combine(Repo.Root(), "data", "scenarios", "MarsBaseline", MarsBaselineScenario.DepositsFileName);

    private static void Equal(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));

    private readonly record struct RunResult(
        string Hash,
        long TemperatureBits,
        RngStreamState Random,
        bool MassStillMatches,
        long Tick,
        RngStreamState RandomBefore);
}
