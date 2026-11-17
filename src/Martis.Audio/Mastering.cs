namespace Martis.Audio;

/// <summary>
/// RBJ "Audio EQ Cookbook" biquad in transposed direct form II (double state).
/// </summary>
public sealed class Biquad
{
    private readonly double _b0;
    private readonly double _b1;
    private readonly double _b2;
    private readonly double _a1;
    private readonly double _a2;
    private double _z1;
    private double _z2;

    public Biquad(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public static Biquad LowPass(double hertz, double q, int sampleRate)
    {
        var (cos, alpha) = Terms(hertz, q, sampleRate);
        return new Biquad((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad HighPass(double hertz, double q, int sampleRate)
    {
        var (cos, alpha) = Terms(hertz, q, sampleRate);
        return new Biquad((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad BandPass(double hertz, double q, int sampleRate)
    {
        var (cos, alpha) = Terms(hertz, q, sampleRate);
        return new Biquad(alpha, 0, -alpha, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public static Biquad Peaking(double hertz, double q, double gainDb, int sampleRate)
    {
        var a = Math.Pow(10, gainDb / 40);
        var (cos, alpha) = Terms(hertz, q, sampleRate);
        return new Biquad(1 + (alpha * a), -2 * cos, 1 - (alpha * a), 1 + (alpha / a), -2 * cos, 1 - (alpha / a));
    }

    public static Biquad HighShelf(double hertz, double gainDb, int sampleRate) => Shelf(hertz, gainDb, sampleRate, high: true);

    public static Biquad LowShelf(double hertz, double gainDb, int sampleRate) => Shelf(hertz, gainDb, sampleRate, high: false);

    public float Process(float input)
    {
        var output = (_b0 * input) + _z1;
        _z1 = (_b1 * input) - (_a1 * output) + _z2;
        _z2 = (_b2 * input) - (_a2 * output);
        return (float)output;
    }

    private static (double Cos, double Alpha) Terms(double hertz, double q, int sampleRate)
    {
        var w = 2 * Math.PI * Math.Min(hertz, sampleRate * 0.49) / sampleRate;
        return (Math.Cos(w), Math.Sin(w) / (2 * q));
    }

    private static Biquad Shelf(double hertz, double gainDb, int sampleRate, bool high)
    {
        var a = Math.Pow(10, gainDb / 40);
        var (cos, alpha) = Terms(hertz, Math.Sqrt(0.5), sampleRate);
        var root = 2 * Math.Sqrt(a) * alpha;
        var sign = high ? 1 : -1;
        return new Biquad(
            a * ((a + 1) + (sign * (a - 1) * cos) + root),
            -sign * 2 * a * ((a - 1) + (sign * (a + 1) * cos)),
            a * ((a + 1) + (sign * (a - 1) * cos) - root),
            (a + 1) - (sign * (a - 1) * cos) + root,
            sign * 2 * ((a - 1) - (sign * (a + 1) * cos)),
            (a + 1) - (sign * (a - 1) * cos) - root);
    }
}

/// <summary>
/// Loudness after ITU-R BS.1770-4 / EBU R128: K-weighting, 400 ms blocks with 75 % overlap,
/// absolute gate at −70 LUFS and relative gate at −10 LU for the integrated value.
/// </summary>
public static class LoudnessMeter
{
    public static double Integrated(ReadOnlySpan<float> mono, int sampleRate) => Integrated(mono, default, sampleRate);

    public static double Integrated(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        var blocks = Blocks(left, right, sampleRate);
        var above = blocks.Where(z => Lufs(z) > -70).ToArray();
        if (above.Length == 0)
        {
            return double.NegativeInfinity;
        }

        var relative = Lufs(above.Average()) - 10;
        var gated = above.Where(z => Lufs(z) > relative).ToArray();
        return Lufs(gated.Average());
    }

    /// <summary>Loudest 400 ms window (momentary loudness, EBU R128), for short sounds.</summary>
    public static double MaxMomentary(ReadOnlySpan<float> mono, int sampleRate)
    {
        var blocks = Blocks(mono, default, sampleRate);
        return blocks.Length == 0 ? double.NegativeInfinity : Lufs(blocks.Max());
    }

    private static double Lufs(double meanSquare) => meanSquare <= 0 ? double.NegativeInfinity : -0.691 + (10 * Math.Log10(meanSquare));

    private static double[] Blocks(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int sampleRate)
    {
        var block = (int)(0.4 * sampleRate);
        var step = block / 4;
        // Only a sound shorter than one block is padded with silence; longer signals use the
        // blocks that fit entirely, as the standard does.
        var length = Math.Max(left.Length, block);
        var weighted = new double[length];
        foreach (var (channel, present) in new[] { (0, true), (1, !right.IsEmpty) })
        {
            if (!present)
            {
                continue;
            }

            var source = channel == 0 ? left : right;
            var (shelf, highPass) = KWeighting(sampleRate);
            for (var i = 0; i < length; i++)
            {
                var x = i < source.Length ? source[i] : 0f;
                var y = highPass.Process(shelf.Process(x));
                weighted[i] += y * (double)y;
            }
        }

        var count = ((length - block) / step) + 1;
        var result = new double[count];
        for (var b = 0; b < count; b++)
        {
            var sum = 0.0;
            for (var i = b * step; i < (b * step) + block; i++)
            {
                sum += weighted[i];
            }

            result[b] = sum / block;
        }

        return result;
    }

    /// <summary>The two K-weighting stages of BS.1770 at any sample rate.</summary>
    private static (Biquad Shelf, Biquad HighPass) KWeighting(int sampleRate)
    {
        var k = Math.Tan(Math.PI * 1681.974450955533 / sampleRate);
        var q = 0.7071752369554196;
        var vh = Math.Pow(10, 3.999843853973347 / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + (k / q) + (k * k);
        var shelf = new Biquad(vh + (vb * k / q) + (k * k), 2 * ((k * k) - vh), vh - (vb * k / q) + (k * k), a0, 2 * ((k * k) - 1), 1 - (k / q) + (k * k));
        k = Math.Tan(Math.PI * 38.13547087602444 / sampleRate);
        q = 0.5003270373238773;
        a0 = 1 + (k / q) + (k * k);
        var highPass = new Biquad(1, -2, 1, a0, 2 * ((k * k) - 1), 1 - (k / q) + (k * k));
        return (shelf, highPass);
    }
}

/// <summary>Stereo-linked feed-forward compressor with a soft knee (gain computed in dB).</summary>
public sealed class Compressor
{
    private readonly float _threshold;
    private readonly float _ratio;
    private readonly float _knee;
    private readonly float _makeup;
    private readonly float _attack;
    private readonly float _release;
    private float _reduction;

    public Compressor(int sampleRate, float thresholdDb, float ratio, float attackMs, float releaseMs, float makeupDb = 0f, float kneeDb = 6f)
    {
        _threshold = thresholdDb;
        _ratio = ratio;
        _knee = kneeDb;
        _makeup = makeupDb;
        _attack = MathF.Exp(-1f / (attackMs * 0.001f * sampleRate));
        _release = MathF.Exp(-1f / (releaseMs * 0.001f * sampleRate));
    }

    /// <summary>Current gain reduction in dB (positive).</summary>
    public float ReductionDb => _reduction;

    public void Process(ref float left, ref float right)
    {
        var peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
        var level = 20f * MathF.Log10(MathF.Max(peak, 1e-6f));
        var over = level - _threshold;
        float target;
        if (over <= -_knee / 2)
        {
            target = 0f;
        }
        else if (over >= _knee / 2)
        {
            target = over * (1f - (1f / _ratio));
        }
        else
        {
            var x = over + (_knee / 2);
            target = (1f - (1f / _ratio)) * x * x / (2f * _knee);
        }

        var coefficient = target > _reduction ? _attack : _release;
        _reduction = target + (coefficient * (_reduction - target));
        var gain = MathF.Pow(10f, (_makeup - _reduction) / 20f);
        left *= gain;
        right *= gain;
    }
}

/// <summary>
/// Look-ahead brick-wall peak limiter: required gain → minimum over the look-ahead window →
/// instant attack, exponential release → moving average over the window, applied to the
/// delayed signal, with a final safety clamp. Output never exceeds the ceiling.
/// </summary>
public sealed class Limiter
{
    private readonly float _ceiling;
    private readonly float _release;
    private readonly float[] _delayLeft;
    private readonly float[] _delayRight;
    private readonly float[] _held;
    private readonly long[] _dequeIndex;
    private readonly float[] _dequeValue;
    private int _head;
    private int _count;
    private long _index;
    private float _gain = 1f;
    private double _sum;

    public Limiter(int sampleRate, float ceilingDb = -1f, float lookaheadMs = 5f, float releaseMs = 120f)
    {
        _ceiling = MathF.Pow(10f, ceilingDb / 20f);
        Latency = Math.Max(1, (int)(lookaheadMs * 0.001f * sampleRate));
        _release = MathF.Exp(-1f / (releaseMs * 0.001f * sampleRate));
        _delayLeft = new float[Latency];
        _delayRight = new float[Latency];
        _held = new float[Latency];
        Array.Fill(_held, 1f);
        _sum = Latency;
        _dequeIndex = new long[Latency + 2];
        _dequeValue = new float[Latency + 2];
    }

    /// <summary>Delay in samples between input and output.</summary>
    public int Latency { get; }

    public float Ceiling => _ceiling;

    public void Process(float inLeft, float inRight, out float outLeft, out float outRight)
    {
        var peak = MathF.Max(MathF.Abs(inLeft), MathF.Abs(inRight));
        var required = peak > _ceiling ? _ceiling / peak : 1f;

        // Sliding minimum of the required gain over the last Latency + 1 samples (monotonic deque),
        // so every average below covers the sample that leaves the delay line.
        var capacity = _dequeIndex.Length;
        while (_count > 0 && _dequeValue[(_head + _count - 1) % capacity] >= required)
        {
            _count--;
        }

        var tail = (_head + _count) % capacity;
        _dequeIndex[tail] = _index;
        _dequeValue[tail] = required;
        _count++;
        while (_dequeIndex[_head] < _index - Latency)
        {
            _head = (_head + 1) % capacity;
            _count--;
        }

        var minimum = _dequeValue[_head];
        _gain = minimum < _gain ? minimum : minimum + (_release * (_gain - minimum));

        // Moving average of the held gain: a smooth ramp that reaches the minimum in time.
        var slot = (int)(_index % Latency);
        _sum += _gain - _held[slot];
        _held[slot] = _gain;
        var gain = (float)Math.Min(1.0, _sum / Latency);

        outLeft = Math.Clamp(_delayLeft[slot] * gain, -_ceiling, _ceiling);
        outRight = Math.Clamp(_delayRight[slot] * gain, -_ceiling, _ceiling);
        _delayLeft[slot] = inLeft;
        _delayRight[slot] = inRight;
        _index++;
    }
}

/// <summary>
/// Feedback delay network reverb (8 lines, Householder matrix, per-line RT60 gains and
/// damping in the loop) with pre-delay and early reflections.
/// </summary>
public sealed class FdnReverb
{
    private static readonly int[] Lengths = [1427, 1601, 1867, 2053, 2251, 2399, 2617, 2797];
    private static readonly (int Delay, float Gain)[] EarlyLeft = [(331, 0.42f), (617, 0.33f), (1013, 0.26f), (1409, 0.19f), (1789, 0.13f)];
    private static readonly (int Delay, float Gain)[] EarlyRight = [(419, 0.40f), (743, 0.31f), (1129, 0.24f), (1553, 0.17f), (1931, 0.12f)];

    private readonly float[][] _lines = new float[8][];
    private readonly int[] _positions = new int[8];
    private readonly float[] _gains = new float[8];
    private readonly float[] _lowpass = new float[8];
    private readonly float[] _outputs = new float[8];
    private readonly float[] _preLeft;
    private readonly float[] _preRight;
    private readonly float _damping;
    private int _pre;

    public FdnReverb(int sampleRate, float rt60Seconds, float preDelayMs, float damping)
    {
        var scale = sampleRate / 44100f;
        for (var i = 0; i < 8; i++)
        {
            var length = (int)(Lengths[i] * scale);
            _lines[i] = new float[length];
            _gains[i] = MathF.Pow(10f, -3f * length / (rt60Seconds * sampleRate));
        }

        _damping = Math.Clamp(damping, 0f, 0.95f);
        var pre = Math.Max(2400, (int)(preDelayMs * 0.001f * sampleRate) + 2048);
        _preLeft = new float[pre];
        _preRight = new float[pre];
        PreDelay = (int)(preDelayMs * 0.001f * sampleRate);
    }

    public int PreDelay { get; }

    public void Process(float inLeft, float inRight, out float outLeft, out float outRight)
    {
        var size = _preLeft.Length;
        _preLeft[_pre] = inLeft;
        _preRight[_pre] = inRight;
        float Tap(float[] buffer, int delay) => buffer[(_pre - delay + size) % size];

        var left = Tap(_preLeft, PreDelay);
        var right = Tap(_preRight, PreDelay);

        // Early reflections from the pre-delayed signal.
        var earlyL = 0f;
        var earlyR = 0f;
        foreach (var (delay, gain) in EarlyLeft)
        {
            earlyL += Tap(_preLeft, PreDelay + delay) * gain;
        }

        foreach (var (delay, gain) in EarlyRight)
        {
            earlyR += Tap(_preRight, PreDelay + delay) * gain;
        }

        // Late field: read, damp, mix with a Householder matrix, write back with input.
        var sum = 0f;
        for (var i = 0; i < 8; i++)
        {
            var line = _lines[i];
            var value = line[_positions[i]];
            _lowpass[i] = value + (_damping * (_lowpass[i] - value));
            _outputs[i] = _lowpass[i] * _gains[i];
            sum += _outputs[i];
        }

        var mix = sum * (2f / 8f);
        var lateL = 0f;
        var lateR = 0f;
        for (var i = 0; i < 8; i++)
        {
            var feed = (i & 1) == 0 ? left : right;
            var sign = (i & 2) == 0 ? 1f : -1f;
            var line = _lines[i];
            line[_positions[i]] = _outputs[i] - mix + (feed * sign * 0.35f);
            _positions[i] = _positions[i] + 1 == line.Length ? 0 : _positions[i] + 1;
            if ((i & 1) == 0)
            {
                lateL += _outputs[i] * sign;
            }
            else
            {
                lateR += _outputs[i] * sign;
            }
        }

        _pre = (_pre + 1) % size;
        outLeft = (earlyL * 0.5f) + (lateL * 0.5f);
        outRight = (earlyR * 0.5f) + (lateR * 0.5f);
    }
}

/// <summary>
/// The soundtrack's mix and master bus: a send into a concert-hall FDN reverb, then a
/// high-pass at 30 Hz (subsonic rumble), −1.5 dB of "mud" at 280 Hz, +1.5 dB of air above
/// 9 kHz, slow 2:1 glue compression, a little mid/side width, a calibrated trim and a
/// lookahead limiter with its ceiling at −1 dBFS (headroom for lossy encoding).
/// </summary>
public sealed class MasterBus
{
    private readonly FdnReverb _hall;
    private readonly Biquad _subLeft;
    private readonly Biquad _subRight;
    private readonly Biquad _mudLeft;
    private readonly Biquad _mudRight;
    private readonly Biquad _airLeft;
    private readonly Biquad _airRight;
    private readonly Compressor _glue;
    private readonly Limiter _limiter;

    public MasterBus(int sampleRate, float reverbSeconds = 2.6f, float trimDb = 0f)
    {
        _hall = new FdnReverb(sampleRate, reverbSeconds, preDelayMs: 22f, damping: 0.45f);
        _subLeft = Biquad.HighPass(30, 0.707, sampleRate);
        _subRight = Biquad.HighPass(30, 0.707, sampleRate);
        _mudLeft = Biquad.Peaking(280, 1.0, -1.5, sampleRate);
        _mudRight = Biquad.Peaking(280, 1.0, -1.5, sampleRate);
        _airLeft = Biquad.HighShelf(9000, 1.5, sampleRate);
        _airRight = Biquad.HighShelf(9000, 1.5, sampleRate);
        _glue = new Compressor(sampleRate, thresholdDb: -26f, ratio: 2f, attackMs: 30f, releaseMs: 300f, kneeDb: 8f);
        _limiter = new Limiter(sampleRate, ceilingDb: -1f, lookaheadMs: 5f, releaseMs: 150f);
        TrimDb = trimDb;
    }

    /// <summary>Gain before the limiter that sets the integrated loudness.</summary>
    public float TrimDb
    {
        get => 20f * MathF.Log10(Trim);
        set => Trim = MathF.Pow(10f, value / 20f);
    }

    /// <summary>The same trim as a linear gain, cheap to automate per sample.</summary>
    public float Trim { get; set; } = 1f;

    public float Width { get; set; } = 1.15f;

    public float ReverbLevel { get; set; } = 0.55f;

    public void Process(float dryLeft, float dryRight, float sendLeft, float sendRight, out float left, out float right)
    {
        _hall.Process(sendLeft, sendRight, out var wetLeft, out var wetRight);
        var l = dryLeft + (wetLeft * ReverbLevel);
        var r = dryRight + (wetRight * ReverbLevel);
        l = _airLeft.Process(_mudLeft.Process(_subLeft.Process(l)));
        r = _airRight.Process(_mudRight.Process(_subRight.Process(r)));
        _glue.Process(ref l, ref r);
        var mid = (l + r) * 0.5f;
        var side = (l - r) * 0.5f * Width;
        _limiter.Process((mid + side) * Trim, (mid - side) * Trim, out left, out right);
    }
}
