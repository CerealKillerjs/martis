using Martis.Domain.Common;
using Martis.Domain.Environment;

namespace Martis.Simulation.Engine;

/// <summary>
/// Environment step for MarsBaseline v0 (TDD §3.2, S1-27).
/// The full model is T(x, y, t) = T_base + D(t) + S(t) + ε.
/// This version sets D and S to 0 and does not draw ε.
/// Diurnal and seasonal cycles belong to T03c.
/// The step reads temperature and pressure so an unset cell fails here.
/// It does not write them, and it does not read the unassigned v0 fields.
/// </summary>
public sealed class EnvironmentSystem : ISimulationSystem
{
    public string Name => SimulationSteps.UpdateEnvironment;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!double.IsFinite(deltaTimeSols) || deltaTimeSols <= 0.0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deltaTimeSols),
                deltaTimeSols,
                "The environment step needs a finite positive time step in sols.");
        }

        var cells = context.State.Environment;
        for (var i = 0; i < cells.Length; i++)
        {
            var environment = cells[i];
            var temperature = ReadAssigned(environment, nameof(EnvironmentState.TemperatureKelvin));
            var pressure = ReadAssigned(environment, nameof(EnvironmentState.PressurePascal));
            if (temperature.IsUnset || pressure.IsUnset)
            {
                throw new InvalidOperationException(
                    $"Cell {i} has unset temperature or pressure. The v0 environment step does not invent them.");
            }

            // Touch the values. D and S stay 0, so the stored numbers do not change.
            _ = temperature.Value;
            _ = pressure.Value;
        }
    }

    /// <summary>
    /// Reads one assigned field for a v0 system.
    /// Ultraviolet irradiance, soil porosity, perchlorate mass and water vapor
    /// are refused by name: v0 must not read them, even if a caller has set a number.
    /// </summary>
    public static AssignedDouble ReadAssigned(EnvironmentState state, string field)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (string.IsNullOrWhiteSpace(field))
        {
            throw new ArgumentException("A field name is required.", nameof(field));
        }

        if (field is nameof(EnvironmentState.UvRadiation)
            or nameof(EnvironmentState.SoilPorosity)
            or nameof(EnvironmentState.PerchlorateMassKg)
            or nameof(EnvironmentState.WaterVapor))
        {
            throw new InvalidOperationException(
                $"A v0 system must not read '{field}'. The field is unassigned.");
        }

        return field switch
        {
            nameof(EnvironmentState.TemperatureKelvin) => state.TemperatureKelvin,
            nameof(EnvironmentState.PressurePascal) => state.PressurePascal,
            _ => throw new InvalidOperationException($"EnvironmentSystem v0 does not read '{field}'."),
        };
    }
}
