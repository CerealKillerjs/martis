using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.Validation;
using Martis.Domain.World;

namespace Martis.Domain.Tests.Validation;

public sealed class SimulationValidatorTests
{
    [Fact]
    public void FreshStateAndALegalPopulationHaveNoViolations()
    {
        var state = new SimulationState(2, 2) { Tick = 0 };
        state.Resources[3].Water = 1;
        state.WaterBudgets[3].RecordedTotalKg = 1;
        state.Environment[1].PressurePascal = AssignedDouble.Set(double.MaxValue);
        state.AllocationBuffers[0].Iron = 0;
        var population = Samples.Population(cell: 3, genome: Samples.Genome(("z", 1), ("a", 0)), id: 9);
        population.Abundance = 0;
        population.BiomassKg = -0.0;
        population.EnergyReserveJ = 2;
        population.AgeDistributionMeanSols = 0;
        population.Viability = 1;
        state.Populations.Add(population);

        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void Val001RejectsEachResourceAndKeepsTheValue()
    {
        foreach (var name in ResourcePoolTests.Names)
        {
            foreach (var row in FiniteEdges.NonFiniteOrNegative())
            {
                var value = (double)row[0];
                var state = new SimulationState(1, 2) { Tick = 17 };
                ScalarProperties.Set(state.Resources[1], name, value);

                var violation = Assert.Single(SimulationValidator.Validate(state));
                Assert.Equal(InvariantIds.NoNegativeResource, violation.InvariantId);
                Assert.Equal(17, violation.Tick);
                Assert.Equal(1, violation.CellIndex);
                Assert.Null(violation.Population);
                Assert.Equal(name, violation.Quantity);
                DoubleBits.Equal(value, violation.Value);
            }
        }
    }

    [Fact]
    public void Val001AcceptsAFiniteNonNegativeResource()
    {
        var state = new SimulationState(1, 1);
        state.Resources[0].Sulfur = Math.BitIncrement(0);
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void EnvironmentAndAllocationUseDomainIdsNotVal001()
    {
        var environment = new SimulationState(1, 1) { Tick = 4 };
        environment.Environment[0].UvRadiation = double.PositiveInfinity;
        var environmentViolation = Assert.Single(SimulationValidator.Validate(environment));
        Assert.Equal(InvariantIds.EnvironmentNonNegative, environmentViolation.InvariantId);
        Assert.Equal(nameof(EnvironmentState.UvRadiation), environmentViolation.Quantity);
        Assert.Equal(4, environmentViolation.Tick);
        Assert.NotEqual(InvariantIds.NoNegativeResource, environmentViolation.InvariantId);

        var buffer = new SimulationState(1, 1);
        buffer.AllocationBuffers[0].MineralCarbon = -1;
        var bufferViolation = Assert.Single(SimulationValidator.Validate(buffer));
        Assert.Equal(InvariantIds.AllocationNonNegative, bufferViolation.InvariantId);
        Assert.Equal(nameof(ResourcePool.MineralCarbon), bufferViolation.Quantity);
        Assert.Null(bufferViolation.Population);
        Assert.NotEqual(InvariantIds.NoNegativeResource, bufferViolation.InvariantId);
    }

    [Fact]
    public void Val002RejectsNonFiniteAbundanceOnly()
    {
        var (state, population) = WorldWithPopulation();
        population.Abundance = double.NaN;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.NoNegativePopulation, violation.InvariantId);
        Assert.Equal(nameof(Population.Abundance), violation.Quantity);
        Assert.Equal(population.Id, violation.Population);
        Assert.True(double.IsNaN(violation.Value));
    }

    [Fact]
    public void EnergyReserveIsNotVal002()
    {
        var (state, population) = WorldWithPopulation();
        population.EnergyReserveJ = -1;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.EnergyReserveNonNegative, violation.InvariantId);
        Assert.NotEqual(InvariantIds.NoNegativePopulation, violation.InvariantId);
        Assert.Equal(nameof(Population.EnergyReserveJ), violation.Quantity);
    }

    [Fact]
    public void AgeInSolsIsNotVal002()
    {
        var (state, population) = WorldWithPopulation();
        population.AgeDistributionMeanSols = double.NaN;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.AgeNonNegative, violation.InvariantId);
        Assert.Equal(nameof(Population.AgeDistributionMeanSols), violation.Quantity);
        Assert.NotEqual(InvariantIds.NoNegativePopulation, violation.InvariantId);
    }

    [Fact]
    public void Val002AcceptsZeroAbundance()
    {
        var (state, population) = WorldWithPopulation();
        population.Abundance = 0;
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void Val003RejectsNonFiniteOrNegativeBiomass()
    {
        var (state, population) = WorldWithPopulation();
        population.BiomassKg = double.NegativeInfinity;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.NoNegativeBiomass, violation.InvariantId);
        Assert.Equal(nameof(Population.BiomassKg), violation.Quantity);
        Assert.Equal(population.Id, violation.Population);
        DoubleBits.Equal(double.NegativeInfinity, violation.Value);
    }

    [Fact]
    public void Val003AcceptsZeroBiomass()
    {
        var (state, population) = WorldWithPopulation();
        population.BiomassKg = 0;
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Theory]
    [MemberData(nameof(FiniteEdges.OutsideUnitInterval), MemberType = typeof(FiniteEdges))]
    public void ViabilityOutsideTheUnitIntervalIsNotVal004(double value)
    {
        var (state, population) = WorldWithPopulation();
        population.Viability = value;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.ViabilityUnitInterval, violation.InvariantId);
        Assert.NotEqual(InvariantIds.ProbabilityInUnitInterval, violation.InvariantId);
        Assert.Equal(nameof(Population.Viability), violation.Quantity);
        Assert.Equal(population.Id, violation.Population);
        DoubleBits.Equal(value, violation.Value);
    }

    [Theory]
    [MemberData(nameof(FiniteEdges.InsideUnitInterval), MemberType = typeof(FiniteEdges))]
    public void ViabilityInsideTheUnitIntervalHasNoViolation(double value)
    {
        var (state, population) = WorldWithPopulation();
        population.Viability = value;
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void Val005RejectsBadExpressionsAndReportsGenesInGeneIdOrder()
    {
        var (state, population) = WorldWithPopulation();
        population.Genome = Genome.CreateUnchecked(
        [
            GeneExpression.Unchecked(new GeneId("b"), 1.01),
            GeneExpression.Unchecked(new GeneId("a"), double.NaN),
            GeneExpression.Unchecked(new GeneId(string.Empty), -0.01),
        ]);

        var violations = SimulationValidator.Validate(state);
        Assert.Equal(3, violations.Count);
        Assert.All(violations, violation =>
        {
            Assert.Equal(InvariantIds.GeneInUnitInterval, violation.InvariantId);
            Assert.Equal(population.Id, violation.Population);
            Assert.Equal(0, violation.CellIndex);
        });
        Assert.Equal(["GeneId.Empty", "a", "b"], violations.Select(violation => violation.Quantity).ToArray());
        DoubleBits.Equal(-0.01, violations[0].Value);
        Assert.True(double.IsNaN(violations[1].Value));
        DoubleBits.Equal(1.01, violations[2].Value);
    }

    [Fact]
    public void Val005AcceptsZeroAndOne()
    {
        var (state, population) = WorldWithPopulation();
        population.Genome = Samples.Genome(("low", 0), ("high", 1));
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void TwoSimultaneousViolationsAreBothReturned()
    {
        var state = new SimulationState(1, 1) { Tick = 8 };
        state.Resources[0].Water = -1;
        var population = Samples.Population();
        population.Abundance = -2;
        state.Populations.Add(population);

        var violations = SimulationValidator.Validate(state);
        Assert.Equal(2, violations.Count);
        Assert.Equal(InvariantIds.NoNegativeResource, violations[0].InvariantId);
        Assert.Equal(nameof(ResourcePool.Water), violations[0].Quantity);
        Assert.Null(violations[0].Population);
        DoubleBits.Equal(-1, violations[0].Value);
        Assert.Equal(8, violations[0].Tick);

        Assert.Equal(InvariantIds.NoNegativePopulation, violations[1].InvariantId);
        Assert.Equal(nameof(Population.Abundance), violations[1].Quantity);
        Assert.Equal(population.Id, violations[1].Population);
        DoubleBits.Equal(-2, violations[1].Value);
        Assert.Equal(8, violations[1].Tick);
        Assert.Equal(0, violations[1].CellIndex);
    }

    [Fact]
    public void CellsAndPopulationsAreVisitedInStableOrder()
    {
        var state = new SimulationState(1, 2);
        state.Resources[1].Oxygen = -1;
        state.Resources[0].CarbonDioxide = -2;
        var second = Samples.Population(cell: 1, id: 2);
        second.Abundance = -3;
        var first = Samples.Population(cell: 0, id: 1);
        first.BiomassKg = -4;
        state.Populations.Add(second);
        state.Populations.Add(first);

        var violations = SimulationValidator.Validate(state);
        Assert.Equal(
            [
                (InvariantIds.NoNegativeResource, (int?)0, "CarbonDioxide"),
                (InvariantIds.NoNegativeResource, (int?)1, "Oxygen"),
                (InvariantIds.NoNegativePopulation, (int?)1, nameof(Population.Abundance)),
                (InvariantIds.NoNegativeBiomass, (int?)0, nameof(Population.BiomassKg)),
            ],
            violations.Select(violation => (violation.InvariantId, violation.CellIndex, violation.Quantity)).ToArray());
        Assert.Equal(second.Id, violations[2].Population);
        Assert.Equal(first.Id, violations[3].Population);
    }

    [Fact]
    public void CellIndexOutsideTheGridIsReportedWithoutIndexingTheArray()
    {
        var state = new SimulationState(1, 1) { Tick = 3 };
        var population = Samples.Population(cell: 5);
        population.Abundance = -1;
        state.Populations.Add(population);

        var violations = SimulationValidator.Validate(state);
        Assert.Equal(2, violations.Count);
        Assert.Equal(InvariantIds.CellIndexInRange, violations[0].InvariantId);
        Assert.Equal(5, violations[0].CellIndex);
        DoubleBits.Equal(5, violations[0].Value);
        Assert.Equal(population.Id, violations[0].Population);
        Assert.Equal(3, violations[0].Tick);
        Assert.Equal(InvariantIds.NoNegativePopulation, violations[1].InvariantId);
        Assert.Equal(5, violations[1].CellIndex);
    }

    [Fact]
    public void NullStateAndBrokenSlotsFailClearly()
    {
        Assert.Throws<ArgumentNullException>(() => SimulationValidator.Validate(null!));

        var missingResource = new SimulationState(1, 1);
        missingResource.Resources[0] = null!;
        var missing = Assert.Throws<InvalidOperationException>(() => SimulationValidator.Validate(missingResource));
        Assert.Contains("Resources[0]", missing.Message, StringComparison.Ordinal);

        var missingPopulation = new SimulationState(1, 1);
        missingPopulation.Populations.Add(null!);
        var populationError = Assert.Throws<InvalidOperationException>(() => SimulationValidator.Validate(missingPopulation));
        Assert.Contains("null", populationError.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PorosityAboveOneIsDomVal003()
    {
        var state = new SimulationState(1, 1);
        state.Environment[0].SoilPorosity = Math.BitIncrement(1.0);
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.SoilPorosityUnitInterval, violation.InvariantId);
        Assert.Equal(nameof(EnvironmentState.SoilPorosity), violation.Quantity);
        DoubleBits.Equal(Math.BitIncrement(1.0), violation.Value);
    }

    [Fact]
    public void ZeroKelvinIsRejectedAndUnsetIsNotALoadedScenario()
    {
        var zero = new SimulationState(1, 1) { Tick = 6 };
        zero.Environment[0].TemperatureKelvin = AssignedDouble.Set(0.0);
        zero.Environment[0].PressurePascal = AssignedDouble.Set(0.0);
        zero.Environment[0].Scope = new EnvironmentScope.Lab();
        var zeroViolation = Assert.Single(SimulationValidator.Validate(zero, ValidationContext.LoadedScenario));
        Assert.Equal(InvariantIds.TemperaturePositive, zeroViolation.InvariantId);
        DoubleBits.Equal(0.0, zeroViolation.Value);
        Assert.Equal(6, zeroViolation.Tick);

        var fresh = new SimulationState(1, 1);
        Assert.Empty(SimulationValidator.Validate(fresh));
        var loaded = SimulationValidator.Validate(fresh, ValidationContext.LoadedScenario);
        Assert.Equal(
            [
                (InvariantIds.ScenarioQuantityUnset, nameof(EnvironmentState.TemperatureKelvin)),
                (InvariantIds.ScenarioQuantityUnset, nameof(EnvironmentState.PressurePascal)),
                (InvariantIds.ScenarioScopeMissing, "Scope"),
            ],
            loaded.Select(violation => (violation.InvariantId, violation.Quantity)).ToArray());
        Assert.All(loaded, violation => Assert.True(double.IsNaN(violation.Value)));
    }

    [Fact]
    public void LoadedScenarioWithAnchorsAndAClosedWaterBudgetPasses()
    {
        var state = new SimulationState(1, 1);
        state.Environment[0].TemperatureKelvin = AssignedDouble.Set(Math.BitIncrement(0.0));
        state.Environment[0].PressurePascal = AssignedDouble.Set(0.0);
        state.Environment[0].Scope = new EnvironmentScope.PlanetaryMean();
        state.Resources[0].Water = 2;
        state.Environment[0].WaterIce = 3;
        state.Environment[0].WaterVapor = 4;
        state.WaterBudgets[0].RecordedTotalKg = 9;

        Assert.Empty(SimulationValidator.Validate(state, ValidationContext.LoadedScenario));
    }

    [Fact]
    public void CountingTheSameWaterAsLiquidAndIceBreaksTheRecordedTotal()
    {
        var state = new SimulationState(1, 1) { Tick = 2 };
        state.Resources[0].Water = 5;
        state.Environment[0].WaterIce = 5;
        state.WaterBudgets[0].RecordedTotalKg = 5;

        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.WaterPartition, violation.InvariantId);
        Assert.Equal(nameof(CellWaterBudget.RecordedTotalKg), violation.Quantity);
        DoubleBits.Equal(5, violation.Value);
        Assert.Equal(2, violation.Tick);
        Assert.Equal(0, violation.CellIndex);
        Assert.Equal(10, CellWaterBudget.ComponentSum(5, 5, 0));
    }

    [Fact]
    public void DoseFieldsStaySeparateAndRejectNonFiniteOrNegativeValues()
    {
        var state = new SimulationState(1, 1);
        state.Environment[0].IonizingAbsorbedDoseGyPerSol = -1;
        state.Environment[0].IonizingEquivalentDoseSvPerSol = double.PositiveInfinity;

        var violations = SimulationValidator.Validate(state);
        Assert.Equal(2, violations.Count);
        Assert.All(violations, violation => Assert.Equal(InvariantIds.IonizingDoseNonNegative, violation.InvariantId));
        Assert.Equal(nameof(EnvironmentState.IonizingAbsorbedDoseGyPerSol), violations[0].Quantity);
        Assert.Equal(nameof(EnvironmentState.IonizingEquivalentDoseSvPerSol), violations[1].Quantity);
        DoubleBits.Equal(-1, violations[0].Value);
        DoubleBits.Equal(double.PositiveInfinity, violations[1].Value);

        var finite = new SimulationState(1, 1);
        // dato de prueba; Sv = Gy × Q con Q ≥ 1
        finite.Environment[0].IonizingAbsorbedDoseGyPerSol = 2e-4;
        finite.Environment[0].IonizingEquivalentDoseSvPerSol = 5e-4;
        Assert.Empty(SimulationValidator.Validate(finite));
    }

    [Fact]
    public void WaterBalanceAcceptsTheToleranceBoundaryAndRejectsTheNextValue()
    {
        const double recorded = 1.0;
        var allowance = MassBalanceTolerances.MassBalanceAbsoluteToleranceKg
            + (MassBalanceTolerances.MassBalanceRelativeTolerance * Math.Abs(recorded));
        // With the absolute term included, recorded + allowance still sits inside.
        // The next representable value is the first one outside.
        var onBoundary = recorded + allowance;
        var pastBoundary = Math.BitIncrement(onBoundary);

        Assert.True(CellWaterBudget.WithinTolerance(CellWaterBudget.ComponentSum(onBoundary, 0, 0), recorded));
        Assert.False(CellWaterBudget.WithinTolerance(CellWaterBudget.ComponentSum(pastBoundary, 0, 0), recorded));

        var inside = new SimulationState(1, 1);
        inside.Resources[0].Water = onBoundary;
        inside.WaterBudgets[0].RecordedTotalKg = recorded;
        Assert.Empty(SimulationValidator.Validate(inside));

        var outside = new SimulationState(1, 1);
        outside.Resources[0].Water = pastBoundary;
        outside.WaterBudgets[0].RecordedTotalKg = recorded;
        var violation = Assert.Single(SimulationValidator.Validate(outside));
        Assert.Equal(InvariantIds.WaterPartition, violation.InvariantId);
    }

    [Fact]
    public void ZeroRecordedTotalAcceptsASumInsideTheAbsoluteTolerance()
    {
        var state = new SimulationState(1, 1);
        state.Resources[0].Water = 5.55e-17;
        state.WaterBudgets[0].RecordedTotalKg = 0;
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void ZeroRecordedTotalRejectsASumOfTwoNanogramsPerCell()
    {
        var state = new SimulationState(1, 1);
        state.Resources[0].Water = 2e-12;
        state.WaterBudgets[0].RecordedTotalKg = 0;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.WaterPartition, violation.InvariantId);
        DoubleBits.Equal(0, violation.Value);
    }

    [Fact]
    public void ZeroRecordedTotalAcceptsExactlyOneNanogramAndRejectsTheNextDouble()
    {
        var onBoundary = new SimulationState(1, 1);
        onBoundary.Resources[0].Water = 1e-12;
        onBoundary.WaterBudgets[0].RecordedTotalKg = 0;
        Assert.Empty(SimulationValidator.Validate(onBoundary));

        var pastBoundary = new SimulationState(1, 1);
        pastBoundary.Resources[0].Water = Math.BitIncrement(1e-12);
        pastBoundary.WaterBudgets[0].RecordedTotalKg = 0;
        var violation = Assert.Single(SimulationValidator.Validate(pastBoundary));
        Assert.Equal(InvariantIds.WaterPartition, violation.InvariantId);
    }

    public static IEnumerable<object[]> NonFiniteWaterBalances()
    {
        yield return [double.PositiveInfinity, 1.0];
        yield return [double.NegativeInfinity, 1.0];
        yield return [double.NaN, 1.0];
        yield return [1.0, double.PositiveInfinity];
        yield return [1.0, double.NegativeInfinity];
        yield return [1.0, double.NaN];
        yield return [double.NaN, double.PositiveInfinity];
    }

    [Theory]
    [MemberData(nameof(NonFiniteWaterBalances))]
    public void WithinToleranceRejectsANonFiniteSumOrTotal(double sum, double recorded)
    {
        Assert.False(CellWaterBudget.WithinTolerance(sum, recorded));
    }

    [Fact]
    public void ANegativeResourceDoesNotAlsoFailTheWaterPartition()
    {
        var state = new SimulationState(1, 1);
        state.Resources[0].Water = -1;
        var violation = Assert.Single(SimulationValidator.Validate(state));
        Assert.Equal(InvariantIds.NoNegativeResource, violation.InvariantId);
    }

    [Fact]
    public void ViolationRejectsAnEmptyIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new InvariantViolation("", 0, 0, null, 0, "Water"));
        Assert.Throws<ArgumentException>(() => new InvariantViolation("VAL-001", 0, 0, null, 0, ""));
    }

    private static (SimulationState State, Population Population) WorldWithPopulation()
    {
        var state = new SimulationState(1, 1) { Tick = 11 };
        var population = Samples.Population();
        state.Populations.Add(population);
        return (state, population);
    }
}
