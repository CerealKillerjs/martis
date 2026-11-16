using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H4: chapter 2 «Ciclo cerrado» and its objective kinds.</summary>
public sealed class ChapterTwoTests
{
    private static readonly CampaignDefinition Campaign = CampaignDefinition.LoadPublished(Repo.Root());

    [Fact]
    public void ChapterTwoIsPlayableOnA128MapWithFourMissions()
    {
        var chapter = Campaign.Chapters[1];

        Assert.True(chapter.Playable);
        Assert.Equal(128, chapter.GridSize);
        Assert.Equal(["gradients", "closed_cycle", "cold_frontier", "oxygen"], chapter.Missions.Select(mission => mission.Id));
    }

    [Fact]
    public void ChapterTwoUnlocksAfterChapterOne()
    {
        var progress = new CampaignProgress(Campaign);
        Assert.False(progress.IsUnlocked("gradients"));

        foreach (var id in new[] { "awakening", "first_colony", "selection" })
        {
            Assert.True(progress.Complete(id));
        }

        Assert.True(progress.IsUnlocked("gradients"));
        Assert.Equal("gradients", progress.Next!.Id);
    }

    [Fact]
    public void NewObjectiveKindsLoadWithTheirBandsAndAmounts()
    {
        var closed = MissionDefinition.LoadPublished(Repo.Root(), "closed_cycle.v1.json");
        var cold = MissionDefinition.LoadPublished(Repo.Root(), "cold_frontier.v1.json");
        var oxygen = MissionDefinition.LoadPublished(Repo.Root(), "oxygen.v1.json");

        Assert.Equal(ObjectiveKind.Occupy, closed.Objectives[0].Kind);
        Assert.Equal(240.0, closed.Objectives[0].MinTemperatureK);
        Assert.Equal(ObjectiveKind.Recycle, closed.Objectives[1].Kind);
        Assert.True(closed.Objectives[1].Amount > 0);
        Assert.Equal("extremophile_e01", cold.Objectives[0].Organism);
        Assert.Equal(205.0, cold.Objectives[1].MaxTemperatureK);
        Assert.Equal(ObjectiveKind.Accumulate, oxygen.Objectives[1].Kind);
        Assert.Equal("Oxygen", oxygen.Objectives[1].Resource);
    }

    [Fact]
    public void AnAccumulateObjectiveNeedsAPoolResource()
    {
        const string json = """
            {"metadata":{"schema":"martis.mission","id":"x","name":"X","objectives":[{"id":"a","type":"accumulate","resource":"Gold","amount":"p","title":"T"}]},
             "location":{"site":"model"},
             "parameters":[{"id":"p","field":"amount","value":1.0,"unit":"kg","classification":"GAME_BALANCE","scope":"model","version":"1","status":"PROPOSED","used_in_v0":true,"source":null,"note":"n","rationale":"r","range":[0,10]}]}
            """;

        Assert.Throws<InvalidDataException>(() => MissionDefinition.Load(json));
    }

    [Fact]
    public void OccupyCountsOnlyCellsInsideTheTemperatureBand()
    {
        var session = Start("closed_cycle.v1.json");
        Assert.True(session.Introduce("phototroph_a01", 64, 20, 0.3, null, out var error), error);
        session.Advance(1);
        Assert.Null(session.Mission!.Status().Single(status => status.Id == "warm_colony").CompletedTick);

        Assert.True(session.Introduce("phototroph_a01", 64, 64, 0.3, null, out error), error);
        session.Advance(1);
        Assert.NotNull(session.Mission.Status().Single(status => status.Id == "warm_colony").CompletedTick);
    }

    [Fact]
    public void GradientsIsCompletableInTheTemperateBands()
    {
        var session = Start("gradients.v1.json");
        session.RecordAction("select-overlay");
        foreach (var (x, y) in new[] { (40, 20), (64, 20), (88, 20) })
        {
            Deploy(session, "phototroph_a01", x, y, 0.4, null);
            Deploy(session, "decomposer_d01", x, y, 0.05, null);
        }

        session.Advance(400);

        AssertComplete(session);
    }

    [Fact]
    public void ClosedCycleIsCompletableWithEnoughDecomposers()
    {
        var session = Start("closed_cycle.v1.json");
        Deploy(session, "phototroph_a01", 64, 64, 0.5, null);
        Deploy(session, "decomposer_d01", 64, 64, 0.3, null);

        session.Advance(400);

        AssertComplete(session);
    }

    [Fact]
    public void ColdFrontierIsCompletableWithASlowExtremophile()
    {
        var session = Start("cold_frontier.v1.json");
        Deploy(session, "extremophile_e01", 64, 8, 0.5, new Dictionary<string, double> { ["PHOTOSYNTHESIS"] = 0.3 });
        Deploy(session, "decomposer_d01", 64, 8, 0.3, null);

        session.Advance(400);

        AssertComplete(session);
    }

    [Fact]
    public void OxygenIsCompletableWithANetworkOfColonies()
    {
        var session = Start("oxygen.v1.json");
        foreach (var (x, y) in new[] { (64, 64), (40, 64), (88, 64), (64, 40), (64, 88), (20, 64) })
        {
            Deploy(session, "phototroph_a01", x, y, 0.5, null);
            Deploy(session, "decomposer_d01", x, y, 0.3, null);
        }

        session.Advance(400);

        AssertComplete(session);
    }

    [Fact]
    public void RecycledBiomassAndTheOxygenBaselineSurviveASave()
    {
        var first = Start("oxygen.v1.json");
        Deploy(first, "phototroph_a01", 64, 64, 0.5, null);
        Deploy(first, "decomposer_d01", 64, 64, 0.3, null);
        first.Advance(30);
        var progress = MissionProgress.FromJson(first.CaptureMission()!.ToJson());

        var resumed = GameSession.Resume(Repo.Root(), first.Capture(), "oxygen.v1.json", progress, Campaign.Chapters[1].World);
        resumed.Advance(1);
        first.Advance(1);

        Assert.True(progress.Recycled > 0);
        Assert.NotNull(progress.Baselines);
        Assert.Equal(
            first.Mission!.Status().Single(status => status.Id == "oxygen").Progress,
            resumed.Mission!.Status().Single(status => status.Id == "oxygen").Progress,
            9);
    }

    [Theory]
    [InlineData("""{"MissionId":"x","Streak":0,"Mutations":0,"Recycled":-1}""")]
    [InlineData("""{"MissionId":"x","Streak":0,"Mutations":0,"Baselines":{"Oxygen":-2}}""")]
    public void BrokenRecycledOrBaselinesAreRefused(string json)
        => Assert.Throws<InvalidDataException>(() => MissionProgress.FromJson(json));

    /// <summary>A chapter-2 mission with the research a player has after chapter 1.</summary>
    private static GameSession Start(string file)
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, file, Campaign.Chapters[1].World);
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        research.Earn(100);
        session.AttachEconomy(research);
        foreach (var technology in new[] { "gene_editing_1", "bioreactor_1", "extremophiles" })
        {
            Assert.True(session.ResearchTechnology(technology, out var error), error);
        }

        return session;
    }

    /// <summary>Deploys, waiting for the bioreactor when the inoculum is short.</summary>
    private static void Deploy(GameSession session, string organism, int x, int y, double kg, Dictionary<string, double>? genes)
    {
        for (var wait = 0; wait < 200; wait++)
        {
            if (session.Introduce(organism, x, y, kg, genes, out _))
            {
                return;
            }

            session.Advance(1);
        }

        Assert.Fail($"Could not deploy {organism} at {x},{y}.");
    }

    private static void AssertComplete(GameSession session)
    {
        Assert.Null(session.Failure);
        Assert.True(session.Mission!.IsComplete, string.Join(", ", session.Mission.Status().Select(status => $"{status.Id}={status.Progress:0.##}/{status.Target}")));
    }
}
