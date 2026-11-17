namespace Martis.Audio;

/// <summary>
/// The title-screen theme (Daniel, 2026-11-17): an original, through-composed piece in D minor
/// at 54 beats per minute, slow, deep and calm. A pipe organ holds long chords with a 16-foot
/// pedal underneath while the piano plays a quiet eighth-note ostinato and, above it, a slow
/// melody; the organ swells through the middle sections and recedes at the end. The sections
/// play in <see cref="Order"/> and the piece loops.
/// </summary>
internal static class TitleTheme
{
    /// <summary>The key the score is written in (D3); the style's root transposes it.</summary>
    public const int Home = 50;

    public static readonly int[] Order = [0, 1, 2, 3, 1, 2, 4, 5];

    public static readonly Section[] Sections =
    [
        // 0 · Intro: the organ alone on D minor, a few piano notes from far away.
        new([Dm, Dm], Ostinato: false, Bass: false, Registration: 0f, Swell: 0.55f,
            [N(0, 0, 74, 0.30f), N(0, 2, 69, 0.26f), N(1, 0, 65, 0.28f), N(1, 2, 64, 0.24f)]),

        // 1 · The ostinato enters: i–VI–III–VII.
        new([Dm, Bb, F, C], Ostinato: true, Bass: false, Registration: 0.2f, Swell: 0.65f, []),

        // 2 · The melody, long notes over the ostinato.
        new([Dm, Bb, F, C], Ostinato: true, Bass: true, Registration: 0.45f, Swell: 0.8f,
            [N(0, 0, 69, 0.46f), N(0, 2, 77, 0.44f), N(0, 3, 76, 0.40f),
             N(1, 0, 74, 0.46f), N(1, 3, 72, 0.38f),
             N(2, 0, 72, 0.44f), N(2, 2, 69, 0.40f), N(2, 3, 72, 0.38f),
             N(3, 0, 76, 0.46f)]),

        // 3 · It turns to G minor and opens onto A major, the dominant.
        new([Gm, Bb, F, A], Ostinato: true, Bass: true, Registration: 0.6f, Swell: 0.85f,
            [N(0, 0, 74, 0.48f), N(0, 2, 70, 0.42f), N(0, 3, 72, 0.40f),
             N(1, 0, 74, 0.48f), N(1, 2, 77, 0.46f),
             N(2, 0, 76, 0.50f), N(2, 3, 72, 0.40f),
             N(3, 0, 73, 0.50f)]),

        // 4 · The full organ; the melody an octave higher, doubled below.
        new([Dm, Bb, Gm, A], Ostinato: true, Bass: true, Registration: 1f, Swell: 1f,
            [N(0, 0, 81, 0.56f), N(0, 0, 69, 0.42f), N(0, 2, 77, 0.50f), N(0, 3, 74, 0.46f),
             N(1, 0, 77, 0.56f), N(1, 0, 65, 0.42f), N(1, 2, 74, 0.48f),
             N(2, 0, 79, 0.56f), N(2, 0, 67, 0.42f), N(2, 2, 82, 0.52f),
             N(3, 0, 81, 0.58f), N(3, 0, 73, 0.44f), N(3, 2, 76, 0.46f)]),

        // 5 · Coda: the organ recedes, the piano walks down to D.
        new([Dm, Bb, F, Dm], Ostinato: false, Bass: false, Registration: 0.15f, Swell: 0.55f,
            [N(0, 0, 69, 0.36f), N(0, 0, 50, 0.30f), N(1, 0, 65, 0.32f), N(1, 0, 46, 0.28f),
             N(2, 0, 64, 0.30f), N(2, 2, 60, 0.26f), N(3, 0, 62, 0.30f), N(3, 0, 38, 0.30f)]),
    ];

    /// <summary>Seconds of silence (organ tail and reverb) before the piece starts again.</summary>
    public const double LoopRestSeconds = 4;

    private static Chord Dm => new([38, 50, 57, 62, 65], [62, 69, 65, 69]);

    private static Chord Bb => new([34, 46, 53, 62, 65], [58, 65, 62, 65]);

    private static Chord F => new([41, 53, 60, 65, 69], [60, 65, 69, 65]);

    private static Chord C => new([36, 48, 55, 64, 67], [60, 67, 64, 67]);

    private static Chord Gm => new([43, 55, 62, 67, 70], [62, 67, 70, 67]);

    private static Chord A => new([33, 45, 52, 61, 64], [61, 69, 64, 69]);

    private static ScoreNote N(int bar, double beat, int midi, float velocity) => new(bar, beat, midi, velocity);

    /// <summary>An organ voicing (pedal note first) and the piano's four-note ostinato cell.</summary>
    public sealed record Chord(int[] Organ, int[] Cell);

    public readonly record struct ScoreNote(int Bar, double Beat, int Midi, float Velocity);

    public sealed record Section(Chord[] Bars, bool Ostinato, bool Bass, float Registration, float Swell, ScoreNote[] Melody);
}

/// <summary>
/// A small pipe organ by additive synthesis: an 8-foot principal always speaks, and the
/// registration adds a 16-foot bourdon an octave below, a 4-foot octave and a 2⅔-foot
/// twelfth. Each pipe is doubled a cent or two apart (two ranks never tune exactly), the
/// chords swell in over 2.5 s and die away over 3.5 s. Harmonics of half the pipe frequency
/// come from a Chebyshev recursion, so a note costs one sine and one cosine per sample.
/// </summary>
internal sealed class Organ
{
    private const int MaxVoices = 32;
    private const int Harmonics = 12;
    private readonly int _sampleRate;
    private readonly Pipe[] _pipes = new Pipe[MaxVoices];
    private readonly float[] _amplitudes = new float[Harmonics + 1];
    private float _registration;
    private float _registrationTarget;
    private float _swell;
    private float _swellTarget;

    public Organ(int sampleRate)
    {
        _sampleRate = sampleRate;
    }

    public void SetChord(IReadOnlyList<int> midis, float registration, float swell)
    {
        Release();
        _registrationTarget = registration;
        _swellTarget = swell;
        var detune = 0;
        foreach (var midi in midis)
        {
            foreach (var cents in (ReadOnlySpan<float>)[-1.1f, 1.3f])
            {
                var slot = FreeSlot();
                var hz = Pitch.Hertz(midi) * Math.Pow(2.0, cents / 1200.0) / 2.0;
                _pipes[slot] = new Pipe
                {
                    Active = true,
                    Increment = Dsp.Tau * hz / _sampleRate,
                    Phase = 0.37 * detune++,
                    Level = 0f,
                    Pan = 0.5f + (cents < 0 ? -0.18f : 0.18f) * (midi < 48 ? 0.3f : 1f),
                    Bright = midi < 48 ? 0.6f : 1f,
                };
            }
        }
    }

    public void Release()
    {
        for (var p = 0; p < _pipes.Length; p++)
        {
            _pipes[p].Releasing = true;
        }
    }

    public void Next(out float left, out float right)
    {
        _registration += 0.00002f * (_registrationTarget - _registration);
        _swell += 0.00002f * (_swellTarget - _swell);
        var r = _registration;

        // Harmonic k of half the pipe frequency: k = 2 is the 8-foot fundamental.
        Array.Clear(_amplitudes);
        _amplitudes[1] = 0.55f * r;
        _amplitudes[2] = 1f + (0.15f * r);
        _amplitudes[3] = 0.08f * r;
        _amplitudes[4] = 0.32f + (0.6f * r);
        _amplitudes[6] = 0.12f + (0.4f * r);
        _amplitudes[8] = 0.05f + (0.18f * r);
        _amplitudes[10] = 0.03f;
        _amplitudes[12] = 0.07f * r;

        var attack = 1f / (2.5f * _sampleRate);
        var release = 1f / (3.5f * _sampleRate);
        left = 0f;
        right = 0f;
        for (var p = 0; p < _pipes.Length; p++)
        {
            ref var pipe = ref _pipes[p];
            if (!pipe.Active)
            {
                continue;
            }

            pipe.Level = pipe.Releasing ? pipe.Level - release : MathF.Min(1f, pipe.Level + attack);
            if (pipe.Level <= 0f)
            {
                pipe.Active = false;
                continue;
            }

            pipe.Phase += pipe.Increment;
            if (pipe.Phase > Dsp.Tau)
            {
                pipe.Phase -= Dsp.Tau;
            }

            var s1 = (float)Math.Sin(pipe.Phase);
            var c2 = 2f * (float)Math.Cos(pipe.Phase);
            var previous = 0f;
            var current = s1;
            var sum = 0f;
            for (var k = 1; k <= Harmonics; k++)
            {
                var weight = k > 6 ? pipe.Bright : 1f;
                sum += current * _amplitudes[k] * weight;
                var nextValue = (c2 * current) - previous;
                previous = current;
                current = nextValue;
            }

            var envelope = pipe.Level * pipe.Level * (3f - (2f * pipe.Level));
            var sample = sum * envelope * 0.013f * _swell;
            left += sample * (1f - pipe.Pan);
            right += sample * pipe.Pan;
        }
    }

    private int FreeSlot()
    {
        var best = 0;
        var quietest = float.MaxValue;
        for (var p = 0; p < _pipes.Length; p++)
        {
            if (!_pipes[p].Active)
            {
                return p;
            }

            if (_pipes[p].Releasing && _pipes[p].Level < quietest)
            {
                quietest = _pipes[p].Level;
                best = p;
            }
        }

        return best;
    }

    private struct Pipe
    {
        public bool Active;
        public bool Releasing;
        public double Phase;
        public double Increment;
        public float Level;
        public float Pan;
        public float Bright;
    }
}
