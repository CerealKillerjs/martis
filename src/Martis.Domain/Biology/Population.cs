using Martis.Domain.Common;
using Martis.Domain.Genetics;

namespace Martis.Domain.Biology;

/// <summary>
/// One aggregated population in a single cell (TDD §2.5, ADR-000 R6).
/// There is no individual organism object. Abundance may be fractional.
/// The phenotype is not stored; it is recalculated from <see cref="Genome"/>.
/// </summary>
public sealed class Population
{
    private Genome _genome;

    public Population(PopulationId id, OrganismId organismId, int cellIndex, Genome genome)
    {
        ArgumentNullException.ThrowIfNull(genome);
        Id = id;
        OrganismId = organismId;
        CellIndex = cellIndex;
        _genome = genome;
    }

    public PopulationId Id { get; }

    public OrganismId OrganismId { get; }

    /// <summary>
    /// Linear cell index, <c>y * width + x</c>. The population does not know the
    /// grid size; the world state checks the range.
    /// </summary>
    public int CellIndex { get; set; }

    public Genome Genome
    {
        get => _genome;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _genome = value;
        }
    }

    /// <summary>
    /// Equivalent number of individuals. Fractional values are allowed.
    /// Unit: dimensionless count. Finite and greater than or equal to 0 (VAL-002).
    /// </summary>
    public double Abundance { get; set; }

    /// <summary>
    /// Biomass. Unit: kilogram (kg). Finite and greater than or equal to 0 (VAL-003).
    /// </summary>
    public double BiomassKg { get; set; }

    /// <summary>
    /// Stored energy. Unit: joule (J). Finite and greater than or equal to 0.
    /// Reported as VAL-002 because it is a population quantity. VAL-008 (energy
    /// consumed versus energy available) is not checked in T02.
    /// </summary>
    public double EnergyReserveJ { get; set; }

    /// <summary>
    /// Mean of the age distribution. The property name follows TDD §2.5 and says
    /// days. H-05 is still open: a sol is not an Earth day, and this type stores
    /// no conversion factor. Finite and greater than or equal to 0 (VAL-002).
    /// </summary>
    public double AgeDistributionMeanDays { get; set; }

    /// <summary>
    /// Fraction of the abundance that remains viable, in the closed interval [0, 1]
    /// (VAL-004). The endpoints are that invariant, not a measured threshold.
    /// TODO: pendiente MARTIS Ciencia.
    /// </summary>
    public double Viability { get; set; }

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> for the first population
    /// quantity that breaks its range. Gene expressions are checked by
    /// <see cref="Genome"/> at construction.
    /// </summary>
    public void CheckInvariants()
    {
        QuantityRules.RequireNonNegativeFinite(Abundance, nameof(Abundance));
        QuantityRules.RequireNonNegativeFinite(BiomassKg, nameof(BiomassKg));
        QuantityRules.RequireNonNegativeFinite(EnergyReserveJ, nameof(EnergyReserveJ));
        QuantityRules.RequireNonNegativeFinite(AgeDistributionMeanDays, nameof(AgeDistributionMeanDays));
        QuantityRules.RequireUnitInterval(Viability, nameof(Viability));
        ArgumentNullException.ThrowIfNull(Genome);
    }
}
