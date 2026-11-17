namespace Martis.Audio.Tests;

public sealed class MusicEngineTests
{
    private const int Rate = MusicEngine.SampleRate;

    private static float[] Render(MusicEngine engine, double seconds, int chunk = 1024)
    {
        var frames = (int)(seconds * Rate);
        var output = new float[frames * 2];
        for (var start = 0; start < frames; start += chunk)
        {
            var count = Math.Min(chunk, frames - start);
            engine.Render(output.AsSpan(start * 2, count * 2));
        }

        return output;
    }

    [Fact]
    public void TheSameSeedAndMoodGiveTheSameMusic()
    {
        var a = Render(new MusicEngine(9, MusicMood.Day), 3);
        var b = Render(new MusicEngine(9, MusicMood.Day), 3);
        Assert.Equal(a, b);
    }

    [Fact]
    public void TheChunkSizeDoesNotChangeTheMusic()
    {
        // The game streams with whatever buffer the audio server asks for.
        var big = Render(new MusicEngine(4, MusicMood.Night), 2.5, chunk: 8192);
        var small = Render(new MusicEngine(4, MusicMood.Night), 2.5, chunk: 37);
        Assert.Equal(big, small);
    }

    [Fact]
    public void DifferentSeedsComposeDifferentMusic()
    {
        Assert.NotEqual(Render(new MusicEngine(1, MusicMood.Day), 4), Render(new MusicEngine(2, MusicMood.Day), 4));
    }

    public static TheoryData<MusicMood> Moods()
    {
        var data = new TheoryData<MusicMood>();
        foreach (var mood in Enum.GetValues<MusicMood>())
        {
            data.Add(mood);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Moods))]
    public void EveryMoodIsAudibleStereoAndBounded(MusicMood mood)
    {
        var output = Render(new MusicEngine(5, mood), 20);
        Assert.True(Signal.AllFinite(output));
        Assert.True(Signal.Peak(output) <= 0.98f, $"{mood} peaks at {Signal.Peak(output)}.");
        var settled = output.AsSpan(output.Length * 3 / 10);
        Assert.True(Signal.Rms(settled) > 0.015, $"{mood} is almost silent ({Signal.Rms(settled)}).");
        var left = Signal.Channel(output, 0);
        var right = Signal.Channel(output, 1);
        Assert.False(left.AsSpan().SequenceEqual(right), $"{mood} is mono.");
    }

    [Fact]
    public void MusicFadesInFromSilence()
    {
        var output = Render(new MusicEngine(5, MusicMood.Title), 0.05);
        Assert.True(MathF.Abs(output[0]) < 0.01f);
        Assert.True(Signal.Peak(output) < 0.2f);
    }

    [Fact]
    public void ChangingMoodCrossfadesWithoutClicks()
    {
        var engine = new MusicEngine(11, MusicMood.Day);
        var before = Render(engine, 3);
        engine.SetMood(MusicMood.Crisis);
        Assert.Equal(MusicMood.Crisis, engine.Mood);
        var after = Render(engine, 6);
        var step = MathF.Max(Signal.MaxStep(Signal.Channel(after, 0)), Signal.MaxStep(Signal.Channel(after, 1)));
        Assert.True(step < 0.25f, $"Step {step}.");
        Assert.True(Signal.Rms(after) > 0.02);
        Assert.NotEmpty(before);
    }

    [Fact]
    public void CrisisSoundsDarkerThanTheTitle()
    {
        var crisis = Signal.Channel(Render(new MusicEngine(3, MusicMood.Crisis), 8), 0);
        var title = Signal.Channel(Render(new MusicEngine(3, MusicMood.Title), 8), 0);
        Assert.True(Signal.Brightness(crisis) < Signal.Brightness(title), $"crisis {Signal.Brightness(crisis)} title {Signal.Brightness(title)}");
    }

    [Fact]
    public void ChaptersChangeTheKeyOfTheMusic()
    {
        var style1 = MusicStyle.For(MusicMood.Day, "ch1");
        var style5 = MusicStyle.For(MusicMood.Day, "ch5");
        var free = MusicStyle.For(MusicMood.Day, null);
        Assert.NotEqual(style1.Root, style5.Root);
        Assert.Equal(free, MusicStyle.For(MusicMood.Day, "unknown"));
        var engine = new MusicEngine(3, MusicMood.Day);
        engine.SetChapter("ch5");
        Assert.Equal("ch5", engine.Chapter);
        Assert.True(Signal.Rms(Render(engine, 4)) > 0.02);
    }

    [Fact]
    public void SettingTheSameMoodTwiceDoesNotRestartTheMusic()
    {
        var a = new MusicEngine(8, MusicMood.Night);
        var b = new MusicEngine(8, MusicMood.Night);
        Render(a, 1);
        Render(b, 1);
        a.SetMood(MusicMood.Night);
        Assert.Equal(Render(a, 1), Render(b, 1));
    }

    [Fact]
    public void ThePianoPlaysPhrasesWithRestsBetweenThem()
    {
        // Calm piano music breathes: phrases of a few bars, then silence (Daniel, 2026-11-17).
        var left = Signal.Channel(Render(new MusicEngine(12, MusicMood.Day), 60), 0);
        var window = MusicEngine.SampleRate / 2;
        var levels = new List<double>();
        for (var start = MusicEngine.SampleRate * 4; start + window <= left.Length; start += window)
        {
            levels.Add(Signal.Rms(left.AsSpan(start, window)));
        }

        levels.Sort();
        var quiet = levels[levels.Count / 10];
        var loud = levels[levels.Count * 9 / 10];
        Assert.True(quiet < loud * 0.35, $"quiet {quiet:0.0000} loud {loud:0.0000}");
    }

    [Fact]
    public void MelodiesAreSparseAndTheirDensityFollowsTheMood()
    {
        int Notes(MusicMood mood)
        {
            var engine = new MusicEngine(21, mood);
            Render(engine, 60);
            return engine.NotesPlayed;
        }

        var night = Notes(MusicMood.Night);
        var day = Notes(MusicMood.Day);
        var thriving = Notes(MusicMood.Thriving);
        Assert.InRange(day, 15, 160);
        Assert.True(night < day, $"night {night} day {day}");
        Assert.True(day < thriving, $"day {day} thriving {thriving}");
    }

    [Fact]
    public void EveryMoodIsMasteredToTheSameLoudnessAndUnderTheCeiling()
    {
        // Mastering (Daniel, 2026-11-17): consistent loudness between moods, peaks under −1 dBFS.
        var ceiling = MathF.Pow(10f, -1f / 20f);
        var levels = new List<double>();
        foreach (var mood in Enum.GetValues<MusicMood>())
        {
            var output = Render(new MusicEngine(31, mood), 40);
            Assert.True(Signal.Peak(output) <= ceiling + 1e-6f, $"{mood} peaks at {Signal.Peak(output)}");
            var lufs = LoudnessMeter.Integrated(Signal.Channel(output, 0), Signal.Channel(output, 1), MusicEngine.SampleRate);
            Assert.InRange(lufs, MusicEngine.TargetLufs - 3, MusicEngine.TargetLufs + 3);
            levels.Add(lufs);
        }

        Assert.True(levels.Max() - levels.Min() < 4, string.Join(", ", levels.Select(l => l.ToString("0.0"))));
    }
}
