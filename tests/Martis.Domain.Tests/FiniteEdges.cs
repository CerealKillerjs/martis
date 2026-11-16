namespace Martis.Domain.Tests;

/// <summary>
/// Boundary values for finite-range checks. These are language edges, not model parameters.
/// </summary>
public static class FiniteEdges
{
    public static IEnumerable<object[]> NonFiniteOrNegative()
    {
        yield return [double.NaN];
        yield return [-0.01];
        yield return [-1.0];
        yield return [double.PositiveInfinity];
        yield return [double.NegativeInfinity];
        yield return [Math.BitDecrement(0.0)];
    }

    public static IEnumerable<object[]> NonNegativeFinite()
    {
        yield return [0.0];
        yield return [-0.0];
        yield return [1.0];
        yield return [Math.BitIncrement(0.0)];
        yield return [double.MaxValue];
    }

    public static IEnumerable<object[]> OutsideUnitInterval()
    {
        yield return [-0.01];
        yield return [1.01];
        yield return [double.NaN];
        yield return [double.PositiveInfinity];
        yield return [double.NegativeInfinity];
        yield return [Math.BitDecrement(0.0)];
        yield return [Math.BitIncrement(1.0)];
    }

    public static IEnumerable<object[]> InsideUnitInterval()
    {
        yield return [0.0];
        yield return [-0.0];
        yield return [1.0];
        yield return [Math.BitDecrement(1.0)];
        yield return [Math.BitIncrement(0.0)];
    }
}
