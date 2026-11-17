using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Simulation.Expedition;

/// <summary>
/// Balance values of the seeding rover (J1, ADR-026), loaded from <c>data/expedition/*.json</c>.
/// Every number is a classified GAME_BALANCE parameter. Times are real seconds of play, not sols:
/// the rover is part of the controls, not of the simulated world.
/// </summary>
public sealed record RoverDefinition(
    string Id,
    double SpeedCellsPerSecond,
    double ReverseFactor,
    double TurnRadiansPerSecond,
    double ReachCells,
    double BatteryCells,
    double CrawlFactor,
    double ChargeRadiusCells,
    double ChargePerSecond)
{
    public const string PublishedFile = "rover.v1.json";

    public static RoverDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "expedition", fileName)));
    }

    public static RoverDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.rover")
        {
            throw new InvalidDataException("A rover file needs metadata.schema = martis.rover.");
        }

        var rover = meta.GetProperty("rover");
        double Number(string name) => parameters.RequireNumber(rover.GetProperty(name).GetString()!).Value;

        return new RoverDefinition(
            meta.GetProperty("id").GetString()!,
            Number("speed_cells_per_s"),
            Number("reverse_factor"),
            Number("turn_rad_per_s"),
            Number("reach_cells"),
            Number("battery_cells"),
            Number("crawl_factor"),
            Number("charge_radius_cells"),
            Number("charge_per_s"));
    }
}

/// <summary>
/// The rover the player drives in the «Expedición» view (J1, ADR-026). Position is in cell
/// units (cell <c>(x, y)</c> covers <c>[x, x+1) × [y, y+1)</c>); heading 0 points to +x and
/// grows towards +y. It never touches the simulation: it only decides which cells the player
/// can seed. The battery drains with distance once the lander is down, recharges while parked
/// next to it, and an empty battery slows the rover to a crawl instead of stranding it.
/// </summary>
public sealed class Rover
{
    private const double Margin = 0.5;
    private (int X, int Y)? _lander;

    public Rover(RoverDefinition definition, int width, int height)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        Place(width / 2, height / 2, 0);
    }

    public RoverDefinition Definition { get; }

    public int Width { get; }

    public int Height { get; }

    public double X { get; private set; }

    public double Y { get; private set; }

    public double Heading { get; private set; }

    /// <summary>Charge from 0 (empty, crawling) to 1 (full).</summary>
    public double Battery { get; private set; } = 1.0;

    /// <summary>Mechanical wear from 0 (new) to 1 (worn out); past one half it slows the rover (V4).</summary>
    public double Wear { get; private set; }

    /// <summary>Fraction of top speed the wear leaves: 1 up to half wear, 0.5 when worn out.</summary>
    public double SpeedFactor => 1.0 - Math.Max(0.0, Wear - 0.5);

    /// <summary>Adds wear (for example per cell driven).</summary>
    public void AddWear(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Wear = Math.Min(1.0, Wear + amount);
    }

    /// <summary>Removes wear (repairs at the lander).</summary>
    public void Repair(double amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        Wear = Math.Max(0.0, Wear - amount);
    }

    /// <summary>
    /// Autopilot for orders (V4): throttle and steering that take the rover towards the centre of
    /// cell (<paramref name="x"/>, <paramref name="y"/>); both zero once within <paramref name="arrival"/> cells.
    /// </summary>
    public (double Throttle, double Steer) SteerTowards(int x, int y, double arrival = 0.6)
    {
        var dx = x + 0.5 - X;
        var dy = y + 0.5 - Y;
        if (Math.Sqrt((dx * dx) + (dy * dy)) <= arrival)
        {
            return (0, 0);
        }

        var error = Math.IEEERemainder(Math.Atan2(dy, dx) - Heading, 2 * Math.PI);
        var steer = Math.Clamp(error * 2.0, -1.0, 1.0);
        var throttle = Math.Abs(error) > 1.2 ? 0.25 : 1.0;
        return (throttle, steer);
    }

    /// <summary>The lander's cell once it is down, else <c>null</c>.</summary>
    public (int X, int Y)? Lander => _lander;

    /// <summary>True while parked within the lander's charging radius.</summary>
    public bool Charging { get; private set; }

    public (int X, int Y) Cell => ((int)Math.Floor(X), (int)Math.Floor(Y));

    public void Place(int x, int y, double heading)
    {
        X = Math.Clamp(x + 0.5, Margin, Width - Margin);
        Y = Math.Clamp(y + 0.5, Margin, Height - Margin);
        Heading = heading;
    }

    /// <summary>Puts a saved rover back exactly as it was (V5).</summary>
    public void Restore(double x, double y, double heading, double battery, double wear, (int X, int Y)? lander)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(heading))
        {
            throw new ArgumentOutOfRangeException(nameof(x), "A saved rover needs a finite position and heading.");
        }

        X = Math.Clamp(x, Margin, Width - Margin);
        Y = Math.Clamp(y, Margin, Height - Margin);
        Heading = heading;
        Battery = Math.Clamp(battery, 0.0, 1.0);
        Wear = Math.Clamp(wear, 0.0, 1.0);
        _lander = lander;
    }

    /// <summary>The lander is down at <paramref name="x"/>, <paramref name="y"/>: from now on driving costs battery.</summary>
    public void Landed(int x, int y) => _lander = (x, y);

    /// <summary>Advances the rover; returns the distance driven, in cells.</summary>
    /// <param name="throttle">-1 (full reverse) … 1 (full ahead).</param>
    /// <param name="steer">-1 … 1; positive turns towards +y.</param>
    /// <param name="seconds">Real time elapsed.</param>
    /// <summary>
    /// Multiplies the data speed: at real scale (M2, ADR-035) the game sets it so the rover drives
    /// at metres per second whatever the size of a cell. 1 on the scaled map.
    /// </summary>
    public double SpeedScale
    {
        get => _speedScale;
        set => _speedScale = value > 0 && double.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value), value, "The speed scale must be positive.");
    }

    private double _speedScale = 1.0;

    public double Drive(double throttle, double steer, double seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);
        throttle = Math.Clamp(throttle, -1.0, 1.0);
        steer = Math.Clamp(steer, -1.0, 1.0);
        Heading += steer * Definition.TurnRadiansPerSecond * seconds;

        var speed = Definition.SpeedCellsPerSecond * SpeedScale * SpeedFactor * Math.Abs(throttle) * (throttle < 0 ? Definition.ReverseFactor : 1.0);
        var wanted = MovableDistance(speed, seconds);
        var sign = throttle < 0 ? -1.0 : 1.0;
        var x = Math.Clamp(X + (Math.Cos(Heading) * wanted * sign), Margin, Width - Margin);
        var y = Math.Clamp(Y + (Math.Sin(Heading) * wanted * sign), Margin, Height - Margin);
        var driven = Math.Sqrt(((x - X) * (x - X)) + ((y - Y) * (y - Y)));
        X = x;
        Y = y;

        if (_lander is not null && Definition.BatteryCells > 0)
        {
            Battery = Math.Max(0.0, Battery - (driven / Definition.BatteryCells));
        }

        Charging = _lander is not null && throttle == 0 && DistanceTo(_lander.Value.X, _lander.Value.Y) <= Definition.ChargeRadiusCells;
        if (Charging)
        {
            Battery = Math.Min(1.0, Battery + (Definition.ChargePerSecond * seconds));
        }

        return driven;
    }

    /// <summary>
    /// Spends a fraction of the battery on an instrument (V2 scanner). Free before landing; refused,
    /// without spending anything, when the charge is short.
    /// </summary>
    public bool Spend(double fraction)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(fraction);
        if (_lander is null)
        {
            return true;
        }

        if (Battery + 1e-12 < fraction)
        {
            return false;
        }

        Battery = Math.Max(0.0, Battery - fraction);
        return true;
    }

    /// <summary>True when cell <paramref name="x"/>, <paramref name="y"/> is close enough to seed.</summary>
    public bool CanReach(int x, int y) => DistanceTo(x, y) <= Definition.ReachCells + 1e-9;

    /// <summary>Distance from the rover to the centre of a cell, in cells.</summary>
    public double DistanceTo(int x, int y)
    {
        var dx = x + 0.5 - X;
        var dy = y + 0.5 - Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // Full speed while the battery lasts (always before landing), then a crawl.
    private double MovableDistance(double speed, double seconds)
    {
        if (speed <= 0)
        {
            return 0;
        }

        if (_lander is null)
        {
            return speed * seconds;
        }

        var fullTime = Math.Min(seconds, Battery * Definition.BatteryCells / speed);
        return (speed * fullTime) + (speed * Definition.CrawlFactor * (seconds - fullTime));
    }
}
