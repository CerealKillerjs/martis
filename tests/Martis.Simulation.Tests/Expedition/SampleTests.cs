using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Expedition;

/// <summary>V3 (ADR-030): taking samples with the rover and analysing them in the laboratory.</summary>
public sealed class SampleTests
{
    private static SampleDefinition Definition() => SampleDefinition.LoadPublished(Repo.Root());

    private static CellReport Cell(int x, int y, int populations = 0)
        => new(
            (y * 64) + x,
            x,
            y,
            220,
            800,
            0.0005,
            150,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["Water"] = 0.08 },
            Enumerable.Range(1, populations)
                .Select(i => new PopulationReport(i, "phototroph_a01", "Fotótrofo A01", 0.1 * i, 1e9, 0, 10, 1, new Dictionary<string, double>(StringComparer.Ordinal) { ["UV_PROTECTION"] = 0.5 + (0.1 * i) }))
                .ToArray());

    [Fact]
    public void ThePublishedSamplingLoadsItsBalanceValues()
    {
        var definition = Definition();

        Assert.Equal(4, definition.Capacity);
        Assert.Equal(2, definition.AnalysisSols);
        Assert.True(definition.ResearchBiological > definition.ResearchRegolith);
    }

    [Fact]
    public void OnlyMeasuredGroundCanBeSampled()
    {
        var store = new SampleStore(Definition());

        Assert.Equal(CollectResult.NotMeasured, store.Collect(Cell(1, 1), KnowledgeLevel.Unknown, 0, out var none));
        Assert.Equal(CollectResult.NotMeasured, store.Collect(Cell(1, 1), KnowledgeLevel.Estimated, 0, out _));
        Assert.Null(none);
        Assert.Equal(CollectResult.Collected, store.Collect(Cell(1, 1), KnowledgeLevel.Measured, 0, out var sample));
        Assert.Equal(1, sample!.Id);
        Assert.Equal(SampleState.Carried, sample.State);
        Assert.Equal(1, store.Carried);
    }

    [Fact]
    public void TheContainerHoldsALimitedNumberOfSamples()
    {
        var store = new SampleStore(Definition());
        for (var i = 0; i < 4; i++)
        {
            Assert.Equal(CollectResult.Collected, store.Collect(Cell(i, 0), KnowledgeLevel.Measured, 0, out _));
        }

        Assert.True(store.Full);
        Assert.Equal(CollectResult.Full, store.Collect(Cell(9, 9), KnowledgeLevel.Measured, 0, out _));
        Assert.Equal(4, store.Deliver(1));
        Assert.False(store.Full);
    }

    [Fact]
    public void LivingPopulationsMakeABiologicalSample()
    {
        var store = new SampleStore(Definition());

        store.Collect(Cell(1, 1), KnowledgeLevel.Measured, 0, out var bare);
        store.Collect(Cell(2, 2, populations: 2), KnowledgeLevel.Measured, 0, out var alive);

        Assert.Equal(SampleKind.Regolith, bare!.Kind);
        Assert.Null(bare.Dominant);
        Assert.Equal(SampleKind.Biological, alive!.Kind);
        Assert.Equal(2, alive.Dominant!.Id);
        Assert.Equal(Definition().ResearchBiological, store.ResearchFor(alive), 12);
        Assert.Equal(Definition().ResearchRegolith, store.ResearchFor(bare), 12);
    }

    [Fact]
    public void TheLaboratoryAnalysesOneSampleAtATime()
    {
        var store = new SampleStore(Definition());
        store.Collect(Cell(1, 1), KnowledgeLevel.Measured, 3, out var first);
        store.Collect(Cell(2, 2), KnowledgeLevel.Measured, 4, out var second);

        store.Deliver(10);

        Assert.Equal(12, first!.ReadySol);
        Assert.Equal(14, second!.ReadySol);
        Assert.Empty(store.Advance(11));
        Assert.Equal([first], store.Advance(12));
        Assert.Equal(SampleState.Analyzed, first.State);
        Assert.Equal(SampleState.Analyzing, second.State);
        Assert.Equal([second], store.Advance(20));
        Assert.Empty(store.Advance(21));
    }

    [Fact]
    public void AnIdleLaboratoryStartsFromTheDeliverySol()
    {
        var store = new SampleStore(Definition());
        store.Collect(Cell(1, 1), KnowledgeLevel.Measured, 0, out var early);
        store.Deliver(0);
        store.Advance(5);
        store.Collect(Cell(2, 2), KnowledgeLevel.Measured, 30, out var late);

        store.Deliver(30);

        Assert.Equal(2, early!.ReadySol);
        Assert.Equal(32, late!.ReadySol);
    }

    [Fact]
    public void ASampleIsAFrozenCopyOfTheCell()
    {
        var store = new SampleStore(Definition());
        var cell = Cell(5, 6, populations: 1);

        store.Collect(cell, KnowledgeLevel.Measured, 7, out var sample);

        Assert.Same(cell, sample!.Source);
        Assert.Equal((5, 6), (sample.X, sample.Y));
        Assert.Equal(7, sample.TakenSol);
    }

    [Fact]
    public void InvalidInputsAreRejected()
    {
        var store = new SampleStore(Definition());

        Assert.Throws<ArgumentNullException>(() => store.Collect(null!, KnowledgeLevel.Measured, 0, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Collect(Cell(0, 0), KnowledgeLevel.Measured, -1, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Deliver(-1));
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "expedition", "samples.v1.json"))
            .Replace("\"schema\": \"martis.samples\"", "\"schema\": \"martis.survey\"", StringComparison.Ordinal);
        Assert.Throws<InvalidDataException>(() => SampleDefinition.Load(json));
    }
}
