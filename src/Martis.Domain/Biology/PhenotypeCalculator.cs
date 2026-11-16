using Martis.Domain.Common;
using Martis.Domain.Genetics;

namespace Martis.Domain.Biology;

/// <summary>
/// Copies bound gene expressions into a <see cref="Phenotype"/>.
/// No coefficient is applied. An unbound component, or a gene the genome does
/// not contain, contributes 0. That 0 is the inclusive lower bound of expression
/// (VAL-005), not a measured constant. Environmental modulation is not applied.
/// TODO: pendiente MARTIS Ciencia.
/// The result is not written back onto <see cref="Population"/>.
/// </summary>
public static class PhenotypeCalculator
{
    public static Phenotype Calculate(Genome genome, PhenotypeGeneBinding binding)
    {
        ArgumentNullException.ThrowIfNull(genome);
        ArgumentNullException.ThrowIfNull(binding);
        return new Phenotype(
            Read(genome, binding.UvResistance),
            Read(genome, binding.ThermalTolerance),
            Read(genome, binding.DesiccationTolerance),
            Read(genome, binding.PhotosyntheticEfficiency),
            Read(genome, binding.MaintenanceCost),
            Read(genome, binding.ReproductionRate),
            Read(genome, binding.BiofilmProduction));
    }

    private static double Read(Genome genome, GeneId? gene)
    {
        if (gene is null)
        {
            return 0.0;
        }

        return genome.TryGetExpression(gene.Value, out var expression) ? expression : 0.0;
    }
}
