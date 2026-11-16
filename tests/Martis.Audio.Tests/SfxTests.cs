namespace Martis.Audio.Tests;

public sealed class SfxTests
{
    public static TheoryData<Sfx> All()
    {
        var data = new TheoryData<Sfx>();
        foreach (var sfx in Enum.GetValues<Sfx>())
        {
            data.Add(sfx);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(All))]
    public void EverySoundIsAudibleBoundedAndFinite(Sfx sfx)
    {
        var samples = SfxSynth.Render(sfx);
        Assert.True(Signal.AllFinite(samples), $"{sfx} has NaN or infinity.");
        var peak = Signal.Peak(samples);
        Assert.InRange(peak, 0.05f, 0.95f);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void EverySoundStartsAndEndsWithoutAClick(Sfx sfx)
    {
        var samples = SfxSynth.Render(sfx);
        Assert.True(MathF.Abs(samples[0]) < 0.02f, $"{sfx} starts at {samples[0]}.");
        var tail = samples.AsSpan(samples.Length - (SfxSynth.SampleRate / 100));
        Assert.True(Signal.Rms(tail) < 0.01, $"{sfx} does not fade out (tail RMS {Signal.Rms(tail)}).");
    }

    [Theory]
    [MemberData(nameof(All))]
    public void EverySoundKeepsItsDeclaredLength(Sfx sfx)
    {
        var samples = SfxSynth.Render(sfx);
        var seconds = samples.Length / (double)SfxSynth.SampleRate;
        Assert.Equal(SfxSynth.Duration(sfx), seconds, 3);
        Assert.InRange(seconds, 0.03, 6.0);
    }

    [Theory]
    [MemberData(nameof(All))]
    public void RenderingIsDeterministic(Sfx sfx)
    {
        Assert.Equal(SfxSynth.Render(sfx), SfxSynth.Render(sfx));
    }

    [Fact]
    public void InterfaceSoundsAreShortAndEventSoundsAreLonger()
    {
        Assert.True(SfxSynth.Duration(Sfx.UiHover) <= 0.1);
        Assert.True(SfxSynth.Duration(Sfx.UiClick) <= 0.15);
        Assert.True(SfxSynth.Duration(Sfx.Victory) >= 2.5);
        Assert.True(SfxSynth.Duration(Sfx.TitleSting) >= 3.0);
    }

    [Fact]
    public void HoverIsQuieterThanClick()
    {
        Assert.True(Signal.Peak(SfxSynth.Render(Sfx.UiHover)) < Signal.Peak(SfxSynth.Render(Sfx.UiClick)));
    }

    [Fact]
    public void ExtinctionIsDarkerThanMutation()
    {
        Assert.True(Signal.Brightness(SfxSynth.Render(Sfx.Extinction)) < Signal.Brightness(SfxSynth.Render(Sfx.Mutation)));
    }

    [Fact]
    public void AllSoundsAreDistinct()
    {
        var rendered = Enum.GetValues<Sfx>().Select(SfxSynth.Render).ToArray();
        for (var i = 0; i < rendered.Length; i++)
        {
            for (var j = i + 1; j < rendered.Length; j++)
            {
                Assert.False(rendered[i].AsSpan().SequenceEqual(rendered[j]), $"{(Sfx)i} and {(Sfx)j} are identical.");
            }
        }
    }

    [Fact]
    public void TheEventKindsOfTheSessionMapToSounds()
    {
        Assert.Equal(Sfx.Mutation, SfxSynth.ForEvent("mutation"));
        Assert.Equal(Sfx.Extinction, SfxSynth.ForEvent("extinction"));
        Assert.Equal(Sfx.Mission, SfxSynth.ForEvent("mission"));
        Assert.Equal(Sfx.Deploy, SfxSynth.ForEvent("deploy"));
        Assert.Equal(Sfx.Error, SfxSynth.ForEvent("failure"));
        Assert.Equal(Sfx.Experiment, SfxSynth.ForEvent("experiment"));
        Assert.Null(SfxSynth.ForEvent("tick"));
        Assert.Null(SfxSynth.ForEvent(string.Empty));
    }
}
