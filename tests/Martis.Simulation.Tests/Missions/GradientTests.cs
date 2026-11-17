using Martis.Simulation.Missions;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Missions;

/// <summary>Story mode H4: environment gradients over the uniform baseline (H-30).</summary>
public sealed class GradientTests
{
    private static readonly WorldOptions Chapter2 = CampaignDefinition.LoadPublished(Repo.Root()).Chapters[1].World;

    [Fact]
    public void ChaptersTwoToFiveHaveGradientsAndChapterOneStaysUniform()
    {
        var campaign = CampaignDefinition.LoadPublished(Repo.Root());

        Assert.Null(campaign.Chapters[0].World.Gradient);
        Assert.All(campaign.Chapters.Skip(1), chapter => Assert.Equal("gradients.v1.json", chapter.World.Gradient));
    }

    [Fact]
    public void TheEquatorIsWarmerAndBrighterThanThePoles()
    {
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter2);
        var equator = session.Inspect(64, 64);
        var pole = session.Inspect(64, 0);
        var flat = GameSession.NewGame(Repo.Root(), 5UL, false).Inspect(32, 32);

        Assert.True(equator.TemperatureKelvin > flat.TemperatureKelvin + 20, $"{equator.TemperatureKelvin}");
        Assert.True(pole.TemperatureKelvin < flat.TemperatureKelvin - 20, $"{pole.TemperatureKelvin}");
        Assert.True(equator.IrradianceWPerM2 > pole.IrradianceWPerM2 * 1.5);
        Assert.Equal(session.Inspect(64, 127).TemperatureKelvin, pole.TemperatureKelvin);
    }

    [Fact]
    public void HighlandsGetAHigherDoseThanBasins()
    {
        var gradient = EnvironmentGradient.LoadPublished(Repo.Root(), "gradients.v1.json");
        var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter2);

        Assert.Equal(0.0, gradient.Highland(0, 128), 6);
        Assert.Equal(1.0, gradient.Highland(32, 129), 6);
        Assert.True(session.Inspect(32, 64).RadiationSvPerSol > session.Inspect(0, 64).RadiationSvPerSol * 1.5);
    }

    [Fact]
    public void TheGradientMovesNoMass()
    {
        var flat = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter2 with { Gradient = null });
        var graded = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter2);

        for (var cell = 0; cell < flat.Engine.State.CellCount; cell += 517)
        {
            for (var slot = 0; slot < 9; slot++)
            {
                Assert.Equal(flat.Engine.State.Resources[cell].ValueAt(slot), graded.Engine.State.Resources[cell].ValueAt(slot));
            }
        }

        graded.Introduce("phototroph_a01", 64, 64, 0.5, null, out _);
        graded.Introduce("decomposer_d01", 64, 64, 0.3, null, out _);
        graded.Advance(20);
        Assert.Null(graded.Failure);
    }

    [Fact]
    public void AResumedRunKeepsItsGradientFromTheSnapshot()
    {
        var first = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter2);
        first.Advance(2);
        var snapshot = StateSnapshotBinary.Read(StateSnapshotBinary.Write(first.Capture()));

        var resumed = GameSession.Resume(Repo.Root(), snapshot, false, Chapter2 with { Gradient = null });

        Assert.Equal(first.Inspect(64, 0).TemperatureKelvin, resumed.Inspect(64, 0).TemperatureKelvin);
        Assert.Equal(first.Inspect(64, 64).TemperatureKelvin, resumed.Inspect(64, 64).TemperatureKelvin);
    }

    /// <summary>The lesson of chapter 2: in the warm band a colony burns its water unless enough decomposers close the cycle.</summary>
    [Fact]
    public void InTheWarmBandEnoughDecomposersKeepTheColonyAlive()
    {
        static double Survivors(double decomposerKg)
        {
            var session = GameSession.NewGame(Repo.Root(), 5UL, false, Chapter2);
            session.Introduce("phototroph_a01", 64, 64, 0.5, null, out _);
            session.Introduce("decomposer_d01", 64, 64, decomposerKg, null, out _);
            session.Advance(240);
            var cell = session.CellIndex(64, 64);
            return session.Engine.State.Populations.Where(p => p.CellIndex == cell && p.OrganismId.Value == "phototroph_a01").Sum(p => p.BiomassKg);
        }

        Assert.True(Survivors(0.05) < 0.05);
        Assert.True(Survivors(0.3) > 0.5);
    }

    [Theory]
    [InlineData("../gradients.v1.json")]
    [InlineData("x\\y.json")]
    public void AGradientPathOutsideTheScenarioFolderIsRefused(string file)
        => Assert.Throws<InvalidDataException>(() => EnvironmentGradient.LoadPublished(Repo.Root(), file));
}
