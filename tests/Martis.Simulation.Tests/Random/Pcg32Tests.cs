using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Random;

/// <summary>
/// Reference vectors for <c>pcg32-v1</c>.
/// Source: pcg-c commit 83252d9c23df9c82ecb42210afed61a7b42402d7 (2019-07-18),
/// https://github.com/imneme/pcg-c/blob/83252d9c23df9c82ecb42210afed61a7b42402d7/test-low/expected/check-setseq-64-xsh-rr-32.out
/// Seed <c>pcg_setseq_64_srandom_r(42, 54)</c> from
/// https://github.com/imneme/pcg-c/blob/83252d9c23df9c82ecb42210afed61a7b42402d7/test-low/check-setseq-64-xsh-rr-32.c
/// The six words are the first Round 1 line. The coin and dice lines are the
/// official <c>boundedrand</c> output that follows those six words.
/// </summary>
public sealed class Pcg32Tests
{
    private static readonly uint[] FirstSix =
    [
        0xa15c02b7,
        0x7b47f409,
        0xba1d3330,
        0x83d2f293,
        0xbfa4784b,
        0xcbed606e,
    ];

    private const string Coins =
        "HHTTTHTHHHTHTTTHHHHHTTTHHHTHTHTHTTHTTTHHHHHHTTTTHHTTTTTHTTTTTTTHT";

    private const string Rolls =
        "3 4 1 1 2 2 3 2 4 3 2 4 3 3 5 2 3 1 3 1 5 1 4 1 5 6 4 6 6 2 6 3 3";

    [Fact]
    public void MatchesTheOfficial32BitVectors()
    {
        var first = Pcg32.Seed(42, 54);
        var second = Pcg32.Seed(42, 54);
        for (var i = 0; i < FirstSix.Length; i++)
        {
            Assert.Equal(FirstSix[i], first.NextUInt32());
            Assert.Equal(FirstSix[i], second.NextUInt32());
        }
    }

    [Fact]
    public void MatchesTheOfficialBoundedCoinAndDiceLines()
    {
        var rng = Pcg32.Seed(42, 54);
        for (var i = 0; i < FirstSix.Length; i++)
        {
            _ = rng.NextUInt32();
        }

        var coins = new char[Coins.Length];
        for (var i = 0; i < coins.Length; i++)
        {
            coins[i] = rng.NextInt(2) == 0 ? 'T' : 'H';
        }

        var rolls = new List<string>();
        for (var i = 0; i < 33; i++)
        {
            rolls.Add((rng.NextInt(6) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        Assert.Equal(Coins, new string(coins));
        Assert.Equal(Rolls, string.Join(' ', rolls));
    }

    [Fact]
    public void NextUInt64PacksTheFirstOfficialPair()
    {
        var rng = Pcg32.Seed(42, 54);
        var packed = rng.NextUInt64();
        Assert.Equal(((ulong)FirstSix[1] << 32) | FirstSix[0], packed);
    }

    [Fact]
    public void NextDoubleUsesTheTop53BitsOfNextUInt64()
    {
        var bits = Pcg32.Seed(42, 54).NextUInt64();
        var expected = (bits >> 11) * (1.0 / (1UL << 53));
        var actual = Pcg32.Seed(42, 54).NextDouble();
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected), BitConverter.DoubleToInt64Bits(actual));
        Assert.True(actual >= 0.0 && actual < 1.0);
    }

    [Fact]
    public void SameSeedRepeatsAndDifferentSeedsDoNotShareState()
    {
        var left = Pcg32.Seed(1, 1).ExportState();
        var right = Pcg32.Seed(1, 1).ExportState();
        Assert.Equal(left, right);
        Assert.NotEqual(left, Pcg32.Seed(0, 1).ExportState());
        Assert.NotEqual(left, Pcg32.Seed(ulong.MaxValue, 1).ExportState());
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void NextBoolRejectsAProbabilityOutsideTheUnitInterval(double probability)
    {
        var rng = Pcg32.Seed(1, 1);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBool(probability));
        Assert.Contains("VAL-004", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NextBoolRejectsNonFiniteProbabilitiesAndDoesNotDrawForEndpoints()
    {
        var rng = Pcg32.Seed(3, 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBool(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextBool(double.PositiveInfinity));
        var before = rng.ExportState();
        Assert.False(rng.NextBool(0.0));
        Assert.True(rng.NextBool(1.0));
        Assert.Equal(before, rng.ExportState());
    }

    [Fact]
    public void NextIntRejectsAnEmptyRangeAndStaysInsideAPositiveRange()
    {
        var rng = Pcg32.Seed(4, 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(-3));
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(2, 2));
        Assert.Equal(0, rng.NextInt(1));
        Assert.Equal(5, rng.NextInt(5, 6));
        var value = rng.NextInt(-2, 3);
        Assert.InRange(value, -2, 2);
    }

    [Fact]
    public void RestoredStateRepeatsTheContinuation()
    {
        var rng = Pcg32.Seed(99, 7);
        _ = rng.NextUInt64();
        var saved = rng.ExportState();
        var first = rng.NextUInt64();
        var second = rng.NextUInt64();
        var third = rng.NextDouble();
        var restored = Pcg32.FromState(saved);
        Assert.Equal(first, restored.NextUInt64());
        Assert.Equal(second, restored.NextUInt64());
        Assert.Equal(BitConverter.DoubleToInt64Bits(third), BitConverter.DoubleToInt64Bits(restored.NextDouble()));
    }
}
