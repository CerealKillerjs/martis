using Martis.Domain.Common;

namespace Martis.Domain.Biology;

/// <summary>
/// Caller-supplied link from a phenotype component to a gene.
/// Null means the component is unbound. An empty <see cref="GeneId"/> is a real
/// identifier, not the same as unbound (T01).
/// The binding is data. It is not a measured Martian relationship.
/// TODO: pendiente MARTIS Ciencia.
/// </summary>
public sealed class PhenotypeGeneBinding
{
    public PhenotypeGeneBinding(
        GeneId? uvResistance = null,
        GeneId? thermalTolerance = null,
        GeneId? desiccationTolerance = null,
        GeneId? photosyntheticEfficiency = null,
        GeneId? maintenanceCost = null,
        GeneId? reproductionRate = null,
        GeneId? biofilmProduction = null)
    {
        UvResistance = uvResistance;
        ThermalTolerance = thermalTolerance;
        DesiccationTolerance = desiccationTolerance;
        PhotosyntheticEfficiency = photosyntheticEfficiency;
        MaintenanceCost = maintenanceCost;
        ReproductionRate = reproductionRate;
        BiofilmProduction = biofilmProduction;
    }

    public GeneId? UvResistance { get; }

    public GeneId? ThermalTolerance { get; }

    public GeneId? DesiccationTolerance { get; }

    public GeneId? PhotosyntheticEfficiency { get; }

    public GeneId? MaintenanceCost { get; }

    public GeneId? ReproductionRate { get; }

    public GeneId? BiofilmProduction { get; }
}
