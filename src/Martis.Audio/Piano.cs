namespace Martis.Audio;

/// <summary>
/// One note of the recorded grand piano (<see cref="PianoBank"/>): the nearest sampled note
/// is replayed at the right pitch (cubic interpolation, at most one semitone away), blending
/// the soft and medium velocity layers so the tone darkens as the touch gets lighter. When
/// the pianist changes the pedal, felt dampers stop the note in about a tenth of a second;
/// above G6 there are no dampers, as on a real instrument.
/// </summary>
internal sealed class PianoVoice
{
    private readonly PianoBank _bank;
    private readonly double _rateRatio;
    private short[] _soft = [];
    private short[] _hard = [];
    private float _softGain;
    private float _hardGain;
    private double _position;
    private double _step;
    private float _release = 1f;
    private float _releaseDecay = 1f;
    private float _fade = 1f;
    private bool _stealing;
    private (int Midi, float Velocity) _pending;

    public PianoVoice(int sampleRate)
    {
        _bank = PianoBank.Instance;
        _rateRatio = _bank.SampleRate / (double)sampleRate;
        _dampDecay = MathF.Exp(-1f / (0.09f * sampleRate));
        _stealStep = 1f / (0.004f * sampleRate);
    }

    private readonly float _dampDecay;
    private readonly float _stealStep;

    public bool Active { get; private set; }

    public int Midi { get; private set; }

    public float Pan { get; private set; }

    public void Strike(int midi, float velocity, ulong seed)
    {
        _ = seed;
        if (Active)
        {
            // A voice in use fades for 4 ms before the new note: no click.
            _stealing = true;
            _pending = (midi, velocity);
            return;
        }

        Begin(midi, velocity);
    }

    /// <summary>Felt dampers: the note dies in about a tenth of a second.</summary>
    public void Damp()
    {
        if (Active && Midi < 91)
        {
            _releaseDecay = _dampDecay;
        }
    }

    public float Next()
    {
        var index = (int)_position;
        if (index + 2 >= _soft.Length || _release < 1e-4f)
        {
            if (_stealing)
            {
                Begin(_pending.Midi, _pending.Velocity);
            }
            else
            {
                Active = false;
            }

            return 0f;
        }

        var frac = (float)(_position - index);
        var sample = (Hermite(_soft, index, frac) * _softGain) + (Hermite(_hard, index, frac) * _hardGain);
        sample *= _release;
        _release *= _releaseDecay;
        _position += _step;

        if (_stealing)
        {
            _fade -= _stealStep;
            sample *= MathF.Max(0f, _fade);
            if (_fade <= 0f)
            {
                Begin(_pending.Midi, _pending.Velocity);
            }
        }

        return sample;
    }

    private static float Hermite(short[] data, int i, float t)
    {
        const float scale = 1f / 32768f;
        var y0 = (i > 0 ? data[i - 1] : data[i]) * scale;
        var y1 = data[i] * scale;
        var y2 = data[i + 1] * scale;
        var y3 = data[i + 2] * scale;
        var c1 = 0.5f * (y2 - y0);
        var c2 = y0 - (2.5f * y1) + (2f * y2) - (0.5f * y3);
        var c3 = (0.5f * (y3 - y0)) + (1.5f * (y1 - y2));
        return ((((c3 * t) + c2) * t) + c1) * t + y1;
    }

    private void Begin(int midi, float velocity)
    {
        var sampled = _bank.Nearest(midi);
        var layers = _bank.Layers;
        _soft = _bank.Sample(sampled, layers[0]);
        _hard = _bank.Sample(sampled, layers[^1]);
        if (_hard.Length != _soft.Length)
        {
            _hard = _soft;
        }

        // The layers are normalised; loudness comes from a velocity curve, timbre from the blend.
        var v = Math.Clamp(velocity, 0f, 1f);
        var blend = Dsp.SmoothStep(0.3f, 0.65f, v);
        var gain = 0.5f * MathF.Pow(v, 1.5f);
        _softGain = gain * (1f - blend);
        _hardGain = gain * blend;
        _step = Math.Pow(2.0, (midi - sampled) / 12.0) * _rateRatio;
        _position = 0;
        _release = 1f;
        _releaseDecay = 1f;
        _fade = 1f;
        _stealing = false;
        Midi = midi;
        Pan = 0.3f + (0.4f * Math.Clamp((midi - 30) / 66f, 0f, 1f));
        Active = true;
    }
}

/// <summary>
/// One player of a string ensemble: an anti-aliased (polyBLEP) sawtooth with delayed vibrato
/// and slow random pitch drift, a bowed attack and release. The section shapes the body.
/// </summary>
internal struct StringPlayer
{
    public bool Active;
    public bool Releasing;
    public float Level;
    public double Phase;
    public double Hz;
    public float Pan;
    public float Age;
    public float VibratoRate;
    public float VibratoPhase;
    public float Drift;
    public float DriftTarget;

    public float Next(int sampleRate, float attack, float release, AudioRandom random)
    {
        var dt = 1f / sampleRate;
        Age += dt;
        Level = Releasing ? Level - (dt / release) : MathF.Min(1f, Level + (dt / attack));
        if (Level <= 0f)
        {
            Active = false;
            return 0f;
        }

        if (random.Chance(1f / 4000f))
        {
            DriftTarget = random.Range(-4f, 4f);
        }

        Drift += 0.00005f * (DriftTarget - Drift);
        VibratoPhase += VibratoRate * dt;
        var depth = 9f * Dsp.SmoothStep(0.4f, 1.8f, Age);
        var cents = Drift + (depth * MathF.Sin(Dsp.Tau * VibratoPhase));
        var increment = Hz * (1.0 + (cents * 0.000578)) / sampleRate;
        Phase += increment;
        if (Phase >= 1.0)
        {
            Phase -= 1.0;
        }

        var t = (float)Phase;
        var dtPhase = (float)increment;
        var saw = (2f * t) - 1f;

        // PolyBLEP correction at the wrap: removes the aliasing of a naive sawtooth.
        if (t < dtPhase)
        {
            var x = t / dtPhase;
            saw -= (x + x) - (x * x) - 1f;
        }
        else if (t > 1f - dtPhase)
        {
            var x = (t - 1f) / dtPhase;
            saw -= (x * x) + (x + x) + 1f;
        }

        var swell = Level * Level * (3f - (2f * Level));
        return saw * swell;
    }
}
