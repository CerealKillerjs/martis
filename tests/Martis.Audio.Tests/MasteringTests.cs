namespace Martis.Audio.Tests;

public sealed class MasteringTests
{
    private const int Rate = 44100;
    private static readonly float Ceiling = MathF.Pow(10f, -1f / 20f);

    private static float[] Sine(double hertz, float amplitude, double seconds)
    {
        var samples = new float[(int)(seconds * Rate)];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = amplitude * MathF.Sin((float)(2 * Math.PI * hertz * i / Rate));
        }

        return samples;
    }

    [Fact]
    public void AFullScaleSineInOneChannelReadsMinusThreeLufs()
    {
        // ITU-R BS.1770-4 calibration: 0 dBFS, 997 Hz, one channel → −3.01 LUFS. The filter
        // constants are specified at 48 kHz; at 44.1 kHz the reading may differ by < 0.1 LU.
        var sine = Sine(997, 1f, 5);
        Assert.InRange(LoudnessMeter.Integrated(sine, new float[sine.Length], Rate), -3.11, -2.91);
        Assert.InRange(LoudnessMeter.Integrated(sine, Rate), -3.11, -2.91);
        Assert.InRange(LoudnessMeter.Integrated(sine, sine, Rate), -0.1, 0.1);
        Assert.InRange(LoudnessMeter.Integrated(Sine(997, 0.1f, 5), Rate), -23.11, -22.91);
    }

    [Fact]
    public void SilenceHasNoLoudnessAndTheGateIgnoresIt()
    {
        Assert.Equal(double.NegativeInfinity, LoudnessMeter.Integrated(new float[Rate * 2], Rate));

        // Gating: adding silence does not lower the integrated loudness of a programme.
        var tone = Sine(997, 0.1f, 4);
        var padded = tone.Concat(new float[Rate * 8]).ToArray();
        // (Only the three blocks that straddle the end of the tone may count, by design.)
        var difference = LoudnessMeter.Integrated(tone, Rate) - LoudnessMeter.Integrated(padded, Rate);
        Assert.InRange(difference, 0, 0.3);
        Assert.InRange(LoudnessMeter.MaxMomentary(padded, Rate), -23.11, -22.91);
    }

    [Fact]
    public void HighPassRemovesDcAndLowPassKeepsIt()
    {
        var high = Biquad.HighPass(30, 0.707, Rate);
        var low = Biquad.LowPass(1000, 0.707, Rate);
        float h = 0, l = 0;
        for (var i = 0; i < Rate; i++)
        {
            h = high.Process(0.5f);
            l = low.Process(0.5f);
        }

        Assert.True(MathF.Abs(h) < 1e-3f, $"high-pass left {h}");
        Assert.Equal(0.5f, l, 3);
    }

    [Fact]
    public void ShelvesAndPeaksHaveTheirGainInTheirBand()
    {
        static double Gain(Biquad filter, double hertz)
        {
            var input = Sine(hertz, 0.5f, 1);
            var output = input.Select(filter.Process).ToArray();
            return 20 * Math.Log10(Signal.Rms(output.AsSpan(Rate / 2)) / Signal.Rms(input.AsSpan(Rate / 2)));
        }

        Assert.Equal(-3.0, Gain(Biquad.Peaking(300, 1.0, -3, Rate), 300), 1);
        Assert.Equal(0.0, Gain(Biquad.Peaking(300, 1.0, -3, Rate), 5000), 1);
        Assert.Equal(2.0, Gain(Biquad.HighShelf(8000, 2, Rate), 16000), 0);
        Assert.Equal(0.0, Gain(Biquad.HighShelf(8000, 2, Rate), 200), 1);
    }

    [Fact]
    public void TheLimiterNeverPassesTheCeiling()
    {
        var limiter = new Limiter(Rate);
        var random = new AudioRandom(3);
        for (var i = 0; i < Rate * 3; i++)
        {
            var burst = (i / 4410) % 3 == 0 ? 4f : 0.3f;
            limiter.Process(random.Noise() * burst, random.Noise() * burst, out var l, out var r);
            Assert.True(MathF.Abs(l) <= Ceiling + 1e-6f && MathF.Abs(r) <= Ceiling + 1e-6f, $"{l} {r} at {i}");
        }
    }

    [Fact]
    public void TheLimiterIsTransparentBelowTheCeiling()
    {
        var limiter = new Limiter(Rate);
        var input = Sine(440, 0.3f, 1);
        var output = new float[input.Length];
        for (var i = 0; i < input.Length; i++)
        {
            limiter.Process(input[i], input[i], out output[i], out _);
        }

        for (var i = limiter.Latency; i < input.Length; i++)
        {
            Assert.Equal(input[i - limiter.Latency], output[i], 4);
        }
    }

    [Fact]
    public void TheCompressorNarrowsTheDynamicRange()
    {
        static double Level(float amplitude)
        {
            var compressor = new Compressor(Rate, thresholdDb: -24, ratio: 3, attackMs: 10, releaseMs: 150);
            var input = Sine(440, amplitude, 2);
            var output = new float[input.Length];
            for (var i = 0; i < input.Length; i++)
            {
                var l = input[i];
                var r = input[i];
                compressor.Process(ref l, ref r);
                output[i] = l;
            }

            return 20 * Math.Log10(Signal.Rms(output.AsSpan(Rate)));
        }

        var loud = Level(0.5f);
        var quiet = Level(0.016f);
        Assert.True(loud - quiet < 20, $"in 30 dB, out {loud - quiet:0.0} dB");
        Assert.True(loud - quiet > 5);
    }

    [Fact]
    public void TheReverbTailDecaysByItsRt60AndIsStereo()
    {
        var reverb = new FdnReverb(Rate, rt60Seconds: 1.5f, preDelayMs: 20, damping: 0.3f);
        var left = new float[Rate * 3];
        var right = new float[Rate * 3];
        for (var i = 0; i < left.Length; i++)
        {
            reverb.Process(i == 0 ? 1f : 0f, i == 0 ? 1f : 0f, out left[i], out right[i]);
        }

        Assert.True(Signal.AllFinite(left) && Signal.AllFinite(right));
        var early = Signal.Rms(left.AsSpan(Rate / 20, Rate / 10));
        var late = Signal.Rms(left.AsSpan((int)(Rate * 1.8), Rate / 10));
        Assert.True(20 * Math.Log10(late / early) < -45, $"tail {20 * Math.Log10(late / early):0.0} dB");
        Assert.True(early > 1e-4);
        Assert.False(left.AsSpan().SequenceEqual(right));
    }
}
