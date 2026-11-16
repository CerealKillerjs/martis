using System.Text.Json;
using Martis.Domain.Parameters;
using Martis.Domain.World;
using Martis.Simulation.Biology;

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

    /// <summary>Total biomass of the capability at or above a minimum for consecutive sols.</summary>
    Sustain,

    /// <summary>A number of new variants created by mutation.</summary>
    Mutations,
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
    double Count);

/// <summary>A mission loaded from <c>data/missions/*.json</c>.</summary>
public sealed record MissionDefinition(string Id, string Name, IReadOnlyList<MissionObjective> Objectives)
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
                Number(item, "count", parameters)));
        }

        return new MissionDefinition(meta.GetProperty("id").GetString()!, meta.GetProperty("name").GetString()!, objectives);
    }

    private static string? Text(JsonElement item, string name)
        => item.TryGetProperty(name, out var value) ? value.GetString() : null;

    private static double Number(JsonElement item, string name, ParameterCatalog parameters)
        => item.TryGetProperty(name, out var value) ? parameters.RequireNumber(value.GetString()!).Value : 0.0;
}

/// <summary>Progress of one objective. <see cref="CompletedTick"/> is null while pending.</summary>
public sealed record ObjectiveStatus(string Id, string Title, long? CompletedTick, double Progress, double Target);

/// <summary>
/// Follows a mission during a run (T09). The host calls <see cref="Observe"/> after every
/// tick and <see cref="RecordAction"/> for player actions. An objective, once complete,
/// stays complete. Progress lives here, not in the save: a resumed run restarts the
/// sustain streak (ADR-016).
/// </summary>
public sealed class MissionTracker
{
    private readonly BiologyCatalog _catalog;
    private readonly Dictionary<string, long> _completed = new(StringComparer.Ordinal);
    private readonly HashSet<string> _actions = new(StringComparer.Ordinal);

    /// <summary>Variants born by mutation: they are not player designs.</summary>
    private readonly HashSet<long> _mutantIds = [];
    private long _streak;
    private long _mutations;

    public MissionTracker(MissionDefinition definition, BiologyCatalog catalog)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
    }

    public MissionDefinition Definition { get; }

    public bool IsComplete => Definition.Objectives.All(objective => _completed.ContainsKey(objective.Id));

    public long? CompletedTick => IsComplete ? _completed.Values.Max() : null;

    public void RecordAction(string action, long tick)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        _actions.Add(action);
        foreach (var objective in Definition.Objectives)
        {
            if (objective.Kind == ObjectiveKind.Action && objective.Action == action)
            {
                Complete(objective, tick);
            }
        }
    }

    public void Observe(SimulationState state, MetabolismWorkspace? workspace)
    {
        ArgumentNullException.ThrowIfNull(state);
        foreach (var item in workspace?.MutationEvents ?? [])
        {
            _mutations++;
            _mutantIds.Add(item.ChildId);
        }

        var counted = false;
        foreach (var objective in Definition.Objectives)
        {
            switch (objective.Kind)
            {
                case ObjectiveKind.Design when Living(state, objective.Capability).Any(Designed):
                case ObjectiveKind.Deploy when Living(state, objective.Capability).Any():
                    Complete(objective, state.Tick);
                    break;
                case ObjectiveKind.Sustain:
                    if (!counted)
                    {
                        var biomass = Living(state, objective.Capability).Sum(population => population.BiomassKg);
                        _streak = biomass >= objective.MinBiomassKg ? _streak + 1 : 0;
                        counted = true;
                    }

                    if (_streak >= objective.Sols)
                    {
                        Complete(objective, state.Tick);
                    }

                    break;
                case ObjectiveKind.Mutations when _mutations >= objective.Count:
                    Complete(objective, state.Tick);
                    break;
            }
        }
    }

    public IReadOnlyList<ObjectiveStatus> Status()
        => Definition.Objectives.Select(objective => new ObjectiveStatus(
            objective.Id,
            objective.Title,
            _completed.TryGetValue(objective.Id, out var tick) ? tick : null,
            objective.Kind switch
            {
                ObjectiveKind.Sustain => Math.Min(_streak, objective.Sols),
                ObjectiveKind.Mutations => Math.Min(_mutations, objective.Count),
                _ => _completed.ContainsKey(objective.Id) ? 1 : 0,
            },
            objective.Kind switch
            {
                ObjectiveKind.Sustain => objective.Sols,
                ObjectiveKind.Mutations => objective.Count,
                _ => 1,
            })).ToArray();

    private IEnumerable<Domain.Biology.Population> Living(SimulationState state, string? capability)
        => state.Populations.Where(population =>
            population.BiomassKg > 0.0
            && _catalog.TryGetOrganism(population.OrganismId.Value, out var organism)
            && (capability is null || organism!.Capabilities.Contains(capability, StringComparer.Ordinal)));

    private bool Designed(Domain.Biology.Population population)
        => _catalog.TryGetOrganism(population.OrganismId.Value, out var organism)
            && EvolutionDraws.Distance(population.Genome, organism!.Definition.BaseGenome.BaseGenome) > 0.0
            && !_mutantIds.Contains(population.Id.Value);

    private void Complete(MissionObjective objective, long tick) => _completed.TryAdd(objective.Id, tick);
}
