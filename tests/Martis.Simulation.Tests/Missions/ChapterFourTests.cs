using Martis.Simulation.Biology;
using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H6: chapter 4 «Cadenas tróficas», herbivory and predation.</summary>
public sealed class ChapterFourTests
{
    private static readonly CampaignDefinition Campaign = CampaignDefinition.LoadPublished(Repo.Root());
    private static readonly WorldOptions Chapter4 = Campaign.Chapters[3].World;

    /// <summary>Temperate band on a 256 map (about 221 K), where the base phototroph lives.</summary>
    private static readonly (int X, int Y)[] Sites = [(128, 40), (110, 40), (146, 40), (128, 50)];

    [Fact]
    public void ChaptersFourAndFiveHaveFeedingLinks()
    {
        Assert.Null(Campaign.Chapters[2].World.Trophic);
        Assert.All(Campaign.Chapters.Skip(3), chapter => Assert.Equal("trophic.v1.json", chapter.World.Trophic));
        Assert.Equal(256, Chapter4.GridSize);
        Assert.Equal(["grazers", "food_web", "predators"], Campaign.Chapters[3].Missions.Select(mission => mission.Id));
    }

    [Fact]
    public void TheLinksFormAChainFromProducersToPredators()
    {
        var biology = BiologyCatalog.LoadPublished(Repo.Root());
        var trophic = TrophicCatalog.LoadPublished(Repo.Root(), "trophic.v1.json", biology);

        var grazing = trophic.Rules.Single(rule => rule.Id == "grazing");
        var predation = trophic.Rules.Single(rule => rule.Id == "predation");
        Assert.Equal("grazer_h01", grazing.Consumer);
        Assert.Contains("phototroph_a01", grazing.Prey);
        Assert.Equal(["grazer_h01"], predation.Prey);
        Assert.All(trophic.Rules, rule => Assert.InRange(rule.Efficiency, 0.0, 1.0));
    }

    [Fact]
    public void AConsumerThatEatsItselfIsRefused()
    {
        var biology = BiologyCatalog.LoadPublished(Repo.Root());
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "organisms", "trophic.v1.json"))
            .Replace("\"prey\": [\n          \"grazer_h01\"\n        ]", "\"prey\": [\"predator_p01\"]", StringComparison.Ordinal);
        Assert.Contains("\"prey\": [\"predator_p01\"]", json, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => TrophicCatalog.Load(json, biology));
    }

    [Fact]
    public void GrazersEatProducersInTheirCellAndKeepTheCellMass()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter4 with { GridSize = 128 });
        session.Introduce("phototroph_a01", 64, 20, 0.5, null, out _);
        session.Introduce("decomposer_d01", 64, 20, 0.05, null, out _);
        session.Advance(30);
        session.Introduce("grazer_h01", 64, 20, 0.1, null, out _);

        var eaten = 0.0;
        for (var sol = 0; sol < 40; sol++)
        {
            session.Advance(1);
            eaten += session.Workspace.Totals.BiomassEatenKg;
        }

        // Conservation runs every tick and would have stopped the session on a mass error.
        Assert.Null(session.Failure);
        Assert.True(eaten > 0.0);
        Assert.True(session.Engine.State.Populations.Where(p => p.OrganismId.Value == "grazer_h01").Sum(p => p.BiomassKg) > 0.1);
    }

    [Fact]
    public void WithoutFeedingLinksGrazersOnlyScavenge()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter4 with { GridSize = 128, Trophic = null });
        session.Introduce("phototroph_a01", 64, 20, 0.5, null, out _);
        session.Introduce("grazer_h01", 64, 20, 0.1, null, out _);

        session.Advance(20);

        Assert.Equal(0.0, session.Workspace.Totals.BiomassEatenKg);
    }

    [Fact]
    public void GrazersIsCompletable()
    {
        var session = Start("grazers.v1.json");
        Colonies(session, 3);
        Deploy(session, "grazer_h01", Sites[0].X, Sites[0].Y, 0.1);

        session.Advance(300);

        AssertComplete(session);
    }

    [Fact]
    public void FoodWebIsCompletable()
    {
        var session = Start("food_web.v1.json");
        Colonies(session, 4);
        foreach (var (x, y) in Sites.Take(3))
        {
            Deploy(session, "grazer_h01", x, y, 0.05);
        }

        session.Advance(300);

        AssertComplete(session);
    }

    [Fact]
    public void PredatorsIsCompletable()
    {
        var session = Start("predators.v1.json");
        Colonies(session, 3);
        foreach (var (x, y) in Sites.Take(3))
        {
            Deploy(session, "grazer_h01", x, y, 0.1);
        }

        session.Advance(60);
        Deploy(session, "predator_p01", Sites[0].X, Sites[0].Y, 0.02);
        session.Advance(300);

        AssertComplete(session);
    }

    [Fact]
    public void BiomassEatenIsZeroOnAFreshTick()
    {
        var totals = new TickTotals { BiomassEatenKg = 3 };
        totals.Reset();
        Assert.Equal(0.0, totals.BiomassEatenKg);
    }

    private static void Colonies(GameSession session, int count)
    {
        foreach (var (x, y) in Sites.Take(count))
        {
            Deploy(session, "phototroph_a01", x, y, 0.5);
            Deploy(session, "decomposer_d01", x, y, 0.05);
        }
    }

    /// <summary>A chapter-4 mission with the research a player has after chapter 3.</summary>
    private static GameSession Start(string file)
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, file, Chapter4);
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        research.Earn(400);
        session.AttachEconomy(research);
        foreach (var technology in new[] { "gene_editing_1", "bioreactor_1", "extremophiles", "eukaryote_culture", "grazer_culture", "predator_culture" })
        {
            Assert.True(session.ResearchTechnology(technology, out var error), error);
        }

        return session;
    }

    private static void Deploy(GameSession session, string organism, int x, int y, double kg)
    {
        for (var wait = 0; wait < 200; wait++)
        {
            if (session.Introduce(organism, x, y, kg, null, out _))
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
        Assert.True(session.Mission!.IsComplete, string.Join(", ", session.Mission.Status().Select(status => $"{status.Id}={status.Progress:0.###}/{status.Target}")));
    }
}
