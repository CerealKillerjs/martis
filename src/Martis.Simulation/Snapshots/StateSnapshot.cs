using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.Validation;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;

namespace Martis.Simulation.Snapshots;

/// <summary>
/// Scenario a run was built from. <see cref="Id"/> is the CLI id (<c>mars_baseline</c>),
/// <see cref="Name"/> the visible name and <see cref="DataVersion"/> the data file version (H-16).
/// </summary>
public sealed record ScenarioIdentity(string Id, string Name, string DataVersion, bool SepEnabled)
{
    public static ScenarioIdentity From(string id, MarsBaselineScenario scenario)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(scenario);
        return new ScenarioIdentity(id, scenario.Name, scenario.DataVersion, scenario.SepEnabled);
    }
}

/// <summary>
/// Full copy of one cell. A null quantity is unset, not 0.
/// <see cref="Deposits"/> and <see cref="Demands"/> follow the order of
/// <see cref="ResourcePool.Quantities"/> and <see cref="AllocationBuffer.Quantities"/>.
/// <see cref="InitialMass"/> is the t0 ledger in <see cref="CellMassLedger.Names"/> order,
/// or null when no scenario recorded it.
/// </summary>
public sealed record CellStateImage(
    string? Scope,
    double? TemperatureKelvin,
    double? PressurePascal,
    double? UvRadiation,
    double IonizingAbsorbedDoseGyPerSol,
    double IonizingEquivalentDoseSvPerSol,
    double SolarIrradiance,
    double WaterIceKg,
    double? WaterVaporKg,
    double? SoilPorosity,
    double? PerchlorateMassKg,
    double PerchlorateWtPercent,
    double WaterIceWtPercent,
    IReadOnlyList<double> Deposits,
    IReadOnlyList<double> Demands,
    double WaterBudgetTotalKg,
    IReadOnlyList<double>? InitialMass);

/// <summary>Full copy of one aggregated population, genome included (TDD §9.3).</summary>
public sealed record PopulationImage(
    long Id,
    string OrganismId,
    int CellIndex,
    IReadOnlyList<GeneExpression> Genome,
    double Abundance,
    double BiomassKg,
    double EnergyReserveJ,
    double AgeDistributionMeanSols,
    double Viability);

/// <summary>
/// Full save state (H-03, S1-31): tick, configuration, the five PRNG streams, every
/// cell, every population with its genome, and the engine, pipeline and PRNG versions.
/// <see cref="Martis.Contracts.Snapshots.SimulationSnapshot"/> stays the aggregated view
/// for the UI; this type is what a run is resumed from.
/// The command queue is not part of it: <see cref="Capture"/> refuses an engine
/// with pending commands. <see cref="SimulationStatistics"/> has no fields yet.
/// </summary>
public sealed record StateSnapshot(
    string SchemaVersion,
    string EngineVersion,
    string PipelineVersion,
    string PrngAlgorithm,
    ScenarioIdentity? Scenario,
    SimulationConfiguration Configuration,
    long Tick,
    RngStreamState Random,
    IReadOnlyList<CellStateImage> Cells,
    IReadOnlyList<PopulationImage> Populations)
{
    /// <summary>Schema id of this layout. A different layout is a new id, not a silent edit.</summary>
    public const string CurrentSchemaVersion = "martis-state-v2";

    /// <summary>T04–T06 layout without <see cref="NextPopulationId"/>. Still readable; the id is derived.</summary>
    public const string SchemaVersionV1 = "martis-state-v1";

    /// <summary>
    /// Id the next new population receives (schema v2). 0 means unknown: <see cref="Restore"/>
    /// then uses one above the largest id in <see cref="Populations"/>.
    /// </summary>
    public long NextPopulationId { get; init; }

    public static StateSnapshot Capture(SimulationEngine engine, ScenarioIdentity? scenario)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.PendingCommandCount != 0)
        {
            throw new InvalidOperationException(
                "A full snapshot cannot be taken while commands are queued for a later tick.");
        }

        var state = engine.State;
        var cells = new CellStateImage[state.CellCount];
        for (var i = 0; i < cells.Length; i++)
        {
            cells[i] = CaptureCell(
                state.Environment[i],
                state.Resources[i],
                state.AllocationBuffers[i],
                state.WaterBudgets[i],
                state.MassLedgers[i]);
        }

        var populations = new PopulationImage[state.Populations.Count];
        for (var i = 0; i < populations.Length; i++)
        {
            var population = state.Populations[i];
            populations[i] = new PopulationImage(
                population.Id.Value,
                population.OrganismId.Value,
                population.CellIndex,
                population.Genome.Expressions.ToArray(),
                population.Abundance,
                population.BiomassKg,
                population.EnergyReserveJ,
                population.AgeDistributionMeanSols,
                population.Viability);
        }

        return new StateSnapshot(
            CurrentSchemaVersion,
            EngineAssembly.Version,
            engine.PipelineVersion,
            Pcg32.AlgorithmId,
            scenario,
            engine.Configuration,
            state.Tick,
            engine.Random.ExportState(),
            cells,
            populations)
        {
            NextPopulationId = NextId(state.NextPopulationId, populations),
        };
    }

    /// <summary>
    /// Puts the tick, the configuration, the PRNG, every cell and every population back.
    /// The snapshot must match the engine's grid, world seed and PRNG algorithm.
    /// After restoring, <see cref="SimulationValidator"/> runs; a violation throws
    /// <see cref="SimulationInvariantException"/> and leaves the engine as it was.
    /// </summary>
    public void Restore(SimulationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (!string.Equals(SchemaVersion, CurrentSchemaVersion, StringComparison.Ordinal)
            && !string.Equals(SchemaVersion, SchemaVersionV1, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Snapshot schema '{SchemaVersion}' is not supported.");
        }

        if (!string.Equals(PrngAlgorithm, Pcg32.AlgorithmId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Snapshot PRNG '{PrngAlgorithm}' is not '{Pcg32.AlgorithmId}'.");
        }

        if (engine.PendingCommandCount != 0)
        {
            throw new InvalidOperationException("Restore needs an engine with an empty command queue.");
        }

        var state = engine.State;
        if (Configuration.MapWidth != state.Width || Configuration.MapHeight != state.Height || Cells.Count != state.CellCount)
        {
            throw new InvalidOperationException("The snapshot grid does not match the world.");
        }

        if (Configuration.WorldSeed != engine.Random.WorldSeed)
        {
            throw new InvalidOperationException("The snapshot world seed does not match the engine streams.");
        }

        var rollback = StateCheckpoint.Capture(state, engine.Random, new Queue<Martis.Contracts.Commands.ISimulationCommand>());
        var previousConfiguration = engine.Configuration;
        try
        {
            engine.Initialize(Configuration);
            state.Tick = Tick;
            engine.Random.Restore(Random);
            for (var i = 0; i < Cells.Count; i++)
            {
                RestoreCell(
                    Cells[i],
                    state.Environment[i],
                    state.Resources[i],
                    state.AllocationBuffers[i],
                    state.WaterBudgets[i],
                    state.MassLedgers[i]);
            }

            state.Populations.Clear();
            foreach (var image in Populations)
            {
                state.Populations.Add(new Population(
                    new PopulationId(image.Id),
                    new OrganismId(image.OrganismId),
                    image.CellIndex,
                    new Genome(image.Genome))
                {
                    Abundance = image.Abundance,
                    BiomassKg = image.BiomassKg,
                    EnergyReserveJ = image.EnergyReserveJ,
                    AgeDistributionMeanSols = image.AgeDistributionMeanSols,
                    Viability = image.Viability,
                });
            }

            state.NextPopulationId = NextId(NextPopulationId, Populations);

            var violations = SimulationValidator.Validate(state);
            if (violations.Count > 0)
            {
                throw new SimulationInvariantException(state.Tick, violations);
            }
        }
        catch (Exception)
        {
            rollback.Restore(state, engine.Random, new Queue<Martis.Contracts.Commands.ISimulationCommand>());
            engine.Initialize(previousConfiguration);
            throw;
        }
    }

    /// <summary>The stored next id, raised above every population id in the image.</summary>
    private static long NextId(long stored, IReadOnlyList<PopulationImage> populations)
    {
        var next = Math.Max(1L, stored);
        foreach (var population in populations)
        {
            next = Math.Max(next, population.Id + 1);
        }

        return next;
    }

    private static CellStateImage CaptureCell(
        EnvironmentState environment,
        ResourcePool resources,
        AllocationBuffer buffer,
        CellWaterBudget budget,
        CellMassLedger ledger)
    {
        return new CellStateImage(
            ScopeText.Format(environment.Scope),
            Read(environment.TemperatureKelvin),
            Read(environment.PressurePascal),
            Read(environment.UvRadiation),
            environment.IonizingAbsorbedDoseGyPerSol,
            environment.IonizingEquivalentDoseSvPerSol,
            environment.SolarIrradiance,
            environment.WaterIce,
            Read(environment.WaterVapor),
            Read(environment.SoilPorosity),
            Read(environment.PerchlorateMassKg),
            Percent(environment.PerchlorateWtPercent),
            Percent(environment.WaterIceWtPercent),
            resources.Quantities.Select(quantity => quantity.Value).ToArray(),
            buffer.Quantities.Select(quantity => quantity.Value).ToArray(),
            budget.RecordedTotalKg,
            ledger.ExportRecorded());
    }

    private static void RestoreCell(
        CellStateImage image,
        EnvironmentState environment,
        ResourcePool resources,
        AllocationBuffer buffer,
        CellWaterBudget budget,
        CellMassLedger ledger)
    {
        environment.Scope = ScopeText.Parse(image.Scope);
        environment.TemperatureKelvin = Write(image.TemperatureKelvin);
        environment.PressurePascal = Write(image.PressurePascal);
        environment.UvRadiation = Write(image.UvRadiation);
        environment.IonizingAbsorbedDoseGyPerSol = image.IonizingAbsorbedDoseGyPerSol;
        environment.IonizingEquivalentDoseSvPerSol = image.IonizingEquivalentDoseSvPerSol;
        environment.SolarIrradiance = image.SolarIrradiance;
        environment.WaterIce = image.WaterIceKg;
        environment.WaterVapor = Write(image.WaterVaporKg);
        environment.SoilPorosity = Write(image.SoilPorosity);
        environment.PerchlorateMassKg = Write(image.PerchlorateMassKg);
        environment.PerchlorateWtPercent = AssignedDouble.Set(image.PerchlorateWtPercent);
        environment.WaterIceWtPercent = AssignedDouble.Set(image.WaterIceWtPercent);
        ResourceColumns.WriteDeposits(resources, image.Deposits);
        ResourceColumns.WriteDemands(buffer, image.Demands);
        budget.RecordedTotalKg = image.WaterBudgetTotalKg;
        if (image.InitialMass is not null && image.InitialMass.Count != CellMassLedger.Names.Count)
        {
            throw new InvalidOperationException("The initial-mass ledger has the wrong length.");
        }

        ledger.RestoreRecorded(image.InitialMass?.ToArray());
    }

    // The save format keeps the two weight percents as plain numbers (T03b, #8 made them
    // AssignedDouble later): an unset percent is saved as 0 and restored as set, as before #8.
    private static double Percent(AssignedDouble value) => value.IsSet ? value.Value : 0.0;

    private static double? Read(AssignedDouble value) => value.IsSet ? value.Value : null;

    private static AssignedDouble Write(double? value)
        => value is { } number ? AssignedDouble.Set(number) : AssignedDouble.Unset;
}
