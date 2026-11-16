using System.Reflection;
using Martis.Domain.Biology;
using Martis.Domain.Common;

namespace Martis.Domain.Tests;

public sealed class OrganismDefinitionTests
{
    [Fact]
    public void DefinitionKeepsTheBaseGenomeAndTheCallerReactionOrder()
    {
        var genome = Samples.Genome(("a", 1));
        var definition = new OrganismDefinition(
            new OrganismId("photo"),
            "Example",
            "phototroph",
            new GenomeDefinition(genome),
            new MetabolismDefinition([new ReactionId("second"), new ReactionId("first")]),
            new ReproductionDefinition());

        Assert.Same(genome, definition.BaseGenome.BaseGenome);
        Assert.Equal(["second", "first"], definition.Metabolism.ReactionIds.Select(id => id.Value).ToArray());
        Assert.Empty(Doubles(typeof(MetabolismDefinition)));
        Assert.Empty(Doubles(typeof(ReproductionDefinition)));
        Assert.Empty(Doubles(typeof(GenomeDefinition)));
    }

    [Fact]
    public void RepeatedReactionIsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => new MetabolismDefinition(
            [new ReactionId("a"), new ReactionId("a")]));
        Assert.Equal("reactionIds", error.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void NameAndCategoryAreRequired(string? text)
    {
        var genome = new GenomeDefinition(Samples.Genome());
        var metabolism = new MetabolismDefinition([]);
        var reproduction = new ReproductionDefinition();
        Assert.ThrowsAny<ArgumentException>(() => new OrganismDefinition(
            new OrganismId("id"),
            text!,
            "category",
            genome,
            metabolism,
            reproduction));
        Assert.ThrowsAny<ArgumentException>(() => new OrganismDefinition(
            new OrganismId("id"),
            "name",
            text!,
            genome,
            metabolism,
            reproduction));
    }

    [Fact]
    public void NestedDefinitionsRejectNull()
    {
        Assert.Throws<ArgumentNullException>(() => new GenomeDefinition(null!));
        Assert.Throws<ArgumentNullException>(() => new MetabolismDefinition(null!));
        Assert.Throws<ArgumentNullException>(() => new OrganismDefinition(
            new OrganismId("id"),
            "name",
            "category",
            null!,
            new MetabolismDefinition([]),
            new ReproductionDefinition()));
    }

    private static IEnumerable<PropertyInfo> Doubles(Type type)
    {
        return type
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.PropertyType == typeof(double));
    }
}
