using Martis.Contracts.Commands;
using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;
using Martis.Simulation.Terraforming;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Terraforming;

/// <summary>E1 (ADR-033): local heating with relaxation, wear, power and save/resume.</summary>
public sealed class ClimateTests
{
    private static HeatingDefinition Definition() => HeatingDefinition.LoadPublished(Repo.Root());

    private static RegionalClimate Flat(double kelvin = 210.0) => RegionalClimate.Start(Definition(), 16, 16, Enumerable.Repeat(kelvin, 256).ToArray());

    [Fact]
    public void ThePublishedHeatingLoadsItsValues()
    {
        var definition = Definition();

        Assert.Equal("calentamiento_local", definition.Id);
        Assert.True(definition.PeakDeltaK > 0);
        Assert.True(definition.RelaxationSols > 1);
        Assert.True(definition.PowerW > 0);
    }

    [Fact]
    public void HeatersNeedMeasuredGroundAFreeCellAndPower()
    {
        var climate = Flat();

        Assert.Equal(BuildResult.NotMeasured, climate.Place(4, 4, KnowledgeLevel.Estimated, 300, 0, out _));
        Assert.Equal(BuildResult.NoPower, climate.Place(4, 4, KnowledgeLevel.Measured, 100, 0, out _));
        Assert.Equal(BuildResult.Built, climate.Place(4, 4, KnowledgeLevel.Measured, 300, 0, out var heater));
        Assert.Equal(BuildResult.Occupied, climate.Place(4, 4, KnowledgeLevel.Measured, 300, 0, out _));
        Assert.Equal(1, heater!.Id);
        Assert.Equal(climate.Definition.PowerW, climate.PowerUsedW, 9);
    }

    [Fact]
    public void HeatBuildsUpSlowlyAndOnlyNearTheHeater()
    {
        var climate = Flat();
        climate.Place(8, 8, KnowledgeLevel.Measured, 300, 0, out _);

        var (first, _) = climate.Step();
        var afterOne = climate.Anomaly(8, 8);
        for (var i = 0; i < 20; i++)
        {
            climate.Step();
        }

        Assert.NotEmpty(first);
        Assert.InRange(afterOne, 0.01, climate.Definition.PeakDeltaK * 0.5);
        Assert.True(climate.Anomaly(8, 8) > afterOne * 3);
        Assert.True(climate.Anomaly(8, 8) <= climate.Definition.PeakDeltaK + 1e-9);
        Assert.True(climate.Anomaly(9, 8) < climate.Anomaly(8, 8));
        Assert.Equal(0.0, climate.Anomaly(0, 0));
        Assert.True(climate.WarmedCells > 1);
    }

    [Fact]
    public void ABrokenHeaterStopsAndTheHeatFades()
    {
        var climate = Flat();
        climate.Place(8, 8, KnowledgeLevel.Measured, 300, 0, out var heater);
        var sols = (int)Math.Ceiling(1.0 / climate.Definition.WearPerSol);
        var broke = new List<Heater>();
        for (var i = 0; i < sols; i++)
        {
            broke.AddRange(climate.Step().Broke);
        }

        var peak = climate.Anomaly(8, 8);
        for (var i = 0; i < 30; i++)
        {
            climate.Step();
        }

        Assert.Same(heater, Assert.Single(broke));
        Assert.True(heater!.Broken);
        Assert.Equal(0.0, climate.PowerUsedW);
        Assert.True(climate.Anomaly(8, 8) < peak * 0.1);
        Assert.True(climate.Repair(heater));
        Assert.False(heater.Broken);
    }

    [Fact]
    public void TheCeilingCapsTheWarming()
    {
        var climate = Flat(climate_max() - 5);
        climate.Place(8, 8, KnowledgeLevel.Measured, 300, 0, out _);
        for (var i = 0; i < 40; i++)
        {
            climate.Step();
        }

        Assert.InRange(climate.Anomaly(8, 8), 0.0, 5.0 + 1e-9);

        static double climate_max() => Definition().MaxTemperatureK;
    }

    [Fact]
    public void TheEngineRejectsInvalidTemperatures()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL);

        Assert.ThrowsAny<ArgumentException>(() =>
        {
            session.Engine.ApplyCommand(new SetCellTemperatureCommand(-1, 210));
            session.Engine.Advance(1);
        });
    }

    [Fact]
    public void ASessionWarmsTheGroundAndResumesTheSameClimate()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL);
        var climate = session.AttachClimate(Definition());
        var before = session.Engine.State.Environment[session.CellIndex(10, 10)].TemperatureKelvin.Value;
        Assert.Equal(BuildResult.Built, climate.Place(10, 10, KnowledgeLevel.Measured, 300, session.Tick, out _));

        Assert.Equal(6, session.Advance(6));
        var after = session.Engine.State.Environment[session.CellIndex(10, 10)].TemperatureKelvin.Value;
        var record = climate.Capture();
        var resumed = RegionalClimate.Resume(Definition(), session.Engine.State.Environment.Select(e => e.TemperatureKelvin.Value).ToArray(), record);

        Assert.Null(session.Failure);
        Assert.True(after > before + 1, $"{before} -> {after}");
        Assert.Equal(before, resumed.BaselineK(10, 10), 9);
        Assert.Equal(climate.Anomaly(10, 10), resumed.Anomaly(10, 10), 12);
        Assert.Single(resumed.Heaters);
    }
}
