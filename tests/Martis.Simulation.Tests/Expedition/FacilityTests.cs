using Martis.Simulation.Expedition;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Expedition;

/// <summary>V4 (ADR-031): fixed stations, power, wear, faults and the rover's autopilot.</summary>
public sealed class FacilityTests
{
    private static FacilityDefinition Definition() => FacilityDefinition.LoadPublished(Repo.Root());

    private static KnowledgeMap Knowledge() => new(SurveyDefinition.LoadPublished(Repo.Root()), 64, 64);

    [Fact]
    public void ThePublishedFacilitiesLoadTheirBalanceValues()
    {
        var definition = Definition();

        Assert.Equal(3, definition.MaxStations);
        Assert.Equal(0.0, definition.FaultProbability(0.5), 12);
        Assert.Equal(definition.FaultMaxProbability, definition.FaultProbability(1.0), 12);
        Assert.True(definition.FaultProbability(0.75) is > 0 and < 0.6);
    }

    [Fact]
    public void StationsNeedMeasuredGroundAFreeCellAndPower()
    {
        var network = new FacilityNetwork(Definition());

        Assert.Equal(BuildResult.NotMeasured, network.Build(1, 1, KnowledgeLevel.Estimated, 0, out _));
        Assert.Equal(BuildResult.Built, network.Build(1, 1, KnowledgeLevel.Measured, 0, out var first));
        Assert.Equal(BuildResult.Occupied, network.Build(1, 1, KnowledgeLevel.Measured, 0, out _));
        Assert.Equal(BuildResult.Built, network.Build(2, 1, KnowledgeLevel.Measured, 0, out _));
        Assert.Equal(BuildResult.Built, network.Build(3, 1, KnowledgeLevel.Measured, 0, out _));
        Assert.Equal(BuildResult.NoPower, network.Build(4, 1, KnowledgeLevel.Measured, 0, out var none));
        Assert.Equal(1, first!.Id);
        Assert.Null(none);
        Assert.Equal(300.0, network.PowerUsedW, 9);
    }

    [Fact]
    public void AWorkingStationMeasuresItsSurroundingsEverySol()
    {
        var network = new FacilityNetwork(Definition());
        var knowledge = Knowledge();
        network.Build(20, 20, KnowledgeLevel.Measured, 0, out _);

        network.Tick(knowledge);

        Assert.Equal(KnowledgeLevel.Measured, knowledge.Level(20, 20));
        Assert.Equal(KnowledgeLevel.Measured, knowledge.Level(23, 20));
        Assert.Equal(KnowledgeLevel.Unknown, knowledge.Level(24, 20));
    }

    [Fact]
    public void StationsWearOutAndFallSilentUntilRepaired()
    {
        var definition = Definition();
        var network = new FacilityNetwork(definition);
        network.Build(10, 10, KnowledgeLevel.Measured, 0, out var station);
        var sols = (int)Math.Ceiling(1.0 / definition.StationWearPerSol);

        var broke = Enumerable.Range(0, sols).SelectMany(_ => network.Tick(Knowledge())).ToArray();

        Assert.Equal([station!], broke);
        Assert.True(station!.Broken);
        var silent = Knowledge();
        network.Tick(silent);
        Assert.Equal(KnowledgeLevel.Unknown, silent.Level(10, 10));
        Assert.True(network.Repair(station));
        Assert.False(station.Broken);
        Assert.False(network.Repair(station));
    }

    [Fact]
    public void TheRoverFindsTheNearestStationWithinRepairRadius()
    {
        var network = new FacilityNetwork(Definition());
        network.Build(10, 10, KnowledgeLevel.Measured, 0, out var a);
        network.Build(12, 10, KnowledgeLevel.Measured, 0, out var b);

        Assert.Same(b, network.Near(12.4, 10.5));
        Assert.Same(a, network.Near(10.5, 10.5));
        Assert.Null(network.Near(30, 30));
    }

    [Fact]
    public void FaultsAreDeterministicAndFollowTheProbability()
    {
        var hits = Enumerable.Range(0, 4000).Count(i => Faults.Happens(42, 7, i, 0.3));

        Assert.Equal(Faults.Happens(42, 7, 5, 0.3), Faults.Happens(42, 7, 5, 0.3));
        Assert.InRange(hits, 1000, 1400);
        Assert.False(Faults.Happens(42, 7, 5, 0));
        Assert.True(Faults.Happens(42, 7, 5, 1));
    }

    [Fact]
    public void WearSlowsTheRoverAndRepairsRestoreIt()
    {
        var rover = new Rover(RoverDefinition.LoadPublished(Repo.Root()), 64, 64);
        rover.AddWear(0.9);

        Assert.Equal(0.6, rover.SpeedFactor, 9);
        Assert.Equal(6.0 * 0.6, rover.Drive(1, 0, 1), 9);
        rover.AddWear(5);
        Assert.Equal(1.0, rover.Wear, 12);
        rover.Repair(0.8);
        Assert.Equal(0.2, rover.Wear, 9);
        Assert.Equal(1.0, rover.SpeedFactor, 12);
        Assert.Throws<ArgumentOutOfRangeException>(() => rover.AddWear(-1));
    }

    [Fact]
    public void TheAutopilotDrivesTheRoverToItsTarget()
    {
        var rover = new Rover(RoverDefinition.LoadPublished(Repo.Root()), 64, 64);
        rover.Place(10, 10, 0);

        for (var i = 0; i < 400 && rover.SteerTowards(10, 30) != (0, 0); i++)
        {
            var (throttle, steer) = rover.SteerTowards(10, 30);
            rover.Drive(throttle, steer, 0.05);
        }

        Assert.Equal((0.0, 0.0), rover.SteerTowards(10, 30));
        Assert.True(rover.DistanceTo(10, 30) <= 0.6);
    }
}
