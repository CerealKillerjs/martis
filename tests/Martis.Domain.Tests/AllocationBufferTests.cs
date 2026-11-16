using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class AllocationBufferTests
{
    [Fact]
    public void SlotsMatchThePoolOrderAndDefaultToZero()
    {
        var buffer = new AllocationBuffer();
        buffer.CheckInvariants();
        Assert.Equal(
            ResourcePoolTests.Names,
            buffer.Quantities.Select(quantity => quantity.Name).ToArray());
        Assert.All(buffer.Quantities, quantity => DoubleBits.Equal(0.0, quantity.Value));
    }

    [Fact]
    public void EveryDoublePropertyIsInTheQuantityList()
    {
        var listed = new AllocationBuffer().Quantities.Select(quantity => quantity.Name).Order(StringComparer.Ordinal);
        var properties = ScalarProperties.WritableDoubles(typeof(AllocationBuffer)).Order(StringComparer.Ordinal);
        Assert.Equal(listed, properties);
    }

    public static IEnumerable<object[]> BadValues() => ResourcePoolTests.BadValues();

    [Theory]
    [MemberData(nameof(BadValues))]
    public void CheckRejectsANonFiniteOrNegativeRequest(string name, double value)
    {
        var buffer = new AllocationBuffer();
        ScalarProperties.Set(buffer, name, value);
        var error = Assert.Throws<ArgumentOutOfRangeException>(buffer.CheckInvariants);
        Assert.Equal(name, error.ParamName);
    }

    [Fact]
    public void RecordingDemandDoesNotChangeThePool()
    {
        var pool = new ResourcePool { Water = 4 };
        var buffer = new AllocationBuffer();
        foreach (var name in ResourcePoolTests.Names)
        {
            ScalarProperties.Set(buffer, name, 1);
        }

        buffer.CheckInvariants();
        DoubleBits.Equal(4, pool.Water);
        foreach (var quantity in pool.Quantities.Where(quantity => quantity.Name != nameof(ResourcePool.Water)))
        {
            DoubleBits.Equal(0, quantity.Value);
        }
    }
}
