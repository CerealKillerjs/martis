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

/// <summary>
/// Stereo reverb after Schroeder and Moorer (parallel damped combs, series all-passes),
/// with different delay lengths on each side for width.
/// </summary>
internal sealed class Reverb
{
    private static readonly int[] CombTuning = [1116, 1188, 1277, 1356, 1422, 1491];
    private static readonly int[] AllPassTuning = [556, 441, 341];
    private const int Spread = 23;

    private readonly float[][] _combs;
    private readonly float[] _combStore;
    private readonly int[] _combIndex;
    private readonly float[][] _allPasses;
    private readonly int[] _allPassIndex;

    public Reverb(int sampleRate, float room = 0.84f, float damping = 0.35f)
    {
        var scale = sampleRate / 44100f;
        _combs = new float[CombTuning.Length * 2][];
        for (var i = 0; i < CombTuning.Length; i++)
        {
            _combs[2 * i] = new float[(int)(CombTuning[i] * scale)];
            _combs[(2 * i) + 1] = new float[(int)((CombTuning[i] + Spread) * scale)];
        }

        _combStore = new float[_combs.Length];
        _combIndex = new int[_combs.Length];
        _allPasses = new float[AllPassTuning.Length * 2][];
        for (var i = 0; i < AllPassTuning.Length; i++)
        {
            _allPasses[2 * i] = new float[(int)(AllPassTuning[i] * scale)];
            _allPasses[(2 * i) + 1] = new float[(int)((AllPassTuning[i] + Spread) * scale)];
        }

        _allPassIndex = new int[_allPasses.Length];
        Room = room;
        Damping = damping;
    }

    public float Room { get; set; }

    public float Damping { get; set; }

    public void Process(float inLeft, float inRight, out float outLeft, out float outRight)
    {
        var input = (inLeft + inRight) * 0.5f * 0.12f;
        outLeft = 0f;
        outRight = 0f;
        for (var c = 0; c < _combs.Length; c++)
        {
            var buffer = _combs[c];
            var index = _combIndex[c];
            var output = buffer[index];
            _combStore[c] = (output * (1f - Damping)) + (_combStore[c] * Damping);
            buffer[index] = input + (_combStore[c] * Room);
            _combIndex[c] = index + 1 == buffer.Length ? 0 : index + 1;
            if ((c & 1) == 0)
            {
                outLeft += output;
            }
            else
            {
                outRight += output;
            }
        }

        for (var a = 0; a < _allPasses.Length; a++)
        {
            var buffer = _allPasses[a];
            var index = _allPassIndex[a];
            var stored = buffer[index];
            ref var signal = ref (a & 1) == 0 ? ref outLeft : ref outRight;
            var output = stored - signal;
            buffer[index] = signal + (stored * 0.5f);
            signal = output;
            _allPassIndex[a] = index + 1 == buffer.Length ? 0 : index + 1;
        }
    }
}

/// <summary>Stereo feedback delay with cross-feed (ping-pong) and a damped loop.</summary>
internal sealed class PingPong
{
    private readonly float[] _left;
    private readonly float[] _right;
    private int _index;
    private float _dampLeft;
    private float _dampRight;

    public PingPong(int samples)
    {
        _left = new float[samples];
        _right = new float[samples];
    }

    public float Feedback { get; set; } = 0.38f;

    public void Process(float inLeft, float inRight, out float outLeft, out float outRight)
    {
        outLeft = _left[_index];
        outRight = _right[_index];
        _dampLeft += 0.35f * (outLeft - _dampLeft);
        _dampRight += 0.35f * (outRight - _dampRight);
        _left[_index] = inLeft + (_dampRight * Feedback);
        _right[_index] = inRight + (_dampLeft * Feedback);
        _index = _index + 1 == _left.Length ? 0 : _index + 1;
    }
}
