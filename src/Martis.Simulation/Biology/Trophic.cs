using System.Text.Json;
using Martis.Domain.Biology;
using Martis.Domain.Parameters;
using Martis.Domain.World;

namespace Martis.Simulation.Biology;

/// <summary>
/// One feeding link (H6, ADR-024): every population of <see cref="Consumer"/> eats the
/// populations of the <see cref="Prey"/> organisms in its own cell with a Holling type II
/// response, <c>intake = MaxIntake·C·P/(HalfSaturation + P)·Δt</c>, never more than
/// <see cref="MaxPreyFraction"/> of the prey. The consumer keeps <see cref="Efficiency"/>
/// of what it eats as biomass and the rest becomes dead biomass for the recycling step, so
/// every cell keeps its mass. Prey below <see cref="RefugeKg"/> per cell hides and is not
/// eaten, so an overgrazed cell can grow back. Each assimilated kilogram adds <see cref="EnergyJPerKg"/> to
/// the consumer's reserve.
/// </summary>
public sealed record FeedingRule(
    string Id,
    string Consumer,
    IReadOnlyList<string> Prey,
    double MaxIntakePerSol,
    double HalfSaturationKg,
    double Efficiency,
    double EnergyJPerKg,
    double MaxPreyFraction,
    double RefugeKg = 0.0);

/// <summary>Feeding links of a world, loaded from <c>data/organisms/*.json</c> (schema <c>martis.trophic</c>).</summary>
public sealed record TrophicCatalog(string Id, IReadOnlyList<FeedingRule> Rules)
{
    public static TrophicCatalog LoadPublished(string repositoryRoot, string fileName, BiologyCatalog biology)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        if (fileName.IndexOfAny(['/', '\\']) >= 0)
        {
            throw new InvalidDataException($"Trophic file '{fileName}' must be a plain file name.");
        }

        return Load(File.ReadAllText(Path.Combine(repositoryRoot, "data", "organisms", fileName)), biology);
    }

    public static TrophicCatalog Load(string json, BiologyCatalog biology)
    {
        ArgumentNullException.ThrowIfNull(biology);
        var parameters = ParameterCatalog.Load(json);
        using var document = JsonDocument.Parse(json);
        var meta = document.RootElement.GetProperty("metadata");
        if (meta.GetProperty("schema").GetString() != "martis.trophic")
        {
            throw new InvalidDataException("A trophic file needs metadata.schema = martis.trophic.");
        }

        var rules = new List<FeedingRule>();
        foreach (var item in meta.GetProperty("links").EnumerateArray())
        {
            double Read(string name) => parameters.RequireNumber(item.GetProperty(name).GetString()!).Value;
            var rule = new FeedingRule(
                item.GetProperty("id").GetString()!,
                item.GetProperty("consumer").GetString()!,
                [.. item.GetProperty("prey").EnumerateArray().Select(prey => prey.GetString()!)],
                Read("max_intake"),
                Read("half_saturation"),
                Read("efficiency"),
                Read("energy"),
                Read("max_prey_fraction"),
                item.TryGetProperty("refuge", out _) ? Read("refuge") : 0.0);
            foreach (var organism in rule.Prey.Append(rule.Consumer))
            {
                if (!biology.TryGetOrganism(organism, out _))
                {
                    throw new InvalidDataException($"Feeding link '{rule.Id}' names unknown organism '{organism}'.");
                }
            }

            if (rule.Prey.Count == 0 || rule.Prey.Contains(rule.Consumer, StringComparer.Ordinal)
                || rule.MaxIntakePerSol < 0 || rule.HalfSaturationKg <= 0 || rule.Efficiency is < 0 or > 1
                || rule.EnergyJPerKg < 0 || rule.MaxPreyFraction is <= 0 or > 1 || rule.RefugeKg < 0)
            {
                throw new InvalidDataException($"Feeding link '{rule.Id}' has an invalid prey list or rate.");
            }

            rules.Add(rule);
        }

        return new TrophicCatalog(meta.GetProperty("id").GetString()!, rules);
    }
}

/// <summary>Feeding inside update-ecology. Deterministic: it draws no random numbers.</summary>
public static class Feeding
{
    /// <summary>Applies every link, cells ascending, links in file order. Returns the prey biomass eaten (kg).</summary>
    public static double Apply(SimulationState state, MetabolismWorkspace workspace, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(workspace);
        if (workspace.Trophic is not { Rules.Count: > 0 } trophic)
        {
            return 0.0;
        }

        var byCell = new SortedDictionary<int, List<Population>>();
        foreach (var population in state.Populations)
        {
            if (population.BiomassKg > 0.0)
            {
                if (!byCell.TryGetValue(population.CellIndex, out var list))
                {
                    byCell[population.CellIndex] = list = [];
                }

                list.Add(population);
            }
        }

        var catalog = workspace.Catalog;
        var eatenTotal = 0.0;
        foreach (var (cell, populations) in byCell)
        {
            foreach (var rule in trophic.Rules)
            {
                var consumers = populations.Where(population => string.Equals(population.OrganismId.Value, rule.Consumer, StringComparison.Ordinal) && population.BiomassKg > 0).ToList();
                var prey = populations.Where(population => rule.Prey.Contains(population.OrganismId.Value, StringComparer.Ordinal) && population.BiomassKg > 0).ToList();
                if (consumers.Count == 0 || prey.Count == 0)
                {
                    continue;
                }

                var c = consumers.Sum(population => population.BiomassKg);
                var p = prey.Sum(population => population.BiomassKg);
                var exposed = Math.Max(0.0, p - rule.RefugeKg);
                var eaten = Math.Min(rule.MaxPreyFraction * exposed, rule.MaxIntakePerSol * c * exposed / (rule.HalfSaturationKg + exposed) * deltaTimeSols);
                if (!(eaten > 0.0))
                {
                    continue;
                }

                foreach (var item in prey)
                {
                    var share = item.BiomassKg / p;
                    var kg = eaten * share;
                    var fraction = kg / item.BiomassKg;
                    item.BiomassKg -= kg;
                    item.EnergyReserveJ -= item.EnergyReserveJ * fraction;
                    item.Abundance = item.BiomassKg / catalog.Organism(item.OrganismId.Value).Kinetics.IndividualMassKg;
                }

                var assimilated = eaten * rule.Efficiency;
                foreach (var item in consumers)
                {
                    var share = item.BiomassKg / c;
                    var joules = assimilated * share * rule.EnergyJPerKg;
                    item.BiomassKg += assimilated * share;
                    item.EnergyReserveJ += joules;
                    workspace.Gain(item.Id.Value, joules);
                    item.Abundance = item.BiomassKg / catalog.Organism(item.OrganismId.Value).Kinetics.IndividualMassKg;
                }

                workspace.DeadKg[cell] += eaten - assimilated;
                eatenTotal += eaten;
            }
        }

        workspace.Totals.BiomassEatenKg += eatenTotal;
        return eatenTotal;
    }
}
