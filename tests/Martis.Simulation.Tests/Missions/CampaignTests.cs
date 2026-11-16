using Martis.Simulation.Missions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H1: the «Génesis» campaign, its order and the saved progress.</summary>
public sealed class CampaignTests
{
    [Fact]
    public void GenesisHasFiveChaptersOnMapsThatGrowFrom64To256()
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());

        Assert.Equal("genesis", campaign.Id);
        Assert.Equal(["Primer aliento", "Ciclo cerrado", "Cooperación", "Cadenas tróficas", "Mente"], campaign.Chapters.Select(chapter => chapter.Title));
        Assert.Equal([64, 128, 128, 256, 256], campaign.Chapters.Select(chapter => chapter.GridSize));
        Assert.Equal([1, 2, 3, 4, 5], campaign.Chapters.Select(chapter => chapter.Number));
    }

    [Fact]
    public void ChapterOneTeachesTheBasicsBeforeFirstColony()
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());

        Assert.True(campaign.Chapters[0].Playable);
        Assert.Equal(["awakening", "first_colony", "selection"], campaign.Chapters[0].Missions.Select(mission => mission.Id));
        Assert.Equal("ch1", campaign.ChapterOf("selection").Id);
    }

    [Fact]
    public void EveryCampaignMissionFileLoadsWithItsOwnIdAndHintsForEveryObjective()
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());

        foreach (var entry in campaign.Missions)
        {
            var mission = MissionDefinition.LoadPublished(Repo.Root(), entry.File);
            Assert.Equal(entry.Id, mission.Id);
            Assert.False(string.IsNullOrWhiteSpace(mission.Briefing), entry.Id);
            Assert.All(mission.Objectives, objective => Assert.False(string.IsNullOrWhiteSpace(objective.Hint), $"{entry.Id}/{objective.Id}"));
        }
    }

    [Fact]
    public void MissionsUnlockInOrder()
    {
        var progress = new CampaignProgress(CampaignDefinition.LoadPublished(Repo.Root()));

        Assert.True(progress.IsUnlocked("awakening"));
        Assert.False(progress.IsUnlocked("first_colony"));
        Assert.False(progress.Complete("selection"));
        Assert.Equal("awakening", progress.Next!.Id);

        Assert.True(progress.Complete("awakening"));
        Assert.True(progress.IsUnlocked("first_colony"));
        Assert.False(progress.IsUnlocked("selection"));
        Assert.Equal("first_colony", progress.Next!.Id);
        Assert.False(progress.IsUnlocked("unknown"));
    }

    [Fact]
    public void ProgressRoundTripsAndDropsMissionsTheCampaignNoLongerHas()
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());
        var progress = new CampaignProgress(campaign);
        progress.Complete("awakening");
        progress.Complete("first_colony");

        var json = progress.ToJson();
        var loaded = CampaignProgress.FromJson(campaign, json);
        var old = CampaignProgress.FromJson(campaign, """{"CampaignId":"genesis","Completed":["awakening","removed_mission"]}""");

        Assert.Equal(["awakening", "first_colony"], loaded.Completed.Order(StringComparer.Ordinal));
        Assert.Equal("selection", loaded.Next!.Id);
        Assert.Equal(["awakening"], old.Completed);
    }

    [Theory]
    [InlineData("""{"CampaignId":"other","Completed":[]}""")]
    [InlineData("not json")]
    public void ProgressOfAnotherCampaignOrGarbageIsRefused(string json)
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());

        Assert.Throws<InvalidDataException>(() => CampaignProgress.FromJson(campaign, json));
    }

    [Fact]
    public void AChapterOnAnUnsupportedMapSizeIsRefused()
    {
        const string json = """
            {"metadata":{"schema":"martis.campaign","id":"x","name":"X"},"location":{"site":"model"},"parameters":[],
             "chapters":[{"id":"c","title":"T","summary":"S","grid":100,"missions":[]}]}
            """;

        Assert.Throws<InvalidDataException>(() => CampaignDefinition.Load(json));
    }

    [Fact]
    public void AMissionListedTwiceIsRefused()
    {
        const string json = """
            {"metadata":{"schema":"martis.campaign","id":"x","name":"X"},"location":{"site":"model"},"parameters":[],
             "chapters":[{"id":"c","title":"T","summary":"S","grid":64,"missions":[{"id":"a","file":"a.json"},{"id":"a","file":"b.json"}]}]}
            """;

        Assert.Throws<InvalidDataException>(() => CampaignDefinition.Load(json));
    }
}
