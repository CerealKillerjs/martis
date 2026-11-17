using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H1: guided missions and mission progress that survives a save (H-31).</summary>
public sealed class TutorialMissionTests
{
    private static readonly Dictionary<string, double> Design = new() { ["UV_PROTECTION"] = 0.85 };

    [Fact]
    public void AwakeningIsASequentialTutorial()
    {
        var mission = MissionDefinition.LoadPublished(Repo.Root(), "awakening.v1.json");

        Assert.True(mission.Sequential);
        Assert.Equal(
            ["orbit_camera", "zoom_camera", "select_overlay", "inspect_cell", "rover_view", "drive_rover", "scan_site", "orbital_view", "laboratory_view", "planetary_view", "analyze_region", "run_time"],
            mission.Objectives.Select(objective => objective.Id));
        Assert.Equal(ObjectiveKind.Elapsed, mission.Objectives[^1].Kind);
    }

    [Fact]
    public void InASequentialMissionOnlyTheActiveStepCompletesAndEarlierActionsCountWhenTheirTurnComes()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "awakening.v1.json");
        var tracker = session.Mission!;

        session.RecordAction("inspect-cell");
        Assert.Equal("orbit_camera", tracker.ActiveObjective!.Id);
        Assert.All(tracker.Status(), status => Assert.Null(status.CompletedTick));

        session.RecordAction("orbit-camera");
        session.RecordAction("zoom-camera");
        Assert.Equal("select_overlay", tracker.ActiveObjective!.Id);

        session.RecordAction("select-overlay");
        Assert.Equal("rover_view", tracker.ActiveObjective!.Id);
        var active = tracker.Status().Single(status => status.Active);
        Assert.Equal("rover_view", active.Id);
        Assert.False(string.IsNullOrWhiteSpace(active.Hint));
    }

    [Fact]
    public void TheElapsedStepOnlyCountsSolsWhileItIsActive()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "awakening.v1.json");
        session.Advance(10);
        var waiting = session.Mission!.Status().Single(status => status.Id == "run_time");
        Assert.Equal(0, waiting.Progress);

        foreach (var action in new[] { "orbit-camera", "zoom-camera", "select-overlay", "inspect-cell", "rover-view", "drive-rover", "scan-site", "orbital-view", "laboratory-view", "planetary-view", "analyze-region" })
        {
            session.RecordAction(action);
        }

        session.Advance(4);
        Assert.False(session.Mission.IsComplete);
        Assert.Equal(4, session.Mission.Status().Single(status => status.Id == "run_time").Progress);

        session.Advance(1);
        Assert.True(session.Mission.IsComplete);
        Assert.Contains(session.Events, item => item.Kind == "mission" && item.Text.Contains("DESPERTAR", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectionIsCompletableWithAColonyAndADecomposer()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "selection.v1.json");
        var research = new ResearchState(EconomyDefinition.LoadPublished(Repo.Root()));
        research.Earn(15);
        session.AttachEconomy(research);
        Assert.True(session.ResearchTechnology("gene_editing_1", out var error), error);
        Assert.True(session.Introduce("phototroph_a01", 32, 32, 0.5, Design, out _));
        Assert.True(session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _));

        session.Advance(400);

        Assert.True(session.Mission!.IsComplete, string.Join(", ", session.Mission.Status().Select(status => $"{status.Id}={status.Progress}/{status.Target}")));
        Assert.Null(session.Failure);
    }

    [Fact]
    public void DiversityCountsDistinctLivingGenomesOfTheCapability()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "selection.v1.json");
        session.Introduce("phototroph_a01", 10, 10, 0.5, null, out _);
        session.Introduce("phototroph_a01", 20, 20, 0.5, null, out _);
        session.Introduce("phototroph_a01", 30, 30, 0.5, Design, out _);
        session.Introduce("decomposer_d01", 30, 30, 0.05, null, out _);
        session.Advance(1);

        Assert.Equal(2, session.Mission!.Status().Single(status => status.Id == "reach_diversity").Progress);
    }

    [Fact]
    public void MissionProgressRoundTripsThroughJson()
    {
        var progress = new MissionProgress(
            "first_colony",
            new Dictionary<string, long> { ["analyze_region"] = 3 },
            ["analyze-region"],
            [7, 4],
            12,
            2,
            new Dictionary<string, long>());

        var copy = MissionProgress.FromJson(progress.ToJson());

        Assert.Equal(progress.ToJson(), copy.ToJson());
        Assert.Equal(12, copy.Streak);
        Assert.Equal([4, 7], copy.MutantIds);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"MissionId":"","Streak":0,"Mutations":0}""")]
    [InlineData("""{"MissionId":"first_colony","Streak":-1,"Mutations":0}""")]
    public void BrokenMissionProgressIsRefused(string json)
        => Assert.Throws<InvalidDataException>(() => MissionProgress.FromJson(json));

    [Fact]
    public void ProgressOfAnotherMissionOrObjectiveIsRefused()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, "awakening.v1.json");
        var empty = new Dictionary<string, long>();

        Assert.Throws<InvalidDataException>(() => session.Mission!.Restore(new MissionProgress("first_colony", empty, [], [], 0, 0, empty)));
        Assert.Throws<InvalidDataException>(() => session.Mission!.Restore(new MissionProgress("awakening", new Dictionary<string, long> { ["nope"] = 1 }, [], [], 0, 0, empty)));
    }

    /// <summary>H-31: a run saved halfway finishes FIRST COLONY on the same tick as one played straight through.</summary>
    [Fact]
    public void AResumedRunKeepsTheSustainStreak()
    {
        static GameSession Start()
        {
            var session = GameSession.NewGame(Repo.Root(), 5UL, "first_colony.v1.json");
            session.RecordAction("analyze-region");
            session.Experiment("phototroph_a01", 32, 32, 0.5, Design, 5);
            session.Introduce("phototroph_a01", 32, 32, 0.5, Design, out _);
            session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
            return session;
        }

        var straight = Start();
        straight.Advance(400);
        var expected = straight.Mission!.CompletedTick;
        Assert.NotNull(expected);

        var first = Start();
        first.Advance(120);
        var snapshot = StateSnapshotBinary.Read(StateSnapshotBinary.Write(first.Capture()));
        var progress = MissionProgress.FromJson(first.CaptureMission()!.ToJson());
        var resumed = GameSession.Resume(Repo.Root(), snapshot, "first_colony.v1.json", progress);
        resumed.Advance(280);

        Assert.Equal(expected, resumed.Mission!.CompletedTick);
        Assert.DoesNotContain(resumed.Events, item => item.Text.Contains("Analizar la región", StringComparison.Ordinal));
    }
}
