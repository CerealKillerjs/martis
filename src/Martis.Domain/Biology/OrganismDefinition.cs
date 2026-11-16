using Martis.Domain.Common;

namespace Martis.Domain.Biology;

/// <summary>
/// Content definition of an organism (TDD §2.4). It carries a base genome and
/// the metabolism and reproduction data. It does not carry a phenotype, and it
/// does not grow a population.
/// </summary>
public sealed record OrganismDefinition
{
    public OrganismDefinition(
        OrganismId id,
        string name,
        string category,
        GenomeDefinition baseGenome,
        MetabolismDefinition metabolism,
        ReproductionDefinition reproduction)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(category);
        ArgumentNullException.ThrowIfNull(baseGenome);
        ArgumentNullException.ThrowIfNull(metabolism);
        ArgumentNullException.ThrowIfNull(reproduction);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("The organism name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("The organism category is required.", nameof(category));
        }

        Id = id;
        Name = name;
        Category = category;
        BaseGenome = baseGenome;
        Metabolism = metabolism;
        Reproduction = reproduction;
    }

    public OrganismId Id { get; }

    public string Name { get; }

    public string Category { get; }

    public GenomeDefinition BaseGenome { get; }

    public MetabolismDefinition Metabolism { get; }

    public ReproductionDefinition Reproduction { get; }
}
