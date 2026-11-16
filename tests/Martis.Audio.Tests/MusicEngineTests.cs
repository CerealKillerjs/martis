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
        var output = Render(new MusicEngine(5, mood), 6);
        Assert.True(Signal.AllFinite(output));
        Assert.True(Signal.Peak(output) <= 0.98f, $"{mood} peaks at {Signal.Peak(output)}.");
        var settled = output.AsSpan(output.Length / 2);
        Assert.True(Signal.Rms(settled) > 0.02, $"{mood} is almost silent ({Signal.Rms(settled)}).");
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
}
