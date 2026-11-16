using Martis.Domain.Common;

namespace Martis.Domain.Tests;

/// <summary>
/// Value semantics of the typed identifiers defined in TDD §2.1.
/// These types do not validate ranges; that belongs to later milestones.
/// </summary>
public sealed class TypedIdTests
{
    [Fact]
    public void RegionIdStoresItsValueAndComparesByValue()
    {
        AssertIntId(static value => new RegionId(value), static id => id.Value);
        Assert.True(new RegionId(7) == new RegionId(7));
        Assert.True(new RegionId(7) != new RegionId(8));
    }

    [Fact]
    public void CellIdStoresItsValueAndComparesByValue()
    {
        AssertIntId(static value => new CellId(value), static id => id.Value);
        Assert.True(new CellId(0) == new CellId(0));
        Assert.True(new CellId(-1) != new CellId(1));
    }

    [Fact]
    public void PopulationIdStoresItsValueAndComparesByValue()
    {
        AssertLongId(static value => new PopulationId(value), static id => id.Value);
        Assert.True(new PopulationId(long.MaxValue) == new PopulationId(long.MaxValue));
        Assert.True(new PopulationId(long.MinValue) != new PopulationId(0));
    }

    [Fact]
    public void OrganismIdStoresItsValueAndComparesByValue()
    {
        AssertStringId(static value => new OrganismId(value), static id => id.Value);
        Assert.True(new OrganismId("a") == new OrganismId("a"));
        Assert.True(new OrganismId("a") != new OrganismId("A"));
    }

    [Fact]
    public void GeneIdStoresItsValueAndComparesByValue()
    {
        AssertStringId(static value => new GeneId(value), static id => id.Value);
    }

    [Fact]
    public void ReactionIdStoresItsValueAndComparesByValue()
    {
        AssertStringId(static value => new ReactionId(value), static id => id.Value);
    }

    [Fact]
    public void ContentIdentifiersAreDistinctTypes()
    {
        Assert.NotEqual(typeof(OrganismId), typeof(GeneId));
        Assert.NotEqual(typeof(GeneId), typeof(ReactionId));
        Assert.NotEqual(typeof(OrganismId), typeof(ReactionId));
    }

    private static void AssertIntId<T>(Func<int, T> create, Func<T, int> value)
        where T : struct, IEquatable<T>
    {
        var zero = create(0);
        var one = create(1);
        var same = create(1);
        var other = create(2);
        var negative = create(-1);

        Assert.Equal(0, value(zero));
        Assert.Equal(1, value(one));
        Assert.Equal(-1, value(negative));
        Assert.Equal(zero, default);
        Assert.Equal(one, same);
        Assert.NotEqual(one, other);
        Assert.True(one.Equals(same));
    }

    private static void AssertLongId<T>(Func<long, T> create, Func<T, long> value)
        where T : struct, IEquatable<T>
    {
        var zero = create(0L);
        var one = create(1L);
        var upper = create(long.MaxValue);
        var lower = create(long.MinValue);

        Assert.Equal(0L, value(zero));
        Assert.Equal(1L, value(one));
        Assert.Equal(long.MaxValue, value(upper));
        Assert.Equal(long.MinValue, value(lower));
        Assert.Equal(zero, default);
        Assert.Equal(one, create(1L));
        Assert.NotEqual(upper, lower);
    }

    private static void AssertStringId<T>(Func<string, T> create, Func<T, string> value)
        where T : struct, IEquatable<T>
    {
        var empty = create(string.Empty);
        var lower = create("a");
        var same = create("a");
        var other = create("b");
        var upper = create("A");

        Assert.Equal(string.Empty, value(empty));
        Assert.Equal("a", value(lower));
        Assert.Equal(lower, same);
        Assert.NotEqual(lower, other);
        Assert.NotEqual(lower, upper);
        Assert.NotEqual(lower, empty);
    }
}
