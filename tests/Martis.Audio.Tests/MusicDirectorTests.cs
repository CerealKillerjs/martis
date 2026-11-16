namespace Martis.Audio.Tests;

public sealed class MusicDirectorTests
{
    [Fact]
    public void TheTitleScreenPlaysTheTitleTheme()
    {
        Assert.Equal(MusicMood.Title, MusicDirector.Choose(new MusicContext { OnTitle = true, TimeOfDay = 0.0f }));
    }

    [Theory]
    [InlineData(0.5f, MusicMood.Day)]
    [InlineData(0.3f, MusicMood.Day)]
    [InlineData(0.02f, MusicMood.Night)]
    [InlineData(0.9f, MusicMood.Night)]
    public void DayAndNightFollowTheSunWhenTimeRunsSlowly(float time, MusicMood expected)
    {
        // Paused or slow play: the sun is the clock of the music.
        Assert.Equal(expected, MusicDirector.Choose(new MusicContext { TimeOfDay = time, BiomassKg = 0.1 }));
        Assert.Equal(expected, MusicDirector.Choose(new MusicContext { TimeOfDay = time, BiomassKg = 0.1, SecondsPerSol = 60 }));
    }

    [Fact]
    public void FastSolsIgnoreTheSunSoTheMusicDoesNotFlicker()
    {
        // At 2 s per sol day and night would swap every second.
        Assert.Equal(MusicMood.Day, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.02f, BiomassKg = 0.1, SecondsPerSol = 2 }));
    }

    [Fact]
    public void AnEmptyWorldPlaysTheSparseNightTheme()
    {
        Assert.Equal(MusicMood.Night, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.5f, SecondsPerSol = 2 }));
    }

    [Fact]
    public void AGrowingColonyPlaysTheThrivingTheme()
    {
        Assert.Equal(MusicMood.Thriving, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.5f, BiomassKg = 2, BiomassGrowthPerSol = 0.05 }));
        Assert.Equal(MusicMood.Day, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.5f, BiomassKg = 1e-4, BiomassGrowthPerSol = 0.05 }));
    }

    [Fact]
    public void CollapseAndFailureOverrideEverythingButVictory()
    {
        Assert.Equal(MusicMood.Crisis, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.5f, BiomassKg = 1, BiomassGrowthPerSol = -0.2 }));
        Assert.Equal(MusicMood.Crisis, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.5f, Failure = true, BiomassKg = 2, BiomassGrowthPerSol = 0.1 }));
        Assert.Equal(MusicMood.Victory, MusicDirector.Choose(new MusicContext { TimeOfDay = 0.5f, Celebrating = true, BiomassGrowthPerSol = -0.5 }));
    }

    [Fact]
    public void GrowthIsMeasuredAsARelativeChangePerSol()
    {
        Assert.Equal(0.1, MusicDirector.Growth(previousKg: 1.0, currentKg: 1.1, sols: 1), 9);
        Assert.Equal(0.0, MusicDirector.Growth(previousKg: 0.0, currentKg: 0.0, sols: 1), 9);
        Assert.Equal(1.0, MusicDirector.Growth(previousKg: 0.0, currentKg: 0.5, sols: 1), 9);
        Assert.Equal(0.0, MusicDirector.Growth(previousKg: 1.0, currentKg: 2.0, sols: 0), 9);
    }
}
