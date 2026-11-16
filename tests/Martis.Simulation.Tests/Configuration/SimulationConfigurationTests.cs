using Martis.Simulation.Engine;

namespace Martis.Simulation.Tests.Configuration;

public sealed class SimulationConfigurationTests
{
    [Fact]
    public void ValidConfigurationPasses()
    {
        var configuration = new SimulationConfiguration(64, 64, 1.0, 12345UL, 32, true);
        configuration.Validate();
        Assert.Equal(1.0, configuration.DeltaTimeSols);
        Assert.Equal(32, configuration.MaxVariantsPerSpeciesPerCell);
        Assert.Equal(32, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(-1, 8)]
    [InlineData(8, 0)]
    [InlineData(8, -3)]
    public void NonPositiveDimensionsThrow(int width, int height)
    {
        var configuration = Sample(width, height, 1.0, 1);
        var error = Assert.Throws<InvalidOperationException>(configuration.Validate);
        Assert.Contains("dimensions", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void NonPositiveStepThrows(double step)
    {
        var configuration = Sample(8, 8, step, 1);
        var error = Assert.Throws<InvalidOperationException>(configuration.Validate);
        Assert.Contains("DeltaTimeSols", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonFiniteStepThrows()
    {
        Assert.Throws<InvalidOperationException>(Sample(8, 8, double.NaN, 1).Validate);
        Assert.Throws<InvalidOperationException>(Sample(8, 8, double.PositiveInfinity, 1).Validate);
        Assert.Throws<InvalidOperationException>(Sample(8, 8, double.NegativeInfinity, 1).Validate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void NonPositiveVariantCapThrows(int cap)
    {
        var configuration = Sample(8, 8, 1.0, cap);
        var error = Assert.Throws<InvalidOperationException>(configuration.Validate);
        Assert.Contains("MaxVariantsPerSpeciesPerCell", error.Message, StringComparison.Ordinal);
    }

    private static SimulationConfiguration Sample(int width, int height, double step, int cap)
    {
        return new SimulationConfiguration(width, height, step, 0UL, cap, false);
    }
}
