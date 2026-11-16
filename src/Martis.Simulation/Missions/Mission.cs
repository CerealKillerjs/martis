using System.Globalization;
using System.Text.Json;
using Martis.Domain.Parameters;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Missions;

/// <summary>Kinds of mission objective (T09, ADR-016).</summary>
public enum ObjectiveKind
{
    /// <summary>A player action outside the engine (analyse, experiment), reported by the host.</summary>
    Action,

    /// <summary>A population whose genome differs from its organism's base genome.</summary>
    Design,

    /// <summary>Any living population of an organism with the capability.</summary>
    Deploy,

    /// <summary>Total biomass of the capability (and organism, inside the temperature band when given) at or above a minimum for consecutive sols.</summary>
    Sustain,

    /// <summary>A number of new variants created by mutation.</summary>
    Mutations,

    /// <summary>Sols that pass while the objective is active (tutorial: «deja correr el tiempo»).</summary>
    Elapsed,

    /// <summary>Distinct living genomes of the capability at the same time, at or above a count.</summary>
    Diversity,

    /// <summary>Distinct cells with living populations of the capability (or organism), optionally inside a temperature band (H4).</summary>
    Occupy,

    /// <summary>Dead biomass recycled by decomposers since the mission started, at or above an amount in kg (H4).</summary>
    Recycle,

    /// <summary>Gain of one pool resource over the whole map since the mission started, at or above an amount in kg (H4).</summary>
    Accumulate,
}

/// <summary>One objective. Thresholds are parameters with classification (H-19 pending).</summary>
public sealed record MissionObjective(
    string Id,
    ObjectiveKind Kind,
    string Title,
    string? Action,
    string? Capability,
    double MinBiomassKg,
    double Sols,
    double Count,
    string? Hint = null,
    string? Organism = null,
    double? MinTemperatureK = null,
    double? MaxTemperatureK = null,
    string? Resource = null,
    double Amount = 0.0);

/// <summary>
/// A mission loaded from <c>data/missions/*.json</c>. <see cref="Briefing"/> is the story
/// text the game shows before playing. A <see cref="Sequential"/> mission (tutorial) only
/// lets the first pending objective complete; the others wait their turn (campaign, H1).
/// </summary>
public sealed record MissionDefinition(
    string Id,
    string Name,
    IReadOnlyList<MissionObjective> Objectives,
    string? Briefing = null,
    bool Sequential = false)
{
    public const string FirstColonyFile = "first_colony.v1.json";

    public static MissionDefinition LoadPublished(string repositoryRoot, string fileName = FirstColonyFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "missions", fileName)));
    }

    public static MissionDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.mission")
        {
            throw new InvalidDataException("A mission file needs metadata.schema = martis.mission.");
        }

        var objectives = new List<MissionObjective>();
        foreach (var item in meta.GetProperty("objectives").EnumerateArray())
        {
            var kind = item.GetProperty("type").GetString() switch
            {
                "action" => ObjectiveKind.Action,
                "design" => ObjectiveKind.Design,
                "deploy" => ObjectiveKind.Deploy,
                "sustain" => ObjectiveKind.Sustain,
                "mutations" => ObjectiveKind.Mutations,
                "elapsed" => ObjectiveKind.Elapsed,
                "diversity" => ObjectiveKind.Diversity,
                "occupy" => ObjectiveKind.Occupy,
                "recycle" => ObjectiveKind.Recycle,
                "accumulate" => ObjectiveKind.Accumulate,
                var other => throw new InvalidDataException($"Unknown objective type '{other}'."),
            };
            objectives.Add(new MissionObjective(
                item.GetProperty("id").GetString()!,
                kind,
                item.GetProperty("title").GetString()!,
                Text(item, "action"),
                Text(item, "capability"),
                Number(item, "min_biomass", parameters),
                Number(item, "sols", parameters),
                Number(item, "count", parameters),
                Text(item, "hint"),
                Text(item, "organism"),
                Optional(item, "min_temperature", parameters),
                Optional(item, "max_temperature", parameters),
                Text(item, "resource"),
                Number(item, "amount", parameters)));
            var added = objectives[^1];
            if (kind == ObjectiveKind.Accumulate && (added.Resource is null || ResourceSlot(added.Resource) < 0))
            {
                throw new InvalidDataException($"Objective '{added.Id}' needs a pool resource ({string.Join(", ", ResourceColumns.Names)}).");
            }
        }

        if (objectives.Count == 0 || objectives.Select(objective => objective.Id).Distinct(StringComparer.Ordinal).Count() != objectives.Count)
        {
            throw new InvalidDataException("A mission needs at least one objective and unique objective ids.");
        }

        return new MissionDefinition(
            meta.GetProperty("id").GetString()!,
            meta.GetProperty("name").GetString()!,
            objectives,
            Text(meta, "briefing"),
            meta.TryGetProperty("sequential", out var sequential) && sequential.GetBoolean());
    }

    private static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static double Number(JsonElement item, string name, ParameterCatalog parameters)
        => item.TryGetProperty(name, out var value) ? parameters.RequireNumber(value.GetString()!).Value : 0.0;

    private static double? Optional(JsonElement item, string name, ParameterCatalog parameters)
        => item.TryGetProperty(name, out var value) ? parameters.RequireNumber(value.GetString()!).Value : null;

    /// <summary>Position of a pool resource in <see cref="ResourceColumns.Names"/>, or -1.</summary>
    public static int ResourceSlot(string resource)
    {
        for (var slot = 0; slot < ResourceColumns.Names.Count; slot++)
        {
            if (string.Equals(ResourceColumns.Names[slot], resource, StringComparison.Ordinal))
            {
                return slot;
            }
        }

        return -1;
    }
}

/// <summary>Progress of one objective. <see cref="CompletedTick"/> is null while pending.</summary>
public sealed record ObjectiveStatus(string Id, string Title, long? CompletedTick, double Progress, double Target, string? Hint = null, bool Active = false);

/// <summary>
/// Everything a <see cref="MissionTracker"/> remembers, so a save can resume a mission where
/// it was (H-31): completed objectives with their tick, reported actions, variants born by
/// mutation, the sustain streak, the mutation count and the sols of each elapsed objective.
/// </summary>
public sealed record MissionProgress(
    string MissionId,
    IReadOnlyDictionary<string, long> Completed,
    IReadOnlyList<string> Actions,
    IReadOnlyList<long> MutantIds,
    long Streak,
    long Mutations,
    IReadOnlyDictionary<string, long> Elapsed,
    double Recycled = 0.0,
    IReadOnlyDictionary<string, double>? Baselines = null)
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(new Dto(MissionId, new SortedDictionary<string, long>(Completed.ToDictionary(), StringComparer.Ordinal), [.. Actions.Order(StringComparer.Ordinal)], [.. MutantIds.Order()], Streak, Mutations, new SortedDictionary<string, long>(Elapsed.ToDictionary(), StringComparer.Ordinal), Recycled, Baselines is null ? null : new SortedDictionary<string, double>(Baselines.ToDictionary(), StringComparer.Ordinal)), Options);

    public static MissionProgress FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The mission progress is not valid JSON.", exception);
        }

        if (dto is null || string.IsNullOrWhiteSpace(dto.MissionId) || dto.Streak < 0 || dto.Mutations < 0
            || !double.IsFinite(dto.Recycled) || dto.Recycled < 0 || (dto.Baselines?.Values.Any(value => !double.IsFinite(value) || value < 0) ?? false))
        {
            throw new InvalidDataException("The mission progress is incomplete.");
        }

        return new MissionProgress(dto.MissionId, dto.Completed ?? new(), dto.Actions ?? [], dto.MutantIds ?? [], dto.Streak, dto.Mutations, dto.Elapsed ?? new(), dto.Recycled, dto.Baselines);
    }

    private sealed record Dto(
        string MissionId,
        SortedDictionary<string, long>? Completed,
        string[]? Actions,
        long[]? MutantIds,
        long Streak,
        long Mutations,
        SortedDictionary<string, long>? Elapsed,
        double Recycled = 0.0,
        SortedDictionary<string, double>? Baselines = null);
}

/// <summary>
/// Follows a mission during a run (T09). The host calls <see cref="Observe"/> after every
/// tick and <see cref="RecordAction"/> for player actions. An objective, once complete,
/// stays complete. <see cref="Export"/> and <see cref="Restore"/> carry the progress through
/// a save (H-31), so a resumed run keeps its sustain streak.
/// In a sequential mission only <see cref="ActiveObjective"/> can complete; an action the
/// player did earlier counts as soon as its objective becomes active.
/// </summary>
public sealed class MissionTracker
{
    private readonly BiologyCatalog _catalog;
    private readonly Dictionary<string, long> _completed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _elapsed = new(StringComparer.Ordinal);

    /// <summary>Variants born by mutation: they are not player designs.</summary>
    private readonly HashSet<long> _mutantIds = [];
    private readonly Dictionary<string, double> _baselines = new(StringComparer.Ordinal);
    private readonly Dictionary<string, double> _gains = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _occupied = new(StringComparer.Ordinal);
    private long _streak;
    private long _mutations;
    private double _recycled;
    private int _diversity;

    public MissionTracker(MissionDefinition definition, BiologyCatalog catalog)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public MissionDefinition Definition { get; }

    public bool IsComplete => Definition.Objectives.All(objective => _completed.ContainsKey(objective.Id));

    public long? CompletedTick => IsComplete ? _completed.Values.Max() : null;

    /// <summary>The first pending objective, or null when the mission is complete.</summary>
    public MissionObjective? ActiveObjective => Definition.Objectives.FirstOrDefault(objective => !_completed.ContainsKey(objective.Id));

    public void RecordAction(string action, long tick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        _actions.Add(action);
        CompleteActions(tick);
    }

    /// <summary>
    /// Takes the map totals that accumulate objectives measure from (H4). Call on a new game
    /// before the first tick; a resumed mission keeps the totals of its save.
    /// </summary>
    public void Begin(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var objective in Definition.Objectives.Where(objective => objective.Kind == ObjectiveKind.Accumulate))
        {
            _baselines.TryAdd(objective.Resource!, Total(state, objective.Resource!));
        }
    }

    public void Observe(SimulationState state, MetabolismWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(state);
        Begin(state);
        _recycled += workspace?.Totals.BiomassRecycledKg ?? 0.0;
        foreach (var item in workspace?.MutationEvents ?? [])
        {
            _mutations++;
            _mutantIds.Add(item.ChildId);
        }

        var counted = false;
        _diversity = -1;
        foreach (var objective in Definition.Objectives)
        {
            if (_completed.ContainsKey(objective.Id))
            {
                continue;
            }

            var open = CanComplete(objective);
            switch (objective.Kind)
            {
                case ObjectiveKind.Design when open && Living(state, objective.Capability).Any(Designed):
                case ObjectiveKind.Deploy when open && Living(state, objective.Capability).Any():
                    Complete(objective, state.Tick);
                    break;
                case ObjectiveKind.Sustain:
                    if (!counted && (open || !Definition.Sequential))
                    {
                        var biomass = Matching(state, objective).Sum(population => population.BiomassKg);
                        _streak = biomass >= objective.MinBiomassKg ? _streak + 1 : 0;
                        counted = true;
                    }

                    if (open && _streak >= objective.Sols)
                    {
                        Complete(objective, state.Tick);
                    }

                    break;
                case ObjectiveKind.Mutations when open && _mutations >= objective.Count:
                    Complete(objective, state.Tick);
                    break;
                case ObjectiveKind.Elapsed when open:
                    var sols = _elapsed[objective.Id] = _elapsed.GetValueOrDefault(objective.Id) + 1;
                    if (sols >= objective.Sols)
                    {
                        Complete(objective, state.Tick);
                    }

                    break;
                case ObjectiveKind.Diversity:
                    _diversity = Genomes(state, objective.Capability);
                    if (open && _diversity >= objective.Count)
                    {
                        Complete(objective, state.Tick);
                    }

                    break;
                case ObjectiveKind.Occupy:
                    var cells = _occupied[objective.Id] = Occupied(state, objective);
                    if (open && cells >= objective.Count)
                    {
                        Complete(objective, state.Tick);
                    }

                    break;
                case ObjectiveKind.Recycle when open && _recycled >= objective.Amount:
                    Complete(objective, state.Tick);
                    break;
                case ObjectiveKind.Accumulate:
                    var gain = _gains[objective.Id] = Total(state, objective.Resource!) - _baselines[objective.Resource!];
                    if (open && gain >= objective.Amount)
                    {
                        Complete(objective, state.Tick);
                    }

                    break;
            }
        }

        CompleteActions(state.Tick);
    }

    public IReadOnlyList<ObjectiveStatus> Status()
    {
        var active = ActiveObjective;
        return Definition.Objectives.Select(objective => new ObjectiveStatus(
            objective.Id,
            objective.Title,
            _completed.TryGetValue(objective.Id, out var tick) ? tick : null,
            _completed.ContainsKey(objective.Id) ? Target(objective) : objective.Kind switch
            {
                ObjectiveKind.Sustain => Math.Min(_streak, objective.Sols),
                ObjectiveKind.Mutations => Math.Min(_mutations, objective.Count),
                ObjectiveKind.Elapsed => Math.Min(_elapsed.GetValueOrDefault(objective.Id), objective.Sols),
                ObjectiveKind.Diversity => Math.Clamp(_diversity, 0, objective.Count),
                ObjectiveKind.Occupy => Math.Min(_occupied.GetValueOrDefault(objective.Id), objective.Count),
                ObjectiveKind.Recycle => Math.Min(_recycled, objective.Amount),
                ObjectiveKind.Accumulate => Math.Clamp(_gains.GetValueOrDefault(objective.Id), 0, objective.Amount),
                _ => 0,
            },
            Target(objective),
            objective.Hint,
            ReferenceEquals(objective, active))).ToArray();
    }

    /// <summary>A copy of the progress for a save (H-31).</summary>
    public MissionProgress Export()
        => new(
            Definition.Id,
            new Dictionary<string, long>(_completed, StringComparer.Ordinal),
            [.. _actions.Order(StringComparer.Ordinal)],
            [.. _mutantIds.Order()],
            _streak,
            _mutations,
            new Dictionary<string, long>(_elapsed, StringComparer.Ordinal),
            _recycled,
            _baselines.Count == 0 ? null : new Dictionary<string, double>(_baselines, StringComparer.Ordinal));

    /// <summary>Replaces the progress with a saved one of the same mission. Unknown objective ids are refused.</summary>
    public void Restore(MissionProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        if (!string.Equals(progress.MissionId, Definition.Id, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"The saved progress belongs to mission '{progress.MissionId}', not '{Definition.Id}'.");
        }

        var ids = Definition.Objectives.Select(objective => objective.Id).ToHashSet(StringComparer.Ordinal);
        if (progress.Completed.Keys.Concat(progress.Elapsed.Keys).Any(id => !ids.Contains(id)))
        {
            throw new InvalidDataException("The saved progress names an objective this mission does not have.");
        }

        _completed.Clear();
        _actions.Clear();
        _mutantIds.Clear();
        _elapsed.Clear();
        foreach (var (id, tick) in progress.Completed)
        {
            _completed[id] = tick;
        }

        _actions.UnionWith(progress.Actions);
        _mutantIds.UnionWith(progress.MutantIds);
        foreach (var (id, sols) in progress.Elapsed)
        {
            _elapsed[id] = sols;
        }

        _streak = progress.Streak;
        _mutations = progress.Mutations;
        _recycled = progress.Recycled;
        _baselines.Clear();
        foreach (var (resource, total) in progress.Baselines ?? new Dictionary<string, double>())
        {
            _baselines[resource] = total;
        }
    }

    private static double Target(MissionObjective objective) => objective.Kind switch
    {
        ObjectiveKind.Sustain or ObjectiveKind.Elapsed => objective.Sols,
        ObjectiveKind.Mutations or ObjectiveKind.Diversity or ObjectiveKind.Occupy => objective.Count,
        ObjectiveKind.Recycle or ObjectiveKind.Accumulate => objective.Amount,
        _ => 1,
    };

    private bool CanComplete(MissionObjective objective)
        => !Definition.Sequential || ReferenceEquals(objective, ActiveObjective);

    /// <summary>Completes every reported action whose objective may complete, walking forward in a sequential mission.</summary>
    private void CompleteActions(long tick)
    {
        bool progressed;
        do
        {
            progressed = false;
            foreach (var objective in Definition.Objectives)
            {
                if (objective.Kind == ObjectiveKind.Action
                    && !_completed.ContainsKey(objective.Id)
                    && objective.Action is not null
                    && _actions.Contains(objective.Action)
                    && CanComplete(objective))
                {
                    Complete(objective, tick);
                    progressed = true;
                }
            }
        }
        while (progressed && Definition.Sequential);
    }

    private int Genomes(SimulationState state, string? capability)
        => Living(state, capability)
            .Select(population => string.Join(';', population.Genome.Expressions.Select(gene => string.Create(CultureInfo.InvariantCulture, $"{gene.GeneId.Value}={gene.Expression:R}"))))
            .Distinct(StringComparer.Ordinal)
            .Count();

    private IEnumerable<Domain.Biology.Population> Living(SimulationState state, string? capability)
        => state.Populations.Where(population =>
            population.BiomassKg > 0.0
            && _catalog.TryGetOrganism(population.OrganismId.Value, out var organism)
            && (capability is null || organism!.Capabilities.Contains(capability, StringComparer.Ordinal)));

    private int Occupied(SimulationState state, MissionObjective objective)
        => Matching(state, objective).Select(population => population.CellIndex).Distinct().Count();

    /// <summary>Living populations of the objective's capability and organism inside its temperature band.</summary>
    private IEnumerable<Domain.Biology.Population> Matching(SimulationState state, MissionObjective objective)
        => Living(state, objective.Capability)
            .Where(population => (objective.Organism is null || string.Equals(population.OrganismId.Value, objective.Organism, StringComparison.Ordinal))
                && InBand(state.Environment[population.CellIndex], objective));

    private static bool InBand(Domain.Environment.EnvironmentState environment, MissionObjective objective)
    {
        if (objective.MinTemperatureK is null && objective.MaxTemperatureK is null)
        {
            return true;
        }

        if (!environment.TemperatureKelvin.IsSet)
        {
            return false;
        }

        var kelvin = environment.TemperatureKelvin.Value;
        return kelvin >= (objective.MinTemperatureK ?? double.NegativeInfinity) && kelvin <= (objective.MaxTemperatureK ?? double.PositiveInfinity);
    }

    private static double Total(SimulationState state, string resource)
    {
        var slot = MissionDefinition.ResourceSlot(resource);
        var total = 0.0;
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            total += state.Resources[cell].ValueAt(slot);
        }

        return total;
    }

    private bool Designed(Domain.Biology.Population population)
        => _catalog.TryGetOrganism(population.OrganismId.Value, out var organism)
            && EvolutionDraws.Distance(population.Genome, organism!.Definition.BaseGenome.BaseGenome) > 0.0
            && !_mutantIds.Contains(population.Id.Value);

    private void Complete(MissionObjective objective, long tick) => _completed.TryAdd(objective.Id, tick);
}
