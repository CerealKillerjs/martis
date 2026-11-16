using Martis.Domain.Common;
using Martis.Domain.Environment;

namespace Martis.Domain.Tests;

public sealed class EnvironmentScopeTests
{
    [Fact]
    public void TheFourScopesAreDistinct()
    {
        EnvironmentScope planetary = new EnvironmentScope.PlanetaryMean();
        EnvironmentScope regional = new EnvironmentScope.Regional(new SiteId("gale"));
        EnvironmentScope lab = new EnvironmentScope.Lab();
        EnvironmentScope game = new EnvironmentScope.Game();

        Assert.IsType<EnvironmentScope.PlanetaryMean>(planetary);
        Assert.IsType<EnvironmentScope.Lab>(lab);
        Assert.IsType<EnvironmentScope.Game>(game);
        var site = Assert.IsType<EnvironmentScope.Regional>(regional);
        Assert.Equal("gale", site.SiteId.Value);
        Assert.NotEqual(planetary, lab);
        Assert.NotEqual(lab, game);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void RegionalScopeRequiresASiteId(string? site)
    {
        Assert.Throws<ArgumentException>(() => new EnvironmentScope.Regional(new SiteId(site!)));
    }
}
