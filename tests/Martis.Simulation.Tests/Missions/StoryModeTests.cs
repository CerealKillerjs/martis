using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>
/// M5 (ADR-039): the story mode plays every chapter in one game. The next mission resumes the
/// living world with its own mission and its chapter's rules on the same map.
/// </summary>
public sealed class StoryModeTests
{
    private static readonly CampaignDefinition Campaign = CampaignDefinition.LoadPublished(Repo.Root());

    [Fact]
    public void TheNextChapterContinuesTheSameWorld()
    {
        var first = Campaign.Chapters[0];
        var third = Campaign.Chapters[2];
        var world = first.World with { GridSize = 256 };
        var session = GameSession.NewGame(Repo.Root(), 7, first.Missions[0].File, world);
        Assert.True(session.Introduce("phototroph_a01", 128, 128, 0.5, null, out _));
        session.Advance(20);
        var biomass = session.View().TotalBiomassKg;

        var next = GameSession.Resume(Repo.Root(), session.Capture(), third.Missions[0].File, null, third.World with { GridSize = 256 });

        Assert.Equal(session.Tick, next.Tick);
        Assert.Equal(256, next.Width);
        Assert.Equal(biomass, next.View().TotalBiomassKg, 6);
        Assert.Equal(third.Missions[0].Id, next.Mission!.Definition.Id);
        Assert.False(next.Mission.IsComplete);
    }

    [Fact]
    public void LaterChaptersReachLaterEras()
    {
        var eras = Campaign.Chapters.Select(chapter => chapter.Era).ToArray();

        Assert.Equal(1, eras[0]);
        Assert.Equal(eras.OrderBy(era => era), eras);
        Assert.True(eras[^1] >= 3);
    }
}
