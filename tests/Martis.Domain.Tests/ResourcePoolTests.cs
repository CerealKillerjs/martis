using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class ResourcePoolTests
{
    public static readonly string[] Names =
    [
        nameof(ResourcePool.CarbonDioxide),
        nameof(ResourcePool.Water),
        nameof(ResourcePool.Oxygen),
        nameof(ResourcePool.Nitrogen),
        nameof(ResourcePool.Phosphorus),
        nameof(ResourcePool.Sulfur),
        nameof(ResourcePool.Iron),
        nameof(ResourcePool.OrganicCarbon),
        nameof(ResourcePool.MineralCarbon),
    ];

    [Fact]
    public void DefaultsAreZeroAndTheNineDepositsStayInTddOrder()
    {
        var pool = new ResourcePool();
        pool.CheckInvariants();
        Assert.Equal(Names, pool.Quantities.Select(quantity => quantity.Name).ToArray());
        Assert.Equal(9, pool.Quantities.Count);
        Assert.All(pool.Quantities, quantity => DoubleBits.Equal(0.0, quantity.Value));
    }

    [Fact]
    public void EveryDoublePropertyIsInTheQuantityList()
    {
        var listed = new ResourcePool().Quantities.Select(quantity => quantity.Name).Order(StringComparer.Ordinal);
        var properties = ScalarProperties.WritableDoubles(typeof(ResourcePool)).Order(StringComparer.Ordinal);
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
    public void CheckRejectsANonFiniteOrNegativeDeposit(string name, double value)
    {
        var pool = new ResourcePool();
        ScalarProperties.Set(pool, name, value);
        var error = Assert.Throws<ArgumentOutOfRangeException>(pool.CheckInvariants);
        Assert.Equal(name, error.ParamName);
        DoubleBits.Equal(value, (double)error.ActualValue!);
    }

    [Theory]
    [MemberData(nameof(GoodValues))]
    public void CheckAcceptsAFiniteNonNegativeDeposit(string name, double value)
    {
        var pool = new ResourcePool();
        ScalarProperties.Set(pool, name, value);
        pool.CheckInvariants();
        DoubleBits.Equal(value, pool.Quantities.Single(quantity => quantity.Name == name).Value);
    }
}
