using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

/// <summary>H3: the allocation-free slot readers agree with the named lists.</summary>
public sealed class SlotAccessTests
{
    [Fact]
    public void ResourcePoolValueAtFollowsQuantities()
    {
        var pool = new ResourcePool { CarbonDioxide = 1, Water = 2, Oxygen = 3, Nitrogen = 4, Phosphorus = 5, Sulfur = 6, Iron = 7, OrganicCarbon = 8, MineralCarbon = 9 };

        Assert.Equal(ResourcePool.SlotCount, pool.Quantities.Count);
        Assert.Equal(pool.Quantities.Select(quantity => quantity.Value), Enumerable.Range(0, ResourcePool.SlotCount).Select(pool.ValueAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => pool.ValueAt(ResourcePool.SlotCount));
    }

    [Fact]
    public void AllocationBufferValueAtFollowsQuantities()
    {
        var buffer = new AllocationBuffer { CarbonDioxide = 1, Water = 2, Oxygen = 3, Nitrogen = 4, Phosphorus = 5, Sulfur = 6, Iron = 7, OrganicCarbon = 8, MineralCarbon = 9 };

        Assert.Equal(buffer.Quantities.Select(quantity => quantity.Value), Enumerable.Range(0, ResourcePool.SlotCount).Select(buffer.ValueAt));
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer.ValueAt(-1));
    }
}
