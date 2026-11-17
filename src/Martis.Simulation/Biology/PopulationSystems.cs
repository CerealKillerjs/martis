using Martis.Domain.Biology;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Biology;

/// <summary>
/// T05 numbers of one organism, read from its <c>org.&lt;id&gt;.*</c> parameters (ADR-013).
/// Rates are per sol; a term becomes a per-tick probability as <c>1 − exp(−r·Δt)</c>.
/// </summary>
public sealed record PopulationParameters(
    double MaintenancePowerJPerKgPerSol,
    double BaseMortalityPerSol,
    double ThermalMortalityPerSol,
    double DesiccationMortalityPerSol,
    double RadiationMortalityPerSv,
    double StarvationMortalityPerSol,
    double CarryingCapacityKg)
{
    public static PopulationParameters From(OrganismModel organism)
    {
        ArgumentNullException.ThrowIfNull(organism);
        var extra = organism.Kinetics.Extra;
        return new PopulationParameters(
            Require(extra, organism.Id, "maintenance_power"),
            Require(extra, organism.Id, "base_mortality"),
            Require(extra, organism.Id, "thermal_mortality"),
            Require(extra, organism.Id, "desiccation_mortality"),
            Require(extra, organism.Id, "radiation_mortality"),
            Require(extra, organism.Id, "starvation_mortality"),
            Require(extra, organism.Id, "carrying_capacity"));
    }

    private static double Require(IReadOnlyDictionary<string, double> extra, string organism, string name)
        => extra.TryGetValue(name, out var value)
            ? value
            : throw new KeyNotFoundException($"Organism '{organism}' has no parameter '{name}' (T05).");
}

/// <summary>Mortality terms of TDD §3.5. Every term is a probability in [0, 1] for one tick.</summary>
public static class MortalityModel
{
    /// <summary>Per-tick probability of a rate <paramref name="perSol"/> scaled by <paramref name="stress"/> ∈ [0, 1].</summary>
    public static double Term(double perSol, double stress, double deltaTimeSols)
    {
        var exposure = perSol * Math.Clamp(stress, 0.0, 1.0) * deltaTimeSols;
        return exposure <= 0.0 ? 0.0 : -double.ExpM1(-exposure);
    }

    /// <summary>m = 1 − Πᵢ(1 − mᵢ). Throws when a term is not a probability.</summary>
    public static double Combine(IReadOnlyList<double> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);
        var survival = 1.0;
        foreach (var term in terms)
        {
            if (!double.IsFinite(term) || term < 0.0 || term > 1.0)
            {
                throw new InvalidOperationException($"Mortality term {term} is not in [0, 1].");
            }

            survival *= 1.0 - term;
        }

        return Math.Clamp(1.0 - survival, 0.0, 1.0);
    }

    /// <summary>
    /// K = min(K_space, K_N, K_P, K_C): the biomass the cell could hold if all of that
    /// element (in the pool and in living biomass) were biomass. It bounds growth and
    /// never adds a resource (TDD §3.5).
    /// </summary>
    public static double CarryingCapacityKg(
        PopulationParameters parameters,
        BiologyCatalog catalog,
        ResourcePool pool,
        double cellBiomassKg)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(pool);
        var capacity = parameters.CarryingCapacityKg;
        var fractions = catalog.Biomass.ElementMassFractions;
        var quantities = pool.Quantities;
        for (var e = 0; e < catalog.Elements.Count; e++)
        {
            var element = catalog.Elements[e];
            if (fractions[e] <= 0.0 || element is "H" or "O")
            {
                continue;
            }

            var held = cellBiomassKg * fractions[e];
            for (var slot = 0; slot < quantities.Count; slot++)
            {
                held += quantities[slot].Value * catalog.PoolSpecies(slot).ElementMassFractions[e];
            }

            capacity = Math.Min(capacity, held / fractions[e]);
        }

        return capacity;
    }
}

/// <summary>
/// calculate-mortality (T05): pays maintenance from the reserve, combines the stress
/// terms m = 1 − Π(1 − mᵢ) and moves the dead biomass to the cell's necromass, which
/// the recycling step breaks down in the same tick. A population with less than one
/// individual left is extinct: it is removed and its biomass is necromass too.
/// Deterministic: no random draw (demographic noise is T06 drift).
/// </summary>
public sealed class MortalitySystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.CalculateMortality;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var catalog = workspace.Catalog;
        var cellBiomass = new double[state.CellCount];
        foreach (var population in state.Populations)
        {
            cellBiomass[population.CellIndex] += Math.Max(0.0, population.BiomassKg);
        }

        var terms = new double[6];
        foreach (var population in state.Populations)
        {
            var organism = catalog.Organism(population.OrganismId.Value);
            var parameters = workspace.PopulationParametersOf(organism);
            var phenotype = PhenotypeCalculator.Calculate(population.Genome, catalog.Binding);
            var cell = population.CellIndex;
            var environment = state.Environment[cell];
            var pool = state.Resources[cell];
            var biomass = Math.Max(0.0, population.BiomassKg);

            var cost = parameters.MaintenancePowerJPerKgPerSol * biomass * deltaTimeSols;
            var paid = Math.Min(cost, population.EnergyReserveJ);
            population.EnergyReserveJ -= paid;
            workspace.Spend(population.Id.Value, paid);
            workspace.Totals.MaintenanceJ += paid;
            workspace.Totals.UnpaidMaintenanceJ += cost - paid;
            var unpaid = cost > 0.0 ? (cost - paid) / cost : 0.0;

            var thermal = environment.TemperatureKelvin.IsSet
                ? 1.0 - GrowthModel.Temperature(environment.TemperatureKelvin.Value, phenotype, organism.Kinetics)
                : throw new InvalidOperationException("Mortality needs an assigned temperature.");
            var dry = 1.0 - GrowthModel.Water(pool.Water, phenotype, organism.Kinetics);
            var dose = environment.IonizingEquivalentDoseSvPerSol * (1.0 - Math.Min(1.0, phenotype.UvResistance));
            var capacity = MortalityModel.CarryingCapacityKg(parameters, catalog, pool, cellBiomass[cell]);
            var crowding = cellBiomass[cell] > capacity && cellBiomass[cell] > 0.0 ? 1.0 - (capacity / cellBiomass[cell]) : 0.0;

            terms[0] = MortalityModel.Term(parameters.BaseMortalityPerSol, 1.0, deltaTimeSols);
            terms[1] = MortalityModel.Term(parameters.ThermalMortalityPerSol, thermal, deltaTimeSols);
            terms[2] = MortalityModel.Term(parameters.DesiccationMortalityPerSol, dry, deltaTimeSols);
            terms[3] = MortalityModel.Term(parameters.RadiationMortalityPerSv * dose, 1.0, deltaTimeSols);
            terms[4] = MortalityModel.Term(parameters.StarvationMortalityPerSol, unpaid, deltaTimeSols);
            terms[5] = Math.Clamp(crowding, 0.0, 1.0);
            var mortality = MortalityModel.Combine(terms);

            var dead = biomass * mortality;
            population.BiomassKg = biomass - dead;
            workspace.DeadKg[cell] += dead;
            workspace.Totals.BiomassDiedKg += dead;
            workspace.Survival[population.Id.Value] = 1.0 - mortality;
            population.Viability = 1.0 - mortality;
            population.Abundance = population.BiomassKg / organism.Kinetics.IndividualMassKg;
        }

        for (var i = state.Populations.Count - 1; i >= 0; i--)
        {
            var population = state.Populations[i];
            if (population.Abundance >= 1.0)
            {
                continue;
            }

            workspace.DeadKg[population.CellIndex] += population.BiomassKg;
            workspace.Totals.BiomassDiedKg += population.BiomassKg;
            workspace.Totals.Extinctions++;
            workspace.Extinct.Add(population.Id.Value);
            state.Populations.RemoveAt(i);
        }
    }
}

/// <summary>
/// generate-reproduction (T05): in the aggregated model new biomass is new individuals
/// (N = B / individual mass). This step counts them and updates the mean age:
/// survivors age by Δt and newborns enter at age 0.
/// </summary>
public sealed class ReproductionSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.GenerateReproduction;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        foreach (var population in context.State.Populations)
        {
            var id = population.Id.Value;
            var start = workspace.AbundanceAtStart.GetValueOrDefault(id, 0.0);
            var survivors = start * workspace.Survival.GetValueOrDefault(id, 1.0);
            var now = population.Abundance;
            var aged = population.AgeDistributionMeanSols + deltaTimeSols;
            if (now > survivors && now > 0.0)
            {
                workspace.Totals.NewIndividuals += now - survivors;
                workspace.Newborns[id] = now - survivors;
                population.AgeDistributionMeanSols = aged * (survivors / now);
            }
            else
            {
                population.AgeDistributionMeanSols = aged;
            }
        }
    }
}

/// <summary>
/// recycle-dead-biomass (T05): runs <c>necromass_breakdown</c> on all of the tick's dead
/// biomass, so its C, N and P return to the pool as CH2O, NH3 and H3PO4 (ADR-012).
/// </summary>
public sealed class RecycleSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public const string BreakdownReaction = "necromass_breakdown";

    public string Name => SimulationSteps.RecycleDeadBiomass;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var reaction = workspace.Catalog.Reaction(BreakdownReaction);
        var biomassPerExtent = reaction.Inputs.Single(term => term.PoolSlot < 0).KgPerExtent;
        var waterSlot = ResourceColumns.Slot(nameof(ResourcePool.Water));
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            var dead = workspace.DeadKg[cell];
            if (dead <= 0.0)
            {
                continue;
            }

            var extent = dead / biomassPerExtent;
            foreach (var term in reaction.Outputs)
            {
                var kg = term.KgPerExtent * extent;
                ResourceColumns.Add(state.Resources[cell], term.PoolSlot, kg);
                if (term.PoolSlot == waterSlot)
                {
                    state.WaterBudgets[cell].RecordedTotalKg += kg;
                }
            }

            workspace.DeadKg[cell] = 0.0;
            workspace.Totals.BiomassRecycledKg += dead;
        }
    }
}
