using Martis.Simulation.Biology;
using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H7: chapter 5 «Mente», sociality and the intelligence index.</summary>
public sealed class ChapterFiveTests
{
    private static readonly CampaignDefinition Campaign = CampaignDefinition.LoadPublished(Repo.Root());
    private static readonly WorldOptions Chapter5 = Campaign.Chapters[4].World;
    private static readonly Dictionary<string, double> LongLived = new() { ["REPAIR_CAPACITY"] = 0.9 };

    /// <summary>Temperate band on a 256 map (about 221 K), where the base phototroph lives.</summary>
    private static readonly (int X, int Y)[] Sites = [(128, 40), (110, 40), (146, 40), (128, 50)];

    [Fact]
    public void ChapterFiveClosesTheCampaignWithSocietyAndMind()
    {
        Assert.Equal(5, Campaign.Chapters.Count);
        Assert.Equal(256, Chapter5.GridSize);
        Assert.Equal(["sociality", "mind"], Campaign.Chapters[4].Missions.Select(mission => mission.Id));
        Assert.All(Campaign.Chapters, chapter => Assert.NotEmpty(chapter.Missions));
    }

    [Fact]
    public void SocialityNeedsALongLivedPredator()
    {
        var biology = BiologyCatalog.LoadPublished(Repo.Root());
        var transitions = TransitionCatalog.LoadPublished(Repo.Root(), "transitions.v1.json", biology);
        var trophic = TrophicCatalog.LoadPublished(Repo.Root(), "trophic.v1.json", biology);

        var sociality = transitions.Rules.Single(rule => rule.Id == "sociality");
        Assert.Equal(["predator_p01"], sociality.Partners);
        Assert.Equal("social_s01", sociality.Product);
        Assert.Equal("REPAIR_CAPACITY", sociality.Gene);
        Assert.True(sociality.MinGeneExpression > 0.7, "The base predator must not qualify without editing.");
        var hunting = trophic.Rules.Single(rule => rule.Id == "hunting");
        Assert.Equal("social_s01", hunting.Consumer);
        Assert.Contains("grazer_h01", hunting.Prey);
        Assert.DoesNotContain("predator_p01", hunting.Prey);
    }

    [Fact]
    public void TheSocialColonyIsNotSoldByTheBioreactor()
    {
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        research.Earn(10_000);
        for (var pass = 0; pass < research.Definition.Technologies.Count; pass++)
        {
            foreach (var technology in research.Definition.Technologies.Select(technology => technology.Id))
            {
                research.TryResearch(technology, out _);
            }
        }

        Assert.Equal(research.Definition.Technologies.Count, research.Unlocked.Count);

        Assert.False(research.IsOrganismAvailable("social_s01"));
        Assert.True(research.IsOrganismAvailable("predator_p01"));
    }

    [Fact]
    public void TheIndexCountsEachLivingLevelAndTheWeightOfMinds()
    {
        var biology = BiologyCatalog.LoadPublished(Repo.Root());
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter5);
        Assert.Equal(0.0, IntelligenceIndex.Measure(session.Engine.State, biology, 0.1));

        session.Introduce("phototroph_a01", 128, 40, 0.5, null, out _);
        session.Advance(1);
        Assert.Equal(20.0, IntelligenceIndex.Measure(session.Engine.State, biology, 0.1));
        session.Introduce("grazer_h01", 128, 40, 0.05, null, out _);
        session.Introduce("predator_p01", 128, 40, 0.02, null, out _);
        session.Advance(1);
        Assert.Equal(60.0, IntelligenceIndex.Measure(session.Engine.State, biology, 0.1));
        Assert.Throws<ArgumentOutOfRangeException>(() => IntelligenceIndex.Measure(session.Engine.State, biology, 0.0));
    }

    [Fact]
    public void AnIntelligenceTargetAboveTheMaximumIsRefused()
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "missions", "mind.v1.json"))
            .Replace("\"value\": 100.0", "\"value\": 120.0", StringComparison.Ordinal);
        Assert.Contains("\"value\": 120.0", json, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => MissionDefinition.Load(json));
    }

    [Fact]
    public void TheSessionShowsTheIndexOnlyWhenTheMissionMeasuresIt()
    {
        Assert.Null(GameSession.NewGame(Repo.Root(), 5UL, "predators.v1.json", Chapter5).Intelligence);
        Assert.Equal(0.0, GameSession.NewGame(Repo.Root(), 5UL, "mind.v1.json", Chapter5).Intelligence);
    }

    [Fact]
    public void BasePredatorsNeverBecomeSocial()
    {
        var session = Start("sociality.v1.json");
        Chain(session, null);

        session.Advance(300);

        Assert.DoesNotContain(session.Engine.State.Populations, population => population.OrganismId.Value == "social_s01");
    }

    [Fact]
    public void SocialityIsCompletable()
    {
        var session = Start("sociality.v1.json");
        Chain(session, LongLived);

        session.Advance(400);

        AssertComplete(session);
        Assert.Contains(session.Engine.State.Populations, population => population.OrganismId.Value == "social_s01" && population.BiomassKg > 0);
    }

    [Fact]
    public void MindIsCompletable()
    {
        var session = Start("mind.v1.json");
        Chain(session, LongLived);

        session.Advance(600);

        AssertComplete(session);
        Assert.Equal(100.0, session.Intelligence);
    }

    /// <summary>Producers and decomposers, then grazers, then predators: the chain chapter 4 taught.</summary>
    private static void Chain(GameSession session, Dictionary<string, double>? predatorGenes)
    {
        foreach (var (x, y) in Sites)
        {
            Deploy(session, "phototroph_a01", x, y, 0.5, null);
            Deploy(session, "decomposer_d01", x, y, 0.05, null);
        }

        foreach (var (x, y) in Sites.Take(3))
        {
            Deploy(session, "grazer_h01", x, y, 0.1, null);
        }

        session.Advance(60);
        Deploy(session, "predator_p01", Sites[0].X, Sites[0].Y, 0.05, predatorGenes);
    }

    /// <summary>A chapter-5 mission with the research a player has after chapter 4.</summary>
    private static GameSession Start(string file)
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, file, Chapter5);
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        research.Earn(400);
        session.AttachEconomy(research);
        foreach (var technology in new[] { "gene_editing_1", "bioreactor_1", "extremophiles", "eukaryote_culture", "grazer_culture", "predator_culture" })
        {
            Assert.True(session.ResearchTechnology(technology, out var error), error);
        }

        return session;
    }

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
        Assert.True(session.Mission!.IsComplete, string.Join(", ", session.Mission.Status().Select(status => $"{status.Id}={status.Progress:0.###}/{status.Target}")));
    }
}
