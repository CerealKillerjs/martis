using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class EnvironmentStateTests
{
    private static readonly string[] Names =
    [
        nameof(EnvironmentState.TemperatureKelvin),
        nameof(EnvironmentState.PressurePascal),
        nameof(EnvironmentState.UvRadiation),
        nameof(EnvironmentState.IonizingRadiation),
        nameof(EnvironmentState.SolarIrradiance),
        nameof(EnvironmentState.WaterIce),
        nameof(EnvironmentState.WaterVapor),
        nameof(EnvironmentState.SoilPorosity),
        nameof(EnvironmentState.PerchlorateConcentration),
    ];

    [Fact]
    public void DefaultsAreZeroAndPassTheCheck()
    {
        var state = new EnvironmentState();
        state.CheckInvariants();
        Assert.Equal(Names, state.Quantities.Select(quantity => quantity.Name).ToArray());
        Assert.All(state.Quantities, quantity => DoubleBits.Equal(0.0, quantity.Value));
    }

    [Fact]
    public void EveryDoublePropertyIsInTheQuantityList()
    {
        var listed = new EnvironmentState().Quantities.Select(quantity => quantity.Name).Order(StringComparer.Ordinal);
        var properties = ScalarProperties.WritableDoubles(typeof(EnvironmentState)).Order(StringComparer.Ordinal);
        Assert.Equal(listed, properties);
    }

    public static IEnumerable<object[]> BadValues()
    {
        foreach (var name in Names)
        {
            foreach (var row in FiniteEdges.NonFiniteOrNegative())
            {
                yield return [name, row[0]];
            }
        }
    }

    public static IEnumerable<object[]> GoodValues()
    {
        foreach (var name in Names)
        {
            foreach (var row in FiniteEdges.NonNegativeFinite())
            {
                yield return [name, row[0]];
            }
        }
    }

    [Theory]
    [MemberData(nameof(BadValues))]
    public void CheckRejectsANonFiniteOrNegativeField(string name, double value)
    {
        var state = new EnvironmentState();
        ScalarProperties.Set(state, name, value);
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(name, error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }

    [Theory]
    [MemberData(nameof(GoodValues))]
    public void CheckAcceptsAFiniteNonNegativeField(string name, double value)
    {
        var state = new EnvironmentState();
        ScalarProperties.Set(state, name, value);
        state.CheckInvariants();
        DoubleBits.Equal(value, state.Quantities.Single(quantity => quantity.Name == name).Value);
    }

    [Fact]
    public void CheckReportsTheFirstBrokenFieldInDeclarationOrder()
    {
        var state = new EnvironmentState
        {
            PerchlorateConcentration = -1,
            TemperatureKelvin = double.NaN,
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(nameof(EnvironmentState.TemperatureKelvin), error.ParamName);
    }
}
