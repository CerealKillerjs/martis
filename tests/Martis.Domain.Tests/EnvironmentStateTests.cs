using Martis.Domain.Common;
using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class EnvironmentStateTests
{
    private static readonly string[] NonNegativeNames =
    [
        nameof(EnvironmentState.UvRadiation),
        nameof(EnvironmentState.IonizingRadiation),
        nameof(EnvironmentState.SolarIrradiance),
        nameof(EnvironmentState.WaterIce),
        nameof(EnvironmentState.WaterVapor),
        nameof(EnvironmentState.PerchlorateConcentration),
    ];

    [Fact]
    public void DefaultsLeaveTemperatureAndPressureUnset()
    {
        var state = new EnvironmentState();

        Assert.True(state.TemperatureKelvin.IsUnset);
        Assert.True(state.PressurePascal.IsUnset);
        Assert.Null(state.Scope);
        state.CheckInvariants();
        Assert.All(state.Quantities, quantity => DoubleBits.Equal(0.0, quantity.Value));
        Assert.Throws<InvalidOperationException>(() => _ = state.TemperatureKelvin.Value);
    }

    [Fact]
    public void AssignedZeroKelvinIsRejectedAndIsNotUnset()
    {
        var unset = AssignedDouble.Unset;
        var zero = AssignedDouble.Set(0.0);
        var negativeZero = AssignedDouble.Set(-0.0);

        Assert.True(unset.IsUnset);
        Assert.True(zero.IsSet);
        Assert.NotEqual(unset, zero);
        DoubleBits.Equal(0.0, zero.Value);

        var state = new EnvironmentState { TemperatureKelvin = zero };
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(nameof(EnvironmentState.TemperatureKelvin), error.ParamName);

        state.TemperatureKelvin = negativeZero;
        Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
    }

    [Fact]
    public void AssignedZeroPascalIsARealPressure()
    {
        var state = new EnvironmentState
        {
            PressurePascal = AssignedDouble.Set(0.0),
        };

        Assert.True(state.PressurePascal.IsSet);
        state.CheckInvariants();
        DoubleBits.Equal(0.0, state.PressurePascal.Value);
    }

    [Fact]
    public void EveryDoublePropertyIsInTheQuantityList()
    {
        var listed = new EnvironmentState().Quantities.Select(quantity => quantity.Name).Order(StringComparer.Ordinal);
        var properties = ScalarProperties.WritableDoubles(typeof(EnvironmentState)).Order(StringComparer.Ordinal);
        Assert.Equal(listed, properties);
        Assert.DoesNotContain(nameof(EnvironmentState.TemperatureKelvin), listed);
        Assert.DoesNotContain(nameof(EnvironmentState.PressurePascal), listed);
    }

    public static IEnumerable<object[]> BadNonNegative()
    {
        foreach (var name in NonNegativeNames)
        {
            foreach (var row in FiniteEdges.NonFiniteOrNegative())
            {
                yield return [name, row[0]];
            }
        }
    }

    public static IEnumerable<object[]> GoodNonNegative()
    {
        foreach (var name in NonNegativeNames)
        {
            foreach (var row in FiniteEdges.NonNegativeFinite())
            {
                yield return [name, row[0]];
            }
        }
    }

    [Theory]
    [MemberData(nameof(BadNonNegative))]
    public void CheckRejectsANonFiniteOrNegativeField(string name, double value)
    {
        var state = new EnvironmentState();
        ScalarProperties.Set(state, name, value);
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(name, error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }

    [Theory]
    [MemberData(nameof(GoodNonNegative))]
    public void CheckAcceptsAFiniteNonNegativeField(string name, double value)
    {
        var state = new EnvironmentState();
        ScalarProperties.Set(state, name, value);
        state.CheckInvariants();
        DoubleBits.Equal(value, state.Quantities.Single(quantity => quantity.Name == name).Value);
    }

    public static IEnumerable<object[]> AcceptedPorosity()
    {
        yield return [0.0];
        yield return [-0.0];
        yield return [1.0];
        yield return [Math.BitDecrement(1.0)];
    }

    public static IEnumerable<object[]> RejectedPorosity()
    {
        yield return [-0.01];
        yield return [1.01];
        yield return [double.NaN];
        yield return [double.PositiveInfinity];
        yield return [double.NegativeInfinity];
        yield return [Math.BitDecrement(0.0)];
        yield return [Math.BitIncrement(1.0)];
    }

    [Theory]
    [MemberData(nameof(AcceptedPorosity))]
    public void PorosityAcceptsTheClosedUnitInterval(double value)
    {
        var state = new EnvironmentState { SoilPorosity = value };
        state.CheckInvariants();
        DoubleBits.Equal(value, state.SoilPorosity);
    }

    [Theory]
    [MemberData(nameof(RejectedPorosity))]
    public void PorosityRejectsValuesOutsideTheUnitInterval(double value)
    {
        var state = new EnvironmentState { SoilPorosity = value };
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(nameof(EnvironmentState.SoilPorosity), error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }

    [Fact]
    public void CheckReportsTemperatureBeforeALaterBrokenField()
    {
        var state = new EnvironmentState
        {
            PerchlorateConcentration = -1,
            TemperatureKelvin = AssignedDouble.Set(double.NaN),
        };

        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(nameof(EnvironmentState.TemperatureKelvin), error.ParamName);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void AssignedPressureRejectsNonFiniteOrNegativeValues(double value)
    {
        var state = new EnvironmentState { PressurePascal = AssignedDouble.Set(value) };
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(nameof(EnvironmentState.PressurePascal), error.ParamName);
    }
}
