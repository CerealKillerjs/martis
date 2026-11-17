using System.Text.Json;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Parameters;
using Martis.Domain.World;
using Martis.Simulation.Engine;

namespace Martis.Simulation.Biology;

/// <summary>
/// One major evolutionary transition (H5, ADR-023): when every partner lives in the same
/// cell with at least <see cref="MinBiomassKg"/> (and, for a gene condition, with that gene
/// at or above <see cref="MinGeneExpression"/>), each sol has probability
/// <see cref="ProbabilityPerSol"/> of founding a population of <see cref="Product"/>. The
/// founder takes <see cref="FounderFraction"/> of each partner's biomass and energy reserve,
/// so mass is conserved (all organisms share the biomass composition).
/// </summary>
public sealed record TransitionRule(
    string Id,
    string Title,
    IReadOnlyList<string> Partners,
    string Product,
    double MinBiomassKg,
    double ProbabilityPerSol,
    double FounderFraction,
    string? Gene = null,
    double MinGeneExpression = 0.0);

/// <summary>A transition that happened this tick: <see cref="ChildId"/> is the new population.</summary>
public sealed record TransitionEvent(long Tick, int CellIndex, string RuleId, string Title, string Product, long ChildId, double BiomassKg);

/// <summary>The transitions of a world, loaded from <c>data/organisms/*.json</c> (schema <c>martis.transitions</c>).</summary>
public sealed record TransitionCatalog(string Id, IReadOnlyList<TransitionRule> Rules)
{
    public static TransitionCatalog LoadPublished(string repositoryRoot, string fileName, BiologyCatalog biology)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new InvalidDataException($"Transition file '{fileName}' must be a plain file name.");
        }

        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "organisms", fileName)), biology);
    }

    public static TransitionCatalog Load(string json, BiologyCatalog biology)
    {
        ArgumentNullException.ThrowIfNull(biology);
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.transitions")
        {
            throw new InvalidDataException("A transitions file needs metadata.schema = martis.transitions.");
        }

        var rules = new List<TransitionRule>();
        foreach (var item in meta.GetProperty("transitions").EnumerateArray())
        {
            double Read(string name) => parameters.RequireNumber(item.GetProperty(name).GetString()!).Value;
            var rule = new TransitionRule(
                item.GetProperty("id").GetString()!,
                item.GetProperty("title").GetString()!,
                [.. item.GetProperty("partners").EnumerateArray().Select(partner => partner.GetString()!)],
                item.GetProperty("product").GetString()!,
                Read("min_biomass"),
                Read("probability"),
                Read("founder_fraction"),
                item.TryGetProperty("gene", out var gene) ? gene.GetString() : null,
                item.TryGetProperty("min_gene", out _) ? Read("min_gene") : 0.0);
            foreach (var organism in rule.Partners.Append(rule.Product))
            {
                if (!biology.TryGetOrganism(organism, out _))
                {
                    throw new InvalidDataException($"Transition '{rule.Id}' names unknown organism '{organism}'.");
                }
            }

            if (rule.Partners.Count == 0 || rule.Partners.Distinct(StringComparer.Ordinal).Count() != rule.Partners.Count
                || rule.ProbabilityPerSol is < 0 or > 1 || rule.FounderFraction is <= 0 or >= 1 || rule.MinBiomassKg < 0)
            {
                throw new InvalidDataException($"Transition '{rule.Id}' needs distinct partners, a probability in [0, 1] and a founder fraction in (0, 1).");
            }

            rules.Add(rule);
        }

        return new TransitionCatalog(meta.GetProperty("id").GetString()!, rules);
    }
}

/// <summary>
/// update-ecology (H5): major transitions. Without a <see cref="MetabolismWorkspace.Transitions"/>
/// catalog it does nothing and draws nothing, so worlds without transitions run exactly as
/// before. It draws from the Events stream (ADR-003), which no other step uses: cells
/// ascending, rules in file order, one draw per eligible (cell, rule).
/// </summary>
public sealed class TransitionSystem(MetabolismWorkspace workspace) : ISimulationSystem
{
    public string Name => SimulationSteps.UpdateEcology;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (workspace.Transitions is not { Rules.Count: > 0 } transitions)
        {
            return;
        }

        var state = context.State;
        var byCell = new SortedDictionary<int, List<Population>>();
        foreach (var population in state.Populations)
        {
            if (population.BiomassKg <= 0.0)
            {
                continue;
            }

            if (!byCell.TryGetValue(population.CellIndex, out var list))
            {
                byCell[population.CellIndex] = list = [];
            }

            list.Add(population);
        }

        var catalog = workspace.Catalog;
        foreach (var (cell, populations) in byCell)
        {
            foreach (var rule in transitions.Rules)
            {
                var partners = new List<Population>(rule.Partners.Count);
                foreach (var organismId in rule.Partners)
                {
                    var best = populations
                        .Where(population => string.Equals(population.OrganismId.Value, organismId, StringComparison.Ordinal) && Qualifies(population, rule))
                        .OrderByDescending(population => population.BiomassKg)
                        .ThenBy(population => population.Id.Value)
                        .FirstOrDefault();
                    if (best is null)
                    {
                        break;
                    }

                    partners.Add(best);
                }

                if (partners.Count != rule.Partners.Count)
                {
                    continue;
                }

                var probability = 1.0 - Math.Pow(1.0 - rule.ProbabilityPerSol, deltaTimeSols);
                if (context.Random.Events.NextDouble() >= probability
                    || state.Populations.Count >= (workspace.PopulationBudget ?? int.MaxValue))
                {
                    continue;
                }

                var product = catalog.Organism(rule.Product);
                var biomass = 0.0;
                var reserve = 0.0;
                foreach (var partner in partners)
                {
                    var kg = partner.BiomassKg * rule.FounderFraction;
                    var joules = partner.EnergyReserveJ * rule.FounderFraction;
                    partner.BiomassKg -= kg;
                    partner.EnergyReserveJ -= joules;
                    partner.Abundance = partner.BiomassKg / catalog.Organism(partner.OrganismId.Value).Kinetics.IndividualMassKg;
                    biomass += kg;
                    reserve += joules;
                }

                var id = state.AllocatePopulationId();
                state.Populations.Add(new Population(new PopulationId(id), new OrganismId(rule.Product), cell, product.Definition.BaseGenome.BaseGenome)
                {
                    BiomassKg = biomass,
                    Abundance = biomass / product.Kinetics.IndividualMassKg,
                    EnergyReserveJ = reserve,
                    AgeDistributionMeanSols = 0.0,
                    Viability = 1.0,
                });
                workspace.Totals.Births++;
                workspace.TransitionEvents.Add(new TransitionEvent(state.Tick, cell, rule.Id, rule.Title, rule.Product, id, biomass));
            }
        }
    }

    private static bool Qualifies(Population population, TransitionRule rule)
    {
        if (population.BiomassKg < rule.MinBiomassKg)
        {
            return false;
        }

        if (rule.Gene is null)
        {
            return true;
        }

        return population.Genome.Expressions.Any(expression =>
            string.Equals(expression.GeneId.Value, rule.Gene, StringComparison.Ordinal) && expression.Expression >= rule.MinGeneExpression);
    }
}
