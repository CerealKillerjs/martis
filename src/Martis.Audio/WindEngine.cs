namespace Martis.Audio;

/// <summary>
/// Martian wind for the ambience bus: two decorrelated noise bands with slow gusts and a
/// faint whistle. Thin atmosphere (≈600 Pa) is quiet and high-passed, so the level stays low.
/// </summary>
public sealed class WindEngine
{
    public const int SampleRate = 44100;

    private readonly AudioRandom _random;
    private readonly float _smooth = Dsp.Smoothing(1.5f, SampleRate);
    private Svf _left;
    private Svf _right;
    private Svf _whistle;
    private float _gust;
    private float _gustTarget = 0.5f;
    private long _clock;
    private long _nextGust;
    private float _strength = 0.5f;
    private float _level;

    public WindEngine(ulong seed)
    {
        _random = new AudioRandom(seed ^ 0x77696E64UL);
    }

    /// <summary>0 = calm, 1 = dust storm.</summary>
    public float Strength
    {
        get => _strength;
        set => _strength = float.IsNaN(value) ? _strength : Math.Clamp(value, 0f, 1f);
    }

    public void Render(Span<float> interleaved)
    {
        for (var i = 0; i + 1 < interleaved.Length; i += 2)
        {
            if (_clock >= _nextGust)
            {
                _gustTarget = _random.Range(0.25f, 1f);
                _nextGust = _clock + (long)(_random.Range(1.5f, 5f) * SampleRate);
            }

            _gust += _smooth * (_gustTarget - _gust);
            _level += _smooth * 0.5f * (_strength - _level);
            var time = _clock / (float)SampleRate;
            var centre = 260f + (520f * _gust) + (90f * MathF.Sin(Dsp.Tau * 0.13f * time));
            _left.Process(_random.Noise(), centre, 1.4f, SampleRate);
            _right.Process(_random.Noise(), centre * 1.07f, 1.4f, SampleRate);
            _whistle.Process(_random.Noise(), 1400f + (600f * _gust), 18f, SampleRate);
            var amplitude = (0.04f + (0.32f * _level)) * (0.35f + (0.65f * _gust));
            var whistle = _whistle.Band * 0.05f * _gust * _level;
            var fade = MathF.Min(1f, _clock / (float)SampleRate);
            interleaved[i] = Dsp.SoftClip(((_left.Band * amplitude) + whistle) * fade, 0.9f);
            interleaved[i + 1] = Dsp.SoftClip(((_right.Band * amplitude) + whistle) * fade, 0.9f);
            _clock++;
        }
    }
}
