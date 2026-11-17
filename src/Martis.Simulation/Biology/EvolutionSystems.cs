using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Genetics;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;

namespace Martis.Simulation.Biology;

/// <summary>T06 numbers of one organism (ADR-014). All are <c>GAME_BALANCE</c>.</summary>
public sealed record EvolutionParameters(
    double MutationEventRate,
    double TraitMutationProbability,
    double MutationSigma,
    double FounderFraction,
    double DriftEffectiveSize,
    double MergeTolerance)
{
    public static EvolutionParameters From(OrganismModel organism)
    {
        ArgumentNullException.ThrowIfNull(organism);
        var extra = organism.Kinetics.Extra;
        var parameters = new EvolutionParameters(
            Require(extra, organism.Id, "mutation.event_rate"),
            Require(extra, organism.Id, "mutation.trait_probability"),
            Require(extra, organism.Id, "mutation.sigma"),
            Require(extra, organism.Id, "mutation.founder_fraction"),
            Require(extra, organism.Id, "drift.effective_size"),
            Require(extra, organism.Id, "variant.merge_tolerance"));
        if (parameters.TraitMutationProbability is < 0.0 or > 1.0 || parameters.FounderFraction is <= 0.0 or >= 1.0)
        {
            throw new InvalidDataException($"Organism '{organism.Id}' has a mutation probability or founder fraction outside its range.");
        }

        return parameters;
    }

    private static double Require(IReadOnlyDictionary<string, double> extra, string organism, string name)
        => extra.TryGetValue(name, out var value)
            ? value
            : throw new KeyNotFoundException($"Organism '{organism}' has no parameter '{name}' (T06).");
}

/// <summary>
/// Draws used by evolution, built only from the PRNG words and exact IEEE operations
/// (+, ×, √), so they give the same bits on every platform (ADR-003, ADR-014).
/// </summary>
public static class EvolutionDraws
{
    /// <summary>Largest trial count sampled draw by draw; above it the normal approximation is used.</summary>
    public const int ExactBinomialLimit = 64;

    /// <summary>
    /// Standard normal by Irwin–Hall: the sum of twelve uniforms minus 6. Mean 0, variance 1,
    /// tails cut at ±6. Uses no transcendental function.
    /// </summary>
    public static double StandardNormal(IRandomSource random)
    {
        ArgumentNullException.ThrowIfNull(random);
        var sum = 0.0;
        for (var i = 0; i < 12; i++)
        {
            sum += random.NextDouble();
        }

        return sum - 6.0;
    }

    /// <summary>Binomial(n, p): exact up to <see cref="ExactBinomialLimit"/> trials, then rounded normal, clamped to [0, n].</summary>
    public static long Binomial(IRandomSource random, long trials, double probability)
    {
        ArgumentNullException.ThrowIfNull(random);
        if (trials <= 0 || probability <= 0.0)
        {
            return 0;
        }

        if (probability >= 1.0)
        {
            return trials;
        }

        if (trials <= ExactBinomialLimit)
        {
            var hits = 0L;
            for (var i = 0; i < trials; i++)
            {
                if (random.NextDouble() < probability)
                {
                    hits++;
                }
            }

            return hits;
        }

        var mean = trials * probability;
        var deviation = Math.Sqrt(mean * (1.0 - probability));
        var value = Math.Round(mean + (deviation * StandardNormal(random)), MidpointRounding.ToEven);
        return (long)Math.Clamp(value, 0.0, trials);
    }

    /// <summary>
    /// Largest gene difference between two genomes of the same organism. Genomes with
    /// different gene sets are infinitely far apart.
    /// </summary>
    public static double Distance(Genome a, Genome b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);
        if (a.Count != b.Count)
        {
            return double.PositiveInfinity;
        }

        var worst = 0.0;
        for (var i = 0; i < a.Count; i++)
        {
            if (a[i].GeneId != b[i].GeneId)
            {
                return double.PositiveInfinity;
            }

            worst = Math.Max(worst, Math.Abs(a[i].Expression - b[i].Expression));
        }

        return worst;
    }
}

/// <summary>
/// apply-mutations (T06, TDD §4.2): each population with newborns has a chance
/// <c>1 − exp(−rate · newborn share)</c> of a mutation event, drawn from the Mutation
/// stream. In an event each gene mutates with probability μ: <c>g′ = clamp(g + δ, 0, 1)</c>,
/// <c>δ ~ Normal(0, σ²)</c>. A genome that differs from the parent by more than the merge
/// tolerance founds a new variant with a fraction of the newborn biomass and the same
/// share of the reserve, taken from the parent: no mass or energy is created.
/// </summary>
public sealed class MutationSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.ApplyMutations;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var random = context.Random.Mutation;
        var parents = state.Populations.ToArray();
        foreach (var parent in parents)
        {
            var newborns = workspace.Newborns.GetValueOrDefault(parent.Id.Value, 0.0);
            if (newborns <= 0.0 || parent.Abundance <= 0.0)
            {
                continue;
            }

            var organism = workspace.Catalog.Organism(parent.OrganismId.Value);
            var parameters = workspace.EvolutionParametersOf(organism);
            var share = Math.Min(1.0, newborns / parent.Abundance);
            var chance = -double.ExpM1(-parameters.MutationEventRate * share);
            if (!random.NextBool(Math.Clamp(chance, 0.0, 1.0)))
            {
                continue;
            }

            var changes = new List<GeneChange>();
            var genes = new GeneExpression[parent.Genome.Count];
            for (var i = 0; i < genes.Length; i++)
            {
                var gene = parent.Genome[i];
                var value = gene.Expression;
                if (random.NextBool(parameters.TraitMutationProbability))
                {
                    var mutated = Math.Clamp(value + (parameters.MutationSigma * EvolutionDraws.StandardNormal(random)), 0.0, 1.0);
                    if (mutated != value)
                    {
                        changes.Add(new GeneChange(gene.GeneId.Value, value, mutated));
                    }

                    value = mutated;
                }

                genes[i] = new GeneExpression(gene.GeneId, value);
            }

            var genome = new Genome(genes);
            var biomass = parameters.FounderFraction * newborns * organism.Kinetics.IndividualMassKg;
            if (EvolutionDraws.Distance(genome, parent.Genome) <= parameters.MergeTolerance
                || biomass < organism.Kinetics.IndividualMassKg
                || biomass >= parent.BiomassKg
                || state.Populations.Count >= (workspace.PopulationBudget ?? int.MaxValue))
            {
                continue;
            }

            var fraction = biomass / parent.BiomassKg;
            var reserve = parent.EnergyReserveJ * fraction;
            parent.BiomassKg -= biomass;
            parent.EnergyReserveJ -= reserve;
            parent.Abundance = parent.BiomassKg / organism.Kinetics.IndividualMassKg;

            var id = state.AllocatePopulationId();
            state.Populations.Add(new Population(new PopulationId(id), parent.OrganismId, parent.CellIndex, genome)
            {
                BiomassKg = biomass,
                Abundance = biomass / organism.Kinetics.IndividualMassKg,
                EnergyReserveJ = reserve,
                AgeDistributionMeanSols = 0.0,
                Viability = parent.Viability,
            });
            workspace.Totals.Mutations++;
            workspace.Totals.Births++;
            workspace.MutationEvents.Add(new MutationEvent(state.Tick, parent.CellIndex, parent.OrganismId.Value, parent.Id.Value, id, biomass, changes));
        }
    }
}

/// <summary>
/// apply-selection-and-drift (T06, TDD §4.3–4.4). Selection is already in the growth and
/// mortality of each variant: <c>pᵢ′ = pᵢwᵢ / Σpⱼwⱼ</c> is what differential growth gives.
/// This step adds drift: in each (cell, organism) group with two or more variants the
/// biomass shares are resampled by sequential binomial draws of the effective size Nₑ
/// (GeneticDrift stream). Then variants closer than the merge tolerance are merged, and
/// the smallest variants are merged into their nearest neighbour until the group fits
/// <see cref="Configuration.SimulationConfiguration.MaxVariantsPerSpeciesPerCell"/>
/// (ADR-006). Merges add biomass, abundance and reserve; nothing is created or lost.
/// </summary>
public sealed class SelectionDriftSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.ApplySelectionAndDrift;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        var state = context.State;
        var random = context.Random.GeneticDrift;
        var cap = context.Configuration.MaxVariantsPerSpeciesPerCell;
        var removed = new HashSet<long>();
        foreach (var group in Groups(state))
        {
            var organism = workspace.Catalog.Organism(group[0].OrganismId.Value);
            var parameters = workspace.EvolutionParametersOf(organism);
            Drift(group, parameters, random, organism.Kinetics.IndividualMassKg, removed);
            var live = group.Where(population => !removed.Contains(population.Id.Value)).ToList();
            MergeClose(live, parameters.MergeTolerance, removed);
            live = live.Where(population => !removed.Contains(population.Id.Value)).ToList();
            EnforceCap(live, cap, removed);
            foreach (var population in live)
            {
                population.Abundance = population.BiomassKg / organism.Kinetics.IndividualMassKg;
            }
        }

        if (removed.Count > 0)
        {
            state.Populations.RemoveAll(population => removed.Contains(population.Id.Value));
        }
    }

    /// <summary>Groups of two or more variants: cells ascending, organism ids ordinal, variants in list order.</summary>
    private static List<List<Population>> Groups(SimulationState state)
    {
        var groups = new SortedDictionary<(int Cell, string Organism), List<Population>>(GroupOrder.Instance);
        foreach (var population in state.Populations)
        {
            var key = (population.CellIndex, population.OrganismId.Value);
            if (!groups.TryGetValue(key, out var list))
            {
                list = [];
                groups[key] = list;
            }

            list.Add(population);
        }

        return groups.Values.Where(list => list.Count > 1).ToList();
    }

    private void Drift(List<Population> group, EvolutionParameters parameters, IRandomSource random, double individualMassKg, HashSet<long> removed)
    {
        var total = group.Sum(population => population.BiomassKg);
        var size = (long)Math.Floor(parameters.DriftEffectiveSize);
        if (total <= 0.0 || size < 1)
        {
            return;
        }

        var remainingTrials = size;
        var remainingShare = 1.0;
        var assigned = 0.0;
        for (var i = 0; i < group.Count; i++)
        {
            var population = group[i];
            var share = population.BiomassKg / total;
            long hits;
            if (i == group.Count - 1)
            {
                hits = remainingTrials;
            }
            else
            {
                var conditional = remainingShare <= 0.0 ? 0.0 : Math.Clamp(share / remainingShare, 0.0, 1.0);
                hits = EvolutionDraws.Binomial(random, remainingTrials, conditional);
            }

            remainingTrials -= hits;
            remainingShare -= share;
            var biomass = i == group.Count - 1 ? total - assigned : total * hits / size;
            population.BiomassKg = Math.Max(0.0, biomass);
            assigned += population.BiomassKg;
            if (population.BiomassKg < individualMassKg)
            {
                workspace.DeadKg[population.CellIndex] += population.BiomassKg;
                workspace.Totals.BiomassDiedKg += population.BiomassKg;
                population.BiomassKg = 0.0;
                removed.Add(population.Id.Value);
                workspace.Extinct.Add(population.Id.Value);
                workspace.Totals.Extinctions++;
            }
        }
    }

    private void MergeClose(List<Population> group, double tolerance, HashSet<long> removed)
    {
        for (var i = 0; i < group.Count; i++)
        {
            if (removed.Contains(group[i].Id.Value))
            {
                continue;
            }

            for (var j = i + 1; j < group.Count; j++)
            {
                if (!removed.Contains(group[j].Id.Value) && EvolutionDraws.Distance(group[i].Genome, group[j].Genome) <= tolerance)
                {
                    Merge(group[j], group[i]);
                    removed.Add(group[j].Id.Value);
                }
            }
        }
    }

    private void EnforceCap(List<Population> group, int cap, HashSet<long> removed)
    {
        while (group.Count > cap)
        {
            var smallest = group[0];
            foreach (var population in group)
            {
                if (population.BiomassKg < smallest.BiomassKg
                    || (population.BiomassKg == smallest.BiomassKg && population.Id.Value > smallest.Id.Value))
                {
                    smallest = population;
                }
            }

            Population? nearest = null;
            var best = double.PositiveInfinity;
            foreach (var population in group)
            {
                if (population.Id == smallest.Id)
                {
                    continue;
                }

                var distance = EvolutionDraws.Distance(population.Genome, smallest.Genome);
                if (nearest is null || distance < best)
                {
                    nearest = population;
                    best = distance;
                }
            }

            Merge(smallest, nearest!);
            removed.Add(smallest.Id.Value);
            group.Remove(smallest);
        }
    }

    /// <summary>Folds <paramref name="source"/> into <paramref name="target"/>; the target keeps its genome.</summary>
    private void Merge(Population source, Population target)
    {
        var abundance = source.Abundance + target.Abundance;
        if (abundance > 0.0)
        {
            target.AgeDistributionMeanSols = ((source.AgeDistributionMeanSols * source.Abundance) + (target.AgeDistributionMeanSols * target.Abundance)) / abundance;
            target.Viability = Math.Clamp(((source.Viability * source.Abundance) + (target.Viability * target.Abundance)) / abundance, 0.0, 1.0);
        }

        target.BiomassKg += source.BiomassKg;
        target.Abundance = abundance;
        target.EnergyReserveJ += source.EnergyReserveJ;
        source.BiomassKg = 0.0;
        source.Abundance = 0.0;
        source.EnergyReserveJ = 0.0;
        workspace.Totals.Merges++;
    }

    private sealed class GroupOrder : IComparer<(int Cell, string Organism)>
    {
        public static readonly GroupOrder Instance = new();

        public int Compare((int Cell, string Organism) x, (int Cell, string Organism) y)
        {
            var cell = x.Cell.CompareTo(y.Cell);
            return cell != 0 ? cell : string.CompareOrdinal(x.Organism, y.Organism);
        }
    }
}
