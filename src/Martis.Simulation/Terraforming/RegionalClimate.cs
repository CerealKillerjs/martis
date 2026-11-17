using System.Text.Json;
using Martis.Contracts.Commands;
using Martis.Domain.Common;
using Martis.Domain.Parameters;
using Martis.Simulation.Commands;
using Martis.Domain.World;
using Martis.Simulation.Expedition;

namespace Martis.Simulation.Terraforming;

/// <summary>
/// Balance values and model assumptions of local heating (E1, ADR-033), loaded from
/// <c>data/terraform/heating.v1.json</c>.
/// </summary>
public sealed record HeatingDefinition(
    string Id,
    double PeakDeltaK,
    double RadiusCells,
    double RelaxationSols,
    double PowerW,
    double WearPerSol,
    double BatteryCost,
    double MaxTemperatureK)
{
    public const string PublishedFile = "heating.v1.json";

    public static HeatingDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "terraform", fileName)));
    }

    public static HeatingDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.heating")
        {
            throw new InvalidDataException("A heating file needs metadata.schema = martis.heating.");
        }

        var heating = meta.GetProperty("heating");
        double Number(string name) => parameters.RequireNumber(heating.GetProperty(name).GetString()!).Value;
        var definition = new HeatingDefinition(
            meta.GetProperty("id").GetString()!,
            Number("peak_delta_k"),
            Number("radius_cells"),
            Number("relaxation_sols"),
            Number("power_w"),
            Number("wear_per_sol"),
            Number("battery_cost"),
            Number("max_temperature_k"));
        if (definition.RelaxationSols <= 0 || definition.RadiusCells <= 0)
        {
            throw new InvalidDataException("Heating relaxation time and radius must be positive.");
        }

        return definition;
    }
}

/// <summary>A heater the rover set up: it warms the ground around it while it works.</summary>
public sealed class Heater
{
    internal Heater(int id, int x, int y, long builtSol)
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

    /// <summary>0 new … 1 broken (switched off until repaired).</summary>
    public double Wear { get; internal set; }

    public bool Broken => Wear >= 1.0;
}

/// <summary>A heater as saved.</summary>
public sealed record HeaterRecord(int X, int Y, long BuiltSol, double Wear);

/// <summary>The regional climate as saved: heaters and the thermal anomaly of every cell.</summary>
public sealed record ClimateRecord(int Version, int Width, int Height, IReadOnlyList<HeaterRecord> Heaters, IReadOnlyList<double> AnomalyK)
{
    public const int CurrentVersion = 1;

    public const string EventType = "climate";
}

/// <summary>
/// Regional climate under intervention (E1, ADR-033, Documento Maestro v2.0 §10): each cell keeps
/// its baseline temperature plus a thermal anomaly. Working heaters force the anomaly up to
/// <see cref="HeatingDefinition.PeakDeltaK"/> at their cell, falling linearly to zero at the
/// radius; every sol the anomaly relaxes towards that forcing with the time constant
/// <see cref="HeatingDefinition.RelaxationSols"/>, so heat builds up slowly and fades when a heater
/// breaks. The engine only sees <see cref="SetCellTemperatureCommand"/>s at the tick boundary.
/// </summary>
public sealed class RegionalClimate
{
    private readonly double[] _baseline;
    private readonly double[] _anomaly;
    private readonly List<Heater> _heaters = [];

    private RegionalClimate(HeatingDefinition definition, int width, int height, double[] baseline, double[] anomaly)
    {
        Definition = definition;
        Width = width;
        Height = height;
        _baseline = baseline;
        _anomaly = anomaly;
    }

    public HeatingDefinition Definition { get; }

    public int Width { get; }

    public int Height { get; }

    public IReadOnlyList<Heater> Heaters => _heaters;

    public IReadOnlyList<double> AnomalyK => _anomaly;

    /// <summary>Power the working heaters draw.</summary>
    public double PowerUsedW => _heaters.Count(heater => !heater.Broken) * Definition.PowerW;

    /// <summary>A climate with no intervention yet: the current temperatures are the baseline.</summary>
    public static RegionalClimate Start(HeatingDefinition definition, int width, int height, IReadOnlyList<double> temperatureK)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(temperatureK);
        Check(width, height, temperatureK.Count);
        return new RegionalClimate(definition, width, height, temperatureK.ToArray(), new double[width * height]);
    }

    /// <summary>A saved climate: the baseline is today's temperature minus the saved anomaly.</summary>
    public static RegionalClimate Resume(HeatingDefinition definition, IReadOnlyList<double> temperatureK, ClimateRecord record)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(temperatureK);
        ArgumentNullException.ThrowIfNull(record);
        if (record.Version != ClimateRecord.CurrentVersion || record.Heaters is null || record.AnomalyK is null)
        {
            throw new InvalidDataException("The saved climate is incomplete or of an unknown version.");
        }

        Check(record.Width, record.Height, temperatureK.Count);
        if (record.AnomalyK.Count != temperatureK.Count)
        {
            throw new InvalidDataException("The saved climate does not match the map size.");
        }

        var anomaly = record.AnomalyK.ToArray();
        var baseline = temperatureK.Select((t, i) => t - anomaly[i]).ToArray();
        var climate = new RegionalClimate(definition, record.Width, record.Height, baseline, anomaly);
        foreach (var heater in record.Heaters)
        {
            climate._heaters.Add(new Heater(climate._heaters.Count + 1, heater.X, heater.Y, heater.BuiltSol) { Wear = Math.Clamp(heater.Wear, 0.0, 1.0) });
        }

        return climate;
    }

    public ClimateRecord Capture()
        => new(ClimateRecord.CurrentVersion, Width, Height, _heaters.Select(heater => new HeaterRecord(heater.X, heater.Y, heater.BuiltSol, heater.Wear)).ToArray(), _anomaly.ToArray());

    public double BaselineK(int x, int y) => _baseline[Index(x, y)];

    public double Anomaly(int x, int y) => _anomaly[Index(x, y)];

    /// <summary>Whether a heater can go on a cell: measured ground, free cell and enough spare power.</summary>
    public BuildResult CanPlace(int x, int y, KnowledgeLevel level, double freePowerW)
    {
        Index(x, y);
        return level != KnowledgeLevel.Measured ? BuildResult.NotMeasured
            : _heaters.Any(heater => heater.X == x && heater.Y == y) ? BuildResult.Occupied
            : freePowerW + 1e-9 < Definition.PowerW ? BuildResult.NoPower
            : BuildResult.Built;
    }

    /// <summary>Sets up a heater when <see cref="CanPlace"/> allows it.</summary>
    public BuildResult Place(int x, int y, KnowledgeLevel level, double freePowerW, long sol, out Heater? heater)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sol);
        heater = null;
        var result = CanPlace(x, y, level, freePowerW);
        if (result != BuildResult.Built)
        {
            return result;
        }

        heater = new Heater(_heaters.Count + 1, x, y, sol);
        _heaters.Add(heater);
        return result;
    }

    /// <summary>The nearest heater within <paramref name="radius"/> cells of a point, if any.</summary>
    public Heater? Near(double x, double y, double radius)
        => _heaters
            .Select(heater => (heater, d: Math.Sqrt(Math.Pow(heater.X + 0.5 - x, 2) + Math.Pow(heater.Y + 0.5 - y, 2))))
            .Where(item => item.d <= radius)
            .OrderBy(item => item.d)
            .Select(item => item.heater)
            .FirstOrDefault();

    /// <summary>Mean anomaly over the cells it reaches (K), or 0 when none is warmed.</summary>
    public double MeanWarmingK => _anomaly.Any(a => a > 0.05) ? _anomaly.Where(a => a > 0.05).Average() : 0.0;

    /// <summary>Cells warmed by more than half a kelvin.</summary>
    public int WarmedCells => _anomaly.Count(a => a > 0.5);

    /// <summary>Extra wear from outside the daily cycle, such as dust or a cold snap.</summary>
    public void AddWear(Heater heater, double amount)
    {
        ArgumentNullException.ThrowIfNull(heater);
        ArgumentOutOfRangeException.ThrowIfNegative(amount);
        heater.Wear = Math.Min(1.0, heater.Wear + amount);
    }

    public bool Repair(Heater heater)
    {
        ArgumentNullException.ThrowIfNull(heater);
        if (heater.Wear <= 0)
        {
            return false;
        }

        heater.Wear = 0;
        return true;
    }

    /// <summary>
    /// One sol: heaters wear, the anomaly relaxes towards the forcing of the working heaters, and
    /// the cells whose temperature changes get a command. Returns the commands and the heaters
    /// that broke this sol.
    /// </summary>
    public (IReadOnlyList<SetCellTemperatureCommand> Commands, IReadOnlyList<Heater> Broke) Step()
    {
        var forcing = new double[_anomaly.Length];
        var r = Definition.RadiusCells;
        foreach (var heater in _heaters.Where(heater => !heater.Broken))
        {
            var reach = (int)Math.Ceiling(r);
            for (var y = Math.Max(0, heater.Y - reach); y <= Math.Min(Height - 1, heater.Y + reach); y++)
            {
                for (var x = Math.Max(0, heater.X - reach); x <= Math.Min(Width - 1, heater.X + reach); x++)
                {
                    var d = Math.Sqrt(((x - heater.X) * (x - heater.X)) + ((y - heater.Y) * (y - heater.Y)));
                    if (d < r)
                    {
                        forcing[(y * Width) + x] += Definition.PeakDeltaK * (1.0 - (d / r));
                    }
                }
            }
        }

        var broke = new List<Heater>();
        foreach (var heater in _heaters.Where(heater => !heater.Broken))
        {
            heater.Wear = Math.Min(1.0, heater.Wear + Definition.WearPerSol);
            if (heater.Broken)
            {
                broke.Add(heater);
            }
        }

        var alpha = 1.0 - Math.Exp(-1.0 / Definition.RelaxationSols);
        var commands = new List<SetCellTemperatureCommand>();
        for (var i = 0; i < _anomaly.Length; i++)
        {
            var target = Math.Min(forcing[i], Math.Max(0.0, Definition.MaxTemperatureK - _baseline[i]));
            var next = _anomaly[i] + ((target - _anomaly[i]) * alpha);
            if (next < 1e-4 && target == 0)
            {
                next = 0;
            }

            if (Math.Abs(next - _anomaly[i]) > 1e-9)
            {
                _anomaly[i] = next;
                commands.Add(new SetCellTemperatureCommand(i, _baseline[i] + next));
            }
        }

        return (commands, broke);
    }

    private int Index(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the {Width}×{Height} map.");
        }

        return (y * Width) + x;
    }

    private static void Check(int width, int height, int cells)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (cells != width * height)
        {
            throw new ArgumentException("One temperature per cell is needed.", nameof(cells));
        }
    }
}

/// <summary>Applies <see cref="SetCellTemperatureCommand"/> (E1): a validated temperature for one cell.</summary>
public sealed class SetCellTemperatureHandler : ICommandHandler
{
    public const double MinimumK = 1.0;
    public const double MaximumK = 400.0;

    public string Kind => SetCellTemperatureCommand.CommandKind;

    public void Handle(ISimulationCommand command, SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (command is not SetCellTemperatureCommand set)
        {
            throw new ArgumentException("Expected a SetCellTemperatureCommand.", nameof(command));
        }

        if (set.CellIndex < 0 || set.CellIndex >= state.CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(command), set.CellIndex, "The cell index is outside the world.");
        }

        if (!double.IsFinite(set.TemperatureKelvin) || set.TemperatureKelvin < MinimumK || set.TemperatureKelvin > MaximumK)
        {
            throw new ArgumentOutOfRangeException(nameof(command), set.TemperatureKelvin, $"A cell temperature must be finite and between {MinimumK} and {MaximumK} K.");
        }

        state.Environment[set.CellIndex].TemperatureKelvin = AssignedDouble.Set(set.TemperatureKelvin);
    }
}
