using Martis.Application;

namespace Martis.Application.Tests;

/// <summary>V1: game time against real time (Documento Maestro v2.0 §3.2, §23).</summary>
public sealed class TimeControllerTests
{
    [Fact]
    public void AtNormalSpeedASolTakesTwoRealSeconds()
    {
        var time = new TimeController();

        Assert.Equal(0, time.Advance(1.5));
        Assert.Equal(0.75, time.SolProgress, 12);
        Assert.Equal(1, time.Advance(0.5));
        Assert.Equal(0.0, time.SolProgress, 12);
    }

    [Fact]
    public void PausedTimeRunsNoSols()
    {
        var time = new TimeController();
        time.Request(0);

        Assert.Equal(0, time.Advance(100));
        Assert.Equal(0.0, time.SolProgress, 12);
        Assert.True(time.Paused);
    }

    [Fact]
    public void AtMostOneSolRunsPerCall()
    {
        var time = new TimeController();
        time.Request(3);

        Assert.Equal(1, time.Advance(10));
        Assert.InRange(time.SolProgress, 0.0, 0.9991);
    }

    [Fact]
    public void AModeCapLowersTheSpeedAndTheRequestComesBackAfterwards()
    {
        var time = new TimeController();
        time.Request(3);

        time.Cap(ModeController.MaxSpeed(GameMode.Exploration));
        Assert.Equal(1, time.EffectiveSpeed);
        Assert.Equal(3, time.RequestedSpeed);

        time.Cap(ModeController.MaxSpeed(GameMode.Strategic));
        Assert.Equal(3, time.EffectiveSpeed);
    }

    [Fact]
    public void SpaceTogglesPause()
    {
        var time = new TimeController();

        time.TogglePause();
        Assert.True(time.Paused);
        time.TogglePause();
        Assert.Equal(1, time.EffectiveSpeed);
    }

    [Fact]
    public void TheClockCanBePlacedInsideTheSol()
    {
        var time = new TimeController();

        time.SetProgress(1.25);

        Assert.Equal(0.25, time.SolProgress, 12);
        Assert.Equal(0.125, TimeController.RealSecondsPerSol(3), 12);
    }

    [Fact]
    public void InvalidSpeedsAndTimesAreRejected()
    {
        var time = new TimeController();

        Assert.Throws<ArgumentOutOfRangeException>(() => time.Request(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => time.Advance(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => time.SetProgress(double.NaN));
    }
}
