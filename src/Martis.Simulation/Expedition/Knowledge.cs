using System.Text.Json;
using Martis.Domain.Parameters;

namespace Martis.Simulation.Expedition;

/// <summary>How well the AI knows a cell (Documento Maestro v2.0 §2: measure, sample, update models).</summary>
public enum KnowledgeLevel
{
    /// <summary>No data: the cell has never been observed.</summary>
    Unknown,

    /// <summary>Remote or passing observation: values carry a wide uncertainty.</summary>
    Estimated,

    /// <summary>Surface measurement by the rover's scanner.</summary>
    Measured,
}

/// <summary>
/// Balance values of the orbital probe and the rover scanner (V2, ADR-029), loaded from
/// <c>data/expedition/survey.v1.json</c>. Every number is a classified GAME_BALANCE parameter.
/// </summary>
public sealed record SurveyDefinition(
    string Id,
    double OrbitalConfidence,
    int OrbitalSwathCells,
    int OrbitalSolsPerPass,
    double ScanRadiusCells,
    double ScanPeakConfidence,
    double ScanFalloff,
    double ScanBatteryCost,
    double PassiveRadiusCells,
    double PassiveConfidence,
    double EstimatedThreshold,
    double MeasuredThreshold)
{
    public const string PublishedFile = "survey.v1.json";

    public static SurveyDefinition LoadPublished(string repositoryRoot, string fileName = PublishedFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "expedition", fileName)));
    }

    public static SurveyDefinition Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.survey")
        {
            throw new InvalidDataException("A survey file needs metadata.schema = martis.survey.");
        }

        var survey = meta.GetProperty("survey");
        double Number(string name) => parameters.RequireNumber(survey.GetProperty(name).GetString()!).Value;

        var definition = new SurveyDefinition(
            meta.GetProperty("id").GetString()!,
            Number("orbital_confidence"),
            (int)Number("orbital_swath_cells"),
            (int)Number("orbital_sols_per_pass"),
            Number("scan_radius_cells"),
            Number("scan_peak_confidence"),
            Number("scan_falloff"),
            Number("scan_battery_cost"),
            Number("passive_radius_cells"),
            Number("passive_confidence"),
            Number("estimated_threshold"),
            Number("measured_threshold"));
        if (definition.OrbitalSwathCells < 1 || definition.OrbitalSolsPerPass < 1
            || definition.EstimatedThreshold <= 0 || definition.MeasuredThreshold <= definition.EstimatedThreshold)
        {
            throw new InvalidDataException("Survey thresholds must satisfy 0 < estimated < measured; swath and period must be at least 1.");
        }

        return definition;
    }
}

/// <summary>
/// The fog of knowledge (V2, ADR-029): a confidence from 0 to 1 for every scientific cell.
/// Observations only ever raise confidence. The orbital probe sweeps the region in vertical
/// swaths, one every <see cref="SurveyDefinition.OrbitalSolsPerPass"/> sols, starting with the
/// swath over the centre; the rover's navigation cameras estimate what it drives past and its
/// scanner measures the ground around it. Deterministic: no randomness, no clock.
/// </summary>
public sealed class KnowledgeMap
{
    private readonly double[] _confidence;

    public KnowledgeMap(SurveyDefinition definition, int width, int height)
    {
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _confidence = new double[width * height];
    }

    public SurveyDefinition Definition { get; }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Number of vertical swaths the orbital probe needs to cover the region once.</summary>
    public int Swaths => (Width + Definition.OrbitalSwathCells - 1) / Definition.OrbitalSwathCells;

    /// <summary>Raised whenever a cell's confidence changes, so views can refresh.</summary>
    public int Version { get; private set; }

    public double Confidence(int x, int y) => _confidence[Index(x, y)];

    public KnowledgeLevel Level(int x, int y) => Classify(Confidence(x, y));

    public KnowledgeLevel Classify(double confidence)
        => confidence >= Definition.MeasuredThreshold ? KnowledgeLevel.Measured
            : confidence >= Definition.EstimatedThreshold ? KnowledgeLevel.Estimated
            : KnowledgeLevel.Unknown;

    /// <summary>Fraction of cells known at least at <paramref name="level"/>.</summary>
    public double Coverage(KnowledgeLevel level)
    {
        if (level == KnowledgeLevel.Unknown)
        {
            return 1.0;
        }

        var count = 0;
        foreach (var value in _confidence)
        {
            if (Classify(value) >= level)
            {
                count++;
            }
        }

        return (double)count / _confidence.Length;
    }

    /// <summary>The swath the probe images on <paramref name="sol"/>, or <c>null</c> between passes.</summary>
    public int? SwathAt(long sol)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sol);
        if (sol % Definition.OrbitalSolsPerPass != 0)
        {
            return null;
        }

        var first = (Width / 2) / Definition.OrbitalSwathCells;
        return (int)((first + (sol / Definition.OrbitalSolsPerPass)) % Swaths);
    }

    /// <summary>Runs the orbital pass due on <paramref name="sol"/>; returns the cells whose level improved.</summary>
    public int Orbit(long sol)
    {
        if (SwathAt(sol) is not { } swath)
        {
            return 0;
        }

        var improved = 0;
        var from = swath * Definition.OrbitalSwathCells;
        var to = Math.Min(Width, from + Definition.OrbitalSwathCells);
        for (var y = 0; y < Height; y++)
        {
            for (var x = from; x < to; x++)
            {
                improved += Raise(x, y, Definition.OrbitalConfidence);
            }
        }

        return improved;
    }

    /// <summary>Navigation cameras: estimate the cells around a point (cell units).</summary>
    public int Glimpse(double x, double y)
        => Observe(x, y, Definition.PassiveRadiusCells, Definition.PassiveConfidence, 0.0);

    /// <summary>Rover scanner: measure the cells around a point (cell units).</summary>
    public int Scan(double x, double y)
        => Observe(x, y, Definition.ScanRadiusCells, Definition.ScanPeakConfidence, Definition.ScanFalloff);

    /// <summary>
    /// Raises the confidence of every cell whose centre lies within <paramref name="radius"/> of
    /// (<paramref name="x"/>, <paramref name="y"/>) to <c>peak · (1 − falloff · d / radius)</c>.
    /// Returns the number of cells whose level improved.
    /// </summary>
    public int Observe(double x, double y, double radius, double peak, double falloff)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(peak) || !double.IsFinite(falloff))
        {
            throw new ArgumentOutOfRangeException(nameof(peak), "Observation values must be finite.");
        }

        var improved = 0;
        var minX = Math.Max(0, (int)Math.Floor(x - radius));
        var maxX = Math.Min(Width - 1, (int)Math.Floor(x + radius));
        var minY = Math.Max(0, (int)Math.Floor(y - radius));
        var maxY = Math.Min(Height - 1, (int)Math.Floor(y + radius));
        for (var cy = minY; cy <= maxY; cy++)
        {
            for (var cx = minX; cx <= maxX; cx++)
            {
                var dx = cx + 0.5 - x;
                var dy = cy + 0.5 - y;
                var d = Math.Sqrt((dx * dx) + (dy * dy));
                if (d > radius + 1e-9)
                {
                    continue;
                }

                var share = radius > 0 ? d / radius : 0.0;
                improved += Raise(cx, cy, peak * (1.0 - (falloff * share)));
            }
        }

        return improved;
    }

    /// <summary>Copies the confidences (row-major) for saving or display.</summary>
    public double[] Snapshot() => (double[])_confidence.Clone();

    /// <summary>Restores confidences produced by <see cref="Snapshot"/>.</summary>
    public void Restore(IReadOnlyList<double> confidence)
    {
        ArgumentNullException.ThrowIfNull(confidence);
        if (confidence.Count != _confidence.Length)
        {
            throw new ArgumentException("The snapshot does not match the map size.", nameof(confidence));
        }

        for (var i = 0; i < _confidence.Length; i++)
        {
            _confidence[i] = Math.Clamp(confidence[i], 0.0, 1.0);
        }

        Version++;
    }

    private int Raise(int x, int y, double confidence)
    {
        var i = Index(x, y);
        confidence = Math.Clamp(confidence, 0.0, 1.0);
        if (confidence <= _confidence[i])
        {
            return 0;
        }

        var before = Classify(_confidence[i]);
        _confidence[i] = confidence;
        Version++;
        return Classify(confidence) > before ? 1 : 0;
    }

    private int Index(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"Cell ({x}, {y}) is outside the {Width}×{Height} map.");
        }

        return (y * Width) + x;
    }
}
