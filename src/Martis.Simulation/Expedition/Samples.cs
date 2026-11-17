using System.Text.Json;
using Martis.Domain.Parameters;
using Martis.Simulation.Sessions;

namespace Martis.Simulation.Expedition;

/// <summary>What a sample holds: bare regolith, or regolith with living populations.</summary>
public enum SampleKind
{
    Regolith,
    Biological,
}

/// <summary>Where a sample is on its way from the ground to a result.</summary>
public enum SampleState
{
    /// <summary>In the rover's container.</summary>
    Carried,

    /// <summary>Delivered to the laboratory, waiting or being analysed.</summary>
    Analyzing,

    /// <summary>Analysed: its findings are available.</summary>
    Analyzed,
}

/// <summary>Why a sample could or could not be taken.</summary>
public enum CollectResult
{
    Collected,

    /// <summary>The ground must be measured by the scanner first: no blind sampling.</summary>
    NotMeasured,

    /// <summary>The rover's container is full: deliver at the lander first.</summary>
    Full,
}

/// <summary>
/// Balance values of sampling and analysis (V3, ADR-030), loaded from
/// <c>data/expedition/samples.v1.json</c>. Every number is a classified GAME_BALANCE parameter.
/// </summary>
public sealed record SampleDefinition(
    string Id,
    int Capacity,
    double BatteryCost,
    int AnalysisSols,
    double DeliveryRadiusCells,
    double ResearchRegolith,
    double ResearchBiological)
{
    public const string PublishedFile = "samples.v1.json";

    public static SampleDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "expedition", fileName)));
    }

    public static SampleDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.samples")
        {
            throw new InvalidDataException("A samples file needs metadata.schema = martis.samples.");
        }

        var samples = meta.GetProperty("samples");
        double Number(string name) => parameters.RequireNumber(samples.GetProperty(name).GetString()!).Value;

        var definition = new SampleDefinition(
            meta.GetProperty("id").GetString()!,
            (int)Number("capacity"),
            Number("battery_cost"),
            (int)Number("analysis_sols"),
            Number("delivery_radius_cells"),
            Number("research_regolith"),
            Number("research_biological"));
        if (definition.Capacity < 1 || definition.AnalysisSols < 1)
        {
            throw new InvalidDataException("Sample capacity and analysis time must be at least 1.");
        }

        return definition;
    }
}

/// <summary>
/// One sample: the cell it came from and a frozen copy of that cell at the moment it was taken.
/// The laboratory analyses that copy, not the living cell, so a result describes the past.
/// </summary>
public sealed class Sample
{
    internal Sample(int id, CellReport source, long takenSol)
    {
        Id = id;
        Source = source;
        TakenSol = takenSol;
        Kind = source.Populations.Count > 0 ? SampleKind.Biological : SampleKind.Regolith;
    }

    public int Id { get; }

    public int X => Source.X;

    public int Y => Source.Y;

    public long TakenSol { get; }

    public SampleKind Kind { get; }

    public SampleState State { get; internal set; } = SampleState.Carried;

    /// <summary>The sol the analysis finishes, once delivered.</summary>
    public long? ReadySol { get; internal set; }

    /// <summary>The cell as it was when sampled.</summary>
    public CellReport Source { get; }

    /// <summary>The population with most biomass in the sample, if any: the genome the lab can reuse.</summary>
    public PopulationReport? Dominant => Source.Populations.Count == 0 ? null : Source.Populations.MaxBy(population => population.BiomassKg);
}

/// <summary>
/// The rover's sample container and the laboratory queue (V3, ADR-030). Samples are taken only on
/// measured ground, ride in the rover until it is back at the lander, then wait in a single queue:
/// the laboratory finishes one sample every <see cref="SampleDefinition.AnalysisSols"/> sols.
/// Deterministic: sols come from the simulation, never from a clock.
/// </summary>
public sealed class SampleStore
{
    private readonly List<Sample> _samples = [];
    private long _labFreeSol;

    public SampleStore(SampleDefinition definition)
        => Definition = definition ?? throw new ArgumentNullException(nameof(definition));

    public SampleDefinition Definition { get; }

    public IReadOnlyList<Sample> All => _samples;

    public int Carried => _samples.Count(sample => sample.State == SampleState.Carried);

    public bool Full => Carried >= Definition.Capacity;

    /// <summary>Takes a sample of <paramref name="cell"/> when its ground is measured and there is room.</summary>
    public CollectResult Collect(CellReport cell, KnowledgeLevel level, long sol, out Sample? sample)
    {
        ArgumentNullException.ThrowIfNull(cell);
        ArgumentOutOfRangeException.ThrowIfNegative(sol);
        sample = null;
        if (level != KnowledgeLevel.Measured)
        {
            return CollectResult.NotMeasured;
        }

        if (Full)
        {
            return CollectResult.Full;
        }

        sample = new Sample(_samples.Count + 1, cell, sol);
        _samples.Add(sample);
        return CollectResult.Collected;
    }

    /// <summary>Hands every carried sample to the laboratory queue; returns how many.</summary>
    public int Deliver(long sol)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sol);
        var delivered = 0;
        foreach (var sample in _samples.Where(sample => sample.State == SampleState.Carried))
        {
            _labFreeSol = Math.Max(_labFreeSol, sol) + Definition.AnalysisSols;
            sample.State = SampleState.Analyzing;
            sample.ReadySol = _labFreeSol;
            delivered++;
        }

        return delivered;
    }

    /// <summary>Finishes every analysis due by <paramref name="sol"/>; returns those samples in queue order.</summary>
    public IReadOnlyList<Sample> Advance(long sol)
    {
        var done = _samples.Where(sample => sample.State == SampleState.Analyzing && sample.ReadySol <= sol).ToArray();
        foreach (var sample in done)
        {
            sample.State = SampleState.Analyzed;
        }

        return done;
    }

    /// <summary>The sol the laboratory is busy until (saved with the samples).</summary>
    public long LabFreeSol => _labFreeSol;

    /// <summary>Puts saved samples back (V5): same ids, states and queue.</summary>
    public void Restore(IEnumerable<(CellReport Source, long TakenSol, SampleState State, long? ReadySol)> samples, long labFreeSol)
    {
        ArgumentNullException.ThrowIfNull(samples);
        _samples.Clear();
        foreach (var (source, takenSol, state, readySol) in samples)
        {
            _samples.Add(new Sample(_samples.Count + 1, source, takenSol) { State = state, ReadySol = readySol });
        }

        _labFreeSol = labFreeSol;
    }

    /// <summary>Research points an analysed sample of this kind earns.</summary>
    public double ResearchFor(Sample sample)
    {
        ArgumentNullException.ThrowIfNull(sample);
        return sample.Kind == SampleKind.Biological ? Definition.ResearchBiological : Definition.ResearchRegolith;
    }
}
