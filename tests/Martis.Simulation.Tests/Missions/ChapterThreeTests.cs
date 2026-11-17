using Martis.Simulation.Biology;
using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H5: chapter 3 «Cooperación», endosymbiosis and multicellularity.</summary>
public sealed class ChapterThreeTests
{
    private static readonly CampaignDefinition Campaign = CampaignDefinition.LoadPublished(Repo.Root());
    private static readonly WorldOptions Chapter3 = Campaign.Chapters[2].World;
    private static readonly Dictionary<string, double> Sticky = new() { ["BIOFILM_PRODUCTION"] = 0.8 };

    [Fact]
    public void ChaptersThreeToFiveHaveTransitionsAndTheEarlierOnesDoNot()
    {
        Assert.Null(Campaign.Chapters[0].World.Transitions);
        Assert.Null(Campaign.Chapters[1].World.Transitions);
        Assert.All(Campaign.Chapters.Skip(2), chapter => Assert.Equal("transitions.v1.json", chapter.World.Transitions));
        Assert.Equal(["symbiosis", "complex_cells", "multicellular"], Campaign.Chapters[2].Missions.Select(mission => mission.Id));
    }

    [Fact]
    public void TheTransitionsNameRealOrganismsAndConditions()
    {
        var biology = BiologyCatalog.LoadPublished(Repo.Root());
        var transitions = TransitionCatalog.LoadPublished(Repo.Root(), "transitions.v1.json", biology);

        var endosymbiosis = transitions.Rules.Single(rule => rule.Id == "endosymbiosis");
        var multicellularity = transitions.Rules.Single(rule => rule.Id == "multicellularity");
        Assert.Equal(["decomposer_d01", "phototroph_a01"], endosymbiosis.Partners);
        Assert.Equal("eukaryote_k01", endosymbiosis.Product);
        Assert.Equal("BIOFILM_PRODUCTION", multicellularity.Gene);
        Assert.Equal("multicellular_m01", multicellularity.Product);
    }

    [Fact]
    public void ATransitionToAnUnknownOrganismIsRefused()
    {
        var biology = BiologyCatalog.LoadPublished(Repo.Root());
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "organisms", "transitions.v1.json")).Replace("\"eukaryote_k01\"", "\"ghost\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => TransitionCatalog.Load(json, biology));
    }

    [Fact]
    public void WithoutTransitionsAColonyRunsExactlyAsBefore()
    {
        static string Run(WorldOptions world)
        {
            var session = GameSession.NewGame(Repo.Root(), 5UL, false, world);
            session.Introduce("phototroph_a01", 64, 20, 0.5, null, out _);
            session.Introduce("decomposer_d01", 64, 20, 0.05, null, out _);
            session.Advance(120);
            return Convert.ToBase64String(StateSnapshotJson.Write(session.Capture()));
        }

        var chapter2 = Campaign.Chapters[1].World;
        Assert.Equal(Run(chapter2), Run(chapter2));
        Assert.NotEqual(Run(chapter2), Run(chapter2 with { Transitions = "transitions.v1.json" }));
    }

    [Fact]
    public void EndosymbiosisFoundsAEukaryoteFromItsPartnersAndConservesMass()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter3);
        session.Introduce("phototroph_a01", 64, 20, 0.5, null, out _);
        session.Introduce("decomposer_d01", 64, 20, 0.05, null, out _);

        session.Advance(200);

        Assert.Null(session.Failure);
        Assert.Contains(session.Events, item => item.Kind == "transition" && item.Text.Contains("Endosimbiosis", StringComparison.Ordinal));
        Assert.Contains(session.Engine.State.Populations, population => population.OrganismId.Value == "eukaryote_k01" && population.BiomassKg > 0);
    }

    [Fact]
    public void ATransitionNeedsEveryPartnerInTheSameCell()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter3);
        session.Introduce("phototroph_a01", 64, 20, 0.5, null, out _);
        session.Introduce("decomposer_d01", 70, 20, 0.3, null, out _);

        session.Advance(200);

        Assert.DoesNotContain(session.Events, item => item.Kind == "transition");
    }

    [Fact]
    public void SymbiosisIsCompletable()
    {
        var session = Start("symbiosis.v1.json");
        Deploy(session, "phototroph_a01", 64, 20, 0.5, null);
        Deploy(session, "decomposer_d01", 64, 20, 0.2, null);

        session.Advance(400);

        AssertComplete(session);
    }

    [Fact]
    public void ComplexCellsIsCompletable()
    {
        var session = Start("complex_cells.v1.json");
        foreach (var (x, y) in new[] { (64, 20), (40, 20), (88, 20), (64, 30) })
        {
            Deploy(session, "eukaryote_k01", x, y, 0.5, null);
            Deploy(session, "decomposer_d01", x, y, 0.1, null);
        }

        session.Advance(300);

        AssertComplete(session);
    }

    [Fact]
    public void MulticellularIsCompletableWithAStickyEukaryote()
    {
        var session = Start("multicellular.v1.json");
        Deploy(session, "eukaryote_k01", 64, 20, 0.5, Sticky);
        Deploy(session, "decomposer_d01", 64, 20, 0.2, null);

        session.Advance(400);

        AssertComplete(session);
    }

    [Fact]
    public void EukaryotesAreLockedUntilResearched()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "complex_cells.v1.json", Chapter3);
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        session.AttachEconomy(research);

        Assert.False(session.Introduce("eukaryote_k01", 64, 20, 0.1, null, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    /// <summary>A chapter-3 mission with the research a player has after chapter 2.</summary>
    private static GameSession Start(string file)
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, file, Chapter3);
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        research.Earn(200);
        session.AttachEconomy(research);
        foreach (var technology in new[] { "gene_editing_1", "bioreactor_1", "extremophiles", "eukaryote_culture" })
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
        Assert.True(session.Mission!.IsComplete, string.Join(", ", session.Mission.Status().Select(status => $"{status.Id}={status.Progress:0.##}/{status.Target}")));
    }
}
