namespace Martis.Audio;

/// <summary>
/// Generative ambient soundtrack (ADR-018). Streams stereo samples: slow detuned pads that
/// follow a modal chord progression, a sub drone, FM bells with a ping-pong delay, an optional
/// arpeggio or heartbeat, all into a stereo reverb. Every decision happens on the sample clock,
/// so the output depends only on the seed and on when moods change, never on the buffer size.
/// </summary>
public sealed class MusicEngine
{
    public const int SampleRate = 44100;
    private const int PadVoices = 16;
    private const int BellVoices = 12;
    private const float PadAttack = 2.6f;
    private const float PadRelease = 4.5f;

    private readonly AudioRandom _random;
    private readonly PadVoice[] _pads = new PadVoice[PadVoices];
    private readonly BellVoice[] _bells = new BellVoice[BellVoices];
    private readonly Reverb _reverb = new(SampleRate, room: 0.86f, damping: 0.3f);
    private readonly PingPong _delay;
    private readonly float _smooth = Dsp.Smoothing(2.5f, SampleRate);
    private MusicStyle _style;
    private MusicStyle _next;
    private long _clock;
    private long _nextChord;
    private long _nextStep;
    private int _step;
    private int _chordIndex;
    private int _chordRoot;
    private int _nextBell;
    private Svf _padLeft;
    private Svf _padRight;
    private float _cutoff;
    private float _padLevel;
    private float _bellDensity;
    private float _droneLevel;
    private float _pulseLevel;
    private float _arpLevel;
    private float _beatLevel;
    private double _dronePhase;
    private double _fifthPhase;
    private double _droneHz;
    private float _droneTarget;
    private double _arpPhase;
    private float _arpHz;
    private float _arpAge = 10f;
    private float _beatAge = 10f;
    private float _master;

    public MusicEngine(ulong seed, MusicMood mood, string? chapter = null)
    {
        _random = new AudioRandom(seed ^ 0x6D75736963UL);
        Mood = mood;
        Chapter = chapter;
        _style = MusicStyle.For(mood, chapter);
        _next = _style;
        _cutoff = _style.PadCutoffHz;
        _padLevel = _style.PadLevel;
        _bellDensity = _style.BellDensity;
        _droneLevel = _style.DroneLevel;
        _pulseLevel = _style.PulseLevel;
        _arpLevel = _style.Pulse == PulseKind.Arpeggio ? 1f : 0f;
        _beatLevel = _style.Pulse == PulseKind.Heartbeat ? 1f : 0f;
        _droneHz = Pitch.Hertz(_style.Root - 12);
        _droneTarget = (float)_droneHz;
        _delay = new PingPong((int)(SampleRate * 0.75 * 60.0 / _style.Bpm));
    }

    public MusicMood Mood { get; private set; }

    public string? Chapter { get; private set; }

    public void SetMood(MusicMood mood)
    {
        if (mood == Mood)
        {
            return;
        }

        Mood = mood;
        Retarget();
    }

    public void SetChapter(string? chapter)
    {
        if (chapter == Chapter)
        {
            return;
        }

        Chapter = chapter;
        Retarget();
    }

    /// <summary>Fills an interleaved stereo buffer (L, R, L, R…).</summary>
    public void Render(Span<float> interleaved)
    {
        for (var i = 0; i + 1 < interleaved.Length; i += 2)
        {
            Tick(out var left, out var right);
            interleaved[i] = left;
            interleaved[i + 1] = right;
        }
    }

    private void Retarget()
    {
        _next = MusicStyle.For(Mood, Chapter);

        // The new harmony arrives at the next chord change, at most a second and a half away.
        _nextChord = Math.Min(_nextChord, _clock + (long)(1.5 * SampleRate));
    }

    private long Samples(double beats) => (long)(beats * 60.0 / _style.Bpm * SampleRate);

    private void Tick(out float left, out float right)
    {
        if (_clock >= _nextChord)
        {
            ChangeChord();
        }

        if (_clock >= _nextStep)
        {
            Step();
        }

        // Layer levels glide toward the target style: this is the crossfade between moods.
        var target = _next;
        _cutoff += _smooth * (target.PadCutoffHz - _cutoff);
        _padLevel += _smooth * (target.PadLevel - _padLevel);
        _bellDensity += _smooth * (target.BellDensity - _bellDensity);
        _droneLevel += _smooth * (target.DroneLevel - _droneLevel);
        _pulseLevel += _smooth * (target.PulseLevel - _pulseLevel);
        _arpLevel += _smooth * ((target.Pulse == PulseKind.Arpeggio ? 1f : 0f) - _arpLevel);
        _beatLevel += _smooth * ((target.Pulse == PulseKind.Heartbeat ? 1f : 0f) - _beatLevel);
        _droneHz += _smooth * 0.2 * (_droneTarget - _droneHz);

        var dt = 1f / SampleRate;
        var time = _clock / (float)SampleRate;

        // Pads: three detuned saws per note, summed per side, then a breathing low-pass.
        var padL = 0f;
        var padR = 0f;
        for (var v = 0; v < _pads.Length; v++)
        {
            ref var pad = ref _pads[v];
            if (!pad.Active)
            {
                continue;
            }

            pad.Level = pad.Releasing
                ? pad.Level - (dt / PadRelease)
                : MathF.Min(1f, pad.Level + (dt / PadAttack));
            if (pad.Level <= 0f)
            {
                pad.Active = false;
                continue;
            }

            var tone = 0f;
            for (var o = 0; o < 3; o++)
            {
                pad.Phase[o] += pad.Hz * Detune[o] / SampleRate;
                pad.Phase[o] -= Math.Floor(pad.Phase[o]);
                tone += (float)((2.0 * pad.Phase[o]) - 1.0);
            }

            var gain = pad.Level * pad.Level * 0.06f;
            padL += tone * gain * (1f - pad.Pan);
            padR += tone * gain * pad.Pan;
        }

        var lfo = 0.75f + (0.25f * MathF.Sin(Dsp.Tau * 0.05f * time));
        _padLeft.Process(padL, _cutoff * lfo, 0.9f, SampleRate);
        _padRight.Process(padR, _cutoff * lfo * 1.03f, 0.9f, SampleRate);
        padL = _padLeft.Low * _padLevel;
        padR = _padRight.Low * _padLevel;

        // Drone: root an octave down plus a quiet fifth, breathing slowly.
        _dronePhase = Wrap(_dronePhase + (_droneHz / SampleRate));
        _fifthPhase = Wrap(_fifthPhase + (_droneHz * 1.5 / SampleRate));
        var breath = 0.7f + (0.3f * MathF.Sin(Dsp.Tau * 0.07f * time));
        var drone = ((float)Math.Sin(Dsp.Tau * _dronePhase) + (0.3f * (float)Math.Sin(Dsp.Tau * _fifthPhase))) * 0.09f * _droneLevel * breath;

        // Bells.
        var bellL = 0f;
        var bellR = 0f;
        for (var b = 0; b < _bells.Length; b++)
        {
            ref var bell = ref _bells[b];
            if (!bell.Active)
            {
                continue;
            }

            bell.Age += dt;
            var envelope = Dsp.Percussive(bell.Age, 0.004f, bell.Decay);
            if (bell.Age > 0.01f && envelope < 1e-4f)
            {
                bell.Active = false;
                continue;
            }

            bell.ModPhase = Wrap(bell.ModPhase + (bell.Hz * 3.5 / SampleRate));
            bell.Phase = Wrap(bell.Phase + (bell.Hz / SampleRate));
            var modulation = 1.3f * envelope * (float)Math.Sin(Dsp.Tau * bell.ModPhase);
            var sample = envelope * (float)Math.Sin((Dsp.Tau * bell.Phase) + modulation) * bell.Gain;
            bellL += sample * (1f - bell.Pan);
            bellR += sample * bell.Pan;
        }

        _delay.Process(bellL * 0.6f, bellR * 0.6f, out var echoL, out var echoR);
        bellL += echoL;
        bellR += echoR;

        // Pulse layer: soft plucked arpeggio or a low heartbeat.
        _arpAge += dt;
        _arpPhase = Wrap(_arpPhase + (_arpHz / SampleRate));
        var arp = (float)Math.Sin(Dsp.Tau * _arpPhase) * Dsp.Percussive(_arpAge, 0.003f, 0.18f) * 0.12f * _arpLevel * _pulseLevel;
        _beatAge += dt;
        var beatHz = 52f * MathF.Exp(-_beatAge * 9f);
        var beat = MathF.Sin(Dsp.Tau * (38f + beatHz) * _beatAge) * Dsp.Percussive(_beatAge, 0.012f, 0.16f) * 0.3f * _beatLevel * _pulseLevel;

        var dryL = padL + drone + bellL + (arp * 0.8f) + beat;
        var dryR = padR + drone + bellR + (arp * 1.2f) + beat;
        _reverb.Process(padL + bellL + arp, padR + bellR + arp, out var wetL, out var wetR);

        // Master fade-in over the first two seconds and a soft limiter.
        _master = MathF.Min(1f, _master + (dt / 2f));
        var fade = _master * _master;
        left = Dsp.SoftClip((dryL + (wetL * 0.9f)) * 1.6f * fade);
        right = Dsp.SoftClip((dryR + (wetR * 0.9f)) * 1.6f * fade);
        _clock++;
    }

    private static readonly double[] Detune = [0.9965, 1.0, 1.0042];

    private void ChangeChord()
    {
        var styleChanged = !ReferenceEquals(_style, _next);
        _style = _next;
        if (styleChanged)
        {
            _chordIndex = 0;
            _droneTarget = (float)Pitch.Hertz(_style.Root - 12);
        }
        else
        {
            _chordIndex = (_chordIndex + 1) % _style.Progression.Length;
        }

        // An occasional substitution keeps long sessions from looping audibly.
        var degree = _random.Chance(0.2f)
            ? _style.Progression[_random.Next(_style.Progression.Length)] + 2
            : _style.Progression[_chordIndex];
        _chordRoot = degree;

        for (var v = 0; v < _pads.Length; v++)
        {
            _pads[v].Releasing = true;
        }

        // Root, third, fifth and a colour tone (seventh or ninth), spread across two octaves.
        int[] tones = [0, 2, 4, _random.Chance(0.5f) ? 6 : 8];
        for (var t = 0; t < tones.Length; t++)
        {
            var midi = Pitch.Degree(_style.Root, _style.Mode, degree + tones[t]) + (t == 0 ? 0 : 12);
            StartPad(Pitch.Hertz(midi), 0.25f + (0.5f * t / (tones.Length - 1)));
        }

        _nextChord = _clock + Samples(8);
    }

    private void StartPad(double hertz, float pan)
    {
        var slot = 0;
        var quietest = float.MaxValue;
        for (var v = 0; v < _pads.Length; v++)
        {
            if (!_pads[v].Active)
            {
                slot = v;
                break;
            }

            if (_pads[v].Releasing && _pads[v].Level < quietest)
            {
                quietest = _pads[v].Level;
                slot = v;
            }
        }

        ref var pad = ref _pads[slot];
        if (pad.Active)
        {
            // Stealing: keep the old level so the voice fades instead of jumping.
            pad.Releasing = false;
        }
        else
        {
            pad.Level = 0f;
        }

        pad.Active = true;
        pad.Releasing = false;
        pad.Hz = hertz;
        pad.Pan = pan;
        pad.Phase ??= new double[3];
        for (var o = 0; o < 3; o++)
        {
            pad.Phase[o] = _random.NextFloat();
        }
    }

    private void Step()
    {
        // Eighth-note grid.
        _nextStep = _clock + Samples(0.5);
        _step++;
        if (_random.Chance(_bellDensity))
        {
            var octave = 12 * _next.BellOctave;
            var degree = _chordRoot + (2 * _random.Next(4)) + (_random.Chance(0.3f) ? 1 : 0);
            ref var bell = ref _bells[_nextBell];
            _nextBell = (_nextBell + 1) % _bells.Length;
            bell.Active = true;
            bell.Age = 0f;
            bell.Phase = 0;
            bell.ModPhase = 0;
            bell.Hz = Pitch.Hertz(Pitch.Degree(_style.Root, _style.Mode, degree) + octave);
            bell.Decay = _random.Range(0.6f, 1.6f);
            bell.Gain = _random.Range(0.05f, 0.11f);
            bell.Pan = _random.Range(0.15f, 0.85f);
        }

        if (_arpLevel > 0.01f)
        {
            int[] pattern = [0, 2, 4, 7, 4, 2];
            _arpHz = (float)Pitch.Hertz(Pitch.Degree(_style.Root, _style.Mode, _chordRoot + pattern[_step % pattern.Length]) + 12);
            _arpAge = 0f;
            _arpPhase = 0;
        }

        // Heartbeat: "lub" on beat one, "dub" an eighth later.
        if (_beatLevel > 0.01f && (_step % 8 == 0 || _step % 8 == 1))
        {
            _beatAge = 0f;
        }
    }

    private static double Wrap(double phase) => phase - Math.Floor(phase);

    private struct PadVoice
    {
        public bool Active;
        public bool Releasing;
        public float Level;
        public double Hz;
        public float Pan;
        public double[] Phase;
    }

    private struct BellVoice
    {
        public bool Active;
        public float Age;
        public double Phase;
        public double ModPhase;
        public double Hz;
        public float Decay;
        public float Gain;
        public float Pan;
    }
}
