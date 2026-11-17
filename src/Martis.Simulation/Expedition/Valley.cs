using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Simulation.Expedition;

/// <summary>What kind of place a point of interest is.</summary>
public enum PoiKind
{
    Delta,
    Ice,
    Brine,
    Shelter,
    Meteorite,
}

/// <summary>How much the AI knows about a point of interest.</summary>
public enum PoiState
{
    /// <summary>Nothing seen yet.</summary>
    Hidden,

    /// <summary>An anomaly in estimated data (orbit, cameras): worth a visit.</summary>
    Sighted,

    /// <summary>Measured on the ground: its finding is known.</summary>
    Discovered,
}

/// <summary>A place of the valley, positioned as a fraction of the map so every map size has it.</summary>
public sealed record PoiDefinition(string Id, PoiKind Kind, string Name, double FractionX, double FractionY, string Finding, double Research);

/// <summary>
/// The parametrized valley of the vertical slice (V5, ADR-032, Documento Maestro v2.0 §32),
/// loaded from <c>data/expedition/valley.v1.json</c>.
/// </summary>
public sealed record ValleyDefinition(string Id, string Name, double DiscoveryRadiusCells, IReadOnlyList<PoiDefinition> Pois)
{
    public const string PublishedFile = "valley.v1.json";

    public static ValleyDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile, Func<double, double, (double X, double Y)>? locate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "expedition", fileName)), locate);
    }

    /// <summary>
    /// Loads a valley. Places may give <c>x</c>/<c>y</c> as fractions of the map or, on a real
    /// region (M3, ADR-036), <c>lat</c>/<c>lon</c>; <paramref name="locate"/> turns those into fractions.
    /// </summary>
    public static ValleyDefinition Load(string json, Func<double, double, (double X, double Y)>? locate = null)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.valley")
        {
            throw new InvalidDataException("A valley file needs metadata.schema = martis.valley.");
        }

        var valley = meta.GetProperty("valley");
        double Number(string name) => parameters.RequireNumber(valley.GetProperty(name).GetString()!).Value;

        var pois = new List<PoiDefinition>();
        foreach (var item in meta.GetProperty("pois").EnumerateArray())
        {
            var kind = Enum.Parse<PoiKind>(item.GetProperty("kind").GetString()!);
            double x, y;
            if (item.TryGetProperty("lat", out var lat))
            {
                if (locate is null)
                {
                    throw new InvalidDataException($"Point of interest {item.GetProperty("id").GetString()} is placed by latitude and longitude; load it with a region.");
                }

                (x, y) = locate(lat.GetDouble(), item.GetProperty("lon").GetDouble());
            }
            else
            {
                x = item.GetProperty("x").GetDouble();
                y = item.GetProperty("y").GetDouble();
            }

            if (x is < 0 or >= 1 || y is < 0 or >= 1)
            {
                throw new InvalidDataException($"Point of interest {item.GetProperty("id").GetString()} must lie inside the map (fractions in [0, 1)).");
            }

            pois.Add(new PoiDefinition(
                item.GetProperty("id").GetString()!,
                kind,
                item.GetProperty("name").GetString()!,
                x,
                y,
                item.GetProperty("finding").GetString()!,
                Number("research_" + kind.ToString().ToLowerInvariant())));
        }

        if (pois.Select(poi => poi.Id).Distinct(StringComparer.Ordinal).Count() != pois.Count)
        {
            throw new InvalidDataException("Point of interest ids must be unique.");
        }

        return new ValleyDefinition(meta.GetProperty("id").GetString()!, meta.GetProperty("name").GetString()!, Number("discovery_radius_cells"), pois);
    }
}

/// <summary>A point of interest placed on a map.</summary>
public sealed class Poi
{
    internal Poi(PoiDefinition definition, int x, int y)
    {
        Definition = definition;
        X = x;
        Y = y;
    }

    public PoiDefinition Definition { get; }

    public int X { get; }

    public int Y { get; }

    public PoiState State { get; internal set; }
}

/// <summary>
/// The valley's points of interest on a map of a given size (V5). Knowledge drives them: an
/// estimate of the cell shows an anomaly, a measurement within the discovery radius reveals the
/// place and its finding. Deterministic and independent of the simulation.
/// </summary>
public sealed class PoiField
{
    public PoiField(ValleyDefinition definition, int width, int height)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Pois = definition.Pois
            .Select(poi => new Poi(poi, (int)Math.Floor(poi.FractionX * width), (int)Math.Floor(poi.FractionY * height)))
            .ToArray();
    }

    public ValleyDefinition Definition { get; }

    public IReadOnlyList<Poi> Pois { get; }

    /// <summary>Moves points of interest forward with what is known; returns those that changed state.</summary>
    public IReadOnlyList<Poi> Update(KnowledgeMap knowledge)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        var changed = new List<Poi>();
        foreach (var poi in Pois.Where(poi => poi.State != PoiState.Discovered))
        {
            var next = MeasuredNear(knowledge, poi) ? PoiState.Discovered
                : knowledge.Level(poi.X, poi.Y) >= KnowledgeLevel.Estimated ? PoiState.Sighted
                : PoiState.Hidden;
            if (next > poi.State)
            {
                poi.State = next;
                changed.Add(poi);
            }
        }

        return changed;
    }

    /// <summary>Restores saved states by id; unknown ids are ignored, missing ones stay hidden.</summary>
    public void Restore(IReadOnlyDictionary<string, PoiState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        foreach (var poi in Pois)
        {
            poi.State = states.TryGetValue(poi.Definition.Id, out var state) ? state : PoiState.Hidden;
        }
    }

    private bool MeasuredNear(KnowledgeMap knowledge, Poi poi)
    {
        var r = (int)Math.Ceiling(Definition.DiscoveryRadiusCells);
        for (var dy = -r; dy <= r; dy++)
        {
            for (var dx = -r; dx <= r; dx++)
            {
                var x = poi.X + dx;
                var y = poi.Y + dy;
                if (x < 0 || y < 0 || x >= knowledge.Width || y >= knowledge.Height || Math.Sqrt((dx * dx) + (dy * dy)) > Definition.DiscoveryRadiusCells + 1e-9)
                {
                    continue;
                }

                if (knowledge.Level(x, y) == KnowledgeLevel.Measured)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
