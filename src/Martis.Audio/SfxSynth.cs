namespace Martis.Audio;

/// <summary>Sound effects of the game. Every one is synthesized from code (ADR-018), no samples.</summary>
public enum Sfx
{
    UiHover,
    UiClick,
    UiBack,
    Toast,
    Error,
    Deploy,
    Mutation,
    Extinction,
    Mission,
    Victory,
    Save,
    Load,
    Analyze,
    Experiment,
    Transition,
    TitleSting,
    Pause,
    Resume,
}

/// <summary>
/// Renders <see cref="Sfx"/> to mono float buffers at <see cref="SampleRate"/>. The output is
/// deterministic, normalised to a per-sound peak and windowed so it never clicks.
/// </summary>
public static class SfxSynth
{
    public const int SampleRate = 44100;

    public static double Duration(Sfx sfx) => sfx switch
    {
        Sfx.UiHover => 0.06,
        Sfx.UiClick => 0.11,
        Sfx.UiBack => 0.16,
        Sfx.Toast => 0.7,
        Sfx.Error => 0.45,
        Sfx.Deploy => 1.8,
        Sfx.Mutation => 1.5,
        Sfx.Extinction => 2.0,
        Sfx.Mission => 1.3,
        Sfx.Victory => 4.0,
        Sfx.Save => 0.5,
        Sfx.Load => 0.5,
        Sfx.Analyze => 1.1,
        Sfx.Experiment => 1.2,
        Sfx.Transition => 1.3,
        Sfx.TitleSting => 5.0,
        Sfx.Pause => 0.22,
        Sfx.Resume => 0.22,
        _ => throw new ArgumentOutOfRangeException(nameof(sfx)),
    };

    /// <summary>Sound for a <c>SessionEvent.Kind</c>, or null when the event stays silent.</summary>
    public static Sfx? ForEvent(string kind) => kind switch
    {
        "mutation" => Sfx.Mutation,
        "extinction" => Sfx.Extinction,
        "mission" => Sfx.Mission,
        "deploy" => Sfx.Deploy,
        "failure" => Sfx.Error,
        "experiment" => Sfx.Experiment,
        _ => null,
    };

    public static float[] Render(Sfx sfx)
    {
        var length = (int)Math.Round(Duration(sfx) * SampleRate);
        var buffer = new float[length];
        var random = new AudioRandom(0xA0D10UL + (ulong)sfx);
        var reverb = 0f;
        switch (sfx)
        {
            case Sfx.UiHover:
                Add(buffer, t => Sine(2400f, t) * Dsp.Percussive(t, 0.004f, 0.012f));
                break;
            case Sfx.UiClick:
                Add(buffer, t => (Sine(1500f - (6000f * t), t) * Dsp.Percussive(t, 0.002f, 0.018f)) + (0.4f * Sine(3100f, t) * Dsp.Percussive(t, 0.001f, 0.006f)));
                break;
            case Sfx.UiBack:
                Add(buffer, t => Sine(t < 0.06f ? 1100f : 820f, t) * Dsp.Percussive(t < 0.06f ? t : t - 0.06f, 0.003f, 0.025f));
                break;
            case Sfx.Pause:
                Add(buffer, t => Bell(880f - (900f * t), t, 0.006f, 0.06f, 1f, 0.8f));
                break;
            case Sfx.Resume:
                Add(buffer, t => Bell(600f + (1200f * t), t, 0.006f, 0.06f, 1f, 0.8f));
                break;
            case Sfx.Toast:
                Add(buffer, t => Bell(1318.5f, t, 0.004f, 0.16f, 3.5f, 1.2f) + (0.7f * Bell(1975.5f, t - 0.09f, 0.004f, 0.2f, 3.5f, 1.2f)));
                reverb = 0.25f;
                break;
            case Sfx.Error:
                {
                    var filter = default(Svf);
                    Add(buffer, t =>
                    {
                        var square = MathF.Sign(Sine(t < 0.18f ? 196f : 165f, t));
                        filter.Process(square, 900f, 0.8f, SampleRate);
                        var gate = Dsp.Percussive(t, 0.005f, 0.07f) + Dsp.Percussive(t - 0.18f, 0.005f, 0.09f);
                        return filter.Low * gate;
                    });
                    break;
                }

            case Sfx.Deploy:
                {
                    // Capsule: descending whoosh, thud on landing, then a bubbling release of spores.
                    var whoosh = default(Svf);
                    Add(buffer, t =>
                    {
                        whoosh.Process(random.Noise(), 3000f * MathF.Exp(-2.4f * t) + 250f, 3f, SampleRate);
                        var sweep = whoosh.Band * Dsp.Swell(t, 0f, 0.75f) * 0.9f;
                        var thud = Sine(70f * MathF.Exp(-(t - 0.6f) * 6f), t) * Dsp.Percussive(t - 0.6f, 0.004f, 0.18f) * 1.2f;
                        return sweep + thud;
                    });
                    for (var i = 0; i < 9; i++)
                    {
                        var start = 0.7f + (i * 0.09f) + random.Range(0f, 0.04f);
                        var note = Pitch.Degree(72, Mode.Lydian, random.Next(8));
                        var f = (float)Pitch.Hertz(note);
                        Add(buffer, t => 0.35f * Bell(f, t - start, 0.003f, 0.12f, 2f, 0.6f));
                    }

                    reverb = 0.35f;
                    break;
                }

            case Sfx.Mutation:
                Add(buffer, t => 0.6f * Bell(520f * MathF.Pow(2f, 1.5f * t), t, 0.02f, 0.5f, 1.41f, 2.5f));
                for (var i = 0; i < 14; i++)
                {
                    var start = random.Range(0.05f, 1.0f);
                    var f = (float)Pitch.Hertz(Pitch.Degree(84, Mode.Lydian, random.Next(10)));
                    Add(buffer, t => 0.3f * Bell(f, t - start, 0.002f, 0.1f, 3.5f, 1.5f));
                }

                reverb = 0.4f;
                break;
            case Sfx.Extinction:
                {
                    var rumble = default(Svf);
                    Add(buffer, t =>
                    {
                        rumble.Process(random.Noise(), 180f, 1.5f, SampleRate);
                        var fall = Sine(220f * MathF.Pow(2f, -1.6f * t), t) + (0.5f * Sine(330f * MathF.Pow(2f, -1.7f * t), t));
                        var envelope = Dsp.Percussive(t, 0.08f, 0.55f);
                        return ((0.6f * fall) + (0.8f * rumble.Low)) * envelope;
                    });
                    reverb = 0.3f;
                    break;
                }

            case Sfx.Mission:
                {
                    int[] notes = [67, 71, 74, 79];
                    for (var i = 0; i < notes.Length; i++)
                    {
                        var start = i * 0.09f;
                        var f = (float)Pitch.Hertz(notes[i]);
                        Add(buffer, t => Bell(f, t - start, 0.004f, 0.35f, 2f, 1f) * (i == notes.Length - 1 ? 1f : 0.7f));
                    }

                    reverb = 0.35f;
                    break;
                }

            case Sfx.Victory:
                {
                    // A swelling F-Lydian chord, a rising arpeggio and a shimmer on top.
                    int[] chord = [53, 60, 64, 67, 71];
                    foreach (var note in chord)
                    {
                        var f = (float)Pitch.Hertz(note);
                        Add(buffer, t => 0.22f * Pad(f, t) * Dsp.Swell(t, 0f, 4.0f));
                    }

                    for (var i = 0; i < 10; i++)
                    {
                        var start = 0.25f + (i * 0.12f);
                        var f = (float)Pitch.Hertz(Pitch.Degree(65, Mode.Lydian, i + 2));
                        Add(buffer, t => 0.45f * Bell(f, t - start, 0.003f, 0.5f, 2f, 1f));
                    }

                    Add(buffer, t => 0.25f * Bell(1760f, t - 1.5f, 0.6f, 0.6f, 3.5f, 0.8f));
                    reverb = 0.45f;
                    break;
                }

            case Sfx.Save:
                Add(buffer, t => Bell(t < 0.08f ? 990f : 1480f, t < 0.08f ? t : t - 0.08f, 0.003f, 0.06f, 1f, 0.4f) + (0.6f * Bell(1980f, t - 0.18f, 0.003f, 0.08f, 1f, 0.4f)));
                reverb = 0.2f;
                break;
            case Sfx.Load:
                Add(buffer, t => Bell(t < 0.08f ? 1980f : 1480f, t < 0.08f ? t : t - 0.08f, 0.003f, 0.06f, 1f, 0.4f) + (0.6f * Bell(990f, t - 0.18f, 0.003f, 0.08f, 1f, 0.4f)));
                reverb = 0.2f;
                break;
            case Sfx.Analyze:
                Add(buffer, t =>
                {
                    var sweep = Sine(600f + (1400f * Dsp.SmoothStep(0f, 0.8f, t)), t);
                    var tremolo = 0.6f + (0.4f * MathF.Sin(Dsp.Tau * 18f * t));
                    return 0.5f * sweep * tremolo * Dsp.Swell(t, 0f, 1.0f);
                });
                reverb = 0.2f;
                break;
            case Sfx.Experiment:
                for (var i = 0; i < 18; i++)
                {
                    var start = random.Range(0f, 0.85f);
                    var f = random.Range(300f, 900f);
                    Add(buffer, t => 0.4f * Sine(f * (1f + (6f * MathF.Max(0f, t - start))), t) * Dsp.Percussive(t - start, 0.004f, 0.03f));
                }

                reverb = 0.25f;
                break;
            case Sfx.Transition:
                {
                    var air = default(Svf);
                    Add(buffer, t =>
                    {
                        air.Process(random.Noise(), 400f + (2600f * Dsp.Swell(t, 0f, 1.25f)), 2f, SampleRate);
                        return air.Band * Dsp.Swell(t, 0f, 1.25f);
                    });
                    reverb = 0.3f;
                    break;
                }

            case Sfx.TitleSting:
                {
                    // Deep impact, a sub drop and a slow rising shimmer for the logo reveal.
                    var dust = default(Svf);
                    Add(buffer, t =>
                    {
                        dust.Process(random.Noise(), 140f, 1.2f, SampleRate);
                        var boom = (Sine(48f * MathF.Exp(-t * 0.8f), t) * Dsp.Percussive(t, 0.01f, 0.9f)) + (dust.Low * Dsp.Percussive(t, 0.01f, 0.6f));
                        return 0.9f * boom;
                    });
                    int[] notes = [62, 69, 74, 76, 81];
                    for (var i = 0; i < notes.Length; i++)
                    {
                        var f = (float)Pitch.Hertz(notes[i]);
                        var start = 0.4f + (i * 0.35f);
                        Add(buffer, t => 0.2f * Pad(f, t) * Dsp.Swell(t, start, 4.8f));
                    }

                    reverb = 0.5f;
                    break;
                }

            default:
                throw new ArgumentOutOfRangeException(nameof(sfx));
        }

        if (reverb > 0f)
        {
            AddReverb(buffer, reverb);
        }

        Finish(buffer, TargetLoudness(sfx));
        return buffer;
    }

    /// <summary>
    /// Loudness each sound is mastered to, as its loudest momentary (400 ms) window in LUFS:
    /// the interface whispers, events speak, milestones stand out above the music
    /// (<see cref="MusicEngine.TargetLufs"/>).
    /// </summary>
    public static double TargetLoudness(Sfx sfx) => sfx switch
    {
        Sfx.UiHover => -30,
        Sfx.UiClick or Sfx.UiBack or Sfx.Pause or Sfx.Resume => -26,
        Sfx.Toast or Sfx.Save or Sfx.Load => -24,
        Sfx.Error or Sfx.Analyze or Sfx.Experiment or Sfx.Transition => -22,
        Sfx.Mutation or Sfx.Extinction => -20,
        Sfx.Mission => -19,
        Sfx.Deploy => -18,
        Sfx.Victory => -17,
        _ => -16,
    };

    private static float Sine(float hertz, float t) => MathF.Sin(Dsp.Tau * hertz * t);

    /// <summary>Two-operator FM bell: the modulation index fades with the amplitude.</summary>
    private static float Bell(float hertz, float t, float attack, float decay, float ratio, float index)
    {
        if (t < 0f)
        {
            return 0f;
        }

        var envelope = Dsp.Percussive(t, attack, decay);
        var modulator = index * envelope * MathF.Sin(Dsp.Tau * hertz * ratio * t);
        return envelope * MathF.Sin((Dsp.Tau * hertz * t) + modulator);
    }

    /// <summary>Soft detuned pad tone made of three slightly detuned sines and an octave.</summary>
    private static float Pad(float hertz, float t)
        => (MathF.Sin(Dsp.Tau * hertz * t) + MathF.Sin(Dsp.Tau * hertz * 1.004f * t) + MathF.Sin(Dsp.Tau * hertz * 0.996f * t) + (0.3f * MathF.Sin(Dsp.Tau * hertz * 2f * t))) / 3.3f;

    private static void Add(float[] buffer, Func<float, float> voice)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] += voice(i / (float)SampleRate);
        }
    }

    /// <summary>A small room: the FDN reverb at a one-second decay, mixed in parallel.</summary>
    private static void AddReverb(float[] buffer, float wet)
    {
        var room = new FdnReverb(SampleRate, rt60Seconds: 1.1f, preDelayMs: 8f, damping: 0.5f);
        for (var i = 0; i < buffer.Length; i++)
        {
            room.Process(buffer[i], buffer[i], out var left, out var right);
            buffer[i] += wet * 0.8f * (left + right);
        }
    }

    /// <summary>
    /// Masters one sound: a 60 Hz high-pass (no rumble from tiny speakers), light 2:1
    /// compression, a short fade-in and a tail fade, then loudness normalisation to
    /// <paramref name="targetLufs"/> through a lookahead limiter at −1 dBFS.
    /// </summary>
    private static void Finish(float[] buffer, double targetLufs)
    {
        var highPass = Biquad.HighPass(60, 0.707, SampleRate);
        var compressor = new Compressor(SampleRate, thresholdDb: -20f, ratio: 2f, attackMs: 4f, releaseMs: 120f, kneeDb: 6f);
        var fadeIn = Math.Min(buffer.Length / 4, SampleRate * 3 / 1000);
        var fadeOut = Math.Max(SampleRate / 50, (int)(buffer.Length * 0.15));
        for (var i = 0; i < buffer.Length; i++)
        {
            var left = highPass.Process(buffer[i]);
            var right = left;
            compressor.Process(ref left, ref right);
            var gain = 1f;
            if (i < fadeIn)
            {
                gain = i / (float)fadeIn;
            }

            var remaining = buffer.Length - 1 - i;
            if (remaining < fadeOut)
            {
                var x = remaining / (float)fadeOut;
                gain *= x * x;
            }

            buffer[i] = left * gain;
        }

        // Limiting a loud transient lowers the loudness a little, so measure and correct.
        var source = (float[])buffer.Clone();
        var unity = LoudnessMeter.MaxMomentary(source, SampleRate);
        if (double.IsNegativeInfinity(unity))
        {
            return;
        }

        var gainDb = targetLufs - unity;
        for (var pass = 0; pass < 4; pass++)
        {
            var measured = LimitInto(buffer, source, (float)gainDb);
            if (Math.Abs(measured - targetLufs) < 0.2)
            {
                return;
            }

            gainDb += targetLufs - measured;
        }
    }

    /// <summary>Writes <paramref name="source"/> × gain through a fresh limiter (latency
    /// compensated) into <paramref name="output"/> and returns the result's loudness.</summary>
    private static double LimitInto(float[] output, float[] source, float gainDb)
    {
        var limiter = new Limiter(SampleRate, ceilingDb: -1f, lookaheadMs: 3f, releaseMs: 60f);
        var gain = MathF.Pow(10f, gainDb / 20f);
        for (var i = 0; i < source.Length + limiter.Latency; i++)
        {
            var x = i < source.Length ? source[i] * gain : 0f;
            limiter.Process(x, x, out var y, out _);
            var at = i - limiter.Latency;
            if (at >= 0)
            {
                output[at] = y;
            }
        }

        return LoudnessMeter.MaxMomentary(output, SampleRate);
    }
}
