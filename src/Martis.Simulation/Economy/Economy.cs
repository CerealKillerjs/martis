using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Simulation.Economy;

/// <summary>What a technology changes (ADR-020).</summary>
public enum TechEffectKind
{
    /// <summary>How far each gene may move from its base value in the laboratory.</summary>
    GeneRange,

    /// <summary>Longest laboratory experiment, in sols.</summary>
    ForecastSols,

    /// <summary>Inoculum the lander's bioreactor holds, in kg.</summary>
    InoculumCapacity,

    /// <summary>Inoculum the bioreactor grows each sol, in kg.</summary>
    InoculumRecharge,

    /// <summary>Makes an organism of the catalog available.</summary>
    Organism,
}

public sealed record TechEffect(TechEffectKind Kind, double Value, string? OrganismId);

public sealed record Technology(string Id, string Title, string Description, double Cost, IReadOnlyList<string> Requires, IReadOnlyList<TechEffect> Effects);

/// <summary>Starting values of the campaign before any research.</summary>
public sealed record EconomyStart(double Research, double GeneRange, double ForecastSols, double InoculumCapacityKg, double InoculumRechargeKgPerSol, IReadOnlyList<string> Organisms);

/// <summary>Research points paid by the session.</summary>
public sealed record EconomyRewards(double Objective, double Mission, double Mutation, double MutationCap);

/// <summary>
/// The story-mode economy (H2, ADR-020), loaded from <c>data/economy/*.json</c>: research
/// points earned by playing and spent on a technology tree, and the inoculum of the lander's
/// bioreactor that every deployment consumes. Every number is a classified parameter.
/// </summary>
public sealed record EconomyDefinition(string Id, EconomyStart Start, EconomyRewards Rewards, IReadOnlyList<Technology> Technologies)
{
    public const string PublishedFile = "economy.v1.json";

    public static EconomyDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "economy", fileName)));
    }

    public static EconomyDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.economy")
        {
            throw new InvalidDataException("An economy file needs metadata.schema = martis.economy.");
        }

        double Number(JsonElement element, string name) => parameters.RequireNumber(element.GetProperty(name).GetString()!).Value;

        var start = meta.GetProperty("start");
        var rewards = meta.GetProperty("rewards");
        var technologies = new List<Technology>();
        foreach (var item in meta.GetProperty("technologies").EnumerateArray())
        {
            var effects = new List<TechEffect>();
            foreach (var effect in item.GetProperty("effects").EnumerateArray())
            {
                var kind = effect.GetProperty("type").GetString() switch
                {
                    "gene_range" => TechEffectKind.GeneRange,
                    "forecast_sols" => TechEffectKind.ForecastSols,
                    "inoculum_capacity" => TechEffectKind.InoculumCapacity,
                    "inoculum_recharge" => TechEffectKind.InoculumRecharge,
                    "organism" => TechEffectKind.Organism,
                    var other => throw new InvalidDataException($"Unknown technology effect '{other}'."),
                };
                effects.Add(kind == TechEffectKind.Organism
                    ? new TechEffect(kind, 0.0, effect.GetProperty("organism").GetString())
                    : new TechEffect(kind, Number(effect, "value"), null));
            }

            technologies.Add(new Technology(
                item.GetProperty("id").GetString()!,
                item.GetProperty("title").GetString()!,
                item.GetProperty("description").GetString()!,
                Number(item, "cost"),
                [.. item.GetProperty("requires").EnumerateArray().Select(value => value.GetString()!)],
                effects));
        }

        var ids = technologies.Select(technology => technology.Id).ToHashSet(StringComparer.Ordinal);
        if (ids.Count != technologies.Count)
        {
            throw new InvalidDataException("Technology ids must be unique.");
        }

        // Requirements must name earlier technologies, so the tree has no cycles.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var technology in technologies)
        {
            if (technology.Requires.Any(id => !seen.Contains(id)))
            {
                throw new InvalidDataException($"Technology '{technology.Id}' requires one that is unknown or listed after it.");
            }

            seen.Add(technology.Id);
        }

        return new EconomyDefinition(
            meta.GetProperty("id").GetString()!,
            new EconomyStart(
                Number(start, "research"),
                Number(start, "gene_range"),
                Number(start, "forecast_sols"),
                Number(start, "inoculum_capacity"),
                Number(start, "inoculum_recharge"),
                [.. start.GetProperty("organisms").EnumerateArray().Select(value => value.GetString()!)]),
            new EconomyRewards(Number(rewards, "objective"), Number(rewards, "mission"), Number(rewards, "mutation"), Number(rewards, "mutation_cap")),
            technologies);
    }

    public Technology Technology(string id)
        => Technologies.FirstOrDefault(technology => technology.Id == id)
            ?? throw new KeyNotFoundException($"Unknown technology '{id}'.");
}

/// <summary>
/// Research of the whole campaign: points and unlocked technologies. It outlives each mission
/// and is stored with the campaign progress. The limits it grants are the best of the start
/// values and every unlocked technology.
/// </summary>
public sealed class ResearchState
{
    private readonly HashSet<string> _unlocked = new(StringComparer.Ordinal);

    public ResearchState(EconomyDefinition definition)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        Points = definition.Start.Research;
    }

    public EconomyDefinition Definition { get; }

    public double Points { get; private set; }

    public IReadOnlyCollection<string> Unlocked => _unlocked;

    public double GeneRange => Best(TechEffectKind.GeneRange, Definition.Start.GeneRange);

    public int ForecastSols => (int)Best(TechEffectKind.ForecastSols, Definition.Start.ForecastSols);

    public double InoculumCapacityKg => Best(TechEffectKind.InoculumCapacity, Definition.Start.InoculumCapacityKg);

    public double InoculumRechargeKgPerSol => Best(TechEffectKind.InoculumRecharge, Definition.Start.InoculumRechargeKgPerSol);

    public bool IsUnlocked(string technologyId) => _unlocked.Contains(technologyId);

    public bool IsOrganismAvailable(string organismId)
        => Definition.Start.Organisms.Contains(organismId, StringComparer.Ordinal)
            || Effects().Any(effect => effect.Kind == TechEffectKind.Organism && effect.OrganismId == organismId);

    public void Earn(double points)
    {
        if (!double.IsFinite(points) || points < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(points), points, "Research points earned must be finite and not negative.");
        }

        Points += points;
    }

    /// <summary>Why a technology cannot be researched now, or null when it can.</summary>
    public string? Blocker(string technologyId)
    {
        var technology = Definition.Technologies.FirstOrDefault(item => item.Id == technologyId);
        if (technology is null)
        {
            return $"Tecnología desconocida: {technologyId}";
        }

        if (_unlocked.Contains(technologyId))
        {
            return "Ya está investigada";
        }

        var missing = technology.Requires.FirstOrDefault(id => !_unlocked.Contains(id));
        if (missing is not null)
        {
            return $"Requiere {Definition.Technology(missing).Title}";
        }

        return Points + 1e-9 < technology.Cost ? $"Faltan {technology.Cost - Points:0} PI" : null;
    }

    public bool TryResearch(string technologyId, out string? error)
    {
        error = Blocker(technologyId);
        if (error is not null)
        {
            return false;
        }

        Points -= Definition.Technology(technologyId).Cost;
        _unlocked.Add(technologyId);
        return true;
    }

    public string ToJson()
        => JsonSerializer.Serialize(new Dto(Definition.Id, Points, [.. _unlocked.Order(StringComparer.Ordinal)]));

    /// <summary>Reads saved research; technologies the tree no longer has are dropped.</summary>
    public static ResearchState FromJson(EconomyDefinition definition, string json)
    {
        Dto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<Dto>(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The research state is not valid JSON.", exception);
        }

        if (dto is null || !string.Equals(dto.EconomyId, definition.Id, StringComparison.Ordinal) || !double.IsFinite(dto.Points) || dto.Points < 0)
        {
            throw new InvalidDataException("The research state is incomplete or belongs to another economy.");
        }

        var state = new ResearchState(definition) { Points = dto.Points };
        var known = definition.Technologies.Select(technology => technology.Id).ToHashSet(StringComparer.Ordinal);
        state._unlocked.UnionWith((dto.Unlocked ?? []).Where(known.Contains));
        return state;
    }

    private IEnumerable<TechEffect> Effects()
        => Definition.Technologies.Where(technology => _unlocked.Contains(technology.Id)).SelectMany(technology => technology.Effects);

    private double Best(TechEffectKind kind, double start)
        => Effects().Where(effect => effect.Kind == kind).Select(effect => effect.Value).Append(start).Max();

    private sealed record Dto(string EconomyId, double Points, string[]? Unlocked);
}

/// <summary>Economy state of one mission run that a save must keep: inoculum in the bioreactor and points already paid for mutations.</summary>
public sealed record SessionEconomyState(double InoculumKg, double MutationPoints);
