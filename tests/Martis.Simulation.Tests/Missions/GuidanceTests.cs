using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H8: help that fades chapter by chapter, and the codex of concepts.</summary>
public sealed class GuidanceTests
{
    private static readonly CampaignDefinition Campaign = CampaignDefinition.LoadPublished(Repo.Root());

    [Fact]
    public void TheHelpFadesChapterByChapter()
    {
        Assert.Equal(
            [GuidanceLevel.Tutorial, GuidanceLevel.Hints, GuidanceLevel.OnRequest, GuidanceLevel.Minimal, GuidanceLevel.None],
            Campaign.Chapters.Select(chapter => chapter.Guidance));
        Assert.True(Campaign.Guidance.OnRequestStallSols < Campaign.Guidance.MinimalStallSols);
    }

    [Theory]
    [InlineData(GuidanceLevel.Tutorial, true)]
    [InlineData(GuidanceLevel.Hints, true)]
    [InlineData(GuidanceLevel.OnRequest, false)]
    [InlineData(GuidanceLevel.Minimal, false)]
    [InlineData(GuidanceLevel.None, false)]
    public void AtTheStartOfAMissionOnlyTheFirstTwoLevelsShowTheHint(GuidanceLevel level, bool shown)
    {
        var (session, guidance) = Start(level);
        guidance.Observe(session.Mission!, session.Tick);

        Assert.Equal(shown, guidance.ShowsHint);
    }

    [Fact]
    public void OnRequestShowsTheHintWhenAskedUntilTheNextObjective()
    {
        var (session, guidance) = Start(GuidanceLevel.OnRequest);
        guidance.Observe(session.Mission!, session.Tick);

        Assert.True(guidance.CanRequest);
        Assert.True(guidance.Request());
        Assert.True(guidance.ShowsHint);
        Assert.Equal(1, guidance.Requests);

        session.RecordAction("orbit-camera");
        guidance.Observe(session.Mission!, session.Tick);
        Assert.False(guidance.ShowsHint);
    }

    [Fact]
    public void AStallBringsTheHintBackAndProgressHidesItAgain()
    {
        var policy = Campaign.Guidance;
        var (session, guidance) = Start(GuidanceLevel.OnRequest);
        for (var sol = 0; sol < policy.OnRequestStallSols; sol++)
        {
            session.Advance(1);
            guidance.Observe(session.Mission!, session.Tick);
        }

        Assert.True(guidance.ShowsHint, $"stalled {guidance.StalledSols} sols");

        session.RecordAction("orbit-camera");
        guidance.Observe(session.Mission!, session.Tick);
        Assert.False(guidance.ShowsHint);
        Assert.Equal(0, guidance.StalledSols);
    }

    [Fact]
    public void MinimalWaitsLongerAndOffersNoButton()
    {
        var policy = Campaign.Guidance;
        var (session, guidance) = Start(GuidanceLevel.Minimal);
        Assert.False(guidance.CanRequest);
        Assert.False(guidance.Request());

        session.Advance((int)policy.OnRequestStallSols);
        guidance.Observe(session.Mission!, session.Tick);
        Assert.False(guidance.ShowsHint);

        session.Advance((int)(policy.MinimalStallSols - policy.OnRequestStallSols));
        guidance.Observe(session.Mission!, session.Tick);
        Assert.True(guidance.ShowsHint);
    }

    [Fact]
    public void WithoutGuidanceNoHintEverAppears()
    {
        var (session, guidance) = Start(GuidanceLevel.None);
        session.Advance(200);
        guidance.Observe(session.Mission!, session.Tick);

        Assert.False(guidance.Request());
        Assert.False(guidance.ShowsHint);
    }

    [Fact]
    public void AProgressBarThatMovesCountsAsProgress()
    {
        // Selection: the sustain bar of a living colony moves every sol, so the hint stays hidden.
        var session = GameSession.NewGame(Repo.Root(), 5UL, "first_colony.v1.json");
        var guidance = new MissionGuidance(GuidanceLevel.Minimal, new GuidancePolicy(5, 5));
        session.RecordAction("analyze-region");
        session.Introduce("phototroph_a01", 32, 32, 0.5, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.85 }, out _);
        session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
        session.RecordAction("run-experiment");
        for (var sol = 0; sol < 20; sol++)
        {
            session.Advance(1);
            guidance.Observe(session.Mission!, session.Tick);
        }

        Assert.True(guidance.StalledSols < 5, $"stalled {guidance.StalledSols}");
    }

    [Fact]
    public void EveryMissionTeachesConceptsAndEndsWithADebrief()
    {
        Assert.All(Campaign.Missions, mission =>
        {
            Assert.NotEmpty(mission.Concepts);
            var definition = MissionDefinition.LoadPublished(Repo.Root(), mission.File);
            Assert.False(string.IsNullOrWhiteSpace(definition.Debrief), mission.Id);
        });
        Assert.Equal(Campaign.Concepts.Count, Campaign.Missions.Sum(mission => mission.Concepts.Count));
    }

    [Fact]
    public void TheChapterOneTutorialTeachesTheRover()
    {
        var awakening = MissionDefinition.LoadPublished(Repo.Root(), "awakening.v1.json");

        Assert.Contains("rover", Campaign.Mission("awakening").Concepts);
        Assert.Equal(["rover-view", "drive-rover", "orbital-view", "laboratory-view", "planetary-view"], awakening.Objectives.Where(objective => objective.Action is "rover-view" or "drive-rover" or "orbital-view" or "laboratory-view" or "planetary-view").Select(objective => objective.Action));
    }

    [Fact]
    public void TheSamplingMissionWalksTheWholeSampleCycleInOrder()
    {
        var sampling = MissionDefinition.LoadPublished(Repo.Root(), "sampling.v1.json");

        Assert.True(sampling.Sequential);
        Assert.Equal(["collect-sample", "deliver-sample", "analyze-sample", "design-from-sample"], sampling.Objectives.Select(objective => objective.Action));
        Assert.Equal(["muestreo"], Campaign.Mission("sampling").Concepts);
        Assert.False(string.IsNullOrWhiteSpace(sampling.Debrief));
    }

    [Fact]
    public void TheStationsMissionTeachesTheAutopilotAndTheFixedStations()
    {
        var stations = MissionDefinition.LoadPublished(Repo.Root(), "stations.v1.json");

        Assert.True(stations.Sequential);
        Assert.Equal(["send-rover", "rover-arrived", "build-station", "discover-poi"], stations.Objectives.Select(objective => objective.Action));
        Assert.Equal(MissionKind.Reconnaissance, Campaign.Mission("stations").Kind);
        Assert.Equal(["red_de_medida"], Campaign.Mission("stations").Concepts);
    }

    [Fact]
    public void FirstHeatTeachesToBuildAndKeepAHeater()
    {
        var heat = MissionDefinition.LoadPublished(Repo.Root(), "first_heat.v1.json");

        Assert.Equal(["build-heater", "repair-heater"], heat.Objectives.Select(objective => objective.Action));
        Assert.Equal(MissionKind.Terraforming, Campaign.Mission("first_heat").Kind);
        Assert.Equal(["calentamiento_regional"], Campaign.Mission("first_heat").Concepts);
    }

    [Fact]
    public void TheCodexFillsAsMissionsAreFinished()
    {
        var progress = new CampaignProgress(Campaign);
        Assert.Empty(progress.Learned);

        progress.Complete("awakening");
        progress.Complete("sampling");
        progress.Complete("stations");
        progress.Complete("first_colony");

        Assert.Equal(["leer_marte", "rover", "cartografia", "muestreo", "red_de_medida", "fotosintesis", "diseno_genetico"], progress.Learned.Select(concept => concept.Id));
        var reloaded = CampaignProgress.FromJson(Campaign, progress.ToJson());
        Assert.Equal(progress.Learned, reloaded.Learned);
    }

    [Theory]
    [InlineData("\"guidance\": \"tutorial\"", "\"guidance\": \"magic\"")]
    [InlineData("\"leer_marte\",\n            \"rover\",\n            \"cartografia\"", "\"leer_marte\",\n            \"ghost\",\n            \"cartografia\"")]
    [InlineData("\"seleccion\"\n          ]", "\"fotosintesis\"\n          ]")]
    public void ABrokenGuidanceOrConceptIsRefused(string from, string to)
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "campaign", "genesis.v1.json"));
        Assert.Contains(from, json, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => CampaignDefinition.Load(json.Replace(from, to, StringComparison.Ordinal)));
    }

    [Fact]
    public void TheCampaignFollowsTheErasOfTheMasterDocument()
    {
        Assert.Equal([1, 1, 2, 2, 3], Campaign.Chapters.Select(chapter => chapter.Era));
        Assert.Equal("Génesis", Eras.Name(Campaign.Chapters[0].Era));
        Assert.Equal(MissionKind.Reconnaissance, Campaign.Mission("awakening").Kind);
        Assert.Equal(MissionKind.Sampling, Campaign.Mission("sampling").Kind);
        Assert.Equal(MissionKind.Colonization, Campaign.Mission("first_colony").Kind);
        Assert.Equal(MissionKind.Terraforming, Campaign.Mission("oxygen").Kind);
    }

    [Fact]
    public void SpeculativeConceptsAreLabelled()
    {
        Assert.Equal(Epistemic.Speculative, Campaign.Concept("socialidad").Epistemic);
        Assert.Equal(Epistemic.Speculative, Campaign.Concept("inteligencia").Epistemic);
        Assert.Equal(Epistemic.Science, Campaign.Concept("fotosintesis").Epistemic);
        Assert.All(Campaign.Missions.Where(mission => Campaign.ChapterOf(mission.Id).Era == 1).SelectMany(mission => mission.Concepts),
            concept => Assert.NotEqual(Epistemic.Speculative, Campaign.Concept(concept).Epistemic));
    }

    [Theory]
    [InlineData("\"era\": 2", "\"era\": 0")]
    [InlineData("\"kind\": \"reconnaissance\"", "\"kind\": \"party\"")]
    [InlineData("\"epistemic\": \"speculative\"", "\"epistemic\": \"rumour\"")]
    public void AnUnknownEraKindOrLabelIsRefused(string from, string to)
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "campaign", "genesis.v1.json"));
        Assert.Contains(from, json, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => CampaignDefinition.Load(json.Replace(from, to, StringComparison.Ordinal)));
    }

    private static (GameSession Session, MissionGuidance Guidance) Start(GuidanceLevel level)
        => (GameSession.NewGame(Repo.Root(), 5UL, "awakening.v1.json"), new MissionGuidance(level, Campaign.Guidance));
}
