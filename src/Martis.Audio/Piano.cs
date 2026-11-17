namespace Martis.Audio;

/// <summary>
/// One piano note by modal (additive) synthesis, modelled on how a real piano sounds:
/// <list type="bullet">
/// <item>stretched partials from string stiffness (inharmonicity grows with the register);</item>
/// <item>a spectrum shaped by where the hammer strikes (1/7 of the string) and by velocity
/// (a harder blow is a shorter contact and a brighter tone);</item>
/// <item>one to three strings per note, slightly out of unison, so the tone beats and blooms;</item>
/// <item>double decay: a fast "prompt" sound and a long "aftersound" per partial;</item>
/// <item>a filtered noise thump from the hammer and felt dampers that stop the note when the
/// pedal lifts (above G6 there are no dampers, as on a real instrument).</item>
/// </list>
/// Oscillators are rotating phasors; silent partials leave the loop.
/// </summary>
internal sealed class PianoVoice
{
    private const int MaxModes = 28;
    private readonly int _sampleRate;
    private readonly float[] _re = new float[MaxModes];
    private readonly float[] _im = new float[MaxModes];
    private readonly float[] _cos = new float[MaxModes];
    private readonly float[] _sin = new float[MaxModes];
    private readonly float[] _fast = new float[MaxModes];
    private readonly float[] _slow = new float[MaxModes];
    private readonly float[] _fastDecay = new float[MaxModes];
    private readonly float[] _slowDecay = new float[MaxModes];
    private Biquad _thumpFilter = new(1, 0, 0, 1, 0, 0);
    private AudioRandom _noise = new(1);
    private int _count;
    private float _attack;
    private float _thump;
    private float _thumpDecay;
    private float _fade = 1f;
    private bool _stealing;
    private (int Midi, float Velocity, ulong Seed) _pending;

    public PianoVoice(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public bool Active { get; private set; }

    public int Midi { get; private set; }

    public float Pan { get; private set; }

    public void Strike(int midi, float velocity, ulong seed)
    {
        if (Active)
        {
            // A voice in use fades for 4 ms before the new note: no click.
            _stealing = true;
            _pending = (midi, velocity, seed);
            return;
        }

        Begin(midi, velocity, seed);
    }

    /// <summary>Felt dampers: the note dies in about a tenth of a second.</summary>
    public void Damp()
    {
        if (!Active || Midi >= 91)
        {
            return;
        }

        var decay = MathF.Exp(-1f / (0.09f * _sampleRate));
        for (var p = 0; p < _count; p++)
        {
            _fastDecay[p] = MathF.Min(_fastDecay[p], decay);
            _slowDecay[p] = MathF.Min(_slowDecay[p], decay);
        }
    }

    public float Next()
    {
        var sum = 0f;
        var alive = 0f;
        for (var p = 0; p < _count; p++)
        {
            var re = _re[p];
            var im = _im[p];
            _re[p] = (re * _cos[p]) - (im * _sin[p]);
            _im[p] = (re * _sin[p]) + (im * _cos[p]);
            var amplitude = _fast[p] + _slow[p];
            sum += _im[p] * amplitude;
            _fast[p] *= _fastDecay[p];
            _slow[p] *= _slowDecay[p];
            alive += amplitude;
            if (amplitude < 2e-6f)
            {
                Remove(p);
                p--;
            }
        }

        _attack = MathF.Min(1f, _attack + (1f / (0.0015f * _sampleRate)));
        var sample = sum * _attack;
        if (_thump > 1e-5f)
        {
            sample += _thumpFilter.Process(_noise.Noise()) * _thump;
            _thump *= _thumpDecay;
        }

        if (_stealing)
        {
            _fade -= 1f / (0.004f * _sampleRate);
            sample *= MathF.Max(0f, _fade);
            if (_fade <= 0f)
            {
                Begin(_pending.Midi, _pending.Velocity, _pending.Seed);
            }
        }
        else if (alive < 1e-4f && _thump <= 1e-5f)
        {
            Active = false;
        }

        return sample;
    }

    private void Remove(int p)
    {
        _count--;
        _re[p] = _re[_count];
        _im[p] = _im[_count];
        _cos[p] = _cos[_count];
        _sin[p] = _sin[_count];
        _fast[p] = _fast[_count];
        _slow[p] = _slow[_count];
        _fastDecay[p] = _fastDecay[_count];
        _slowDecay[p] = _slowDecay[_count];
    }

    private void Begin(int midi, float velocity, ulong seed)
    {
        var random = new AudioRandom(seed);
        _stealing = false;
        _fade = 1f;
        Midi = midi;
        var hertz = Pitch.Hertz(midi);
        var register = Math.Clamp((midi - 21) / 87f, 0f, 1f);

        // Stiffer, shorter strings in the treble stretch the partials more.
        var inharmonicity = 0.00012 * Math.Pow(2.0, (midi - 48) / 14.0);
        var sustain = 9f * MathF.Pow(0.32f, register * 2.2f) + 0.6f;
        var contact = 500f + (4200f * velocity * velocity);
        var strings = midi < 34 ? 1 : midi < 46 ? 2 : 3;
        var gain = 0.11f * MathF.Pow(velocity, 1.4f);
        _count = 0;
        for (var n = 1; n <= 18 && _count < MaxModes; n++)
        {
            var f = n * hertz * Math.Sqrt(1.0 + (inharmonicity * n * n));
            if (f > 11000)
            {
                break;
            }

            var strike = MathF.Abs(MathF.Sin(MathF.PI * n / 7f)) + 0.04f;
            var felt = 1f / (1f + MathF.Pow((float)f / contact, 2f));
            var amplitude = gain * strike * felt / MathF.Pow(n, 0.55f);
            var unison = n <= 4 ? strings : 1;
            for (var s = 0; s < unison && _count < MaxModes; s++)
            {
                var cents = unison == 1 ? 0f : (s - ((unison - 1) / 2f)) * random.Range(0.5f, 1.3f);
                var w = 2.0 * Math.PI * f * Math.Pow(2.0, cents / 1200.0) / _sampleRate;
                var slowSeconds = sustain / (1f + (0.45f * (n - 1)));
                var share = 0.55f + (0.25f * register);
                _re[_count] = 1f;
                _im[_count] = 0f;
                _cos[_count] = (float)Math.Cos(w);
                _sin[_count] = (float)Math.Sin(w);
                _fast[_count] = amplitude * share / unison;
                _slow[_count] = amplitude * (1f - share) / unison;
                _fastDecay[_count] = MathF.Exp(-1f / (slowSeconds * 0.12f * _sampleRate));
                _slowDecay[_count] = MathF.Exp(-1f / (slowSeconds * _sampleRate));
                _count++;
            }
        }

        _noise = new AudioRandom(seed ^ 0x7468756D70UL);
        _thumpFilter = Biquad.BandPass(Math.Clamp(hertz * 2.5, 250, 3500), 0.9, _sampleRate);
        _thump = 0.05f * velocity * velocity;
        _thumpDecay = MathF.Exp(-1f / (0.007f * _sampleRate));
        _attack = 0f;
        Pan = 0.25f + (0.5f * register);
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
