namespace Martis.Audio;

/// <summary>Small building blocks shared by the effects, the soundtrack and the wind.</summary>
internal static class Dsp
{
    public const float Tau = MathF.PI * 2f;

    /// <summary>Attack-decay envelope: linear rise, exponential fall (time constant <paramref name="decay"/>).</summary>
    public static float Percussive(float t, float attack, float decay)
        => t < 0f ? 0f : t < attack ? t / attack : MathF.Exp(-(t - attack) / decay);

    /// <summary>Raised-cosine window over [start, end]: 0 → 1 → 0 with smooth ends.</summary>
    public static float Swell(float t, float start, float end)
    {
        if (t <= start || t >= end)
        {
            return 0f;
        }

        var x = (t - start) / (end - start);
        return 0.5f - (0.5f * MathF.Cos(Tau * x));
    }

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>Soft clipper with |y| &lt; ceiling for every input.</summary>
    public static float SoftClip(float x, float ceiling = 0.97f) => ceiling * MathF.Tanh(x / ceiling);

    /// <summary>Coefficient of a one-pole smoother with the given time constant in seconds.</summary>
    public static float Smoothing(float seconds, int sampleRate)
        => 1f - MathF.Exp(-1f / (seconds * sampleRate));
}

/// <summary>Chamberlin state-variable filter (low, band and high outputs).</summary>
internal struct Svf
{
    private float _low;
    private float _band;

    public float Low;
    public float Band;
    public float High;

    public void Process(float input, float cutoffHz, float resonance, int sampleRate)
    {
        var f = 2f * MathF.Sin(MathF.PI * Math.Clamp(cutoffHz, 10f, sampleRate / 6f) / sampleRate);
        var q = 1f / Math.Clamp(resonance, 0.5f, 20f);
        _low += f * _band;
        var high = input - _low - (q * _band);
        _band += f * high;
        Low = _low;
        Band = _band;
        High = high;
    }
}
