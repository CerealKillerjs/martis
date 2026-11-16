using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.World;

namespace Martis.Domain.Validation;

/// <summary>
/// Walks a <see cref="SimulationState"/> and returns every broken invariant.
/// It does not stop at the first failure (H-15).
/// Order is fixed: cells by ascending linear index; inside a cell, temperature,
/// pressure, the other environmental fields in declaration order, scope when a
/// scenario is loaded, the nine resource deposits, the water partition, then
/// the allocation buffer; then populations in list order.
/// VAL-001 is only a negative or non-finite <see cref="ResourcePool"/> deposit.
/// VAL-002 is only abundance. VAL-003 is only biomass. VAL-005 is only a gene.
/// VAL-004 is not emitted: no field here is a probability as TDD §10 defines it.
/// </summary>
public static class SimulationValidator
{
    public static IReadOnlyList<InvariantViolation> Validate(SimulationState state)
        => Validate(state, ValidationContext.World);

    public static IReadOnlyList<InvariantViolation> Validate(SimulationState state, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(state);
        var violations = new List<InvariantViolation>();
        var scenario = context == ValidationContext.LoadedScenario;
        for (var index = 0; index < state.CellCount; index++)
        {
            var environment = state.Environment[index]
                ?? throw new InvalidOperationException($"Environment[{index}] is null.");
            var resources = state.Resources[index]
                ?? throw new InvalidOperationException($"Resources[{index}] is null.");
            var buffer = state.AllocationBuffers[index]
                ?? throw new InvalidOperationException($"AllocationBuffers[{index}] is null.");
            var water = state.WaterBudgets[index]
                ?? throw new InvalidOperationException($"WaterBudgets[{index}] is null.");

            CollectTemperature(violations, state.Tick, index, environment, scenario);
            CollectPressure(violations, state.Tick, index, environment, scenario);
            CollectEnvironmentDoubles(violations, state.Tick, index, environment);
            if (scenario && environment.Scope is null)
            {
                violations.Add(Missing(InvariantIds.ScenarioScopeMissing, state.Tick, index, "Scope"));
            }

            CollectQuantities(
                violations,
                InvariantIds.NoNegativeResource,
                state.Tick,
                index,
                resources.Quantities);
            CollectWater(violations, state.Tick, index, environment, resources, water);
            CollectQuantities(
                violations,
                InvariantIds.AllocationNonNegative,
                state.Tick,
                index,
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

    private static void CollectTemperature(
        List<InvariantViolation> violations,
        long tick,
        int index,
        EnvironmentState environment,
        bool scenario)
    {
        if (environment.TemperatureKelvin.IsUnset)
        {
            if (scenario)
            {
                violations.Add(Missing(InvariantIds.ScenarioQuantityUnset, tick, index, nameof(EnvironmentState.TemperatureKelvin)));
            }

            return;
        }

        CollectOne(
            violations,
            InvariantIds.TemperaturePositive,
            tick,
            index,
            environment.TemperatureKelvin.Value,
            nameof(EnvironmentState.TemperatureKelvin),
            QuantityRules.IsStrictlyPositiveFinite);
    }

    private static void CollectPressure(
        List<InvariantViolation> violations,
        long tick,
        int index,
        EnvironmentState environment,
        bool scenario)
    {
        if (environment.PressurePascal.IsUnset)
        {
            if (scenario)
            {
                violations.Add(Missing(InvariantIds.ScenarioQuantityUnset, tick, index, nameof(EnvironmentState.PressurePascal)));
            }

            return;
        }

        CollectOne(
            violations,
            InvariantIds.PressureNonNegative,
            tick,
            index,
            environment.PressurePascal.Value,
            nameof(EnvironmentState.PressurePascal),
            QuantityRules.IsNonNegativeFinite);
    }

    private static void CollectEnvironmentDoubles(
        List<InvariantViolation> violations,
        long tick,
        int index,
        EnvironmentState environment)
    {
        foreach (var quantity in environment.Quantities)
        {
            if (quantity.Name == nameof(EnvironmentState.SoilPorosity))
            {
                CollectOne(
                    violations,
                    InvariantIds.SoilPorosityUnitInterval,
                    tick,
                    index,
                    quantity.Value,
                    quantity.Name,
                    QuantityRules.IsUnitInterval);
            }
            else
            {
                CollectOne(
                    violations,
                    InvariantIds.EnvironmentNonNegative,
                    tick,
                    index,
                    quantity.Value,
                    quantity.Name,
                    QuantityRules.IsNonNegativeFinite);
            }
        }
    }

    private static void CollectWater(
        List<InvariantViolation> violations,
        long tick,
        int index,
        EnvironmentState environment,
        ResourcePool resources,
        CellWaterBudget budget)
    {
        var totalOk = QuantityRules.IsNonNegativeFinite(budget.RecordedTotalKg);
        if (!totalOk)
        {
            violations.Add(new InvariantViolation(
                InvariantIds.WaterPartition,
                tick,
                index,
                null,
                budget.RecordedTotalKg,
                nameof(CellWaterBudget.RecordedTotalKg)));
            return;
        }

        if (!QuantityRules.IsNonNegativeFinite(resources.Water)
            || !QuantityRules.IsNonNegativeFinite(environment.WaterIce)
            || !QuantityRules.IsNonNegativeFinite(environment.WaterVapor))
        {
            return;
        }

        var sum = CellWaterBudget.ComponentSum(resources.Water, environment.WaterIce, environment.WaterVapor);
        if (sum == budget.RecordedTotalKg)
        {
            return;
        }

        violations.Add(new InvariantViolation(
            InvariantIds.WaterPartition,
            tick,
            index,
            null,
            budget.RecordedTotalKg,
            nameof(CellWaterBudget.RecordedTotalKg)));
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

        CollectPopulationQuantity(
            violations,
            InvariantIds.NoNegativePopulation,
            tick,
            cell,
            id,
            population.Abundance,
            nameof(Population.Abundance),
            QuantityRules.IsNonNegativeFinite);
        CollectPopulationQuantity(
            violations,
            InvariantIds.NoNegativeBiomass,
            tick,
            cell,
            id,
            population.BiomassKg,
            nameof(Population.BiomassKg),
            QuantityRules.IsNonNegativeFinite);
        CollectPopulationQuantity(
            violations,
            InvariantIds.EnergyReserveNonNegative,
            tick,
            cell,
            id,
            population.EnergyReserveJ,
            nameof(Population.EnergyReserveJ),
            QuantityRules.IsNonNegativeFinite);
        CollectPopulationQuantity(
            violations,
            InvariantIds.AgeNonNegative,
            tick,
            cell,
            id,
            population.AgeDistributionMeanSols,
            nameof(Population.AgeDistributionMeanSols),
            QuantityRules.IsNonNegativeFinite);
        CollectPopulationQuantity(
            violations,
            InvariantIds.ViabilityUnitInterval,
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
        IReadOnlyList<NamedQuantity> quantities)
    {
        foreach (var quantity in quantities)
        {
            CollectOne(
                violations,
                invariantId,
                tick,
                cellIndex,
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
        double value,
        string quantity,
        Func<double, bool> valid)
    {
        if (valid(value))
        {
            return;
        }

        violations.Add(new InvariantViolation(invariantId, tick, cellIndex, null, value, quantity));
    }

    private static void CollectPopulationQuantity(
        List<InvariantViolation> violations,
        string invariantId,
        long tick,
        int cellIndex,
        PopulationId population,
        double value,
        string quantity,
        Func<double, bool> valid)
    {
        if (valid(value))
        {
            return;
        }

        violations.Add(new InvariantViolation(invariantId, tick, cellIndex, population, value, quantity));
    }

    private static InvariantViolation Missing(string invariantId, long tick, int index, string quantity)
        => new(invariantId, tick, index, null, double.NaN, quantity);
}
