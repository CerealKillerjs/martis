using Martis.Domain.World;
using Martis.Simulation.Biology;

namespace Martis.Simulation.Missions;

/// <summary>
/// The intelligence index of chapter 5 (H7, ADR-025): a score from 0 to 100 that tells the
/// player how far the biosphere has climbed. Each living level of the food chain adds 20
/// points (producers with <c>photoautotrophy</c>, herbivores with <c>grazing</c>, predators
/// with <c>predation</c>, minds with <c>cognition</c>) and the biomass of <c>cognition</c>
/// adds up to 20 more, reaching them at <c>referenceKg</c>. A society without the chain
/// under it scores little, which is the lesson of the chapter.
/// </summary>
public static class IntelligenceIndex
{
    /// <summary>Capabilities of the four levels, from the bottom of the chain to the top.</summary>
    public static readonly IReadOnlyList<string> Levels = ["photoautotrophy", "grazing", "predation", "cognition"];

    public const double PointsPerLevel = 20.0;

    public const double Maximum = 100.0;

    public static double Measure(SimulationState state, BiologyCatalog catalog, double referenceKg)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(catalog);
        if (!(referenceKg > 0.0))
        {
            throw new ArgumentOutOfRangeException(nameof(referenceKg), referenceKg, "The reference biomass must be positive.");
        }

        var living = new HashSet<string>(StringComparer.Ordinal);
        var mindKg = 0.0;
        foreach (var population in state.Populations)
        {
            if (population.BiomassKg <= 0.0 || !catalog.TryGetOrganism(population.OrganismId.Value, out var organism))
            {
                continue;
            }

            foreach (var capability in organism!.Capabilities)
            {
                living.Add(capability);
            }

            if (organism.Capabilities.Contains("cognition", StringComparer.Ordinal))
            {
                mindKg += population.BiomassKg;
            }
        }

        var score = Levels.Count(living.Contains) * PointsPerLevel;
        return score + (PointsPerLevel * Math.Min(1.0, mindKg / referenceKg));
    }
}
