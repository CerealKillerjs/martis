using System.Reflection;
using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class WaterBudgetTests
{
    [Fact]
    public void LiquidIceAndVaporAreDifferentProperties()
    {
        var pool = DoubleNames(typeof(ResourcePool));
        var environment = DoubleNames(typeof(EnvironmentState));

        Assert.Contains(nameof(ResourcePool.Water), pool);
        Assert.DoesNotContain(nameof(EnvironmentState.WaterIce), pool);
        Assert.DoesNotContain(nameof(EnvironmentState.WaterVapor), pool);
        Assert.Contains(nameof(EnvironmentState.WaterIce), environment);
        Assert.Contains(nameof(EnvironmentState.WaterVapor), environment);
        Assert.DoesNotContain(nameof(ResourcePool.Water), environment);
    }

    [Fact]
    public void ComponentSumUsesLiquidThenIceThenVapor()
    {
        Assert.Equal(9.0, CellWaterBudget.ComponentSum(2, 3, 4));
    }

    [Fact]
    public void MatrixAStoresNoNumericMolarMassBecauseItIsDerived()
    {
        var fields = typeof(ElementalCompositionMatrix).GetFields(BindingFlags.Public | BindingFlags.Static);
        var properties = typeof(ElementalCompositionMatrix).GetProperties(BindingFlags.Public | BindingFlags.Static);
        Assert.Empty(fields);
        Assert.Empty(properties);
        Assert.DoesNotContain(
            typeof(ElementalCompositionMatrix).GetMembers(),
            static member => member.Name.Contains("Molar", StringComparison.Ordinal));
    }

    private static string[] DoubleNames(Type type)
        => ScalarProperties.WritableDoubles(type).ToArray();
}
