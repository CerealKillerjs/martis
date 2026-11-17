using Martis.Persistence.SQLite;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;

namespace Martis.Integration.Tests;

/// <summary>
/// H-31 (story mode H1): a campaign mission saved to SQLite with its progress as a
/// <c>mission-progress</c> event resumes where it was, the way the game's SaveService does it.
/// </summary>
[Trait("Category", "Integration")]
public sealed class CampaignSaveTests
{
    private static readonly Dictionary<string, double> Design = new() { ["UV_PROTECTION"] = 0.85 };

    [Fact]
    public void AMissionSavedHalfwayFinishesOnTheSameSolAsAStraightRun()
    {
        var straight = Start();
        straight.Advance(400);
        var expected = straight.Mission!.CompletedTick;
        Assert.NotNull(expected);

        var path = Path.Combine(Path.GetTempPath(), $"martis-campaign-{Guid.NewGuid():N}.db");
        try
        {
            var first = Start();
            first.Advance(150);
            using (var store = SaveGameStore.Open(path))
            {
                store.CreateSaveGame("save-1", "FIRST COLONY", "test", 1, first.Seed, "mars_baseline", "test");
                var snapshot = first.Capture();
                store.SaveSnapshot("save-1", first.Tick, snapshot.SchemaVersion, StateSnapshotBinary.Write(snapshot));
                store.RecordEvent("save-1", first.Tick, MissionSaveRecord.EventType, new MissionSaveRecord("first_colony.v1.json", first.CaptureMission()!).ToJson());
            }

            GameSession resumed;
            using (var store = SaveGameStore.Open(path))
            {
                var stored = store.LoadLatestValid("save-1").Snapshot!;
                var record = MissionSaveRecord.FromJson(store.Events("save-1")
                    .Last(item => item.EventType == MissionSaveRecord.EventType && item.Tick == stored.Tick).Payload);
                resumed = GameSession.Resume(RepoLayout.Root, StateSnapshotBinary.Read(stored.Payload), record.MissionFile, record.Progress);
            }

            Assert.Equal(150, resumed.Tick);
            resumed.Advance(250);
            Assert.Equal(expected, resumed.Mission!.CompletedTick);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("""{"MissionFile":"../secrets.json","Progress":"{}"}""")]
    [InlineData("""{"MissionFile":"","Progress":"{}"}""")]
    [InlineData("[]")]
    public void ABrokenSavedMissionIsRefused(string json)
        => Assert.Throws<InvalidDataException>(() => MissionSaveRecord.FromJson(json));

    private static GameSession Start()
    {
        var session = GameSession.NewGame(RepoLayout.Root, 5UL, "first_colony.v1.json");
        session.RecordAction("analyze-region");
        session.Experiment("phototroph_a01", 32, 32, 0.5, Design, 5);
        session.Introduce("phototroph_a01", 32, 32, 0.5, Design, out _);
        session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
        return session;
    }
}
