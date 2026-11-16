using Martis.Contracts.Commands;
using Martis.Domain.Biology;
using Martis.Domain.Chemistry;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.Validation;
using Martis.Domain.World;
using Martis.Simulation.Commands;
using Martis.Simulation.Engine;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Biology;

/// <summary>
/// Rate laws of TDD §3.3–3.4 (ADR-012). Every factor is in [0, 1]:
/// f_T = exp(−(T−T_opt)²/(2σ²)) with σ widened by thermal tolerance,
/// f_R = 1/(1+α·R·(1−UV resistance)) with R the equivalent dose per sol,
/// f_W = W/(K_W+W) with K_W lowered by desiccation tolerance,
/// f_I = photosynthetic efficiency · I/(K_I+I) for light-driven reactions,
/// F_substrate = min_i C_i/(K_i+C_i) over the reaction inputs.
/// </summary>
public static class GrowthModel
{
    public static double Temperature(double kelvin, Phenotype phenotype, OrganismKinetics kinetics)
    {
        var sigma = kinetics.TemperatureSigmaKelvin * (1.0 + Math.Min(1.0, phenotype.ThermalTolerance));
        var delta = kelvin - kinetics.TemperatureOptimumKelvin;
        return Math.Exp(-(delta * delta) / (2.0 * sigma * sigma));
    }

    public static double Radiation(double sievertPerSol, Phenotype phenotype, OrganismKinetics kinetics)
        => 1.0 / (1.0 + (kinetics.RadiationAlphaSolPerSv * sievertPerSol * (1.0 - Math.Min(1.0, phenotype.UvResistance))));

    public static double Water(double waterKg, Phenotype phenotype, OrganismKinetics kinetics)
    {
        var half = kinetics.WaterHalfSaturationKg * (1.0 - (0.5 * Math.Min(1.0, phenotype.DesiccationTolerance)));
        return waterKg / (half + waterKg);
    }

    public static double Light(double irradiance, Phenotype phenotype, OrganismKinetics kinetics)
        => Math.Min(1.0, phenotype.PhotosyntheticEfficiency) * (irradiance / (kinetics.LightHalfSaturationWPerM2 + irradiance));

    public static double Monod(double concentration, double halfSaturation) => concentration / (halfSaturation + concentration);

    /// <summary>Product of the environment factors (TDD §3.3 F_environment), without substrates.</summary>
    public static double Environment(EnvironmentState environment, ResourcePool pool, Phenotype phenotype, OrganismKinetics kinetics)
    {
        if (!environment.TemperatureKelvin.IsSet)
        {
            throw new InvalidOperationException("Metabolism needs an assigned temperature.");
        }

        return Temperature(environment.TemperatureKelvin.Value, phenotype, kinetics)
            * Radiation(environment.IonizingEquivalentDoseSvPerSol, phenotype, kinetics)
            * Water(pool.Water, phenotype, kinetics);
    }

    /// <summary>
    /// Potential reaction extent of one population for one tick, before shared allocation.
    /// Extent units: one extent runs the reaction's coefficients once.
    /// </summary>
    public static double PotentialExtent(
        ReactionModel reaction,
        OrganismModel organism,
        Population population,
        Phenotype phenotype,
        EnvironmentState environment,
        ResourcePool pool,
        double deltaTimeSols)
    {
        if (population.BiomassKg <= 0.0)
        {
            return 0.0;
        }

        var factor = Environment(environment, pool, phenotype, organism.Kinetics);
        if (reaction.Definition.EnergyMode == ReactionEnergyMode.Light)
        {
            factor *= Light(environment.SolarIrradiance, phenotype, organism.Kinetics);
        }

        var substrate = 1.0;
        var quantities = pool.Quantities;
        foreach (var term in reaction.Inputs)
        {
            if (term.PoolSlot < 0)
            {
                continue;
            }

            var name = quantities[term.PoolSlot].Name;
            substrate = Math.Min(substrate, Monod(quantities[term.PoolSlot].Value, organism.Kinetics.SubstrateHalfSaturationKg[name]));
        }

        var carbon = organism.Kinetics.VmaxCarbonPerKgPerSol * population.BiomassKg * deltaTimeSols * factor * substrate;
        return carbon / reaction.CarbonMoles;
    }
}

/// <summary>Applies <see cref="IntroducePopulationCommand"/>: a new population with the organism's base genome.</summary>
public sealed class IntroducePopulationHandler : ICommandHandler
{
    private readonly MetabolismWorkspace _workspace;

    public IntroducePopulationHandler(MetabolismWorkspace workspace)
    {
        _workspace = workspace ?? throw new ArgumentNullException(nameof(workspace));
    }

    public string Kind => IntroducePopulationCommand.CommandKind;

    public void Handle(ISimulationCommand command, SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (command is not IntroducePopulationCommand introduce)
        {
            throw new ArgumentException("Expected an IntroducePopulationCommand.", nameof(command));
        }

        var organism = _workspace.Catalog.Organism(introduce.OrganismId);
        if (introduce.CellIndex < 0 || introduce.CellIndex >= state.CellCount)
        {
            throw new ArgumentOutOfRangeException(nameof(command), introduce.CellIndex, "The cell index is outside the world.");
        }

        if (!double.IsFinite(introduce.BiomassKg) || introduce.BiomassKg <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(command), introduce.BiomassKg, "Inoculated biomass must be finite and positive.");
        }

        var nextId = state.AllocatePopulationId();
        state.Populations.Add(new Population(
            new PopulationId(nextId),
            organism.Definition.Id,
            introduce.CellIndex,
            DesignedGenome(organism, introduce.GeneOverrides))
        {
            BiomassKg = introduce.BiomassKg,
            Abundance = introduce.BiomassKg / organism.Kinetics.IndividualMassKg,
            EnergyReserveJ = 0.0,
            AgeDistributionMeanSols = 0.0,
            Viability = 1.0,
        });
        _workspace.PendingInflowKg += introduce.BiomassKg;
    }

    /// <summary>The base genome with the player's overrides. An unknown gene or a value outside [0, 1] is an error.</summary>
    internal static Genome DesignedGenome(OrganismModel organism, IReadOnlyDictionary<string, double>? overrides)
    {
        var genome = organism.Definition.BaseGenome.BaseGenome;
        if (overrides is null || overrides.Count == 0)
        {
            return genome;
        }

        foreach (var name in overrides.Keys.Order(StringComparer.Ordinal))
        {
            if (!genome.TryGetExpression(new GeneId(name), out _))
            {
                throw new ArgumentException($"Organism '{organism.Id}' has no gene '{name}'.", nameof(overrides));
            }
        }

        return new Genome(genome.Expressions.Select(gene =>
            overrides.TryGetValue(gene.GeneId.Value, out var value)
                ? new GeneExpression(gene.GeneId, value)
                : gene));
    }
}

/// <summary>calculate-resource-availability: opens the tick's mass, energy and consumption account.</summary>
public sealed class ResourceAvailabilitySystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.CalculateResourceAvailability;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        workspace.Open(context.State);
    }
}

/// <summary>
/// calculate-metabolism: each population plans its growth reaction and records the
/// mass it asks for on the cell's <see cref="AllocationBuffer"/>. Nothing is subtracted
/// here (TDD §12.3): the pool is shared and reconciled in the allocation step.
/// </summary>
public sealed class MetabolismSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.CalculateMetabolism;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        foreach (var buffer in state.AllocationBuffers)
        {
            ResourceColumns.Clear(buffer);
        }

        var catalog = workspace.Catalog;
        foreach (var population in state.Populations)
        {
            var organism = catalog.Organism(population.OrganismId.Value);
            var phenotype = PhenotypeCalculator.Calculate(population.Genome, catalog.Binding);
            var cell = population.CellIndex;
            AddPlan(workspace, state, population, organism.GrowthReaction,
                GrowthModel.PotentialExtent(organism.GrowthReaction, organism, population, phenotype, state.Environment[cell], state.Resources[cell], deltaTimeSols));
            if (organism.EnergyReaction is { } energy)
            {
                AddPlan(workspace, state, population, energy,
                    GrowthModel.PotentialExtent(energy, organism, population, phenotype, state.Environment[cell], state.Resources[cell], deltaTimeSols));
            }
        }
    }

    private static void AddPlan(MetabolismWorkspace workspace, SimulationState state, Population population, ReactionModel reaction, double extent)
    {
        if (!double.IsFinite(extent) || extent < 0.0)
        {
            throw new InvalidOperationException($"Population {population.Id.Value} planned a non-finite extent.");
        }

        workspace.Fluxes.Add(new PopulationFlux(population.Id.Value, reaction, extent));
        foreach (var term in reaction.Inputs)
        {
            if (term.PoolSlot >= 0)
            {
                var kg = term.KgPerExtent * extent;
                workspace.Demand[(population.CellIndex * workspace.Slots) + term.PoolSlot] += kg;
                ResourceColumns.Add(state.AllocationBuffers[population.CellIndex], term.PoolSlot, kg);
            }
        }
    }
}

/// <summary>
/// allocate-shared-resources: when a cell's demand for a slot exceeds what it holds,
/// every request on that slot is scaled by the same factor available/demand (TDD §3.6,
/// C_i = C_total·D_i/ΣD_j). A flux takes the smallest factor over its inputs, so the order
/// of populations gives no advantage and no slot is overdrawn (VAL-006).
/// </summary>
public sealed class AllocationSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    /// <summary>Relative margin that keeps rounding in the products from overdrawing a slot.</summary>
    public const double RoundingMargin = 1e-12;

    public string Name => SimulationSteps.AllocateSharedResources;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var slots = workspace.Slots;
        var factors = new double[state.CellCount * slots];
        for (var i = 0; i < factors.Length; i++)
        {
            var demand = workspace.Demand[i];
            var available = workspace.StartPool[i];
            factors[i] = demand <= available ? 1.0 : (available / demand) * (1.0 - RoundingMargin);
        }

        var cells = PopulationCells(state);
        foreach (var flux in workspace.Fluxes)
        {
            var cell = cells[flux.PopulationId];
            var factor = 1.0;
            foreach (var term in flux.Reaction.Inputs)
            {
                if (term.PoolSlot >= 0)
                {
                    factor = Math.Min(factor, factors[(cell * slots) + term.PoolSlot]);
                }
            }

            flux.AllocatedExtent = flux.PlannedExtent * factor;
        }
    }

    internal static Dictionary<long, int> PopulationCells(SimulationState state)
        => state.Populations.ToDictionary(population => population.Id.Value, population => population.CellIndex);
}

/// <summary>
/// update-biomass-and-energy: runs each allocated flux. Inputs leave the pool (or the
/// population biomass), outputs enter it, the cell water budget follows the water slot,
/// and the reserve gains or pays energy by the reaction's mode (ADR-012).
/// Abundance follows biomass: N = B / individual mass.
/// </summary>
public sealed class BiomassEnergySystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.UpdateBiomassAndEnergy;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var byId = state.Populations.ToDictionary(population => population.Id.Value);
        var catalog = workspace.Catalog;
        var waterSlot = ResourceColumns.Slot(nameof(ResourcePool.Water));
        foreach (var flux in workspace.Fluxes)
        {
            if (flux.AllocatedExtent <= 0.0)
            {
                continue;
            }

            var population = byId[flux.PopulationId];
            var organism = catalog.Organism(population.OrganismId.Value);
            var cell = population.CellIndex;
            var pool = state.Resources[cell];
            var extent = flux.AllocatedExtent;
            var carbon = extent * flux.Reaction.CarbonMoles;
            var energyMode = flux.Reaction.Definition.EnergyMode;
            if (energyMode == ReactionEnergyMode.Cost)
            {
                var cost = carbon * organism.Kinetics.Extra.GetValueOrDefault("assimilation_cost", 0.0);
                var reserve = population.EnergyReserveJ;
                if (cost > reserve)
                {
                    // Not enough stored energy: run only what the reserve can pay (VAL-008).
                    var scale = cost <= 0 ? 0 : reserve / cost;
                    extent *= scale;
                    carbon *= scale;
                    cost = reserve;
                }

                population.EnergyReserveJ = Math.Max(0.0, population.EnergyReserveJ - cost);
                workspace.Spend(population.Id.Value, cost);
            }

            foreach (var term in flux.Reaction.Inputs)
            {
                var kg = term.KgPerExtent * extent;
                if (term.PoolSlot >= 0)
                {
                    ResourceColumns.Add(pool, term.PoolSlot, -kg);
                    workspace.Consumed[(cell * workspace.Slots) + term.PoolSlot] += kg;
                    if (term.PoolSlot == waterSlot)
                    {
                        state.WaterBudgets[cell].RecordedTotalKg -= kg;
                    }
                }
                else
                {
                    population.BiomassKg -= kg;
                }
            }

            foreach (var term in flux.Reaction.Outputs)
            {
                var kg = term.KgPerExtent * extent;
                if (term.PoolSlot >= 0)
                {
                    ResourceColumns.Add(pool, term.PoolSlot, kg);
                    if (term.PoolSlot == waterSlot)
                    {
                        state.WaterBudgets[cell].RecordedTotalKg += kg;
                    }
                }
                else
                {
                    population.BiomassKg += kg;
                }
            }

            switch (energyMode)
            {
                case ReactionEnergyMode.Light:
                    var gain = carbon * organism.Kinetics.ReserveGainJPerMolCarbon;
                    population.EnergyReserveJ += gain;
                    workspace.Gain(population.Id.Value, gain);
                    workspace.Totals.LightEnergyJ += (carbon * catalog.CarbonEnergyJPerMol) + gain;
                    workspace.Totals.CarbonFixedMol += carbon;
                    break;
                case ReactionEnergyMode.Release:
                    var released = carbon * catalog.CarbonEnergyJPerMol * organism.Kinetics.Extra.GetValueOrDefault("respiration_efficiency", 0.0);
                    population.EnergyReserveJ += released;
                    workspace.Gain(population.Id.Value, released);
                    workspace.Totals.ReleasedEnergyJ += carbon * catalog.CarbonEnergyJPerMol;
                    workspace.Totals.CarbonRespiredMol += carbon;
                    break;
                case ReactionEnergyMode.Cost:
                    workspace.Totals.CarbonFixedMol += carbon;
                    break;
            }

            population.Abundance = Math.Max(0.0, population.BiomassKg) / organism.Kinetics.IndividualMassKg;
        }
    }
}

/// <summary>
/// validate-conservation: closes the tick's account. VAL-006: no slot gave more than it
/// held at the start. VAL-007: every element mass in every cell is unchanged within
/// ε_abs + ε_rel·|M| (ADR-011 tolerances). VAL-008: no reserve paid more than it held
/// plus what it gained, and none is negative. A breach throws, and the engine rolls the
/// tick back.
/// </summary>
public sealed class ConservationSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.ValidateConservation;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var violations = new List<InvariantViolation>();
        var slots = workspace.Slots;
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            for (var slot = 0; slot < slots; slot++)
            {
                var index = (cell * slots) + slot;
                var allowed = workspace.StartPool[index] * (1.0 + 1e-12);
                if (workspace.Consumed[index] > allowed)
                {
                    violations.Add(new InvariantViolation(
                        InvariantIds.ConsumptionWithinAvailability,
                        state.Tick,
                        cell,
                        null,
                        workspace.Consumed[index],
                        ResourceColumns.Name(slot)));
                }
            }
        }

        var elements = workspace.ElementCount;
        var end = new double[state.CellCount * elements];
        workspace.ElementTotals(state, end);
        var worst = 0.0;
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            for (var e = 0; e < elements; e++)
            {
                var index = (cell * elements) + e;
                var expected = workspace.StartElements[index];
                var error = Math.Abs(end[index] - expected);
                worst = Math.Max(worst, error);
                var allowance = MassBalanceTolerances.MassBalanceAbsoluteToleranceKg
                    + (MassBalanceTolerances.MassBalanceRelativeTolerance * Math.Abs(expected));
                if (!(error <= allowance))
                {
                    violations.Add(new InvariantViolation(
                        InvariantIds.ElementalMassConserved,
                        state.Tick,
                        cell,
                        null,
                        end[index] - expected,
                        "element:" + workspace.Catalog.Elements[e]));
                }
            }
        }

        workspace.Totals.MaxElementErrorKg = worst;
        foreach (var population in state.Populations)
        {
            var id = population.Id.Value;
            var start = workspace.ReserveAtStart.GetValueOrDefault(id, 0.0);
            var gained = workspace.ReserveGained.GetValueOrDefault(id, 0.0);
            var spent = workspace.ReserveSpent.GetValueOrDefault(id, 0.0);
            var slack = 1e-9 * Math.Max(1.0, start + gained);
            if (population.EnergyReserveJ < 0.0 || spent > start + gained + slack)
            {
                violations.Add(new InvariantViolation(
                    InvariantIds.EnergyWithinAvailability,
                    state.Tick,
                    population.CellIndex,
                    population.Id,
                    spent - (start + gained),
                    nameof(Population.EnergyReserveJ)));
            }
        }

        workspace.Close();
        if (violations.Count > 0)
        {
            throw new SimulationInvariantException(state.Tick, violations);
        }
    }
}
