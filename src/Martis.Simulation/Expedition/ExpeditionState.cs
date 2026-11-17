using System.Text.Json;
using Martis.Simulation.Sessions;
using Martis.Simulation.Terraforming;

namespace Martis.Simulation.Expedition;

/// <summary>The rover as saved.</summary>
public sealed record RoverRecord(double X, double Y, double Heading, double Battery, double Wear, int? LanderX, int? LanderY);

/// <summary>A sample as saved: the frozen cell travels with it.</summary>
public sealed record SampleRecord(CellReport Source, long TakenSol, SampleState State, long? ReadySol);

/// <summary>A station as saved.</summary>
public sealed record StationRecord(int X, int Y, long BuiltSol, double Wear);

/// <summary>
/// Everything the expedition layer knows that the simulation snapshot does not (V5, ADR-032):
/// the rover, the fog of knowledge, samples, stations and points of interest. It is stored as
/// a JSON event at the snapshot's tick, next to the mission progress, so a load restores the
/// whole game and not only the biology. Knowledge is packed as 16-bit confidences in base64.
/// Since E1 (ADR-033) it also carries the regional climate, absent in older saves.
/// </summary>
public sealed record ExpeditionState(
    int Version,
    int Width,
    int Height,
    RoverRecord Rover,
    string Knowledge,
    IReadOnlyList<SampleRecord> Samples,
    long LabFreeSol,
    IReadOnlyList<StationRecord> Stations,
    IReadOnlyDictionary<string, PoiState> Pois,
    ClimateRecord? Climate = null)
{
    public const int CurrentVersion = 1;

    /// <summary>Event type of the record in the save store.</summary>
    public const string EventType = "expedition";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public static ExpeditionState Capture(Rover rover, KnowledgeMap knowledge, SampleStore samples, FacilityNetwork network, PoiField pois, RegionalClimate? climate = null)
    {
        ArgumentNullException.ThrowIfNull(rover);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(pois);
        return new ExpeditionState(
            CurrentVersion,
            knowledge.Width,
            knowledge.Height,
            new RoverRecord(rover.X, rover.Y, rover.Heading, rover.Battery, rover.Wear, rover.Lander?.X, rover.Lander?.Y),
            Pack(knowledge.Snapshot()),
            samples.All.Select(sample => new SampleRecord(sample.Source, sample.TakenSol, sample.State, sample.ReadySol)).ToArray(),
            samples.LabFreeSol,
            network.Stations.Select(station => new StationRecord(station.X, station.Y, station.BuiltSol, station.Wear)).ToArray(),
            pois.Pois.ToDictionary(poi => poi.Definition.Id, poi => poi.State, StringComparer.Ordinal),
            climate?.Capture());
    }

    /// <summary>Writes this state into freshly built expedition objects of the same map size.</summary>
    public void Apply(Rover rover, KnowledgeMap knowledge, SampleStore samples, FacilityNetwork network, PoiField pois)
    {
        ArgumentNullException.ThrowIfNull(rover);
        ArgumentNullException.ThrowIfNull(knowledge);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(network);
        ArgumentNullException.ThrowIfNull(pois);
        if (knowledge.Width != Width || knowledge.Height != Height)
        {
            throw new InvalidDataException($"The saved expedition is for a {Width}×{Height} map, not {knowledge.Width}×{knowledge.Height}.");
        }

        var lander = Rover.LanderX is { } lx && Rover.LanderY is { } ly ? (lx, ly) : ((int, int)?)null;
        rover.Restore(Rover.X, Rover.Y, Rover.Heading, Rover.Battery, Rover.Wear, lander);
        knowledge.Restore(Unpack(Knowledge, Width * Height));
        samples.Restore(Samples.Select(sample => (sample.Source, sample.TakenSol, sample.State, sample.ReadySol)), LabFreeSol);
        network.Restore(Stations.Select(station => (station.X, station.Y, station.BuiltSol, station.Wear)));
        pois.Restore(Pois);
    }

    public string ToJson() => JsonSerializer.Serialize(this, Options);

    public static ExpeditionState FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ExpeditionState? state;
        try
        {
            state = JsonSerializer.Deserialize<ExpeditionState>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The saved expedition is not valid JSON: {exception.Message}", exception);
        }

        if (state is null || state.Version != CurrentVersion || state.Rover is null || state.Knowledge is null || state.Samples is null || state.Stations is null || state.Pois is null)
        {
            throw new InvalidDataException("The saved expedition is incomplete or of an unknown version.");
        }

        return state;
    }

    private static string Pack(IReadOnlyList<double> confidence)
    {
        var bytes = new byte[confidence.Count * 2];
        for (var i = 0; i < confidence.Count; i++)
        {
            // Rounded up: a restored cell never falls below the level it had.
            var q = (ushort)Math.Ceiling(Math.Clamp(confidence[i], 0.0, 1.0) * ushort.MaxValue);
            bytes[2 * i] = (byte)(q & 0xFF);
            bytes[(2 * i) + 1] = (byte)(q >> 8);
        }

        return Convert.ToBase64String(bytes);
    }

    private static double[] Unpack(string packed, int cells)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(packed);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("The saved knowledge is not valid base64.", exception);
        }

        if (bytes.Length != cells * 2)
        {
            throw new InvalidDataException("The saved knowledge does not match the map size.");
        }

        var confidence = new double[cells];
        for (var i = 0; i < cells; i++)
        {
            confidence[i] = (bytes[2 * i] | (bytes[(2 * i) + 1] << 8)) / (double)ushort.MaxValue;
        }

        return confidence;
    }
}
