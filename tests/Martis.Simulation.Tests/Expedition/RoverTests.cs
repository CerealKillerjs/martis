using Martis.Simulation.Expedition;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Expedition;

/// <summary>J1 «Expedición» (ADR-026): the seeding rover the player drives over the map.</summary>
public sealed class RoverTests
{
    private static RoverDefinition Definition() => RoverDefinition.LoadPublished(Repo.Root());

    private static Rover NewRover(int size = 64) => new(Definition(), size, size);

    [Fact]
    public void ThePublishedRoverLoadsItsBalanceValues()
    {
        var definition = Definition();

        Assert.Equal(6.0, definition.SpeedCellsPerSecond, 12);
        Assert.Equal(3.0, definition.ReachCells, 12);
        Assert.Equal(160.0, definition.BatteryCells, 12);
        Assert.InRange(definition.CrawlFactor, 0.0, 1.0);
    }

    [Fact]
    public void TheRoverStartsChargedAtTheCentreOfTheMap()
    {
        var rover = NewRover();

        Assert.Equal((32, 32), rover.Cell);
        Assert.Equal(1.0, rover.Battery, 12);
    }

    [Fact]
    public void DrivingForwardMovesAlongTheHeadingAtFullSpeed()
    {
        var rover = NewRover();

        var driven = rover.Drive(throttle: 1, steer: 0, seconds: 1);

        Assert.Equal(6.0, driven, 9);
        Assert.Equal(32.5 + 6.0, rover.X, 9);
        Assert.Equal(32.5, rover.Y, 9);
    }

    [Fact]
    public void ReverseIsSlowerAndSteeringTurnsTheHeading()
    {
        var rover = NewRover();

        rover.Drive(throttle: -1, steer: 0, seconds: 1);
        Assert.Equal(32.5 - 3.0, rover.X, 9);

        rover.Drive(throttle: 0, steer: 1, seconds: 0.5);
        Assert.Equal(1.1, rover.Heading, 9);
    }

    [Fact]
    public void TheRoverStaysInsideTheMap()
    {
        var rover = NewRover();

        for (var i = 0; i < 100; i++)
        {
            rover.Drive(1, 0, 1);
        }

        Assert.Equal(63, rover.Cell.X);
        Assert.InRange(rover.X, 0.0, 64.0);
    }

    [Fact]
    public void DrivingDrainsTheBatteryAndAnEmptyBatteryOnlyCrawls()
    {
        var rover = NewRover(256);
        rover.Landed(10, 10);

        rover.Drive(1, 0, 8);
        Assert.Equal(1.0 - (48.0 / 160.0), rover.Battery, 9);

        rover.Place(10, 128, heading: 0);
        rover.Drive(1, 0, 20);
        Assert.Equal(0.0, rover.Battery, 12);
        Assert.Equal(10.5 + 112.0 + ((20.0 - (112.0 / 6.0)) * 1.8), rover.X, 9);

        var x = rover.X;
        var driven = rover.Drive(1, 0, 1);
        Assert.Equal(6.0 * 0.3, driven, 9);
        Assert.Equal(x + 1.8, rover.X, 9);
    }

    [Fact]
    public void BeforeTheLanderArrivesTheBatteryDoesNotDrain()
    {
        var rover = NewRover();

        rover.Drive(1, 0, 4);

        Assert.Equal(1.0, rover.Battery, 12);
    }

    [Fact]
    public void TheBatteryRechargesNextToTheLander()
    {
        var rover = NewRover();
        rover.Landed(32, 32);
        rover.Drive(1, 0, 2);
        rover.Drive(-1, 0, 4);
        Assert.False(rover.Charging);
        var low = rover.Battery;
        Assert.True(low < 1.0);

        rover.Drive(0, 0, 0.2);

        Assert.Equal(low + 0.05, rover.Battery, 9);
        Assert.True(rover.Charging);
    }

    [Fact]
    public void FarFromTheLanderTheBatteryDoesNotRecharge()
    {
        var rover = NewRover();
        rover.Landed(5, 5);
        rover.Drive(1, 0, 1);
        var battery = rover.Battery;

        rover.Drive(0, 0, 10);

        Assert.Equal(battery, rover.Battery, 12);
        Assert.False(rover.Charging);
    }

    [Fact]
    public void OnlyCellsWithinReachCanBeSeeded()
    {
        var rover = NewRover();

        Assert.True(rover.CanReach(32, 32));
        Assert.True(rover.CanReach(35, 32));
        Assert.True(rover.CanReach(34, 34));
        Assert.False(rover.CanReach(36, 32));
        Assert.False(rover.CanReach(32, 40));
    }

    [Fact]
    public void PlacingTheRoverMovesItToACellCentre()
    {
        var rover = NewRover();

        rover.Place(10, 20, heading: Math.PI);

        Assert.Equal((10, 20), rover.Cell);
        Assert.Equal(10.5, rover.X, 12);
        Assert.Equal(Math.PI, rover.Heading, 12);
    }

    [Fact]
    public void ARoverFileWithAnotherSchemaIsRejected()
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "expedition", RoverDefinition.PublishedFile))
            .Replace("\"martis.rover\"", "\"martis.economy\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => RoverDefinition.Load(json));
    }

    [Fact]
    public void InputsAreClampedAndNegativeTimeIsRejected()
    {
        var rover = NewRover();

        rover.Drive(5, 0, 1);
        Assert.Equal(38.5, rover.X, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => rover.Drive(1, 0, -1));
    }

    [Fact]
    public void InstrumentsAreFreeBeforeLandingAndCostBatteryAfter()
    {
        var rover = NewRover();
        Assert.True(rover.Spend(0.5));
        Assert.Equal(1.0, rover.Battery, 12);

        rover.Landed(32, 32);

        Assert.True(rover.Spend(0.25));
        Assert.Equal(0.75, rover.Battery, 12);
    }

    [Fact]
    public void AnInstrumentIsRefusedWhenTheBatteryIsShort()
    {
        var rover = NewRover();
        rover.Landed(32, 32);
        Assert.True(rover.Spend(0.98));

        Assert.False(rover.Spend(0.04));
        Assert.Equal(0.02, rover.Battery, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => rover.Spend(-0.1));
    }
}
