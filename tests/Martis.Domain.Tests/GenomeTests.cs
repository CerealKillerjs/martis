using Martis.Domain.Common;
using Martis.Domain.Genetics;

namespace Martis.Domain.Tests;

public sealed class GenomeTests
{
    public static IEnumerable<object[]> RejectedExpressions() => FiniteEdges.OutsideUnitInterval();

    public static IEnumerable<object[]> AcceptedExpressions() => FiniteEdges.InsideUnitInterval();

    [Theory]
    [MemberData(nameof(RejectedExpressions))]
    public void ConstructorRejectsAnExpressionOutsideTheUnitInterval(double expression)
    {
        var map = new Dictionary<GeneId, double> { [new GeneId("a")] = expression };
        Assert.Throws<ArgumentOutOfRangeException>(() => new Genome(map));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GeneExpression(new GeneId("a"), expression));
    }

    [Theory]
    [MemberData(nameof(AcceptedExpressions))]
    public void ConstructorAcceptsTheClosedUnitInterval(double expression)
    {
        var genome = new Genome(new Dictionary<GeneId, double> { [new GeneId("a")] = expression });
        Assert.Equal("a", genome.Single().GeneId.Value);
        DoubleBits.Equal(expression, genome[new GeneId("a")]);
    }

    [Fact]
    public void EnumerationOrderFollowsGeneIdAndIgnoresInsertionOrder()
    {
        // Fixed identifiers, not a seed. Ordinal order is part of the result.
        var insertedForward = new Genome(new Dictionary<GeneId, double>
        {
            [new GeneId("10")] = 0.25,
            [new GeneId("2")] = 0.75,
            [new GeneId("A")] = 0.5,
            [new GeneId("B")] = 0.0,
            [new GeneId("a")] = 1.0,
            [new GeneId("b")] = -0.0,
        });
        var insertedBackward = new Genome(new Dictionary<GeneId, double>
        {
            [new GeneId("b")] = -0.0,
            [new GeneId("a")] = 1.0,
            [new GeneId("B")] = 0.0,
            [new GeneId("A")] = 0.5,
            [new GeneId("2")] = 0.75,
            [new GeneId("10")] = 0.25,
        });

        var expected = new[] { "10", "2", "A", "B", "a", "b" };
        Assert.Equal(expected, Names(insertedForward));
        Assert.Equal(expected, Names(insertedBackward));
        Assert.Equal(Names(insertedForward), Names(insertedBackward));
    }

    [Fact]
    public void SequenceConstructorRejectsARepeatedGene()
    {
        var genes = new[]
        {
            new GeneExpression(new GeneId("a"), 0),
            new GeneExpression(new GeneId("b"), 1),
            new GeneExpression(new GeneId("a"), 0.5),
        };

        var error = Assert.Throws<ArgumentException>(() => new Genome(genes));
        Assert.Equal("expressions", error.ParamName);
        Assert.Contains("a", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyGenomeIsValid()
    {
        var genome = new Genome(new Dictionary<GeneId, double>());
        Assert.Empty(genome);
        Assert.False(genome.TryGetExpression(new GeneId("a"), out var expression));
        DoubleBits.Equal(0, expression);
        Assert.Throws<KeyNotFoundException>(() => genome[new GeneId("a")]);
    }

    [Fact]
    public void NullInputIsRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new Genome((IDictionary<GeneId, double>)null!));
        Assert.Throws<ArgumentNullException>(() => new Genome((IEnumerable<GeneExpression>)null!));
    }

    private static string[] Names(Genome genome)
        => genome.Select(gene => gene.GeneId.Value).ToArray();
}
