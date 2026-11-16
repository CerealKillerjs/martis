using Martis.Domain.Environment;
using Martis.Domain.Validation;
using Martis.Domain.World;

namespace Martis.Domain.Tests;

public sealed class SimulationStateTests
{
    [Fact]
    public void SquareExampleHasOneSlotPerCellAndValidatesClean()
    {
        const int width = 64;
        const int height = 64;
        var state = new SimulationState(width, height);

        Assert.Equal(width, state.Width);
        Assert.Equal(height, state.Height);
        Assert.Equal(4096, state.CellCount);
        Assert.Equal(4096, state.Environment.Length);
        Assert.Equal(4096, state.Resources.Length);
        Assert.Equal(4096, state.AllocationBuffers.Length);
        Assert.Equal(4095, state.Index(width - 1, height - 1).ToLinearIndex());
        Assert.Equal(state.ToLinearIndex(width - 1, height - 1), state.Index(width - 1, height - 1).ToLinearIndex());
        Assert.Empty(state.Populations);
        Assert.NotNull(state.Statistics);
        Assert.Equal(0, state.Tick);
        Assert.All(state.Environment, static cell => Assert.NotNull(cell));
        Assert.All(state.Resources, static cell => Assert.NotNull(cell));
        Assert.All(state.AllocationBuffers, static cell => Assert.NotNull(cell));
        Assert.Empty(SimulationValidator.Validate(state));
    }

    [Fact]
    public void CellsAreIndependentObjects()
    {
        var state = new SimulationState(2, 2);
        state.Environment[0].TemperatureKelvin = 1;
        state.Resources[0].Water = 2;
        state.AllocationBuffers[0].Water = 3;

        DoubleBits.Equal(0, state.Environment[1].TemperatureKelvin);
        DoubleBits.Equal(0, state.Resources[1].Water);
        DoubleBits.Equal(2, state.Resources[0].Water);
        DoubleBits.Equal(0, state.Resources[3].Water);
        Assert.NotSame(state.Environment[0], state.Environment[1]);
        Assert.NotSame(state.Resources[0], state.AllocationBuffers[0]);
    }

    [Theory]
    [InlineData(0, 1, "width")]
    [InlineData(-1, 1, "width")]
    [InlineData(1, 0, "height")]
    [InlineData(1, -4, "height")]
    public void NonPositiveDimensionsThrow(int width, int height, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new SimulationState(width, height));
        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void OverflowingDimensionsThrowBeforeAllocation()
    {
        Assert.Throws<OverflowException>(() => new SimulationState(100_000, 100_000));
    }

    [Fact]
    public void EveryCoordinateRoundTripsOnASmallGrid()
    {
        const int width = 5;
        const int height = 4;
        var state = new SimulationState(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                Assert.Equal((y * width) + x, state.ToLinearIndex(x, y));
            }
        }
    }

    [Fact]
    public void SourceDoesNotHardcodeTheExampleMapSize()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "Martis.Domain", "World", "SimulationState.cs"));
        Assert.DoesNotContain("64", source, StringComparison.Ordinal);
        Assert.DoesNotContain("4096", source, StringComparison.Ordinal);
    }
}
