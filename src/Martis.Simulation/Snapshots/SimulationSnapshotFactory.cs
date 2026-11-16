using Martis.Contracts.Snapshots;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Scenarios;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Copies a world into the immutable view from TDD §8.1.
/// The copy does not share cell objects with the live state, so a later edit
/// of the world is not visible in the snapshot.
/// </summary>
public static class SimulationSnapshotFactory
{
    public static SimulationSnapshot Capture(SimulationEngine engine, MarsBaselineScenario scenario)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(scenario);
        var state = engine.State;
        var cells = new CellSnapshot[state.CellCount];
        var populationCounts = CountPopulations(state);
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = CaptureCell(i, state.Environment[i], state.Resources[i], populationCounts[i]);
        }

        var populations = new PopulationSnapshot[state.Populations.Count];
        for (var i = 0; i < populations.Length; i++)
        {
            var population = state.Populations[i];
            populations[i] = new PopulationSnapshot(
                population.Id.Value,
                population.OrganismId.Value,
                population.Abundance,
                population.BiomassKg,
                null,
                1);
        }

        return new SimulationSnapshot(
            state.Tick,
            scenario.Name,
            scenario.DataVersion,
            scenario.SepEnabled,
            cells,
            populations,
            new StatisticsSnapshot(state.Tick, state.CellCount, state.Populations.Count));
    }

    private static CellSnapshot CaptureCell(int index, EnvironmentState environment, ResourcePool resources, int populationCount)
    {
        return new CellSnapshot(
            index,
            Nullable(environment.TemperatureKelvin),
            Nullable(environment.PressurePascal),
            Nullable(environment.UvRadiation),
            environment.IonizingAbsorbedDoseGyPerSol,
            environment.IonizingEquivalentDoseSvPerSol,
            environment.SolarIrradiance,
            environment.WaterIce,
            Nullable(environment.WaterVapor),
            Nullable(environment.SoilPorosity),
            Nullable(environment.PerchlorateMassKg),
            environment.PerchlorateWtPercent,
            environment.WaterIceWtPercent,
            ScopeText.Format(environment.Scope),
            populationCount,
            resources.Water,
            resources.OrganicCarbon);
    }

    private static double? Nullable(AssignedDouble value)
        => value.IsSet ? value.Value : null;

    private static int[] CountPopulations(SimulationState state)
    {
        var counts = new int[state.CellCount];
        foreach (var population in state.Populations)
        {
            if (population.CellIndex >= 0 && population.CellIndex < counts.Length)
            {
                counts[population.CellIndex]++;
            }
        }

        return counts;
    }
}
