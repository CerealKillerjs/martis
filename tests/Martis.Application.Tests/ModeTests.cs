using Martis.Application;

namespace Martis.Application.Tests;

/// <summary>V1: modes are presentations of one world (Documento Maestro v2.0 §3).</summary>
public sealed class ModeTests
{
    [Fact]
    public void TheGameOpensOnTheStrategicView()
        => Assert.Equal(GameMode.Strategic, new ModeController().Current);

    [Fact]
    public void SwitchingReportsTheChangeAndRepeatingItDoesNothing()
    {
        var modes = new ModeController();

        Assert.Equal(new ModeChange(GameMode.Strategic, GameMode.Laboratory), modes.Switch(GameMode.Laboratory));
        Assert.Null(modes.Switch(GameMode.Laboratory));
        Assert.Equal(GameMode.Laboratory, modes.Current);
    }

    [Fact]
    public void TabGoesBetweenTheRobotAndTheStrategicView()
    {
        var modes = new ModeController();

        modes.Toggle();
        Assert.Equal(GameMode.Exploration, modes.Current);
        modes.Toggle();
        Assert.Equal(GameMode.Strategic, modes.Current);

        modes.Switch(GameMode.Planetary);
        modes.Toggle();
        Assert.Equal(GameMode.Exploration, modes.Current);
    }

    [Fact]
    public void BackReturnsToThePreviousMode()
    {
        var modes = new ModeController(GameMode.Exploration);

        modes.Switch(GameMode.Laboratory);
        modes.Back();

        Assert.Equal(GameMode.Exploration, modes.Current);
    }

    [Fact]
    public void ExplorationKeepsTimeReadable()
    {
        Assert.Equal(1, ModeController.MaxSpeed(GameMode.Exploration));
        Assert.Equal(TimeController.FastestSpeed, ModeController.MaxSpeed(GameMode.Strategic));
        Assert.Equal(TimeController.FastestSpeed, ModeController.MaxSpeed(GameMode.Planetary));
    }

    [Fact]
    public void AnUnknownModeIsRejected()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new ModeController().Switch((GameMode)42));
}
