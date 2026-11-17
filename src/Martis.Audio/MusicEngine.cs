namespace Martis.Audio;

/// <summary>
/// Generative soundtrack (ADR-018): calm solo piano in the spirit of quiet sandbox-game
/// scores. The piano plays short phrases of a few bars (a wandering melody over a sparse or
/// broken-chord left hand) separated by rests, over a soft pad and a low drone, all in a warm
/// reverb. Every decision happens on the sample clock, so the output depends only on the seed
/// and on when moods change, never on the buffer size.
/// </summary>
public sealed class MusicEngine
{
    public const int SampleRate = 44100;
    private const int PadVoices = 12;
    private const int PianoVoices = 28;
    private const int Partials = 8;
    private const int MaxNotesPerPhrase = 96;
    private const float PadAttack = 3.5f;
    private const float PadRelease = 5f;

    private static readonly double[] Detune = [0.9975, 1.0, 1.0028];

    private readonly AudioRandom _random;
    private readonly PadVoice[] _pads = new PadVoice[PadVoices];
    private readonly PianoVoice[] _piano = new PianoVoice[PianoVoices];
    private readonly Reverb _reverb = new(SampleRate, room: 0.88f, damping: 0.45f);
    private readonly float _smooth = Dsp.Smoothing(2.5f, SampleRate);
    private readonly Note[] _phrase = new Note[MaxNotesPerPhrase];
    private MusicStyle _style;
    private MusicStyle _next;
    private long _clock;
    private long _nextPhrase;
    private int _phraseLength;
    private int _phraseCursor;
    private long _phraseEnd;
    private int _chordIndex;
    private int _melody = 4;
    private int _nextVoice;
    private Svf _padLeft;
    private Svf _padRight;
    private float _cutoff;
    private float _padLevel;
    private float _droneLevel;
    private float _beatLevel;
    private double _dronePhase;
    private double _fifthPhase;
    private double _droneHz;
    private float _droneTarget;
    private float _beatAge = 10f;
    private long _nextBeat;
    private float _toneLeft;
    private float _toneRight;
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
        _droneLevel = _style.DroneLevel;
        _beatLevel = _style.HeartbeatLevel;
        _droneHz = Pitch.Hertz(_style.Root - 12);
        _droneTarget = (float)_droneHz;
        for (var v = 0; v < _piano.Length; v++)
        {
            _piano[v] = new PianoVoice();
        }

        // The first phrase enters after the pad has faded in.
        _nextPhrase = (long)(1.2 * SampleRate);
    }

    public MusicMood Mood { get; private set; }

    public string? Chapter { get; private set; }

    /// <summary>Piano notes struck so far.</summary>
    public int NotesPlayed { get; private set; }

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

        // During a rest the new mood starts soon; a phrase in progress is allowed to finish.
        if (_clock >= _phraseEnd)
        {
            _nextPhrase = Math.Min(_nextPhrase, _clock + (long)(1.5 * SampleRate));
        }
    }

    private long Beats(double beats) => (long)(beats * 60.0 / _style.Bpm * SampleRate);

    private void Tick(out float left, out float right)
    {
        if (_clock >= _nextPhrase)
        {
            ComposePhrase();
        }

        while (_phraseCursor < _phraseLength && _phrase[_phraseCursor].At <= _clock)
        {
            var note = _phrase[_phraseCursor++];
            if (note.Midi < 0)
            {
                ChangeChord(note.Degree);
            }
            else
            {
                Strike(note.Midi, note.Velocity);
            }
        }

        var target = _next;
        _cutoff += _smooth * (target.PadCutoffHz - _cutoff);
        _padLevel += _smooth * (target.PadLevel - _padLevel);
        _droneLevel += _smooth * (target.DroneLevel - _droneLevel);
        _beatLevel += _smooth * (target.HeartbeatLevel - _beatLevel);
        _droneHz += _smooth * 0.2 * (_droneTarget - _droneHz);

        var dt = 1f / SampleRate;
        var time = _clock / (float)SampleRate;

        // Piano.
        var pianoL = 0f;
        var pianoR = 0f;
        for (var v = 0; v < _piano.Length; v++)
        {
            var voice = _piano[v];
            if (!voice.Active)
            {
                continue;
            }

            var sample = voice.Next();
            pianoL += sample * (1f - voice.Pan);
            pianoR += sample * voice.Pan;
        }

        // Felt: a gentle one-pole low-pass takes the edge off the hammer.
        _toneLeft += 0.55f * (pianoL - _toneLeft);
        _toneRight += 0.55f * (pianoR - _toneRight);
        pianoL = _toneLeft;
        pianoR = _toneRight;

        // Pad: soft detuned saws through a breathing low-pass.
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

            var gain = pad.Level * pad.Level * 0.035f;
            padL += tone * gain * (1f - pad.Pan);
            padR += tone * gain * pad.Pan;
        }

        var lfo = 0.8f + (0.2f * MathF.Sin(Dsp.Tau * 0.04f * time));
        _padLeft.Process(padL, _cutoff * lfo, 0.8f, SampleRate);
        _padRight.Process(padR, _cutoff * lfo * 1.03f, 0.8f, SampleRate);
        padL = _padLeft.Low * _padLevel;
        padR = _padRight.Low * _padLevel;

        // Drone.
        _dronePhase = Wrap(_dronePhase + (_droneHz / SampleRate));
        _fifthPhase = Wrap(_fifthPhase + (_droneHz * 1.5 / SampleRate));
        var breath = 0.7f + (0.3f * MathF.Sin(Dsp.Tau * 0.06f * time));
        var drone = ((float)Math.Sin(Dsp.Tau * _dronePhase) + (0.3f * (float)Math.Sin(Dsp.Tau * _fifthPhase))) * 0.07f * _droneLevel * breath;

        // Heartbeat for the crisis theme: "lub" then "dub", once per bar.
        if (_beatLevel > 0.01f && _clock >= _nextBeat)
        {
            _beatAge = 0f;
            _nextBeat = _clock + (_secondBeat ? Beats(3.5) : Beats(0.5));
            _secondBeat = !_secondBeat;
        }

        _beatAge += dt;
        var beatHz = 52f * MathF.Exp(-_beatAge * 9f);
        var beat = MathF.Sin(Dsp.Tau * (38f + beatHz) * _beatAge) * Dsp.Percussive(_beatAge, 0.012f, 0.16f) * 0.28f * _beatLevel;

        var dryL = (pianoL * 0.9f) + padL + drone + beat;
        var dryR = (pianoR * 0.9f) + padR + drone + beat;
        _reverb.Process(pianoL + (padL * 0.7f), pianoR + (padR * 0.7f), out var wetL, out var wetR);

        _master = MathF.Min(1f, _master + (dt / 2f));
        var fade = _master * _master;
        left = Dsp.SoftClip((dryL + (wetL * 1.1f)) * 1.5f * fade);
        right = Dsp.SoftClip((dryR + (wetR * 1.1f)) * 1.5f * fade);
        _clock++;
    }

    private bool _secondBeat;

    /// <summary>
    /// Writes the next phrase: two to four bars of melody over the left hand, with chord
    /// changes on each bar, then schedules a rest before the following phrase.
    /// </summary>
    private void ComposePhrase()
    {
        _style = _next;
        _droneTarget = (float)Pitch.Hertz(_style.Root - 12);
        var style = _style;
        _phraseLength = 0;
        _phraseCursor = 0;
        var bars = 2 + _random.Next(3);
        var start = _clock;
        for (var bar = 0; bar < bars; bar++)
        {
            var barStart = start + Beats(4 * bar);
            var degree = style.Progression[_chordIndex % style.Progression.Length];
            _chordIndex++;
            Add(barStart, -1, 0f, degree);

            // Left hand.
            var bass = Pitch.Degree(style.Root, style.Mode, degree) - 12;
            switch (style.LeftHand)
            {
                case LeftHand.Sparse:
                    Add(barStart, bass, 0.45f);
                    if (_random.Chance(0.5f))
                    {
                        Add(barStart + Beats(2), Pitch.Degree(style.Root, style.Mode, degree + 4) - 12, 0.32f);
                    }

                    break;
                case LeftHand.Broken:
                    Add(barStart, bass, 0.48f);
                    Add(barStart + Beats(1), Pitch.Degree(style.Root, style.Mode, degree + 4) - 12, 0.34f);
                    Add(barStart + Beats(2), Pitch.Degree(style.Root, style.Mode, degree + 9) - 12, 0.32f);
                    break;
                default:
                    int[] pattern = [0, 4, 7, 9, 11, 9, 7, 4];
                    for (var step = 0; step < pattern.Length; step++)
                    {
                        Add(barStart + Beats(step * 0.5), Pitch.Degree(style.Root, style.Mode, degree + pattern[step]) - 12, step == 0 ? 0.45f : 0.28f);
                    }

                    break;
            }

            // Right hand: a melody that walks the scale and leans on chord tones on strong beats.
            var beat = 0.0;
            while (beat < 4.0)
            {
                double[] lengths = [1, 1, 2, 1.5, 0.5, 1, 3];
                var length = Math.Min(lengths[_random.Next(lengths.Length)], 4.0 - beat);
                if (_random.Chance(style.MelodyDensity))
                {
                    if (beat == 0.0)
                    {
                        int[] chordTones = [0, 2, 4, 7];
                        var tone = degree + chordTones[_random.Next(chordTones.Length)];
                        _melody = tone - (7 * (int)Math.Floor((tone - _melody + 3.5) / 7.0));
                    }
                    else
                    {
                        int[] steps = [-2, -1, -1, 1, 1, 2, -3, 3, 0];
                        _melody += steps[_random.Next(steps.Length)];
                    }

                    _melody = Math.Clamp(_melody, 0, 11);
                    var midi = Pitch.Degree(style.Root, style.Mode, _melody) + (12 * style.MelodyOctave);
                    var humanise = (long)(_random.Range(-0.012f, 0.012f) * SampleRate);
                    Add(barStart + Beats(beat) + Math.Max(0, humanise), midi, _random.Range(0.42f, 0.66f));
                }

                beat += length;
            }
        }

        // A final, longer chord tone closes the phrase.
        var end = start + Beats(4 * bars);
        Add(end, Pitch.Degree(style.Root, style.Mode, style.Progression[0]) + (12 * style.MelodyOctave), 0.4f);
        Array.Sort(_phrase, 0, _phraseLength, NoteOrder.Instance);
        _phraseEnd = end + Beats(2);
        _nextPhrase = _phraseEnd + (long)(style.RestSeconds * _random.Range(0.7f, 1.4f) * SampleRate);
    }

    private void Add(long at, int midi, float velocity, int degree = 0)
    {
        if (_phraseLength < _phrase.Length)
        {
            _phrase[_phraseLength++] = new Note(at, midi, velocity, degree);
        }
    }

    private void ChangeChord(int degree)
    {
        for (var v = 0; v < _pads.Length; v++)
        {
            _pads[v].Releasing = true;
        }

        int[] tones = [0, 2, 4];
        for (var t = 0; t < tones.Length; t++)
        {
            var midi = Pitch.Degree(_style.Root, _style.Mode, degree + tones[t]) + (t == 0 ? 0 : 12);
            StartPad(Pitch.Hertz(midi), 0.3f + (0.4f * t / (tones.Length - 1)));
        }
    }

    private void Strike(int midi, float velocity)
    {
        var voice = _piano[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _piano.Length;
        voice.Start(midi, velocity);
        NotesPlayed++;
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
        if (!pad.Active)
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

    private static double Wrap(double phase) => phase - Math.Floor(phase);

    private readonly record struct Note(long At, int Midi, float Velocity, int Degree);

    private sealed class NoteOrder : IComparer<Note>
    {
        public static readonly NoteOrder Instance = new();

        public int Compare(Note x, Note y) => x.At.CompareTo(y.At);
    }

    private struct PadVoice
    {
        public bool Active;
        public bool Releasing;
        public float Level;
        public double Hz;
        public float Pan;
        public double[] Phase;
    }

    /// <summary>
    /// Additive piano: eight slightly stretched partials (string inharmonicity), each with its
    /// own decay, a quick hammer "punch" that fades into the long tail, and two strings per
    /// note for the gentle beating of a real instrument. Oscillators are rotating phasors.
    /// </summary>
    private sealed class PianoVoice
    {
        private const double Inharmonicity = 0.00035;
        private readonly float[] _re = new float[Partials * 2];
        private readonly float[] _im = new float[Partials * 2];
        private readonly float[] _cos = new float[Partials * 2];
        private readonly float[] _sin = new float[Partials * 2];
        private readonly float[] _amp = new float[Partials * 2];
        private readonly float[] _decay = new float[Partials * 2];
        private float _attack;
        private float _punch;
        private float _punchDecay;
        private float _fadeOut = 1f;
        private bool _stealing;
        private int _pendingMidi;
        private float _pendingVelocity;
        private int _count;

        public bool Active { get; private set; }

        public float Pan { get; private set; }

        public void Start(int midi, float velocity)
        {
            if (Active)
            {
                // Steal gracefully: a 5 ms fade, then the new note.
                _stealing = true;
                _pendingMidi = midi;
                _pendingVelocity = velocity;
                return;
            }

            Begin(midi, velocity);
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
                sum += _im[p] * _amp[p];
                _amp[p] *= _decay[p];
                alive += _amp[p];
            }

            // Silent partials leave the loop: the last one takes their slot.
            if (_count > 0 && _amp[_count - 1] < 1e-5f)
            {
                _count--;
            }

            _attack = MathF.Min(1f, _attack + (1f / (0.003f * SampleRate)));
            _punch *= _punchDecay;
            var sample = sum * _attack * (1f + _punch);
            if (_stealing)
            {
                _fadeOut -= 1f / (0.005f * SampleRate);
                sample *= MathF.Max(0f, _fadeOut);
                if (_fadeOut <= 0f)
                {
                    Begin(_pendingMidi, _pendingVelocity);
                }
            }
            else if (alive < 4e-4f)
            {
                Active = false;
            }

            return sample;
        }

        private void Begin(int midi, float velocity)
        {
            _stealing = false;
            _fadeOut = 1f;
            var hertz = Pitch.Hertz(midi);
            var register = Math.Clamp((midi - 36) / 60f, 0f, 1f);
            var sustain = 1.6f + (5f * (1f - register));
            var brightness = 0.35f + (0.45f * velocity);
            _count = 0;
            for (var n = 1; n <= Partials; n++)
            {
                var stretch = Math.Sqrt(1.0 + (Inharmonicity * n * n));
                // Two detuned strings for the low partials (beating), one above.
                for (var s = 0; s < (n <= 3 ? 2 : 1); s++)
                {
                    var f = n * hertz * stretch * (n > 3 ? 1.0 : s == 0 ? 0.9996 : 1.0004);
                    if (f > 9000)
                    {
                        continue;
                    }

                    var amp = (n <= 3 ? 1f : 2f) * velocity * 0.075f * MathF.Pow(brightness, n - 1) / MathF.Pow(n, 0.6f);
                    var seconds = sustain / (1f + (0.7f * (n - 1)));
                    var w = 2.0 * Math.PI * f / SampleRate;
                    _re[_count] = 1f;
                    _im[_count] = 0f;
                    _cos[_count] = (float)Math.Cos(w);
                    _sin[_count] = (float)Math.Sin(w);
                    _amp[_count] = amp;
                    _decay[_count] = MathF.Exp(-1f / (seconds * SampleRate));
                    _count++;
                }
            }

            _attack = 0f;
            _punch = 1.2f;
            _punchDecay = MathF.Exp(-1f / (0.09f * SampleRate));
            Pan = 0.3f + (0.4f * register);
            Active = true;
        }
    }
}
