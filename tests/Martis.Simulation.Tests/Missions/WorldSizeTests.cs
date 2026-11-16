using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H3: maps of 64, 128 and 256 cells and the population budget (H-29).</summary>
public sealed class WorldSizeTests
{
    private static readonly Dictionary<string, double> Design = new() { ["UV_PROTECTION"] = 0.85 };

    [Fact]
    public void ChaptersOnBigMapsCarryAPopulationBudget()
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());

        Assert.Equal([null, 20000, 20000, 30000, 30000], campaign.Chapters.Select(chapter => chapter.PopulationBudget));
        Assert.Equal(new WorldOptions(128, 20000, "gradients.v1.json"), campaign.WorldFor(128));
        Assert.Equal(new WorldOptions(64), campaign.WorldFor(64));
        Assert.Null(campaign.WorldFor(100));
    }

    [Theory]
    [InlineData(128)]
    [InlineData(256)]
    public void ASessionPlaysOnABigMapAndItsLaboratoryUsesTheSameMap(int size)
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, withMission: false, new WorldOptions(size, 20000));

        Assert.Equal(size, session.Width);
        Assert.Equal(size, session.Height);
        Assert.Equal(size * size, session.View().BiomassKg.Length);
        Assert.True(session.Experiment("phototroph_a01", size - 2, size - 2, 0.5, Design, 5).Survived);
        Assert.True(session.Introduce("phototroph_a01", size - 1, size - 1, 0.5, Design, out var error), error);
        Assert.False(session.Introduce("phototroph_a01", size, 0, 0.5, null, out _));
        Assert.Equal(3, session.Advance(3));
        Assert.True(session.View().BiomassKg[(size * size) - 1] > 0);
    }

    [Fact]
    public void AResumedBigMapKeepsItsSizeFromTheSnapshot()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, withMission: false, new WorldOptions(128, 20000));
        session.Introduce("phototroph_a01", 100, 90, 0.5, null, out _);
        session.Advance(4);

        var resumed = GameSession.Resume(Repo.Root(), StateSnapshotBinary.Read(StateSnapshotBinary.Write(session.Capture())), withMission: false, new WorldOptions(64, 20000));

        Assert.Equal(128, resumed.Width);
        Assert.Equal(StateSnapshotJson.Write(session.Capture()), StateSnapshotJson.Write(resumed.Capture()));
    }

    [Fact]
    public void TheBudgetStopsNewVariantsButAnUnreachedBudgetChangesNothing()
    {
        static GameSession Run(int? budget)
        {
            var session = GameSession.NewGame(Repo.Root(), 5UL, withMission: false, new WorldOptions(64, budget));
            session.Introduce("phototroph_a01", 32, 32, 0.5, Design, out _);
            session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
            session.Advance(300);
            return session;
        }

        var free = Run(null);
        var roomy = Run(1_000_000);
        var tight = Run(2);

        Assert.Contains(free.Events, item => item.Kind == "mutation");
        Assert.Equal(StateSnapshotJson.Write(free.Capture()), StateSnapshotJson.Write(roomy.Capture()));
        Assert.DoesNotContain(tight.Events, item => item.Kind == "mutation");
        Assert.True(tight.View().TotalBiomassKg > 0);
    }

    [Fact]
    public void AnInvalidBudgetIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => GameSession.NewGame(Repo.Root(), 5UL, withMission: false, new WorldOptions(64, 0)));
}
