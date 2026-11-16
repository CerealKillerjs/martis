using Martis.Domain.World;

namespace Martis.Domain.Tests;

public sealed class GridIndexTests
{
    [Fact]
    public void LinearIndexIsYTimesWidthPlusX()
    {
        const int width = 3;
        const int height = 2;
        Assert.Equal(0, new GridIndex(0, 0, width, height).ToLinearIndex());
        Assert.Equal(1, new GridIndex(1, 0, width, height).ToLinearIndex());
        Assert.Equal(2, new GridIndex(2, 0, width, height).ToLinearIndex());
        Assert.Equal(3, new GridIndex(0, 1, width, height).ToLinearIndex());
        Assert.Equal(5, new GridIndex(2, 1, width, height).ToLinearIndex());
    }

    [Fact]
    public void ExampleCornerMatchesTheSprintAcceptanceIndex()
    {
        const int width = 64;
        const int height = 64;
        Assert.Equal(4095, new GridIndex(width - 1, height - 1, width, height).ToLinearIndex());
        Assert.Equal(0, new GridIndex(0, 0, width, height).ToLinearIndex());
        Assert.Equal(width, new GridIndex(0, 1, width, height).ToLinearIndex());
    }

    [Theory]
    [InlineData(-1, 0, 4, 4, "x")]
    [InlineData(4, 0, 4, 4, "x")]
    [InlineData(0, -1, 4, 4, "y")]
    [InlineData(0, 4, 4, 4, "y")]
    [InlineData(0, 0, 0, 4, "width")]
    [InlineData(0, 0, -1, 4, "width")]
    [InlineData(0, 0, 4, 0, "height")]
    [InlineData(0, 0, 4, -2, "height")]
    public void OutOfRangeCoordinatesThrow(int x, int y, int width, int height, string parameter)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => new GridIndex(x, y, width, height));
        Assert.Equal(parameter, error.ParamName);
    }

    [Fact]
    public void EqualityUsesTheDimensionsAsWellAsTheCoordinate()
    {
        Assert.Equal(new GridIndex(1, 1, 3, 3), new GridIndex(1, 1, 3, 3));
        Assert.NotEqual(new GridIndex(1, 1, 3, 3), new GridIndex(1, 1, 4, 3));
    }

    [Fact]
    public void LinearIndexThrowsWhenTheProductDoesNotFit()
    {
        var index = new GridIndex(1, 1, int.MaxValue, 2);
        Assert.Throws<OverflowException>(() => index.ToLinearIndex());
    }

    [Fact]
    public void SourceDoesNotHardcodeTheExampleMapSize()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot.Find(), "src", "Martis.Domain", "World", "GridIndex.cs"));
        Assert.DoesNotContain("64", source, StringComparison.Ordinal);
        Assert.DoesNotContain("4096", source, StringComparison.Ordinal);
    }
}
