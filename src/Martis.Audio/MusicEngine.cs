namespace Martis.Audio;

/// <summary>
/// Generative soundtrack (ADR-018): calm solo grand piano (a recorded Steinway, see <see cref="PianoBank"/>) in the spirit of quiet sandbox-game
/// scores. The piano plays short phrases of a few bars (a wandering melody over a sparse or
/// broken-chord left hand) separated by rests, over a soft bowed string section and a low
/// drone, in a concert-hall reverb, through a mastering chain (EQ, glue compression, width,
/// a true lookahead limiter at −1 dBFS) calibrated to <see cref="TargetLufs"/>.
/// Every decision happens on the sample clock, so the output depends only on the seed and on
/// when moods change, never on the buffer size.
/// </summary>
public sealed class MusicEngine
{
    public const int SampleRate = 44100;

    /// <summary>Integrated loudness the soundtrack is mastered to (ITU-R BS.1770 / EBU R128).
    /// Quiet and dynamic on purpose: background music under −16 LUFS effects.</summary>
    public const double TargetLufs = -18;

    private const int PianoVoices = 32;
    private const int StringPlayers = 18;
    private const int MaxNotesPerPhrase = 96;
    private const float StringAttack = 2.8f;
    private const float StringRelease = 3f;

    private readonly AudioRandom _random;
    private readonly AudioRandom _performance;
    private readonly PianoVoice[] _piano = new PianoVoice[PianoVoices];
    private readonly StringPlayer[] _strings = new StringPlayer[StringPlayers];
    private readonly float _smooth = Dsp.Smoothing(2.5f, SampleRate);
    private readonly Note[] _phrase = new Note[MaxNotesPerPhrase];
    private readonly MasterBus _bus = new(SampleRate, reverbSeconds: 2.2f);
    private readonly Section _sectionLeft = new(SampleRate);
    private readonly Section _sectionRight = new(SampleRate);
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
    private ulong _strikes;
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
    private bool _secondBeat;
    private float _master;
    private float _trim;

    public MusicEngine(ulong seed, MusicMood mood, string? chapter = null)
    {
        _random = new AudioRandom(seed ^ 0x6D75736963UL);
        _performance = new AudioRandom(seed ^ 0x706C6179UL);
        Mood = mood;
        Chapter = chapter;
        _style = MusicStyle.For(mood, chapter);
        _next = _style;
        _cutoff = _style.PadCutoffHz;
        _padLevel = _style.PadLevel;
        _droneLevel = _style.DroneLevel;
        _beatLevel = _style.HeartbeatLevel;
        _trim = CueTrim(mood);
        _droneHz = Pitch.Hertz(_style.Root - 12);
        _droneTarget = (float)_droneHz;
        for (var v = 0; v < _piano.Length; v++)
        {
            _piano[v] = new PianoVoice(SampleRate);
        }

        // The first phrase enters after the strings have faded in.
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

        // The strings breathe with the piano: they let go when the phrase ends.
        if (_clock == _phraseEnd)
        {
            for (var v = 0; v < _strings.Length; v++)
            {
                _strings[v].Releasing = true;
            }
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
        _trim += _smooth * (CueTrim(Mood) - _trim);
        _bus.Trim = _trim;

        var dt = 1f / SampleRate;
        var time = _clock / (float)SampleRate;

        // Piano: the recorded Steinway, panned by register as heard from the bench.
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
            var pan = voice.Pan;
            pianoL += sample * MathF.Sqrt(1f - pan);
            pianoR += sample * MathF.Sqrt(pan);
        }

        // Strings: players into two section buses that model the instruments' bodies.
        var stringsL = 0f;
        var stringsR = 0f;
        for (var v = 0; v < _strings.Length; v++)
        {
            ref var player = ref _strings[v];
            if (!player.Active)
            {
                continue;
            }

            var sample = player.Next(SampleRate, StringAttack, StringRelease, _performance) * 0.012f;
            stringsL += sample * (1f - player.Pan);
            stringsR += sample * player.Pan;
        }

        stringsL = _sectionLeft.Process(stringsL, _cutoff) * _padLevel;
        stringsR = _sectionRight.Process(stringsR, _cutoff * 1.04f) * _padLevel;

        // Drone: a low sine and its fifth, breathing slowly.
        _dronePhase = Wrap(_dronePhase + (_droneHz / SampleRate));
        _fifthPhase = Wrap(_fifthPhase + (_droneHz * 1.5 / SampleRate));
        var breath = 0.7f + (0.3f * MathF.Sin(Dsp.Tau * 0.06f * time));
        var drone = ((float)Math.Sin(Dsp.Tau * _dronePhase) + (0.3f * (float)Math.Sin(Dsp.Tau * _fifthPhase))) * 0.05f * _droneLevel * breath;

        // Heartbeat for the crisis theme: "lub" then "dub", once per bar.
        if (_beatLevel > 0.01f && _clock >= _nextBeat)
        {
            _beatAge = 0f;
            _nextBeat = _clock + (_secondBeat ? Beats(3.5) : Beats(0.5));
            _secondBeat = !_secondBeat;
        }

        _beatAge += dt;
        var beatHz = 52f * MathF.Exp(-_beatAge * 9f);
        var beat = MathF.Sin(Dsp.Tau * (38f + beatHz) * _beatAge) * Dsp.Percussive(_beatAge, 0.012f, 0.16f) * 0.15f * _beatLevel;

        _master = MathF.Min(1f, _master + (dt / 2f));
        var fade = _master * _master;
        _bus.Process(
            (pianoL + stringsL + drone + beat) * fade,
            (pianoR + stringsR + drone + beat) * fade,
            (pianoL + (stringsL * 0.8f)) * fade,
            (pianoR + (stringsR * 0.8f)) * fade,
            out left,
            out right);
        _clock++;
    }

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
            _phrase[_phraseLength] = new Note(at, midi, velocity, degree, _phraseLength);
            _phraseLength++;
        }
    }

    private void ChangeChord(int degree)
    {
        // The pianist changes the pedal with the harmony: notes still ringing are damped.
        for (var v = 0; v < _piano.Length; v++)
        {
            _piano[v].Damp();
        }

        for (var v = 0; v < _strings.Length; v++)
        {
            _strings[v].Releasing = true;
        }

        // Three chord tones, each played by a desk of three players slightly apart in pitch,
        // position and vibrato: the beating between them is what makes a section sound wide.
        int[] tones = [0, 2, 4];
        for (var t = 0; t < tones.Length; t++)
        {
            var midi = Pitch.Degree(_style.Root, _style.Mode, degree + tones[t]) + (t == 0 ? 0 : 12);
            var pan = 0.3f + (0.4f * t / (tones.Length - 1));
            for (var desk = 0; desk < 3; desk++)
            {
                // The composition consumes the same numbers as before, so melodies stay put.
                _random.NextFloat();
                StartPlayer(Pitch.Hertz(midi), pan + ((desk - 1) * 0.12f), desk);
            }
        }
    }

    private void Strike(int midi, float velocity)
    {
        var voice = _piano[_nextVoice];
        _nextVoice = (_nextVoice + 1) % _piano.Length;
        voice.Strike(midi, velocity, 0x9E3779B97F4A7C15UL * ++_strikes);
        NotesPlayed++;
    }

    private void StartPlayer(double hertz, float pan, int desk)
    {
        var slot = 0;
        var quietest = float.MaxValue;
        for (var v = 0; v < _strings.Length; v++)
        {
            if (!_strings[v].Active)
            {
                slot = v;
                quietest = -1f;
                break;
            }

            if (_strings[v].Releasing && _strings[v].Level < quietest)
            {
                quietest = _strings[v].Level;
                slot = v;
            }
        }

        ref var player = ref _strings[slot];
        if (!player.Active)
        {
            player.Level = 0f;
            player.Phase = _performance.NextFloat();
        }

        player.Active = true;
        player.Releasing = false;
        player.Age = 0f;
        player.Hz = hertz * Math.Pow(2.0, (desk - 1) * _performance.Range(3f, 6f) / 1200.0);
        player.Pan = Math.Clamp(pan, 0.05f, 0.95f);
        player.VibratoRate = _performance.Range(4.6f, 5.8f);
        player.VibratoPhase = _performance.NextFloat();
        player.Drift = _performance.Range(-3f, 3f);
        player.DriftTarget = player.Drift;
    }

    /// <summary>
    /// Mastering trim per cue, measured with <see cref="LoudnessMeter"/> so that every mood
    /// sits at <see cref="TargetLufs"/>: the crisis theme is dense, the night theme sparse.
    /// </summary>
    private static float CueTrim(MusicMood mood) => mood switch
    {
        MusicMood.Title => Gain(18.9f),
        MusicMood.Day => Gain(19f),
        MusicMood.Night => Gain(19.4f),
        MusicMood.Thriving => Gain(17.1f),
        MusicMood.Crisis => Gain(14f),
        _ => Gain(17.4f),
    };

    private static float Gain(float decibels) => MathF.Pow(10f, decibels / 20f);

    private static double Wrap(double phase) => phase - Math.Floor(phase);

    private readonly record struct Note(long At, int Midi, float Velocity, int Degree, int Order);

    private sealed class NoteOrder : IComparer<Note>
    {
        public static readonly NoteOrder Instance = new();

        // Chord changes (the pedal) come before notes struck at the same moment; otherwise
        // the order of writing is kept, so the sort is stable.
        public int Compare(Note x, Note y)
        {
            var at = x.At.CompareTo(y.At);
            if (at != 0)
            {
                return at;
            }

            var pedal = (x.Midi < 0 ? 0 : 1).CompareTo(y.Midi < 0 ? 0 : 1);
            return pedal != 0 ? pedal : x.Order.CompareTo(y.Order);
        }
    }

    /// <summary>
    /// The resonances of a string section's bodies: a gentle high-pass, three formant
    /// peaks (air resonance near 280 Hz, wood near 1.1 kHz, bridge "presence" near 2.6 kHz)
    /// and a low-pass that follows the mood.
    /// </summary>
    private sealed class Section(int sampleRate)
    {
        private readonly Biquad _rumble = Biquad.HighPass(110, 0.7, sampleRate);
        private readonly Biquad _air = Biquad.Peaking(280, 1.4, 4, sampleRate);
        private readonly Biquad _wood = Biquad.Peaking(1100, 1.8, 3, sampleRate);
        private readonly Biquad _bridge = Biquad.Peaking(2600, 2.0, 2, sampleRate);
        private readonly int _sampleRate = sampleRate;
        private Svf _tone;

        public float Process(float input, float cutoff)
        {
            var body = _bridge.Process(_wood.Process(_air.Process(_rumble.Process(input))));
            _tone.Process(body, cutoff, 0.6f, _sampleRate);
            return _tone.Low;
        }
    }
}
