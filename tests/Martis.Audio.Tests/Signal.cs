namespace Martis.Audio.Tests;

/// <summary>Small measurements on rendered buffers.</summary>
internal static class Signal
{
    public static float Peak(ReadOnlySpan<float> samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            peak = MathF.Max(peak, MathF.Abs(sample));
        }

        return peak;
    }

    public static double Rms(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return 0;
        }

        var sum = 0.0;
        foreach (var sample in samples)
        {
            sum += sample * (double)sample;
        }

        return Math.Sqrt(sum / samples.Length);
    }

    public static bool AllFinite(ReadOnlySpan<float> samples)
    {
        foreach (var sample in samples)
        {
            if (!float.IsFinite(sample))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Largest jump between consecutive samples: a click detector.</summary>
    public static float MaxStep(ReadOnlySpan<float> samples)
    {
        var step = 0f;
        for (var i = 1; i < samples.Length; i++)
        {
            step = MathF.Max(step, MathF.Abs(samples[i] - samples[i - 1]));
        }

        return step;
    }

    /// <summary>Rough brightness: share of energy in the first difference (high frequencies).</summary>
    public static double Brightness(ReadOnlySpan<float> samples)
    {
        var energy = 0.0;
        var diff = 0.0;
        for (var i = 1; i < samples.Length; i++)
        {
            energy += samples[i] * (double)samples[i];
            var d = samples[i] - (double)samples[i - 1];
            diff += d * d;
        }

        return energy <= 0 ? 0 : diff / energy;
    }

    public static float[] Channel(float[] interleaved, int channel)
    {
        var result = new float[interleaved.Length / 2];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = interleaved[(2 * i) + channel];
        }

        return result;
    }
}
