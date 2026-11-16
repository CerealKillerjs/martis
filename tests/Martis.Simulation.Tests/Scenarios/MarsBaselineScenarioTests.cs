using System.Text.Json.Nodes;
using Martis.Domain.Environment;
using Martis.Domain.Parameters;
using Martis.Domain.Validation;
using Martis.Simulation.Engine;
using Martis.Simulation.Scenarios;

namespace Martis.Simulation.Tests.Scenarios;

public sealed class MarsBaselineScenarioTests
{
    [Fact]
    public void FixtureLoadsMappedValuesAndLeavesTheUnassignedFieldsUnset()
    {
        var scenario = Load();
        var engine = MarsBaselineWorld.Create(scenario, 7UL, 2, 2);
        var cell = engine.State.Environment[0];

        Assert.Equal("gale", scenario.CellSite);
        Assert.Equal("fixture-baseline", scenario.Name);
        Assert.IsType<EnvironmentScope.Regional>(scenario.Scope);
        Equal(250, cell.TemperatureKelvin.Value);
        Equal(100, cell.PressurePascal.Value);
        Equal(0.25, cell.IonizingAbsorbedDoseGyPerSol);
        Equal(0.5, cell.IonizingEquivalentDoseSvPerSol);
        Equal(10, cell.SolarIrradiance);
        Equal(20, cell.PerchlorateWtPercent);
        Equal(5, cell.WaterIceWtPercent);
        Equal(0.5, cell.WaterIce);
        Assert.NotEqual(cell.WaterIce, cell.WaterIceWtPercent);
        Assert.False(scenario.SepEnabled);
        Assert.True(cell.UvRadiation.IsUnset);
        Assert.True(cell.SoilPorosity.IsUnset);
        Assert.True(cell.PerchlorateMassKg.IsUnset);
        Assert.True(cell.WaterVapor.IsUnset);
        Assert.Equal("regional:gale", cell.Scope switch
        {
            EnvironmentScope.Regional regional => "regional:" + regional.SiteId.Value,
            _ => "other",
        });

        var temperature = scenario.DeliveredNumbers.Single(value => value.Id == "fix.temperature");
        Assert.Equal(ParameterClassification.ScientificEstimated, temperature.Classification);
        Assert.IsType<ParameterScope.Regional>(temperature.Scope);
        Equal(2, engine.State.Resources[0].Water);
        Assert.True(engine.State.MassLedgers[0].Matches(engine.State.Resources[0], cell.WaterIce));
        Equal(
            CellWaterBudget.ComponentSum(2, 0.5, 0),
            engine.State.WaterBudgets[0].RecordedTotalKg);
        Assert.Empty(SimulationValidator.Validate(engine.State, ValidationContext.LoadedScenario));
        Assert.Empty(scenario.Warnings);
    }

    [Theory]
    [InlineData("fix.pending", ParameterUnavailableReason.Pending)]
    [InlineData("fix.historical", ParameterUnavailableReason.Historical)]
    [InlineData("fix.superseded", ParameterUnavailableReason.Superseded)]
    [InlineData("fix.unused", ParameterUnavailableReason.NotUsedInV0)]
    public void ReadingABlockedParameterNamesTheId(string id, ParameterUnavailableReason reason)
    {
        var scenario = Load();
        var error = Assert.Throws<ParameterUnavailableException>(() => scenario.ReadNumber(id));
        Assert.Equal(id, error.ParameterId);
        Assert.Equal(reason, error.Reason);
        Assert.Contains(id, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnmappedIdIsNotDelivered()
    {
        var scenario = Load();
        var error = Assert.Throws<ScenarioLoadException>(() => scenario.ReadNumber("fix.extra"));
        Assert.Equal("fix.extra", error.ParameterId);
        Assert.Contains("fix.extra", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMapIdWithoutUsedInV0FailsAndNamesTheId()
    {
        var json = MutateScience(root => root["parameters"]![0]!["used_in_v0"] = false);
        var error = Assert.Throws<ScenarioLoadException>(() => MarsBaselineScenario.Load(json, ScenarioFixtures.Deposits));
        Assert.Equal("fix.temperature", error.ParameterId);
        Assert.Contains("fix.temperature", error.Message, StringComparison.Ordinal);
        Assert.Contains("used_in_v0", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingMapIdFailsAndNamesTheId()
    {
        var json = MutateScience(root => root["metadata"]!["v0_field_map"]!["TemperatureKelvin"]!["id"] = "fix.missing");
        var error = Assert.Throws<ScenarioLoadException>(() => MarsBaselineScenario.Load(json, ScenarioFixtures.Deposits));
        Assert.Equal("fix.missing", error.ParameterId);
        Assert.Contains("fix.missing", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnUnknownMapKeyFailsAndNamesTheKey()
    {
        var json = MutateScience(root =>
        {
            var map = root["metadata"]!["v0_field_map"]!.AsObject();
            map["Temperature"] = new JsonObject
            {
                ["id"] = "fix.temperature",
                ["unit"] = "K",
            };
        });
        var error = Assert.Throws<ScenarioLoadException>(() => MarsBaselineScenario.Load(json, ScenarioFixtures.Deposits));
        Assert.Equal("Temperature", error.FieldName);
        Assert.Contains("Temperature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMissingEnvironmentPropertyFailsTheCoverageCheck()
    {
        var json = MutateScience(root => root["metadata"]!["v0_field_map"]!.AsObject().Remove("SoilPorosity"));
        var error = Assert.Throws<ScenarioLoadException>(() => MarsBaselineScenario.Load(json, ScenarioFixtures.Deposits));
        Assert.Equal("SoilPorosity", error.FieldName);
        Assert.Contains("SoilPorosity", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARegionalMismatchWarnsAndDoesNotRewriteTheValue()
    {
        var json = MutateScience(root => root["parameters"]![0]!["scope"] = "regional:jezero");
        var scenario = MarsBaselineScenario.Load(json, ScenarioFixtures.Deposits);
        var engine = MarsBaselineWorld.Create(scenario, 1UL, 1, 1);
        Equal(250, engine.State.Environment[0].TemperatureKelvin.Value);
        var warning = Assert.Single(scenario.Warnings);
        Assert.Equal("fix.temperature", warning.ParameterId);
        Assert.Equal("gale", warning.CellSite);
        Assert.Contains("regional:jezero", warning.Message, StringComparison.Ordinal);
        Assert.Contains("gale", warning.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(EnvironmentState.UvRadiation))]
    [InlineData(nameof(EnvironmentState.SoilPorosity))]
    [InlineData(nameof(EnvironmentState.PerchlorateMassKg))]
    [InlineData(nameof(EnvironmentState.WaterVapor))]
    public void AV0SystemCannotReadAnUnassignedField(string field)
    {
        var engine = MarsBaselineWorld.Create(Load(), 1UL, 1, 1);
        var error = Assert.Throws<InvalidOperationException>(() =>
            EnvironmentSystem.ReadAssigned(engine.State.Environment[0], field));
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
        engine.Advance(3);
        Assert.True(engine.State.Environment[0].UvRadiation.IsUnset);
        Assert.True(engine.State.Environment[0].SoilPorosity.IsUnset);
        Assert.True(engine.State.Environment[0].PerchlorateMassKg.IsUnset);
        Assert.True(engine.State.Environment[0].WaterVapor.IsUnset);
    }

    private static MarsBaselineScenario Load()
        => MarsBaselineScenario.Load(ScenarioFixtures.Science, ScenarioFixtures.Deposits);

    private static string MutateScience(Action<JsonNode> edit)
    {
        var root = JsonNode.Parse(ScenarioFixtures.Science)!;
        edit(root);
        return root.ToJsonString();
    }

    private static void Equal(double expected, double actual)
        => Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
}
