namespace Martis.Audio.Tests;

public sealed class AudioRandomTests
{
    [Fact]
    public void TheSameSeedGivesTheSameSequence()
    {
        var a = new AudioRandom(42);
        var b = new AudioRandom(42);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
        }
    }

    [Fact]
    public void DifferentSeedsGiveDifferentSequences()
    {
        Assert.NotEqual(new AudioRandom(1).NextUInt64(), new AudioRandom(2).NextUInt64());
    }

    [Fact]
    public void FloatsStayInTheUnitIntervalAndIntegersInRange()
    {
        var random = new AudioRandom(7);
        for (var i = 0; i < 10_000; i++)
        {
            var value = random.NextFloat();
            Assert.InRange(value, 0f, 0.99999994f);
            Assert.InRange(random.Next(5), 0, 4);
            Assert.InRange(random.Range(-2f, 3f), -2f, 3f);
        }
    }

    [Fact]
    public void NextCoversEveryValue()
    {
        var random = new AudioRandom(3);
        var seen = new bool[6];
        for (var i = 0; i < 600; i++)
        {
            seen[random.Next(6)] = true;
        }

        Assert.All(seen, Assert.True);
    }
}
