using Martis.Domain.Biology;
using Martis.Domain.Common;

namespace Martis.Domain.Tests;

public sealed class PopulationTests
{
    [Fact]
    public void DefaultsPassAndZeroAbundanceIsLegal()
    {
        var population = Samples.Population();
        population.CheckInvariants();
        DoubleBits.Equal(0, population.Abundance);
        DoubleBits.Equal(0, population.BiomassKg);
        DoubleBits.Equal(0, population.EnergyReserveJ);
        DoubleBits.Equal(0, population.AgeDistributionMeanSols);
        DoubleBits.Equal(0, population.Viability);
    }

    public static IEnumerable<object[]> BadNonNegative() => FiniteEdges.NonFiniteOrNegative();

    public static IEnumerable<object[]> GoodNonNegative() => FiniteEdges.NonNegativeFinite();

    [Theory]
    [MemberData(nameof(BadNonNegative))]
    public void AbundanceBiomassEnergyAndAgeRejectNonFiniteOrNegativeValues(double value)
    {
        AssertRejected(population => population.Abundance = value, nameof(Population.Abundance), value);
        AssertRejected(population => population.BiomassKg = value, nameof(Population.BiomassKg), value);
        AssertRejected(population => population.EnergyReserveJ = value, nameof(Population.EnergyReserveJ), value);
        AssertRejected(population => population.AgeDistributionMeanSols = value, nameof(Population.AgeDistributionMeanSols), value);
    }

    [Theory]
    [MemberData(nameof(GoodNonNegative))]
    public void AbundanceBiomassEnergyAndAgeAcceptFiniteNonNegativeValues(double value)
    {
        var population = Samples.Population();
        population.Abundance = value;
        population.BiomassKg = value;
        population.EnergyReserveJ = value;
        population.AgeDistributionMeanSols = value;
        population.Viability = 1;
        population.CheckInvariants();
    }

    [Theory]
    [MemberData(nameof(FiniteEdges.OutsideUnitInterval), MemberType = typeof(FiniteEdges))]
    public void ViabilityRejectsValuesOutsideTheUnitInterval(double value)
    {
        AssertRejected(population => population.Viability = value, nameof(Population.Viability), value);
    }

    [Theory]
    [MemberData(nameof(FiniteEdges.InsideUnitInterval), MemberType = typeof(FiniteEdges))]
    public void ViabilityAcceptsTheClosedUnitInterval(double value)
    {
        var population = Samples.Population();
        population.Viability = value;
        population.CheckInvariants();
        DoubleBits.Equal(value, population.Viability);
    }

    [Fact]
    public void GenomeCannotBeNull()
    {
        Assert.Throws<ArgumentNullException>(() => new Population(
            new PopulationId(1),
            new OrganismId("organism"),
            0,
            null!));

        var population = Samples.Population();
        Assert.Throws<ArgumentNullException>(() => population.Genome = null!);
    }

    private static void AssertRejected(Action<Population> assign, string name, double value)
    {
        var population = Samples.Population();
        assign(population);
        var error = Assert.Throws<ArgumentOutOfRangeException>(population.CheckInvariants);
        Assert.Equal(name, error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }
}
