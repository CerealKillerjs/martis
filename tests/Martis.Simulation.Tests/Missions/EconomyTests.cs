using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H2: research points, the technology tree and the lander's inoculum.</summary>
public sealed class EconomyTests
{
    private static EconomyDefinition Economy() => EconomyDefinition.LoadPublished(Repo.Root());

    [Fact]
    public void TheCampaignStartsWithASmallLabAndTheTwoBasicMicrobes()
    {
        var research = new ResearchState(Economy());

        Assert.Equal(0, research.Points);
        Assert.Equal(0.1, research.GeneRange, 12);
        Assert.Equal(40, research.ForecastSols);
        Assert.Equal(1.0, research.InoculumCapacityKg, 12);
        Assert.True(research.IsOrganismAvailable("phototroph_a01"));
        Assert.True(research.IsOrganismAvailable("decomposer_d01"));
        Assert.False(research.IsOrganismAvailable("extremophile_e01"));
    }

    [Fact]
    public void EveryTechnologyEffectRefersToRealOrganismsAndTheTreeIsOrdered()
    {
        var economy = Economy();
        var organisms = Martis.Simulation.Biology.BiologyCatalog.LoadPublished(Repo.Root()).Organisms.Select(organism => organism.Id).ToHashSet(StringComparer.Ordinal);

        Assert.All(economy.Start.Organisms, id => Assert.Contains(id, organisms));
        Assert.All(
            economy.Technologies.SelectMany(technology => technology.Effects).Where(effect => effect.Kind == TechEffectKind.Organism),
            effect => Assert.Contains(effect.OrganismId!, organisms));
        Assert.All(economy.Technologies, technology => Assert.True(technology.Cost > 0, technology.Id));
    }

    [Fact]
    public void ResearchCostsPointsNeedsItsRequirementsAndRaisesTheLimits()
    {
        var research = new ResearchState(Economy());

        Assert.False(research.TryResearch("gene_editing_1", out var poor));
        Assert.Contains("PI", poor, StringComparison.Ordinal);

        research.Earn(100);
        Assert.False(research.TryResearch("gene_editing_2", out var missing));
        Assert.Contains("Edición génica I", missing, StringComparison.Ordinal);

        Assert.True(research.TryResearch("gene_editing_1", out _));
        Assert.True(research.TryResearch("extremophiles", out _));
        Assert.Equal(55, research.Points, 9);
        Assert.Equal(0.2, research.GeneRange, 12);
        Assert.True(research.IsOrganismAvailable("extremophile_e01"));
        Assert.False(research.TryResearch("gene_editing_1", out _));
    }

    [Fact]
    public void ResearchRoundTripsInsideTheCampaignProgress()
    {
        var economy = Economy();
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());
        var progress = new CampaignProgress(campaign) { Research = new ResearchState(economy) };
        progress.Research.Earn(30);
        progress.Research.TryResearch("long_forecast", out _);
        progress.Complete("awakening");

        var loaded = CampaignProgress.FromJson(campaign, progress.ToJson(), economy);
        var old = CampaignProgress.FromJson(campaign, """{"CampaignId":"genesis","Completed":["awakening"]}""", economy);

        Assert.Equal(20, loaded.Research!.Points, 9);
        Assert.True(loaded.Research.IsUnlocked("long_forecast"));
        Assert.Equal(200, loaded.Research.ForecastSols);
        Assert.Equal(0, old.Research!.Points);
    }

    [Theory]
    [InlineData("""{"EconomyId":"other","Points":1}""")]
    [InlineData("""{"EconomyId":"genesis_economy","Points":-1}""")]
    [InlineData("nope")]
    public void BrokenResearchIsRefused(string json)
        => Assert.Throws<InvalidDataException>(() => ResearchState.FromJson(Economy(), json));

    [Fact]
    public void DeploymentsSpendInoculumThatTheBioreactorRecharges()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "first_colony.v1.json");
        session.AttachEconomy(new ResearchState(Economy()));

        Assert.True(session.Introduce("phototroph_a01", 32, 32, 0.5, null, out _));
        Assert.True(session.Introduce("decomposer_d01", 32, 32, 0.4, null, out _));
        Assert.Equal(0.1, session.InoculumKg, 9);
        Assert.False(session.Introduce("phototroph_a01", 30, 30, 0.2, null, out var error));
        Assert.Contains("Inóculo insuficiente", error, StringComparison.Ordinal);

        session.Advance(10);
        Assert.Equal(0.3, session.InoculumKg, 9);
        session.Advance(100);
        Assert.Equal(1.0, session.InoculumKg, 9);
    }

    [Fact]
    public void TheLabRefusesDesignsAndOrganismsBeyondTheResearch()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "first_colony.v1.json");
        var research = new ResearchState(Economy());
        session.AttachEconomy(research);

        Assert.True(session.Introduce("phototroph_a01", 32, 32, 0.1, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.9 }, out _));
        Assert.False(session.Introduce("phototroph_a01", 32, 32, 0.1, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.5 }, out var range));
        Assert.Contains("edición génica", range, StringComparison.Ordinal);
        Assert.False(session.Introduce("extremophile_e01", 32, 32, 0.1, null, out var locked));
        Assert.Contains("no está investigado", locked, StringComparison.Ordinal);
        Assert.False(session.Experiment("phototroph_a01", 32, 32, 0.1, null, 100).Survived);
        Assert.Contains("40 soles", session.Experiment("phototroph_a01", 32, 32, 0.1, null, 100).Failure, StringComparison.Ordinal);
    }

    [Fact]
    public void PlayingEarnsPointsForObjectivesMissionsAndAFewMutations()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "awakening.v1.json");
        var research = new ResearchState(Economy());
        session.AttachEconomy(research);
        foreach (var action in new[] { "orbit-camera", "zoom-camera", "select-overlay", "inspect-cell", "rover-view", "drive-rover", "scan-site", "orbital-view", "laboratory-view", "planetary-view", "analyze-region" })
        {
            session.RecordAction(action);
        }

        session.Advance(5);

        Assert.True(session.Mission!.IsComplete);
        Assert.Equal((12 * 5) + 15, research.Points, 9);
        Assert.Contains(session.Events, item => item.Kind == "research");

        var colony = GameSession.NewGame(Repo.Root(), 5UL, "first_colony.v1.json");
        var mutations = new ResearchState(Economy());
        colony.AttachEconomy(mutations);
        colony.Introduce("phototroph_a01", 32, 32, 0.5, null, out _);
        colony.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
        colony.Advance(1000);
        var events = colony.Events.Count(item => item.Kind == "mutation");
        Assert.True(events > 20, $"only {events} mutations");
        Assert.Equal(20, colony.CaptureEconomy()!.MutationPoints, 9);
    }

    [Fact]
    public void ResearchingCountsAsTheMissionAction()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "selection.v1.json");
        var research = new ResearchState(Economy());
        session.AttachEconomy(research);
        Assert.False(session.ResearchTechnology("long_forecast", out _));
        research.Earn(10);

        Assert.True(session.ResearchTechnology("long_forecast", out _));

        Assert.Equal("first_research", session.Mission!.Status().First(status => status.CompletedTick is not null).Id);
        Assert.Equal(5, research.Points, 9);
    }

    [Fact]
    public void FreePlayHasNoLimits()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, withMission: false);

        Assert.True(session.Introduce("extremophile_e01", 32, 32, 5.0, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.1 }, out _));
        Assert.False(session.ResearchTechnology("long_forecast", out _));
        Assert.Null(session.CaptureEconomy());
    }

    [Fact]
    public void TheSavedEconomyTravelsWithTheMission()
    {
        var record = new MissionSaveRecord("first_colony.v1.json", GameSession.NewGame(Repo.Root(), 5UL, "first_colony.v1.json").CaptureMission()!, new SessionEconomyState(0.25, 3));

        var copy = MissionSaveRecord.FromJson(record.ToJson());

        Assert.Equal(new SessionEconomyState(0.25, 3), copy.Economy);
        Assert.Null(MissionSaveRecord.FromJson(new MissionSaveRecord("a.json", record.Progress).ToJson()).Economy);
    }
}
