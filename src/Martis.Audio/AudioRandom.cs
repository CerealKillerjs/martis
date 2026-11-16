namespace Martis.Audio;

/// <summary>
/// SplitMix64 generator for sound design. Audio has its own generator so that the
/// soundtrack never consumes numbers from the simulation streams (ADR-003, ADR-018).
/// </summary>
public sealed class AudioRandom
{
    private ulong _state;

    public AudioRandom(ulong seed)
    {
        _state = seed;
    }

    public ulong NextUInt64()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float NextFloat() => (NextUInt64() >> 40) * (1f / (1 << 24));

    /// <summary>Uniform in [min, max].</summary>
    public float Range(float min, float max) => min + ((max - min) * NextFloat());

    /// <summary>Uniform integer in [0, count).</summary>
    public int Next(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        return (int)((NextUInt64() >> 33) % (ulong)count);
    }

    /// <summary>White noise sample in [-1, 1).</summary>
    public float Noise() => (NextFloat() * 2f) - 1f;

    public bool Chance(float probability) => NextFloat() < probability;
}
