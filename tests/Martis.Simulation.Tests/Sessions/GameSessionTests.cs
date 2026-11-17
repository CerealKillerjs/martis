using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Sessions;

/// <summary>T08: the API the Godot game drives.</summary>
[Trait("Category", "Integration")]
public sealed class GameSessionTests
{
    private static readonly Dictionary<string, double> Design = new() { ["UV_PROTECTION"] = 0.85 };

    [Fact]
    public void ANewGameCanPlayFirstColonyToTheEnd()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL);
        session.RecordAction("analyze-region");
        var forecast = session.Experiment("phototroph_a01", 32, 32, 0.5, Design, 20);
        Assert.True(forecast.Survived, forecast.Failure);
        Assert.Equal(20, forecast.BiomassKg.Count);
        Assert.Equal(0, session.Tick);

        Assert.True(session.Introduce("phototroph_a01", 32, 32, 0.5, Design, out _));
        Assert.True(session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _));
        Assert.Equal(400, session.Advance(400));

        Assert.True(session.Mission!.IsComplete);
        Assert.Contains(session.Events, item => item.Kind == "mission" && item.Text.Contains("completada", StringComparison.Ordinal));
        Assert.Contains(session.Events, item => item.Kind == "mutation");
        Assert.Null(session.Failure);
    }

    [Fact]
    public void AnExperimentInAnEstablishedColonyFollowsTheMergedCulture()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL);
        session.Introduce("phototroph_a01", 32, 32, 0.5, Design, out _);
        session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
        session.Advance(60);

        var forecast = session.Experiment("phototroph_a01", 32, 32, 0.5, Design, 20);

        Assert.True(forecast.Survived, forecast.Failure);
        Assert.True(forecast.BiomassKg[^1] > 0.1);
    }

    [Fact]
    public void TheLaboratoryDoesNotTouchTheLiveWorld()
    {
        var session = GameSession.NewGame(Repo.Root(), 9UL);
        session.Introduce("phototroph_a01", 10, 10, 0.2, null, out _);
        session.Advance(5);
        var before = StateSnapshotJson.Write(session.Capture());

        session.Experiment("extremophile_e01", 12, 12, 0.3, null, 30);

        Assert.Equal(before, StateSnapshotJson.Write(session.Capture()));
        Assert.Equal(5, session.Tick);
    }

    [Fact]
    public void TheViewAndTheInspectorShowTheColony()
    {
        var session = GameSession.NewGame(Repo.Root(), 3UL);
        session.Introduce("phototroph_a01", 7, 9, 0.4, null, out _);
        session.Introduce("decomposer_d01", 7, 9, 0.04, null, out _);
        session.Advance(10);

        var view = session.View();
        var cell = session.CellIndex(7, 9);
        Assert.Equal(64 * 64, view.BiomassKg.Length);
        Assert.True(view.PhototrophBiomassKg[cell] > 0 && view.DecomposerBiomassKg[cell] > 0);
        Assert.Equal(view.BiomassKg.Sum(value => (double)value), view.TotalBiomassKg, 4);
        Assert.InRange(view.VariantHue[cell], 0f, 1f);

        var report = session.Inspect(7, 9);
        Assert.Equal(2, report.Populations.Count);
        Assert.Contains("CarbonDioxide", report.ResourcesKg.Keys);
        Assert.Equal(7, report.Populations[0].Genome.Count);
        Assert.Contains(session.Events, item => item.Kind == "deploy" && item.Cell == cell);
        Assert.Equal(session.Events.Count, session.TotalEvents);
        Assert.Equal(8, session.Organisms.Count);
        Assert.All(session.Organisms, organism => Assert.All(organism.BaseGenome, gene => Assert.InRange(gene.Value, 0.0, 1.0)));
    }

    [Fact]
    public void InvalidDeploymentsAreRefusedWithAReason()
    {
        var session = GameSession.NewGame(Repo.Root(), 1UL);

        Assert.False(session.Introduce("nobody", 1, 1, 1, null, out var unknown));
        Assert.False(session.Introduce("phototroph_a01", 64, 1, 1, null, out var outside));
        Assert.False(session.Introduce("phototroph_a01", 1, 1, 0, null, out var empty));
        Assert.False(session.Introduce("phototroph_a01", 1, 1, 1, new Dictionary<string, double> { ["UV_PROTECTION"] = 3 }, out var gene));
        Assert.All(new[] { unknown, outside, empty, gene }, Assert.NotNull);
        Assert.Equal(0, session.Engine.PendingCommandCount);
    }

    [Fact]
    public void ASavedSessionResumesWhereItStopped()
    {
        var session = GameSession.NewGame(Repo.Root(), 77UL);
        session.Introduce("phototroph_a01", 20, 20, 0.5, null, out _);
        session.Advance(15);
        var bytes = StateSnapshotBinary.Write(session.Capture());
        session.Advance(10);

        var resumed = GameSession.Resume(Repo.Root(), StateSnapshotBinary.Read(bytes));
        resumed.Advance(10);

        Assert.Equal(77UL, resumed.Seed);
        Assert.Equal(StateSnapshotJson.Write(session.Capture()), StateSnapshotJson.Write(resumed.Capture()));
    }
}
