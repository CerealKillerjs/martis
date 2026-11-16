using System.Reflection;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Genetics;
using Martis.Domain.Validation;
using Martis.Domain.World;

namespace Martis.Domain.Tests;

public sealed class PhenotypeTests
{
    [Fact]
    public void CalculatorCopiesBoundExpressionsAndDoesNotInventACoefficient()
    {
        var genome = Samples.Genome(("uv", 0.25), ("", 0.4));
        var binding = new PhenotypeGeneBinding(
            uvResistance: new GeneId("uv"),
            thermalTolerance: new GeneId("missing"),
            photosyntheticEfficiency: new GeneId(string.Empty));

        var phenotype = PhenotypeCalculator.Calculate(genome, binding);

        DoubleBits.Equal(0.25, phenotype.UvResistance);
        DoubleBits.Equal(0.0, phenotype.ThermalTolerance);
        DoubleBits.Equal(0.4, phenotype.PhotosyntheticEfficiency);
        DoubleBits.Equal(0.0, phenotype.DesiccationTolerance);
        DoubleBits.Equal(0.0, phenotype.MaintenanceCost);
        DoubleBits.Equal(0.0, phenotype.ReproductionRate);
        DoubleBits.Equal(0.0, phenotype.BiofilmProduction);
        Assert.Equal(phenotype, PhenotypeCalculator.Calculate(genome, binding));
    }

    [Fact]
    public void UnboundComponentsStayAtTheLowerBound()
    {
        var phenotype = PhenotypeCalculator.Calculate(Samples.Genome(("uv", 1)), new PhenotypeGeneBinding());
        Assert.Equal(new Phenotype(0, 0, 0, 0, 0, 0, 0), phenotype);
    }

    [Fact]
    public void PopulationDoesNotStoreThePhenotype()
    {
        var population = Samples.Population(genome: Samples.Genome(("uv", 0.2)));
        var binding = new PhenotypeGeneBinding(uvResistance: new GeneId("uv"));
        var first = PhenotypeCalculator.Calculate(population.Genome, binding);

        population.Genome = Samples.Genome(("uv", 0.8));
        var second = PhenotypeCalculator.Calculate(population.Genome, binding);

        DoubleBits.Equal(0.2, first.UvResistance);
        DoubleBits.Equal(0.8, second.UvResistance);
        Assert.DoesNotContain(PropertiesAndFields(typeof(Population)), static type => type == typeof(Phenotype));
        Assert.DoesNotContain(PropertiesAndFields(typeof(SimulationState)), static type => type == typeof(Phenotype));
        Assert.DoesNotContain(PropertiesAndFields(typeof(OrganismDefinition)), static type => type == typeof(Phenotype));
    }

    [Theory]
    [MemberData(nameof(FiniteEdges.NonFiniteOrNegative), MemberType = typeof(FiniteEdges))]
    public void ConstructorRejectsANonFiniteOrNegativeComponent(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Phenotype(value, 0, 0, 0, 0, 0, 0));
        var valid = new Phenotype(0, 0, 0, 0, 0, 0, 0);
        Assert.Throws<ArgumentOutOfRangeException>(() => valid with { MaintenanceCost = value });
    }

    [Fact]
    public void MaintenanceCostRejectsAFractionAboveOne()
    {
        var above = Math.BitIncrement(1.0);
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new Phenotype(0, 0, 0, 0, above, 1, 0));
        Assert.Equal(nameof(Phenotype.MaintenanceCost), error.ParamName);
        Assert.Contains(InvariantIds.MaintenanceCostFraction, error.Message, StringComparison.Ordinal);

        var atOne = new Phenotype(0, 0, 0, 0, 1, 1, 0);
        DoubleBits.Equal(1, atOne.MaintenanceCost);
        DoubleBits.Equal(1, atOne.ReproductionRate);
    }

    public static IEnumerable<object[]> RejectedReproductionRates()
    {
        yield return [Math.BitIncrement(1.0)];
        yield return [-0.01];
        yield return [double.NaN];
        yield return [double.PositiveInfinity];
        yield return [double.NegativeInfinity];
    }

    [Theory]
    [MemberData(nameof(RejectedReproductionRates))]
    public void ReproductionRateRejectsValuesOutsideTheUnitInterval(double value)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new Phenotype(0, 0, 0, 0, 0, value, 0));
        Assert.Equal(nameof(Phenotype.ReproductionRate), error.ParamName);
        Assert.Contains(InvariantIds.ReproductionRatePerCapita, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    public void ReproductionRateAcceptsZeroAndOne(double value)
    {
        var phenotype = new Phenotype(0, 0, 0, 0, 0, value, 0);
        DoubleBits.Equal(value, phenotype.ReproductionRate);
    }

    [Fact]
    public void ConstructorAcceptsZeroAndALargeFiniteValue()
    {
        var phenotype = new Phenotype(0, -0.0, 1, double.MaxValue, Math.BitIncrement(0), 0, 0);
        phenotype = phenotype with { BiofilmProduction = 2 };
        DoubleBits.Equal(0, phenotype.UvResistance);
        DoubleBits.Equal(double.MaxValue, phenotype.PhotosyntheticEfficiency);
        DoubleBits.Equal(2, phenotype.BiofilmProduction);
    }

    [Fact]
    public void CalculatorRejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => PhenotypeCalculator.Calculate(null!, new PhenotypeGeneBinding()));
        Assert.Throws<ArgumentNullException>(() => PhenotypeCalculator.Calculate(Samples.Genome(), null!));
    }

    private static IEnumerable<Type> PropertiesAndFields(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return property.PropertyType;
        }

        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            yield return field.FieldType;
        }
    }
}
