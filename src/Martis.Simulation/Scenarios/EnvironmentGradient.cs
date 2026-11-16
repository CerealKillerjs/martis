using System.Text.Json;
using Martis.Domain.Parameters;
using Martis.Domain.Common;
using Martis.Domain.World;

namespace Martis.Simulation.Scenarios;

/// <summary>
/// Gradients laid over the uniform MarsBaseline (H-30, ADR-022): latitude by map row
/// (warmer and brighter at the central row, colder and darker at the north and south
/// edges) and relief by map column (highlands get a higher ionising dose). It moves no
/// mass, so the cell ledgers recorded by the scenario stay valid. Values are GAME_BALANCE
/// in <c>data/scenarios/*.json</c> until Ciencia replaces them with measured ones.
/// </summary>
public sealed record EnvironmentGradient(
    string Id,
    double EquatorTemperatureDeltaK,
    double PoleTemperatureDeltaK,
    double EquatorIrradianceFactor,
    double PoleIrradianceFactor,
    double HighlandDoseFactor,
    double LowlandDoseFactor,
    double Ridges)
{
    public static EnvironmentGradient LoadPublished(string repositoryRoot, string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new InvalidDataException($"Gradient file '{fileName}' must be a plain file name.");
        }

        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "scenarios", fileName)));
    }

    public static EnvironmentGradient Load(string json)
    {
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.gradient")
        {
            throw new InvalidDataException("A gradient file needs metadata.schema = martis.gradient.");
        }

        var latitude = meta.GetProperty("latitude");
        var relief = meta.GetProperty("relief");
        double Read(JsonElement section, string name) => parameters.RequireNumber(section.GetProperty(name).GetString()!).Value;
        var gradient = new EnvironmentGradient(
            meta.GetProperty("id").GetString()!,
            Read(latitude, "equator_temperature_delta"),
            Read(latitude, "pole_temperature_delta"),
            Read(latitude, "equator_irradiance_factor"),
            Read(latitude, "pole_irradiance_factor"),
            Read(relief, "highland_dose_factor"),
            Read(relief, "lowland_dose_factor"),
            Read(relief, "ridges"));
        if (gradient.EquatorIrradianceFactor < 0 || gradient.PoleIrradianceFactor < 0 || gradient.HighlandDoseFactor < 0 || gradient.LowlandDoseFactor < 0)
        {
            throw new InvalidDataException("Gradient factors cannot be negative.");
        }

        return gradient;
    }

    /// <summary>Latitude of a row in [-1, 1]: 0 at the central row, ±1 at the edges.</summary>
    public static double Latitude(int y, int height)
        => height <= 1 ? 0.0 : ((2.0 * y) / (height - 1)) - 1.0;

    /// <summary>Highland weight of a column in [0, 1]: 1 on a ridge, 0 in a basin.</summary>
    public double Highland(int x, int width)
        => width <= 1 ? 0.0 : 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * Ridges * x / (width - 1)));

    /// <summary>Applies the gradient to every cell. Call once, right after the scenario.</summary>
    public void Apply(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        for (var y = 0; y < state.Height; y++)
        {
            var band = Latitude(y, state.Height);
            band *= band;
            var deltaK = EquatorTemperatureDeltaK + ((PoleTemperatureDeltaK - EquatorTemperatureDeltaK) * band);
            var light = EquatorIrradianceFactor + ((PoleIrradianceFactor - EquatorIrradianceFactor) * band);
            for (var x = 0; x < state.Width; x++)
            {
                var environment = state.Environment[state.ToLinearIndex(x, y)];
                var dose = LowlandDoseFactor + ((HighlandDoseFactor - LowlandDoseFactor) * Highland(x, state.Width));
                if (environment.TemperatureKelvin.IsSet)
                {
                    environment.TemperatureKelvin = AssignedDouble.Set(Math.Max(1.0, environment.TemperatureKelvin.Value + deltaK));
                }

                environment.SolarIrradiance *= light;
                environment.IonizingAbsorbedDoseGyPerSol *= dose;
                environment.IonizingEquivalentDoseSvPerSol *= dose;
                environment.CheckInvariants();
            }
        }
    }
}
