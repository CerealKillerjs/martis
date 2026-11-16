using Martis.Simulation.Random;

namespace Martis.Simulation.Tests.Random;

public sealed class RngStreamsTests
{
    [Fact]
    public void SequencesAreTheDocumentedConstants()
    {
        Assert.Equal(1UL, RngStreams.EnvironmentSequence);
        Assert.Equal(2UL, RngStreams.MutationSequence);
        Assert.Equal(3UL, RngStreams.ReproductionSequence);
        Assert.Equal(4UL, RngStreams.GeneticDriftSequence);
        Assert.Equal(5UL, RngStreams.EventsSequence);

        var streams = RngStreams.Create(12345UL);
        Assert.Equal((RngStreams.EnvironmentSequence << 1) | 1UL, streams.Environment.ExportState().Increment);
        Assert.Equal((RngStreams.MutationSequence << 1) | 1UL, streams.Mutation.ExportState().Increment);
        Assert.Equal((RngStreams.ReproductionSequence << 1) | 1UL, streams.Reproduction.ExportState().Increment);
        Assert.Equal((RngStreams.GeneticDriftSequence << 1) | 1UL, streams.GeneticDrift.ExportState().Increment);
        Assert.Equal((RngStreams.EventsSequence << 1) | 1UL, streams.Events.ExportState().Increment);
    }

    [Fact]
    public void ConsumingOneStreamLeavesTheOthersUntouched()
    {
        const ulong seed = 77UL;
        var touched = RngStreams.Create(seed);
        var fresh = RngStreams.Create(seed);
        for (var i = 0; i < 1000; i++)
        {
            _ = touched.Environment.NextUInt64();
        }

        Assert.Equal(fresh.Mutation.ExportState(), touched.Mutation.ExportState());
        Assert.Equal(fresh.Reproduction.ExportState(), touched.Reproduction.ExportState());
        Assert.Equal(fresh.GeneticDrift.ExportState(), touched.GeneticDrift.ExportState());
        Assert.Equal(fresh.Events.ExportState(), touched.Events.ExportState());

        var expected = new ulong[100];
        var actual = new ulong[100];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = fresh.Mutation.NextUInt64();
            actual[i] = touched.Mutation.NextUInt64();
        }

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void RestoredStreamsRepeatTheContinuation()
    {
        var streams = RngStreams.Create(5UL);
        _ = streams.Events.NextDouble();
        var saved = streams.ExportState();
        var expected = streams.Events.NextUInt64();
        streams.Restore(saved);
        Assert.Equal(expected, streams.Events.NextUInt64());

        var copy = RngStreams.Create(5UL);
        copy.Restore(saved);
        Assert.Equal(expected, copy.Events.NextUInt64());
    }

    [Fact]
    public void ASixthSequenceIsRejectedAndLeavesTheFiveStreamsUntouched()
    {
        var streams = RngStreams.Create(12345UL);
        var before = streams.ExportState();
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => streams.ForSequence(6));
        Assert.Contains("five", error.Message, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => streams.ForSequence(0));
        Assert.Equal(before, streams.ExportState());
        Assert.Same(streams.Environment, streams.ForSequence(RngStreams.EnvironmentSequence));
        Assert.Same(streams.Events, streams.ForSequence(RngStreams.EventsSequence));
    }

    [Fact]
    public void DistinctSeedsProduceDistinctEnvironmentState()
    {
        var zero = RngStreams.Create(0UL).Environment.ExportState();
        var one = RngStreams.Create(1UL).Environment.ExportState();
        var max = RngStreams.Create(ulong.MaxValue).Environment.ExportState();
        Assert.NotEqual(zero, one);
        Assert.NotEqual(zero, max);
        Assert.NotEqual(one, max);
        Assert.Equal(RngStreams.Create(1UL).Environment.ExportState(), one);
    }
}
