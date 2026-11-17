using Martis.Simulation.Expedition;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Expedition;

/// <summary>V2 (ADR-029): the fog of knowledge, the orbital probe and the rover scanner.</summary>
public sealed class KnowledgeTests
{
    private static SurveyDefinition Definition() => SurveyDefinition.LoadPublished(Repo.Root());

    private static KnowledgeMap NewMap(int size = 64) => new(Definition(), size, size);

    [Fact]
    public void ThePublishedSurveyLoadsItsBalanceValues()
    {
        var definition = Definition();

        Assert.Equal(8, definition.OrbitalSwathCells);
        Assert.Equal(2, definition.OrbitalSolsPerPass);
        Assert.InRange(definition.OrbitalConfidence, definition.EstimatedThreshold, definition.MeasuredThreshold);
        Assert.True(definition.ScanPeakConfidence >= definition.MeasuredThreshold);
        Assert.InRange(definition.PassiveConfidence, definition.EstimatedThreshold, definition.MeasuredThreshold);
    }

    [Fact]
    public void ANewMapKnowsNothing()
    {
        var map = NewMap();

        Assert.Equal(KnowledgeLevel.Unknown, map.Level(32, 32));
        Assert.Equal(0.0, map.Coverage(KnowledgeLevel.Estimated), 12);
        Assert.Equal(1.0, map.Coverage(KnowledgeLevel.Unknown), 12);
    }

    [Fact]
    public void TheFirstOrbitalPassEstimatesTheCentralSwath()
    {
        var map = NewMap();

        var improved = map.Orbit(0);

        Assert.Equal(8 * 64, improved);
        Assert.Equal(KnowledgeLevel.Estimated, map.Level(32, 0));
        Assert.Equal(KnowledgeLevel.Estimated, map.Level(39, 63));
        Assert.Equal(KnowledgeLevel.Unknown, map.Level(31, 32));
        Assert.Equal(KnowledgeLevel.Unknown, map.Level(40, 32));
    }

    [Fact]
    public void ThereIsNoPassBetweenOrbits()
    {
        var map = NewMap();

        Assert.Null(map.SwathAt(1));
        Assert.Equal(0, map.Orbit(1));
        Assert.Equal(0.0, map.Coverage(KnowledgeLevel.Estimated), 12);
    }

    [Fact]
    public void TheProbeCoversTheWholeRegionOnceAndThenWrapsAround()
    {
        var map = NewMap();

        for (long sol = 0; sol < map.Swaths * 2; sol++)
        {
            map.Orbit(sol);
        }

        Assert.Equal(1.0, map.Coverage(KnowledgeLevel.Estimated), 12);
        Assert.Equal(0.0, map.Coverage(KnowledgeLevel.Measured), 12);
        Assert.Equal(map.SwathAt(0), map.SwathAt(map.Swaths * 2L));
        Assert.Equal(0, map.Orbit(map.Swaths * 2L));
    }

    [Fact]
    public void AnOddWidthStillGetsCovered()
    {
        var map = new KnowledgeMap(Definition(), 20, 5);

        for (long sol = 0; sol < map.Swaths * 2; sol++)
        {
            map.Orbit(sol);
        }

        Assert.Equal(3, map.Swaths);
        Assert.Equal(1.0, map.Coverage(KnowledgeLevel.Estimated), 12);
    }

    [Fact]
    public void TheScannerMeasuresTheGroundAroundTheRover()
    {
        var map = NewMap();

        var improved = map.Scan(10.5, 10.5);

        Assert.True(improved > 0);
        Assert.Equal(KnowledgeLevel.Measured, map.Level(10, 10));
        Assert.Equal(KnowledgeLevel.Measured, map.Level(13, 10));
        Assert.Equal(KnowledgeLevel.Unknown, map.Level(15, 10));
        Assert.True(map.Confidence(10, 10) > map.Confidence(13, 10));
    }

    [Fact]
    public void NavigationCamerasOnlyEstimate()
    {
        var map = NewMap();

        map.Glimpse(20.5, 20.5);

        Assert.Equal(KnowledgeLevel.Estimated, map.Level(20, 20));
        Assert.Equal(KnowledgeLevel.Estimated, map.Level(22, 20));
        Assert.Equal(KnowledgeLevel.Unknown, map.Level(23, 20));
    }

    [Fact]
    public void ObservationsNeverLowerConfidence()
    {
        var map = NewMap();
        map.Scan(32.5, 32.5);
        var measured = map.Confidence(32, 32);

        var improved = map.Orbit(0) + map.Glimpse(32.5, 32.5);

        Assert.Equal(measured, map.Confidence(32, 32), 12);
        Assert.Equal(KnowledgeLevel.Measured, map.Level(32, 32));
        Assert.True(improved > 0, "the orbit still estimates the rest of the swath");
    }

    [Fact]
    public void RepeatingAScanImprovesNothing()
    {
        var map = NewMap();
        map.Scan(5.5, 5.5);
        var version = map.Version;

        Assert.Equal(0, map.Scan(5.5, 5.5));
        Assert.Equal(version, map.Version);
    }

    [Fact]
    public void ScansAtTheEdgeStayInsideTheMap()
    {
        var map = NewMap();

        map.Scan(0, 0);
        map.Scan(64, 64);

        Assert.Equal(KnowledgeLevel.Measured, map.Level(0, 0));
        Assert.Equal(KnowledgeLevel.Measured, map.Level(63, 63));
    }

    [Fact]
    public void ASnapshotRestoresTheSameKnowledge()
    {
        var map = NewMap();
        map.Orbit(0);
        map.Scan(12.5, 40.5);
        var copy = NewMap();

        copy.Restore(map.Snapshot());

        Assert.Equal(map.Snapshot(), copy.Snapshot());
        Assert.Throws<ArgumentException>(() => NewMap(32).Restore(map.Snapshot()));
    }

    [Fact]
    public void CellsOutsideTheMapAreRejected()
    {
        var map = NewMap();

        Assert.Throws<ArgumentOutOfRangeException>(() => map.Level(64, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Confidence(0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.SwathAt(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => map.Observe(1, 1, -1, 0.5, 0));
    }

    [Fact]
    public void AnInvalidSurveyFileIsRejected()
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "expedition", "survey.v1.json"))
            .Replace("\"schema\": \"martis.survey\"", "\"schema\": \"martis.rover\"", StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => SurveyDefinition.Load(json));
    }
}
