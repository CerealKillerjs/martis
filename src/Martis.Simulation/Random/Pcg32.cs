namespace Martis.Simulation.Random;

/// <summary>
/// <c>pcg32-v1</c>: PCG XSH-RR 32-bit output on a 64-bit LCG (ADR-003).
/// The step, the multiplier and the output match
/// <c>pcg_setseq_64_xsh_rr_32_random_r</c> from pcg-c commit
/// <c>83252d9c23df9c82ecb42210afed61a7b42402d7</c>.
/// <see cref="Seed"/> matches <c>pcg_setseq_64_srandom_r</c>.
/// The constructor installs a raw state and does not seed.
/// <see cref="NextUInt64"/>, <see cref="NextDouble"/>, <see cref="NextInt(int)"/> and
/// <see cref="NextBool"/> are part of <c>pcg32-v1</c>. Changing them requires <c>pcg32-v2</c>.
/// </summary>
public sealed class Pcg32 : IRandomSource
{
    /// <summary>Algorithm id stored beside snapshots. Not a scientific parameter.</summary>
    public const string AlgorithmId = "pcg32-v1";

    /// <summary>PCG_DEFAULT_MULTIPLIER_64 from the cited pcg-c header.</summary>
    public const ulong Multiplier = 6364136223846793005UL;

    /// <summary>53-bit denominator used by <see cref="NextDouble"/>.</summary>
    private const double UnitInLastPlace = 1.0 / (1UL << 53);

    private ulong _state;
    private ulong _increment;

    /// <summary>Installs a raw generator state. This is not <see cref="Seed"/>.</summary>
    public Pcg32(ulong state, ulong increment)
    {
        _state = state;
        _increment = increment;
    }

    /// <summary>
    /// Official seeding: <c>pcg32_srandom_r(initState, initSequence)</c>.
    /// The stored increment becomes <c>(initSequence &lt;&lt; 1) | 1</c>.
    /// </summary>
    public static Pcg32 Seed(ulong initState, ulong initSequence)
    {
        var rng = new Pcg32(0UL, (initSequence << 1) | 1UL);
        rng._state = unchecked(rng._state * Multiplier + rng._increment);
        rng._state = unchecked(rng._state + initState);
        rng._state = unchecked(rng._state * Multiplier + rng._increment);
        return rng;
    }

    public static Pcg32 FromState(RandomState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return new Pcg32(state.State, state.Increment);
    }

    public RandomState ExportState() => new(_state, _increment);

    public void Restore(RandomState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state.State;
        _increment = state.Increment;
    }

    /// <summary>One XSH-RR output. Reference vectors compare this word, not <see cref="NextUInt64"/>.</summary>
    public uint NextUInt32()
    {
        var oldState = _state;
        _state = unchecked(oldState * Multiplier + _increment);
        var xorshifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        var rotation = (int)(oldState >> 59);
        return (xorshifted >> rotation) | (xorshifted << ((-rotation) & 31));
    }

    /// <summary>
    /// Low 32 bits are the first <see cref="NextUInt32"/>; high 32 bits are the second.
    /// </summary>
    public ulong NextUInt64()
    {
        var low = NextUInt32();
        var high = NextUInt32();
        return ((ulong)high << 32) | low;
    }

    /// <summary>Takes the top 53 bits of <see cref="NextUInt64"/> and scales them into [0, 1).</summary>
    public double NextDouble()
    {
        return (NextUInt64() >> 11) * UnitInLastPlace;
    }

    /// <summary>Unbiased value in [0, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int maxExclusive)
    {
        if (maxExclusive <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                "The exclusive upper bound must be positive.");
        }

        return (int)NextUInt32Below((uint)maxExclusive);
    }

    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (minInclusive >= maxExclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxExclusive),
                maxExclusive,
                "The exclusive maximum must be greater than the inclusive minimum.");
        }

        var width = (uint)((long)maxExclusive - minInclusive);
        return (int)(minInclusive + (long)NextUInt32Below(width));
    }

    /// <summary>
    /// VAL-004. Values outside [0, 1], including NaN and infinities, throw.
    /// 0 returns false and 1 returns true without consuming a draw. Any other
    /// probability consumes one <see cref="NextDouble"/>.
    /// </summary>
    public bool NextBool(double probability)
    {
        if (!double.IsFinite(probability) || probability < 0.0 || probability > 1.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(probability),
                probability,
                "VAL-004: probability must be finite and inside [0, 1].");
        }

        if (probability == 0.0)
        {
            return false;
        }

        if (probability == 1.0)
        {
            return true;
        }

        return NextDouble() < probability;
    }

    /// <summary>
    /// Debiased bounded draw from pcg-c <c>pcg_setseq_64_xsh_rr_32_boundedrand_r</c>:
    /// reject values below <c>2^32 % bound</c>, then reduce with the remainder.
    /// </summary>
    private uint NextUInt32Below(uint bound)
    {
        var threshold = (0u - bound) % bound;
        uint candidate;
        do
        {
            candidate = NextUInt32();
        }
        while (candidate < threshold);

        return candidate % bound;
    }
}
