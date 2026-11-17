using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Simulation.Expedition;

/// <summary>Why a station could or could not be built.</summary>
public enum BuildResult
{
    Built,

    /// <summary>Stations go on measured ground only.</summary>
    NotMeasured,

    /// <summary>The lander cannot power another station.</summary>
    NoPower,

    /// <summary>A station already stands on that cell.</summary>
    Occupied,
}

/// <summary>
/// Balance values of robots and facilities (V4, ADR-031), loaded from
/// <c>data/expedition/facilities.v1.json</c>. Every number is a classified GAME_BALANCE parameter.
/// </summary>
public sealed record FacilityDefinition(
    string Id,
    double PowerBudgetW,
    double StationPowerW,
    double StationRadiusCells,
    double StationConfidence,
    double StationWearPerSol,
    double StationBatteryCost,
    double RoverWearPerCell,
    double RoverRepairPerSecond,
    double FaultThreshold,
    double FaultMaxProbability,
    double RepairRadiusCells)
{
    public const string PublishedFile = "facilities.v1.json";

    /// <summary>Stations the power budget can feed at once.</summary>
    public int MaxStations => (int)Math.Floor((PowerBudgetW + 1e-9) / StationPowerW);

    public static FacilityDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "expedition", fileName)));
    }

    public static FacilityDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.facilities")
        {
            throw new InvalidDataException("A facilities file needs metadata.schema = martis.facilities.");
        }

        var facilities = meta.GetProperty("facilities");
        double Number(string name) => parameters.RequireNumber(facilities.GetProperty(name).GetString()!).Value;

        var definition = new FacilityDefinition(
            meta.GetProperty("id").GetString()!,
            Number("power_budget_w"),
            Number("station_power_w"),
            Number("station_radius_cells"),
            Number("station_confidence"),
            Number("station_wear_per_sol"),
            Number("station_battery_cost"),
            Number("rover_wear_per_cell"),
            Number("rover_repair_per_s"),
            Number("fault_threshold"),
            Number("fault_max_probability"),
            Number("repair_radius_cells"));
        if (definition.StationPowerW <= 0 || definition.FaultThreshold >= 1)
        {
            throw new InvalidDataException("Station power must be positive and the fault threshold below 1.");
        }

        return definition;
    }

    /// <summary>Chance that an instrument fails at this wear: zero up to the threshold, then rising linearly.</summary>
    public double FaultProbability(double wear)
        => FaultMaxProbability * Math.Clamp((wear - FaultThreshold) / (1.0 - FaultThreshold), 0.0, 1.0);
}

/// <summary>A fixed sensor station: it measures the ground around it every sol until dust and cold wear it out.</summary>
public sealed class Station
{
    internal Station(int id, int x, int y, long builtSol)
    {
        Id = id;
        X = x;
        Y = y;
        BuiltSol = builtSol;
    }

    public int Id { get; }

    public int X { get; }

    public int Y { get; }

    public long BuiltSol { get; }

    /// <summary>0 new … 1 broken.</summary>
    public double Wear { get; internal set; }

    public bool Broken => Wear >= 1.0;
}

/// <summary>
/// The network of fixed stations the rover builds (V4, ADR-031). The lander powers a few of them;
/// each working station measures its surroundings every sol and wears down, and a broken one stays
/// silent until the rover comes to repair it. Deterministic: driven by sols only.
/// </summary>
public sealed class FacilityNetwork
{
    private readonly List<Station> _stations = [];

    public FacilityNetwork(FacilityDefinition definition)
        => Definition = definition ?? throw new ArgumentNullException(nameof(definition));

    public FacilityDefinition Definition { get; }

    public IReadOnlyList<Station> Stations => _stations;

    public double PowerUsedW => _stations.Count * Definition.StationPowerW;

    public BuildResult CanBuild(int x, int y, KnowledgeLevel level)
        => level != KnowledgeLevel.Measured ? BuildResult.NotMeasured
            : _stations.Any(station => station.X == x && station.Y == y) ? BuildResult.Occupied
            : PowerUsedW + Definition.StationPowerW > Definition.PowerBudgetW + 1e-9 ? BuildResult.NoPower
            : BuildResult.Built;

    public BuildResult Build(int x, int y, KnowledgeLevel level, long sol, out Station? station)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sol);
        station = null;
        var result = CanBuild(x, y, level);
        if (result != BuildResult.Built)
        {
            return result;
        }

        station = new Station(_stations.Count + 1, x, y, sol);
        _stations.Add(station);
        return result;
    }

    /// <summary>
    /// One sol of the network: every working station measures its surroundings, then wears.
    /// Returns the stations that broke down this sol.
    /// </summary>
    public IReadOnlyList<Station> Tick(KnowledgeMap knowledge)
    {
        ArgumentNullException.ThrowIfNull(knowledge);
        var broke = new List<Station>();
        foreach (var station in _stations.Where(station => !station.Broken))
        {
            knowledge.Observe(station.X + 0.5, station.Y + 0.5, Definition.StationRadiusCells, Definition.StationConfidence, 0.0);
            station.Wear = Math.Min(1.0, station.Wear + Definition.StationWearPerSol);
            if (station.Broken)
            {
                broke.Add(station);
            }
        }

        return broke;
    }

    /// <summary>The nearest station within the repair radius of a point (cell units), if any.</summary>
    public Station? Near(double x, double y)
        => _stations
            .Select(station => (Station: station, Distance: Math.Sqrt(Math.Pow(station.X + 0.5 - x, 2) + Math.Pow(station.Y + 0.5 - y, 2))))
            .Where(item => item.Distance <= Definition.RepairRadiusCells + 1e-9)
            .OrderBy(item => item.Distance)
            .Select(item => item.Station)
            .FirstOrDefault();

    /// <summary>Restores a station to new; returns false when it did not need it.</summary>
    public bool Repair(Station station)
    {
        ArgumentNullException.ThrowIfNull(station);
        if (station.Wear <= 0)
        {
            return false;
        }

        station.Wear = 0;
        return true;
    }
}

/// <summary>
/// Deterministic «dice» for instrument faults (V4): a SplitMix64 hash of the run seed, the sol and
/// an order number gives the same outcome on every replay. No <see cref="System.Random"/>.
/// </summary>
public static class Faults
{
    public static bool Happens(ulong seed, long sol, long sequence, double probability)
    {
        if (probability <= 0)
        {
            return false;
        }

        if (probability >= 1)
        {
            return true;
        }

        var z = seed ^ ((ulong)sol * 0x9E3779B97F4A7C15UL) ^ ((ulong)sequence * 0xBF58476D1CE4E5B9UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return (z >> 11) * (1.0 / (1UL << 53)) < probability;
    }
}
