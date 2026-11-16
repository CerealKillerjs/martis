namespace Martis.Audio.Tests;

public sealed class WindEngineTests
{
    private static float[] Render(WindEngine wind, int frames, int chunk)
    {
        var output = new float[frames * 2];
        for (var start = 0; start < frames; start += chunk)
        {
            wind.Render(output.AsSpan(start * 2, Math.Min(chunk, frames - start) * 2));
        }

        return output;
    }

    [Fact]
    public void WindIsAudibleBoundedAndChunkIndependent()
    {
        var a = Render(new WindEngine(6), 44100 * 3, 512);
        var b = Render(new WindEngine(6), 44100 * 3, 999);
        Assert.Equal(a, b);
        Assert.True(Signal.AllFinite(a));
        Assert.InRange(Signal.Peak(a), 0.01f, 0.9f);
        Assert.True(Signal.Rms(a.AsSpan(a.Length / 2)) > 0.01);
    }

    [Fact]
    public void StrongerWindIsLouder()
    {
        var calm = new WindEngine(6) { Strength = 0.1f };
        var storm = new WindEngine(6) { Strength = 1f };
        Assert.True(Signal.Rms(Render(storm, 44100 * 4, 1024)) > Signal.Rms(Render(calm, 44100 * 4, 1024)));
    }
}
