namespace Martis.Simulation.Random;

/// <summary>
/// Deterministic draw used by the simulation (TDD §6). Implementations must not
/// call the BCL random type. The concrete algorithm is <c>pcg32-v1</c> (ADR-003).
/// </summary>
public interface IRandomSource
{
    /// <summary>Next 64-bit word. For <c>pcg32-v1</c> this is two 32-bit outputs.</summary>
    ulong NextUInt64();

    /// <summary>Uniform value in the half-open interval [0, 1).</summary>
    double NextDouble();

    /// <summary>Unbiased integer in the half-open interval [<paramref name="minInclusive"/>, <paramref name="maxExclusive"/>).</summary>
    int NextInt(int minInclusive, int maxExclusive);

    /// <summary>
    /// Bernoulli draw. <paramref name="probability"/> must be finite and inside [0, 1] (VAL-004).
    /// </summary>
    bool NextBool(double probability);
}
