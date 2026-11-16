using Martis.Contracts.Commands;
using Martis.Domain.Biology;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Missions;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Sessions;

/// <summary>Per-cell arrays for drawing the map (T08). Index = cell. Values are copies.</summary>
public sealed record WorldView(
    long Tick,
    int Width,
    int Height,
    float[] TemperatureKelvin,
    float[] WaterKg,
    float[] RadiationSvPerSol,
    float[] Irradiance,
    float[] BiomassKg,
    float[] PhototrophBiomassKg,
    float[] DecomposerBiomassKg,
    float[] VariantHue,
    int[] VariantCount,
    double TotalBiomassKg,
    int PopulationCount);

/// <summary>One population as the inspector shows it.</summary>
public sealed record PopulationReport(
    long Id,
    string OrganismId,
    string OrganismName,
    double BiomassKg,
    double Abundance,
    double EnergyReserveJ,
    double AgeSols,
    double Viability,
    IReadOnlyDictionary<string, double> Genome);

/// <summary>Everything the inspector shows for one cell.</summary>
public sealed record CellReport(
    int CellIndex,
    int X,
    int Y,
    double? TemperatureKelvin,
    double? PressurePascal,
    double RadiationSvPerSol,
    double IrradianceWPerM2,
    IReadOnlyDictionary<string, double> ResourcesKg,
    IReadOnlyList<PopulationReport> Populations);

/// <summary>Result of a laboratory run: total biomass of the test culture after each sol.</summary>
public sealed record ExperimentForecast(string OrganismId, int Sols, IReadOnlyList<double> BiomassKg, bool Survived, string? Failure);

/// <summary>A line for the event feed: mutation, extinction, mission objective or failure. <see cref="Cell"/> is -1 when the event has no place.</summary>
public sealed record SessionEvent(long Tick, string Kind, string Text, int Cell = -1);

/// <summary>An organism the laboratory offers, with its base genome (genes in [0, 1]).</summary>
public sealed record OrganismInfo(string Id, string Name, string Category, IReadOnlyList<KeyValuePair<string, double>> BaseGenome);

/// <summary>
/// One playable run (T08): the engine with the biology pipeline, the mission and an event
/// feed, behind an API the Godot game calls. It owns no Godot type, so it is tested here.
/// The game only sends commands and reads copies (TDD §8: no mutable internals leak out).
/// </summary>
public sealed class GameSession
{
    public const int MaxEvents = 200;
    private readonly List<SessionEvent> _events = [];
    private readonly HashSet<string> _reportedObjectives = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _organismIndex;

    private GameSession(MarsBaselineScenario scenario, BiologyCatalog biology, MissionDefinition? mission, ulong seed, StateSnapshot? resume, MissionProgress? progress = null)
    {
        Scenario = scenario;
        Biology = biology;
        Seed = seed;
        (Engine, Workspace) = MarsBaselineWorld.CreateWithBiology(scenario, biology, seed);
        if (resume is not null)
        {
            resume.Restore(Engine);
        }

        Mission = mission is null ? null : new MissionTracker(mission, biology);
        if (progress is not null)
        {
            if (Mission is null)
            {
                throw new InvalidOperationException("Mission progress needs a mission.");
            }

            Mission.Restore(progress);
            foreach (var status in Mission.Status().Where(status => status.CompletedTick is not null))
            {
                _reportedObjectives.Add(status.Id);
            }

            if (Mission.IsComplete)
            {
                _reportedObjectives.Add("*");
            }
        }
        Organisms = biology.Organisms
            .Select(organism => new OrganismInfo(
                organism.Id,
                organism.Definition.Name,
                organism.Definition.Category,
                organism.Definition.BaseGenome.BaseGenome.Expressions.Select(gene => KeyValuePair.Create(gene.GeneId.Value, gene.Expression)).ToArray()))
            .ToArray();
        _organismIndex = biology.Organisms.Select((organism, index) => (organism.Id, index))
            .ToDictionary(item => item.Id, item => item.index, StringComparer.Ordinal);
    }

    public MarsBaselineScenario Scenario { get; }

    public BiologyCatalog Biology { get; }

    public ulong Seed { get; }

    public SimulationEngine Engine { get; }

    public MetabolismWorkspace Workspace { get; }

    public MissionTracker? Mission { get; }

    /// <summary>Organisms of the catalog in file order.</summary>
    public IReadOnlyList<OrganismInfo> Organisms { get; }

    public long Tick => Engine.CurrentTick;

    public int Width => Engine.State.Width;

    public int Height => Engine.State.Height;

    /// <summary>Set when a tick failed; the session then refuses to advance.</summary>
    public string? Failure { get; private set; }

    /// <summary>Newest events last, at most <see cref="MaxEvents"/>.</summary>
    public IReadOnlyList<SessionEvent> Events => _events;

    /// <summary>Events ever added, including those dropped from <see cref="Events"/>; lets a host find the new ones.</summary>
    public long TotalEvents { get; private set; }

    public static GameSession NewGame(string repositoryRoot, ulong seed, bool withMission = true)
        => new(
            MarsBaselineScenario.LoadPublished(repositoryRoot),
            BiologyCatalog.LoadPublished(repositoryRoot),
            withMission ? MissionDefinition.LoadPublished(repositoryRoot) : null,
            seed,
            null);

    /// <summary>A new game of one campaign mission (<c>data/missions/<paramref name="missionFile"/></c>).</summary>
    public static GameSession NewGame(string repositoryRoot, ulong seed, string missionFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(missionFile);
        return new(
            MarsBaselineScenario.LoadPublished(repositoryRoot),
            BiologyCatalog.LoadPublished(repositoryRoot),
            MissionDefinition.LoadPublished(repositoryRoot, missionFile),
            seed,
            null);
    }

    /// <summary>
    /// Resumes a campaign mission from a snapshot and, when the save has it, the mission
    /// progress at that tick (H-31). Without progress the mission starts over.
    /// </summary>
    public static GameSession Resume(string repositoryRoot, StateSnapshot snapshot, string missionFile, MissionProgress? progress)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(missionFile);
        return new(
            MarsBaselineScenario.LoadPublished(repositoryRoot),
            BiologyCatalog.LoadPublished(repositoryRoot),
            MissionDefinition.LoadPublished(repositoryRoot, missionFile),
            snapshot.Configuration.WorldSeed,
            snapshot,
            progress);
    }

    public static GameSession Resume(string repositoryRoot, StateSnapshot snapshot, bool withMission = true)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new(
            MarsBaselineScenario.LoadPublished(repositoryRoot),
            BiologyCatalog.LoadPublished(repositoryRoot),
            withMission ? MissionDefinition.LoadPublished(repositoryRoot) : null,
            snapshot.Configuration.WorldSeed,
            snapshot);
    }

    public int CellIndex(int x, int y) => Engine.State.ToLinearIndex(x, y);

    /// <summary>A player action for the mission (<c>analyze-region</c>, <c>run-experiment</c>).</summary>
    public void RecordAction(string action)
    {
        Mission?.RecordAction(action, Tick);
        ReportMission();
    }

    /// <summary>Queues an inoculation for the next tick. Returns false with a reason when the request is invalid.</summary>
    public bool Introduce(string organismId, int x, int y, double biomassKg, IReadOnlyDictionary<string, double>? genes, out string? error)
    {
        error = Check(organismId, x, y, biomassKg, genes);
        if (error is not null)
        {
            return false;
        }

        Engine.ApplyCommand(new IntroducePopulationCommand(organismId, CellIndex(x, y), biomassKg, genes));
        Add("deploy", $"Desplegados {biomassKg:0.###} kg de {Biology.Organism(organismId).Definition.Name} en ({x}, {y})", CellIndex(x, y));
        return true;
    }

    /// <summary>Advances <paramref name="ticks"/> sols. Stops at the first failure and keeps the state of the last good tick.</summary>
    public int Advance(int ticks)
    {
        var done = 0;
        for (; done < ticks && Failure is null; done++)
        {
            try
            {
                Engine.Advance(1);
            }
            catch (Exception exception) when (exception is SimulationInvariantException or ArithmeticException or ArgumentException or InvalidOperationException or KeyNotFoundException)
            {
                Engine.DiscardPendingCommands();
                Failure = exception.Message;
                Add("failure", $"La simulación se detuvo: {exception.Message}");
                break;
            }

            foreach (var item in Workspace.MutationEvents)
            {
                var genes = string.Join(", ", item.Changes.Select(change => $"{change.GeneId} {change.Before:0.00}→{change.After:0.00}"));
                Add("mutation", $"Nueva variante #{item.ChildId} de {Name(item.OrganismId)} en ({item.CellIndex % Width}, {item.CellIndex / Width}): {genes}", item.CellIndex);
            }

            foreach (var id in Workspace.Extinct)
            {
                Add("extinction", $"La población #{id} se extinguió");
            }

            Mission?.Observe(Engine.State, Workspace);
            ReportMission();
        }

        return done;
    }

    /// <summary>Clears a failure after the host restored a good state, for example by loading a save.</summary>
    public void ClearFailure() => Failure = null;

    public StateSnapshot Capture() => StateSnapshot.Capture(Engine, null);

    /// <summary>The mission progress to store next to a snapshot (H-31), or null without a mission.</summary>
    public MissionProgress? CaptureMission() => Mission?.Export();

    public WorldView View()
    {
        var state = Engine.State;
        var cells = state.CellCount;
        var temperature = new float[cells];
        var water = new float[cells];
        var radiation = new float[cells];
        var light = new float[cells];
        for (var i = 0; i < cells; i++)
        {
            var environment = state.Environment[i];
            temperature[i] = environment.TemperatureKelvin.IsSet ? (float)environment.TemperatureKelvin.Value : 0f;
            water[i] = (float)state.Resources[i].Water;
            radiation[i] = (float)environment.IonizingEquivalentDoseSvPerSol;
            light[i] = (float)environment.SolarIrradiance;
        }

        var biomass = new float[cells];
        var phototroph = new float[cells];
        var decomposer = new float[cells];
        var hue = new float[cells];
        var variants = new int[cells];
        var dominant = new double[cells];
        var total = 0.0;
        foreach (var population in state.Populations)
        {
            var cell = population.CellIndex;
            var kg = population.BiomassKg;
            total += kg;
            biomass[cell] += (float)kg;
            variants[cell]++;
            var organism = Biology.Organism(population.OrganismId.Value);
            if (organism.Capabilities.Contains("photoautotrophy", StringComparer.Ordinal))
            {
                phototroph[cell] += (float)kg;
            }
            else
            {
                decomposer[cell] += (float)kg;
            }

            if (kg > dominant[cell])
            {
                dominant[cell] = kg;
                hue[cell] = Hue(population);
            }
        }

        return new WorldView(Tick, state.Width, state.Height, temperature, water, radiation, light, biomass, phototroph, decomposer, hue, variants, total, state.Populations.Count);
    }

    public CellReport Inspect(int x, int y)
    {
        var cell = CellIndex(x, y);
        var state = Engine.State;
        var environment = state.Environment[cell];
        var resources = state.Resources[cell].Quantities.ToDictionary(quantity => quantity.Name, quantity => quantity.Value, StringComparer.Ordinal);
        var populations = state.Populations
            .Where(population => population.CellIndex == cell)
            .Select(population => new PopulationReport(
                population.Id.Value,
                population.OrganismId.Value,
                Name(population.OrganismId.Value),
                population.BiomassKg,
                population.Abundance,
                population.EnergyReserveJ,
                population.AgeDistributionMeanSols,
                population.Viability,
                population.Genome.Expressions.ToDictionary(gene => gene.GeneId.Value, gene => gene.Expression, StringComparer.Ordinal)))
            .ToArray();
        return new CellReport(
            cell,
            x,
            y,
            environment.TemperatureKelvin.IsSet ? environment.TemperatureKelvin.Value : null,
            environment.PressurePascal.IsSet ? environment.PressurePascal.Value : null,
            environment.IonizingEquivalentDoseSvPerSol,
            environment.SolarIrradiance,
            resources,
            populations);
    }

    /// <summary>
    /// Laboratory (TDD B.6 «experimentar»): runs a copy of the current world with the test
    /// culture for <paramref name="sols"/> sols and reports the biomass of that organism in the
    /// target cell (existing variants included). The live world, its PRNG and its queue are
    /// not touched. Records the <c>run-experiment</c> action.
    /// </summary>
    public ExperimentForecast Experiment(string organismId, int x, int y, double biomassKg, IReadOnlyDictionary<string, double>? genes, int sols)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sols);
        var error = Check(organismId, x, y, biomassKg, genes);
        if (error is not null)
        {
            return new ExperimentForecast(organismId, 0, [], false, error);
        }

        if (Engine.PendingCommandCount > 0)
        {
            return new ExperimentForecast(organismId, 0, [], false, "Hay un despliegue pendiente: avanza un sol antes de experimentar");
        }

        var (lab, _) = MarsBaselineWorld.CreateWithBiology(Scenario, Biology, Seed);
        Capture().Restore(lab);
        var cell = CellIndex(x, y);
        lab.ApplyCommand(new IntroducePopulationCommand(organismId, cell, biomassKg, genes));
        var series = new List<double>(sols);
        string? failure = null;
        for (var sol = 0; sol < sols; sol++)
        {
            try
            {
                lab.Advance(1);
            }
            catch (Exception exception) when (exception is SimulationInvariantException or ArithmeticException or ArgumentException or InvalidOperationException)
            {
                failure = exception.Message;
                break;
            }

            // The culture is everything of that organism in the target cell: a design equal
            // to a variant already there merges into it, so ids alone would lose track of it.
            series.Add(lab.State.Populations
                .Where(population => population.CellIndex == cell && string.Equals(population.OrganismId.Value, organismId, StringComparison.Ordinal))
                .Sum(population => population.BiomassKg));
        }

        RecordAction("run-experiment");
        Add("experiment", $"Laboratorio: {Name(organismId)} durante {series.Count} soles, biomasa final {(series.Count == 0 ? 0 : series[^1]):0.####} kg");
        return new ExperimentForecast(organismId, series.Count, series, series.Count == sols && series[^1] > 0, failure);
    }

    /// <summary>Hue in [0, 1) that tells variants apart on the map, from the genome only.</summary>
    public static float Hue(Population population)
    {
        ArgumentNullException.ThrowIfNull(population);
        var value = 0.0;
        var weight = 0.61803398875;
        foreach (var gene in population.Genome.Expressions)
        {
            value += gene.Expression * weight;
            weight = (weight * 1.61803398875) % 1.0;
        }

        return (float)(value - Math.Floor(value));
    }

    public int OrganismIndex(string organismId) => _organismIndex[organismId];

    private string? Check(string organismId, int x, int y, double biomassKg, IReadOnlyDictionary<string, double>? genes)
    {
        if (!Biology.TryGetOrganism(organismId, out var organism))
        {
            return $"Organismo desconocido: {organismId}";
        }

        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return "La celda está fuera del mapa";
        }

        if (!double.IsFinite(biomassKg) || biomassKg <= 0)
        {
            return "La biomasa tiene que ser positiva";
        }

        foreach (var (gene, value) in genes ?? new Dictionary<string, double>())
        {
            if (!organism!.HasGene(gene) || value is < 0.0 or > 1.0 || double.IsNaN(value))
            {
                return $"Gen no válido: {gene}={value}";
            }
        }

        return null;
    }

    private string Name(string organismId)
        => Biology.TryGetOrganism(organismId, out var organism) ? organism!.Definition.Name : organismId;

    private void ReportMission()
    {
        if (Mission is null)
        {
            return;
        }

        foreach (var status in Mission.Status())
        {
            if (status.CompletedTick is not null && _reportedObjectives.Add(status.Id))
            {
                Add("mission", $"Objetivo cumplido: {status.Title}");
            }
        }

        if (Mission.IsComplete && _reportedObjectives.Add("*"))
        {
            Add("mission", $"¡Misión {Mission.Definition.Name} completada!");
        }
    }

    private void Add(string kind, string text, int cell = -1)
    {
        _events.Add(new SessionEvent(Tick, kind, text, cell));
        TotalEvents++;
        if (_events.Count > MaxEvents)
        {
            _events.RemoveRange(0, _events.Count - MaxEvents);
        }
    }
}
