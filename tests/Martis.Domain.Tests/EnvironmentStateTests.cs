using System.Reflection;
using Martis.Domain.Common;
using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class EnvironmentStateTests
{
    private static readonly string[] NonNegativeNames =
    [
        nameof(EnvironmentState.IonizingAbsorbedDoseGyPerSol),
        nameof(EnvironmentState.IonizingEquivalentDoseSvPerSol),
        nameof(EnvironmentState.SolarIrradiance),
        nameof(EnvironmentState.WaterIce),
    ];

    /// <summary>
    /// Fields that used to be plain doubles in fdaf2be and are now
    /// <see cref="AssignedDouble"/>. The edge matrix is the same; the value is assigned.
    /// </summary>
    private static readonly string[] AssignedNonNegativeNames =
    [
        nameof(EnvironmentState.UvRadiation),
        nameof(EnvironmentState.WaterVapor),
        nameof(EnvironmentState.PerchlorateMassKg),
    ];

    private static readonly string[] UnassignedNames =
    [
        nameof(EnvironmentState.UvRadiation),
        nameof(EnvironmentState.WaterVapor),
        nameof(EnvironmentState.SoilPorosity),
        nameof(EnvironmentState.PerchlorateMassKg),
        nameof(EnvironmentState.PerchlorateWtPercent),
        nameof(EnvironmentState.WaterIceWtPercent),
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
        Assert.All(UnassignedNames, name => Assert.DoesNotContain(name, listed));
    }

    [Fact]
    public void UnassignedFieldsStayOutOfTheQuantityListUntilTheyAreSet()
    {
        var state = new EnvironmentState();
        foreach (var name in UnassignedNames)
        {
            var property = typeof(EnvironmentState).GetProperty(name);
            Assert.NotNull(property);
            var assigned = Assert.IsType<AssignedDouble>(property!.GetValue(state));
            Assert.True(assigned.IsUnset);
            Assert.Throws<InvalidOperationException>(() => _ = assigned.Value);
        }

        state.UvRadiation = AssignedDouble.Set(1.5);
        state.CheckInvariants();
        DoubleBits.Equal(1.5, state.Quantities.Single(quantity => quantity.Name == nameof(EnvironmentState.UvRadiation)).Value);
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

    public static IEnumerable<object[]> BadAssignedNonNegative()
    {
        foreach (var name in AssignedNonNegativeNames)
        {
            foreach (var row in FiniteEdges.NonFiniteOrNegative())
            {
                yield return [name, row[0]];
            }
        }
    }

    public static IEnumerable<object[]> GoodAssignedNonNegative()
    {
        foreach (var name in AssignedNonNegativeNames)
        {
            foreach (var row in FiniteEdges.NonNegativeFinite())
            {
                yield return [name, row[0]];
            }
        }
    }

    [Theory]
    [MemberData(nameof(BadAssignedNonNegative))]
    public void CheckRejectsAnAssignedNonFiniteOrNegativeField(string name, double value)
    {
        var state = new EnvironmentState();
        SetAssigned(state, name, value);
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(name, error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }

    [Theory]
    [MemberData(nameof(GoodAssignedNonNegative))]
    public void CheckAcceptsAnAssignedFiniteNonNegativeField(string name, double value)
    {
        var state = new EnvironmentState();
        SetAssigned(state, name, value);
        state.CheckInvariants();
        var stored = state.Quantities.Single(quantity => quantity.Name == name).Value;
        DoubleBits.Equal(value, stored);
    }

    private static void SetAssigned(EnvironmentState state, string name, double value)
    {
        var property = typeof(EnvironmentState).GetProperty(name);
        Assert.NotNull(property);
        property!.SetValue(state, AssignedDouble.Set(value));
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
        var state = new EnvironmentState { SoilPorosity = AssignedDouble.Set(value) };
        state.CheckInvariants();
        DoubleBits.Equal(value, state.SoilPorosity.Value);
    }

    [Theory]
    [MemberData(nameof(RejectedPorosity))]
    public void PorosityRejectsValuesOutsideTheUnitInterval(double value)
    {
        var state = new EnvironmentState { SoilPorosity = AssignedDouble.Set(value) };
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(nameof(EnvironmentState.SoilPorosity), error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }

    [Fact]
    public void CheckReportsTemperatureBeforeALaterBrokenField()
    {
        var state = new EnvironmentState
        {
            PerchlorateMassKg = AssignedDouble.Set(-1),
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

    [Theory]
    [InlineData(0.0)]
    [InlineData(100.0)]
    public void WeightPercentAcceptsTheClosedInterval(double value)
    {
        var state = new EnvironmentState
        {
            PerchlorateWtPercent = AssignedDouble.Set(value),
            WaterIceWtPercent = AssignedDouble.Set(value),
        };
        state.CheckInvariants();
        DoubleBits.Equal(value, state.PerchlorateWtPercent.Value);
        DoubleBits.Equal(value, state.WaterIceWtPercent.Value);
        DoubleBits.Equal(value, state.Quantities.Single(quantity => quantity.Name == nameof(EnvironmentState.PerchlorateWtPercent)).Value);
        DoubleBits.Equal(value, state.Quantities.Single(quantity => quantity.Name == nameof(EnvironmentState.WaterIceWtPercent)).Value);
    }

    [Theory]
    [InlineData(nameof(EnvironmentState.PerchlorateWtPercent), -0.01)]
    [InlineData(nameof(EnvironmentState.PerchlorateWtPercent), 100.01)]
    [InlineData(nameof(EnvironmentState.PerchlorateWtPercent), double.NaN)]
    [InlineData(nameof(EnvironmentState.PerchlorateWtPercent), double.PositiveInfinity)]
    [InlineData(nameof(EnvironmentState.PerchlorateWtPercent), double.NegativeInfinity)]
    [InlineData(nameof(EnvironmentState.WaterIceWtPercent), -0.01)]
    [InlineData(nameof(EnvironmentState.WaterIceWtPercent), 100.01)]
    [InlineData(nameof(EnvironmentState.WaterIceWtPercent), double.NaN)]
    [InlineData(nameof(EnvironmentState.WaterIceWtPercent), double.PositiveInfinity)]
    [InlineData(nameof(EnvironmentState.WaterIceWtPercent), double.NegativeInfinity)]
    public void WeightPercentRejectsValuesOutsideTheClosedInterval(string name, double value)
    {
        var state = new EnvironmentState();
        var property = typeof(EnvironmentState).GetProperty(name);
        Assert.NotNull(property);
        property!.SetValue(state, AssignedDouble.Set(value));
        var error = Assert.Throws<ArgumentOutOfRangeException>(state.CheckInvariants);
        Assert.Equal(name, error.ParamName);
    }

    [Fact]
    public void ReadingAnUnsetWeightPercentThrows()
    {
        var state = new EnvironmentState();
        Assert.True(state.PerchlorateWtPercent.IsUnset);
        Assert.True(state.WaterIceWtPercent.IsUnset);
        Assert.Throws<InvalidOperationException>(() => _ = state.PerchlorateWtPercent.Value);
        Assert.Throws<InvalidOperationException>(() => _ = state.WaterIceWtPercent.Value);
        Assert.DoesNotContain(state.Quantities, quantity => quantity.Name == nameof(EnvironmentState.PerchlorateWtPercent));
        Assert.DoesNotContain(state.Quantities, quantity => quantity.Name == nameof(EnvironmentState.WaterIceWtPercent));
    }
}
