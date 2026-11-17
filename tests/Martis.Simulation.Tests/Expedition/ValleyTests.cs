using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Expedition;

/// <summary>V5 (ADR-032): the valley's points of interest and saving the whole expedition.</summary>
public sealed class ValleyTests
{
    private static ValleyDefinition Valley() => ValleyDefinition.LoadPublished(Repo.Root());

    private static KnowledgeMap Knowledge(int size = 64) => new(SurveyDefinition.LoadPublished(Repo.Root()), size, size);

    [Fact]
    public void ThePublishedValleyHasFivePlacesOfInterest()
    {
        var valley = Valley();

        Assert.Equal(5, valley.Pois.Count);
        Assert.Contains(valley.Pois, poi => poi.Kind == PoiKind.Delta && poi.Research >= 5);
        Assert.All(valley.Pois, poi => Assert.False(string.IsNullOrWhiteSpace(poi.Finding)));
    }

    [Fact]
    public void PlacesScaleWithTheMap()
    {
        var small = new PoiField(Valley(), 64, 64);
        var big = new PoiField(Valley(), 256, 256);

        Assert.Equal((32, 8), (small.Pois[0].X, small.Pois[0].Y));
        Assert.Equal((128, 35), (big.Pois[0].X, big.Pois[0].Y));
    }

    [Fact]
    public void AnEstimateShowsAnAnomalyAndAMeasurementRevealsThePlace()
    {
        var field = new PoiField(Valley(), 64, 64);
        var knowledge = Knowledge();
        var delta = field.Pois[0];

        Assert.Empty(field.Update(knowledge));
        knowledge.Observe(delta.X + 0.5, delta.Y + 0.5, 0, 0.45, 0);
        Assert.Equal([delta], field.Update(knowledge));
        Assert.Equal(PoiState.Sighted, delta.State);
        knowledge.Scan(delta.X + 1.5, delta.Y + 0.5);
        Assert.Equal([delta], field.Update(knowledge));
        Assert.Equal(PoiState.Discovered, delta.State);
        Assert.Empty(field.Update(knowledge));
    }

    [Fact]
    public void AMeasurementOutsideTheRadiusRevealsNothing()
    {
        var field = new PoiField(Valley(), 64, 64);
        var knowledge = Knowledge();
        var delta = field.Pois[0];

        knowledge.Observe(delta.X + 4.5, delta.Y + 0.5, 0, 0.95, 0);

        Assert.Empty(field.Update(knowledge));
        Assert.Equal(PoiState.Hidden, delta.State);
    }

    [Fact]
    public void AnInvalidValleyIsRejected()
    {
        var json = File.ReadAllText(Path.Combine(Repo.Root(), "data", "expedition", "valley.v1.json"));

        Assert.Throws<InvalidDataException>(() => ValleyDefinition.Load(json.Replace("\"x\": 0.5,", "\"x\": 1.5,", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => ValleyDefinition.Load(json.Replace("\"id\": \"salmuera\"", "\"id\": \"delta\"", StringComparison.Ordinal)));
    }

    [Fact]
    public void TheWholeExpeditionSurvivesASaveAndALoad()
    {
        var root = Repo.Root();
        var rover = new Rover(RoverDefinition.LoadPublished(root), 64, 64);
        var knowledge = Knowledge();
        var samples = new SampleStore(SampleDefinition.LoadPublished(root));
        var network = new FacilityNetwork(FacilityDefinition.LoadPublished(root));
        var pois = new PoiField(Valley(), 64, 64);
        rover.Place(20, 30, 1.2);
        rover.Landed(22, 30);
        rover.Drive(1, 0.3, 1.5);
        rover.AddWear(0.37);
        knowledge.Orbit(0);
        knowledge.Scan(20.5, 30.5);
        knowledge.Scan(pois.Pois[0].X + 0.5, pois.Pois[0].Y + 0.5);
        pois.Update(knowledge);
        var cell = new CellReport(0, 20, 30, 221, 830, 0.0005, 150, new Dictionary<string, double>(StringComparer.Ordinal) { ["Water"] = 0.1 }, [new PopulationReport(7, "phototroph_a01", "Fotótrofo A01", 0.2, 1e9, 0, 12, 1, new Dictionary<string, double>(StringComparer.Ordinal) { ["UV_PROTECTION"] = 0.8 })]);
        samples.Collect(cell, KnowledgeLevel.Measured, 5, out _);
        samples.Collect(cell with { X = 21 }, KnowledgeLevel.Measured, 6, out _);
        samples.Deliver(8);
        samples.Advance(10);
        network.Build(20, 30, KnowledgeLevel.Measured, 4, out _);
        network.Tick(knowledge);

        var json = ExpeditionState.Capture(rover, knowledge, samples, network, pois).ToJson();
        var rover2 = new Rover(RoverDefinition.LoadPublished(root), 64, 64);
        var knowledge2 = Knowledge();
        var samples2 = new SampleStore(SampleDefinition.LoadPublished(root));
        var network2 = new FacilityNetwork(FacilityDefinition.LoadPublished(root));
        var pois2 = new PoiField(Valley(), 64, 64);
        ExpeditionState.FromJson(json).Apply(rover2, knowledge2, samples2, network2, pois2);

        Assert.Equal((rover.X, rover.Y, rover.Heading, rover.Battery, rover.Wear, rover.Lander), (rover2.X, rover2.Y, rover2.Heading, rover2.Battery, rover2.Wear, rover2.Lander));
        for (var y = 0; y < 64; y++)
        {
            for (var x = 0; x < 64; x++)
            {
                Assert.Equal(knowledge.Level(x, y), knowledge2.Level(x, y));
                Assert.InRange(knowledge2.Confidence(x, y) - knowledge.Confidence(x, y), 0.0, 2e-5);
            }
        }

        Assert.Equal(samples.All.Select(s => (s.Id, s.X, s.Y, s.Kind, s.State, s.ReadySol, s.TakenSol)), samples2.All.Select(s => (s.Id, s.X, s.Y, s.Kind, s.State, s.ReadySol, s.TakenSol)));
        Assert.Equal(samples.LabFreeSol, samples2.LabFreeSol);
        Assert.Equal(0.8, samples2.All[0].Dominant!.Genome["UV_PROTECTION"], 12);
        Assert.Equal(network.Stations.Select(s => (s.Id, s.X, s.Y, s.BuiltSol, s.Wear)), network2.Stations.Select(s => (s.Id, s.X, s.Y, s.BuiltSol, s.Wear)));
        Assert.Equal(pois.Pois.Select(p => p.State), pois2.Pois.Select(p => p.State));
        Assert.Equal(PoiState.Discovered, pois2.Pois[0].State);
    }

    [Fact]
    public void ABrokenOrMismatchedSaveIsRejected()
    {
        var root = Repo.Root();
        var state = ExpeditionState.Capture(new Rover(RoverDefinition.LoadPublished(root), 64, 64), Knowledge(), new SampleStore(SampleDefinition.LoadPublished(root)), new FacilityNetwork(FacilityDefinition.LoadPublished(root)), new PoiField(Valley(), 64, 64));

        Assert.Throws<InvalidDataException>(() => ExpeditionState.FromJson("{not json"));
        Assert.Throws<InvalidDataException>(() => ExpeditionState.FromJson(state.ToJson().Replace("\"Version\":1", "\"Version\":9", StringComparison.Ordinal)));
        Assert.Throws<InvalidDataException>(() => state.Apply(new Rover(RoverDefinition.LoadPublished(root), 32, 32), Knowledge(32), new SampleStore(SampleDefinition.LoadPublished(root)), new FacilityNetwork(FacilityDefinition.LoadPublished(root)), new PoiField(Valley(), 32, 32)));
    }
}
