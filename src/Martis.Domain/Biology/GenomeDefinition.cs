using Martis.Domain.Genetics;

namespace Martis.Domain.Biology;

/// <summary>
/// Base genome carried by an <see cref="OrganismDefinition"/>.
/// The expressions are the definition's own data, not measured constants.
/// </summary>
public sealed record GenomeDefinition
{
    public GenomeDefinition(Genome baseGenome)
    {
        ArgumentNullException.ThrowIfNull(baseGenome);
        BaseGenome = baseGenome;
    }

    public Genome BaseGenome { get; }
}
