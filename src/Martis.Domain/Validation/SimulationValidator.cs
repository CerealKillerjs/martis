using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.World;

namespace Martis.Domain.Validation;

/// <summary>
/// Walks a <see cref="SimulationState"/> and returns every broken invariant.
/// It does not stop at the first failure (H-15).
/// Order is fixed: cells by ascending linear index; inside a cell, environment,
/// then the resource pool, then the allocation buffer, each in declaration order;
/// then populations in list order; inside a population, cell range, abundance,
/// biomass, energy reserve, age mean, viability, then genes in <see cref="Genetics.GeneId"/> order.
/// VAL-001 covers environment, the nine pool deposits and the nine buffer slots.
/// VAL-002 covers abundance, energy reserve and age mean. VAL-003 is biomass.
/// VAL-004 is viability, as a fraction in [0, 1]. VAL-005 is each gene expression.
/// </summary>
public static class SimulationValidator
{
    public static IReadOnlyList<InvariantViolation> Validate(SimulationState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var violations = new List<InvariantViolation>();
        for (var index = 0; index < state.CellCount; index++)
        {
            var environment = state.Environment[index]
                ?? throw new InvalidOperationException($"Environment[{index}] is null.");
            var resources = state.Resources[index]
                ?? throw new InvalidOperationException($"Resources[{index}] is null.");
            var buffer = state.AllocationBuffers[index]
                ?? throw new InvalidOperationException($"AllocationBuffers[{index}] is null.");

            CollectQuantities(
                violations,
                InvariantIds.NoNegativeResource,
                state.Tick,
                index,
                population: null,
                environment.Quantities);
            CollectQuantities(
                violations,
                InvariantIds.NoNegativeResource,
                state.Tick,
                index,
                population: null,
                resources.Quantities);
            CollectQuantities(
                violations,
                InvariantIds.NoNegativeResource,
                state.Tick,
                index,
                population: null,
                buffer.Quantities);
        }

        foreach (var population in state.Populations)
        {
            if (population is null)
            {
                throw new InvalidOperationException("The population list contains a null entry.");
            }

            CollectPopulation(state, population, violations);
        }

        return violations;
    }

    private static void CollectPopulation(
        SimulationState state,
        Population population,
        List<InvariantViolation> violations)
    {
        var tick = state.Tick;
        var cell = population.CellIndex;
        var id = population.Id;
        if (cell < 0 || cell >= state.CellCount)
        {
            violations.Add(new InvariantViolation(
                InvariantIds.CellIndexInRange,
                tick,
                cell,
                id,
                cell,
                "CellIndex"));
        }

        CollectOne(
            violations,
            InvariantIds.NoNegativePopulation,
            tick,
            cell,
            id,
            population.Abundance,
            nameof(Population.Abundance),
            QuantityRules.IsNonNegativeFinite);
        CollectOne(
            violations,
            InvariantIds.NoNegativeBiomass,
            tick,
            cell,
            id,
            population.BiomassKg,
            nameof(Population.BiomassKg),
            QuantityRules.IsNonNegativeFinite);
        CollectOne(
            violations,
            InvariantIds.NoNegativePopulation,
            tick,
            cell,
            id,
            population.EnergyReserveJ,
            nameof(Population.EnergyReserveJ),
            QuantityRules.IsNonNegativeFinite);
        CollectOne(
            violations,
            InvariantIds.NoNegativePopulation,
            tick,
            cell,
            id,
            population.AgeDistributionMeanDays,
            nameof(Population.AgeDistributionMeanDays),
            QuantityRules.IsNonNegativeFinite);
        CollectOne(
            violations,
            InvariantIds.ProbabilityInUnitInterval,
            tick,
            cell,
            id,
            population.Viability,
            nameof(Population.Viability),
            QuantityRules.IsUnitInterval);

        foreach (var gene in population.Genome)
        {
            if (QuantityRules.IsUnitInterval(gene.Expression))
            {
                continue;
            }

            var quantity = gene.GeneId.Value.Length == 0 ? "GeneId.Empty" : gene.GeneId.Value;
            violations.Add(new InvariantViolation(
                InvariantIds.GeneInUnitInterval,
                tick,
                cell,
                id,
                gene.Expression,
                quantity));
        }
    }

    private static void CollectQuantities(
        List<InvariantViolation> violations,
        string invariantId,
        long tick,
        int cellIndex,
        PopulationId? population,
        IReadOnlyList<NamedQuantity> quantities)
    {
        foreach (var quantity in quantities)
        {
            CollectOne(
                violations,
                invariantId,
                tick,
                cellIndex,
                population,
                quantity.Value,
                quantity.Name,
                QuantityRules.IsNonNegativeFinite);
        }
    }

    private static void CollectOne(
        List<InvariantViolation> violations,
        string invariantId,
        long tick,
        int cellIndex,
        PopulationId? population,
        double value,
        string quantity,
        Func<double, bool> valid)
    {
        if (valid(value))
        {
            return;
        }

        violations.Add(new InvariantViolation(
            invariantId,
            tick,
            cellIndex,
            population,
            value,
            quantity));
    }
}
