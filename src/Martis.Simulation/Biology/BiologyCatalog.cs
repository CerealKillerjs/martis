using System.Globalization;
using System.Text.Json;
using Martis.Domain.Biology;
using Martis.Domain.Chemistry;
using Martis.Domain.Common;
using Martis.Domain.Environment;
using Martis.Domain.Genetics;
using Martis.Domain.Parameters;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Biology;

/// <summary>A species with its derived molar mass and where it lives (ADR-012).</summary>
public sealed record SpeciesModel(
    string Id,
    ChemicalFormula Formula,
    double MolarMassKgPerMol,
    int PoolSlot,
    IReadOnlyList<double> ElementMassFractions)
{
    /// <summary>True for the population biomass compartment (no pool slot).</summary>
    public bool IsBiomass => PoolSlot < 0;
}

/// <summary>One side term of a reaction in kilograms per unit extent.</summary>
public sealed record ReactionTerm(int SpeciesIndex, int PoolSlot, double KgPerExtent);

/// <summary>A reaction resolved against the species table.</summary>
public sealed record ReactionModel(
    ReactionDefinition Definition,
    IReadOnlyList<ReactionTerm> Inputs,
    IReadOnlyList<ReactionTerm> Outputs,
    double CarbonMoles)
{
    public string Id => Definition.Id.Value;
}

/// <summary>Numbers of one organism, delivered from the parameter catalog by id.</summary>
public sealed record OrganismKinetics(
    double IndividualMassKg,
    double VmaxCarbonPerKgPerSol,
    double TemperatureOptimumKelvin,
    double TemperatureSigmaKelvin,
    double RadiationAlphaSolPerSv,
    double WaterHalfSaturationKg,
    double LightHalfSaturationWPerM2,
    IReadOnlyDictionary<string, double> SubstrateHalfSaturationKg,
    double ReserveGainJPerMolCarbon,
    IReadOnlyDictionary<string, double> Extra);

/// <summary>An organism definition plus the model numbers the systems read.</summary>
public sealed record OrganismModel(
    OrganismDefinition Definition,
    IReadOnlyList<string> Capabilities,
    ReactionModel GrowthReaction,
    ReactionModel? EnergyReaction,
    OrganismKinetics Kinetics)
{
    public string Id => Definition.Id.Value;

    /// <summary>True when the base genome has <paramref name="gene"/> (used to check a player design).</summary>
    public bool HasGene(string gene)
        => Definition.BaseGenome.BaseGenome.Expressions.Any(item => string.Equals(item.GeneId.Value, gene, StringComparison.Ordinal));
}

/// <summary>
/// Species, reactions and organisms loaded from <c>data/reactions</c> and <c>data/organisms</c>
/// (ADR-012). Every reaction is checked for exact element balance at load (VAL-007):
/// an unbalanced reaction is a load error, not a runtime surprise.
/// Every number is delivered by <see cref="ParameterCatalog"/>, so it carries its classification.
/// </summary>
public sealed class BiologyCatalog
{
    public const string MetabolismFile = "metabolism.v1.json";
    public const string OrganismsFile = "organisms.v1.json";
    public const string BiomassSpecies = "Biomass";

    private readonly Dictionary<string, OrganismModel> _organisms;

    private BiologyCatalog(
        IReadOnlyList<string> elements,
        IReadOnlyList<SpeciesModel> species,
        IReadOnlyList<ReactionModel> reactions,
        IReadOnlyList<OrganismModel> organisms,
        PhenotypeGeneBinding binding,
        double carbonEnergyJPerMol,
        ParameterCatalog metabolism,
        ParameterCatalog organismParameters)
    {
        Elements = elements;
        Species = species;
        Reactions = reactions;
        Organisms = organisms;
        Binding = binding;
        CarbonEnergyJPerMol = carbonEnergyJPerMol;
        MetabolismParameters = metabolism;
        OrganismParameters = organismParameters;
        _organisms = organisms.ToDictionary(organism => organism.Id, StringComparer.Ordinal);
        BiomassIndex = species.Select((model, index) => (model, index)).Single(pair => pair.model.IsBiomass).index;
        _poolBySlot = new SpeciesModel[ResourceColumns.Names.Count];
        foreach (var model in species.Where(model => !model.IsBiomass))
        {
            _poolBySlot[model.PoolSlot] = model;
        }
    }

    private readonly SpeciesModel[] _poolBySlot;

    /// <summary>Element symbols in ordinal order; mass fractions follow this order.</summary>
    public IReadOnlyList<string> Elements { get; }

    public IReadOnlyList<SpeciesModel> Species { get; }

    public int BiomassIndex { get; }

    public SpeciesModel Biomass => Species[BiomassIndex];

    public IReadOnlyList<ReactionModel> Reactions { get; }

    /// <summary>Organisms in file order.</summary>
    public IReadOnlyList<OrganismModel> Organisms { get; }

    public PhenotypeGeneBinding Binding { get; }

    /// <summary>Free energy per mole of carbon fixed or oxidised (MODEL_ASSUMPTION, ADR-012).</summary>
    public double CarbonEnergyJPerMol { get; }

    public ParameterCatalog MetabolismParameters { get; }

    public ParameterCatalog OrganismParameters { get; }

    public OrganismModel Organism(string id)
        => _organisms.TryGetValue(id, out var model)
            ? model
            : throw new KeyNotFoundException($"Organism '{id}' is not in the catalog.");

    public bool TryGetOrganism(string id, out OrganismModel model)
        => _organisms.TryGetValue(id, out model!);

    public ReactionModel Reaction(string id)
        => Reactions.FirstOrDefault(reaction => string.Equals(reaction.Id, id, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"Reaction '{id}' is not in the catalog.");

    /// <summary>Mass of the species in the pool slot, indexed by <see cref="ResourceColumns.Names"/>.</summary>
    public SpeciesModel PoolSpecies(int slot) => _poolBySlot[slot];

    public static BiologyCatalog LoadPublished(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        var metabolism = Path.Combine(repositoryRoot, "data", "reactions", MetabolismFile);
        var organisms = Path.Combine(repositoryRoot, "data", "organisms", OrganismsFile);
        return Load(File.ReadAllText(metabolism), File.ReadAllText(organisms));
    }

    public static BiologyCatalog Load(string metabolismJson, string organismsJson)
    {
        var chemistry = ParameterCatalog.Load(metabolismJson);
        var organismParameters = ParameterCatalog.Load(organismsJson);
        using var chemistryMeta = JsonDocument.Parse(chemistry.MetadataJson);
        using var organismMeta = JsonDocument.Parse(organismParameters.MetadataJson);
        var meta = chemistryMeta.RootElement;
        RequireSchema(meta, "martis.metabolism");
        RequireSchema(organismMeta.RootElement, "martis.organisms");

        var weights = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var element in meta.GetProperty("elements").EnumerateObject())
        {
            var value = chemistry.RequireNumber(element.Value.GetString()!);
            if (!string.Equals(value.Unit, "g/mol", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Atomic weight '{value.Id}' must be in g/mol.");
            }

            weights[element.Name] = value.Value;
        }

        var elements = weights.Keys.ToArray();
        var species = new List<SpeciesModel>();
        var formulas = new Dictionary<string, ChemicalFormula>(StringComparer.Ordinal);
        foreach (var entry in meta.GetProperty("species").EnumerateArray())
        {
            var id = entry.GetProperty("id").GetString()!;
            var formula = ChemicalFormula.Parse(entry.GetProperty("formula").GetString()!);
            var compartment = entry.GetProperty("compartment").GetString();
            var slot = compartment switch
            {
                "pool" => IndexOfSlot(id),
                "biomass" => -1,
                _ => throw new InvalidDataException($"Species '{id}' has unknown compartment '{compartment}'."),
            };
            formulas.Add(id, formula);
            species.Add(new SpeciesModel(
                id,
                formula,
                ElementalCompositionMatrix.FormulaWeightKgPerMol(formula, weights),
                slot,
                ElementalCompositionMatrix.MassFractions(formula, elements, weights)));
        }

        if (species.Count(model => model.IsBiomass) != 1 || !formulas.ContainsKey(BiomassSpecies))
        {
            throw new InvalidDataException("Exactly one biomass species named 'Biomass' is required.");
        }

        foreach (var name in ResourceColumns.Names)
        {
            if (!species.Any(model => string.Equals(model.Id, name, StringComparison.Ordinal)))
            {
                throw new InvalidDataException($"Pool slot '{name}' has no species.");
            }
        }

        var reactions = new List<ReactionModel>();
        foreach (var entry in meta.GetProperty("reactions").EnumerateArray())
        {
            var definition = new ReactionDefinition(
                new ReactionId(entry.GetProperty("id").GetString()!),
                entry.GetProperty("capability").GetString()!,
                Terms(entry.GetProperty("inputs")),
                Terms(entry.GetProperty("outputs")),
                entry.GetProperty("carbon_moles").GetDecimal(),
                ParseEnergy(entry.GetProperty("energy").GetString()));
            var residuals = ElementalBalance.Residuals(definition, formulas);
            if (residuals.Count > 0)
            {
                throw new InvalidDataException(
                    $"Reaction '{definition.Id.Value}' is not balanced (VAL-007): "
                    + string.Join(", ", residuals.Select(pair => $"{pair.Key} {pair.Value.ToString(CultureInfo.InvariantCulture)}")));
            }

            reactions.Add(new ReactionModel(
                definition,
                Resolve(definition.Inputs, species),
                Resolve(definition.Outputs, species),
                (double)definition.CarbonMoles));
        }

        var carbonEnergy = chemistry.RequireNumber("chem.energy.carbon_fixation").Value;
        var organismRoot = organismMeta.RootElement;
        var binding = ReadBinding(organismRoot.GetProperty("phenotype_binding"));
        var organisms = new List<OrganismModel>();
        foreach (var entry in organismRoot.GetProperty("organisms").EnumerateArray())
        {
            organisms.Add(ReadOrganism(entry, organismParameters, reactions));
        }

        return new BiologyCatalog(elements, species, reactions, organisms, binding, carbonEnergy, chemistry, organismParameters);
    }

    private static OrganismModel ReadOrganism(JsonElement entry, ParameterCatalog parameters, List<ReactionModel> reactions)
    {
        var id = entry.GetProperty("id").GetString()!;
        var prefix = entry.GetProperty("parameter_prefix").GetString()!;
        var capabilities = entry.GetProperty("capabilities").EnumerateArray().Select(item => item.GetString()!).ToArray();
        var genes = new Dictionary<GeneId, double>();
        foreach (var gene in entry.GetProperty("base_genome").EnumerateObject())
        {
            genes.Add(new GeneId(gene.Name), gene.Value.GetDouble());
        }

        var growth = FindReaction(reactions, entry.GetProperty("growth_reaction").GetString()!, capabilities, id);
        ReactionModel? energy = null;
        if (entry.TryGetProperty("energy_reaction", out var energyId) && energyId.ValueKind == JsonValueKind.String)
        {
            energy = FindReaction(reactions, energyId.GetString()!, capabilities, id);
        }

        var substrates = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var term in growth.Definition.Inputs.Concat(energy?.Definition.Inputs ?? []))
        {
            if (string.Equals(term.SpeciesId, BiomassSpecies, StringComparison.Ordinal) || substrates.ContainsKey(term.SpeciesId))
            {
                continue;
            }

            substrates[term.SpeciesId] = parameters.RequireNumber($"{prefix}.half_saturation.{term.SpeciesId}").Value;
        }

        var extra = new SortedDictionary<string, double>(StringComparer.Ordinal);
        foreach (var definition in parameters.Definitions)
        {
            if (definition.Id.StartsWith(prefix + ".", StringComparison.Ordinal)
                && definition.Value is ParameterLiteral.Number)
            {
                extra[definition.Id[(prefix.Length + 1)..]] = parameters.RequireNumber(definition.Id).Value;
            }
        }

        var kinetics = new OrganismKinetics(
            Number(parameters, prefix, "individual_mass"),
            Number(parameters, prefix, "vmax_carbon"),
            Number(parameters, prefix, "temperature_optimum"),
            Number(parameters, prefix, "temperature_sigma"),
            Number(parameters, prefix, "radiation_alpha"),
            Number(parameters, prefix, "water_half_saturation"),
            extra.TryGetValue("light_half_saturation", out var light) ? light : double.NaN,
            substrates,
            extra.TryGetValue("reserve_gain", out var gain) ? gain : 0.0,
            extra);
        var definitionRecord = new OrganismDefinition(
            new OrganismId(id),
            entry.GetProperty("name").GetString()!,
            entry.GetProperty("category").GetString()!,
            new GenomeDefinition(new Genome(genes)),
            new MetabolismDefinition(new[] { growth.Definition.Id }.Concat(energy is null ? [] : [energy.Definition.Id])),
            new ReproductionDefinition());
        return new OrganismModel(definitionRecord, capabilities, growth, energy, kinetics);
    }

    private static ReactionModel FindReaction(List<ReactionModel> reactions, string id, string[] capabilities, string organism)
    {
        var reaction = reactions.FirstOrDefault(model => string.Equals(model.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Organism '{organism}' names unknown reaction '{id}'.");
        if (!capabilities.Contains(reaction.Definition.RequiredCapability, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"Organism '{organism}' lacks capability '{reaction.Definition.RequiredCapability}' for reaction '{id}'.");
        }

        return reaction;
    }

    private static double Number(ParameterCatalog parameters, string prefix, string name)
        => parameters.RequireNumber($"{prefix}.{name}").Value;

    private static PhenotypeGeneBinding ReadBinding(JsonElement element)
    {
        GeneId? Gene(string name)
            => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? new GeneId(value.GetString()!)
                : null;

        return new PhenotypeGeneBinding(
            Gene(nameof(Phenotype.UvResistance)),
            Gene(nameof(Phenotype.ThermalTolerance)),
            Gene(nameof(Phenotype.DesiccationTolerance)),
            Gene(nameof(Phenotype.PhotosyntheticEfficiency)),
            Gene(nameof(Phenotype.MaintenanceCost)),
            Gene(nameof(Phenotype.ReproductionRate)),
            Gene(nameof(Phenotype.BiofilmProduction)));
    }

    private static IEnumerable<StoichiometricTerm> Terms(JsonElement element)
        => element.EnumerateObject().Select(property => new StoichiometricTerm(property.Name, property.Value.GetDecimal())).ToArray();

    private static ReactionTerm[] Resolve(IReadOnlyList<StoichiometricTerm> terms, List<SpeciesModel> species)
    {
        return terms.Select(term =>
        {
            var index = species.FindIndex(model => string.Equals(model.Id, term.SpeciesId, StringComparison.Ordinal));
            if (index < 0)
            {
                throw new InvalidDataException($"Species '{term.SpeciesId}' is not declared.");
            }

            return new ReactionTerm(index, species[index].PoolSlot, (double)term.Moles * species[index].MolarMassKgPerMol);
        }).ToArray();
    }

    private static int IndexOfSlot(string id)
    {
        for (var i = 0; i < ResourceColumns.Names.Count; i++)
        {
            if (string.Equals(ResourceColumns.Names[i], id, StringComparison.Ordinal))
            {
                return i;
            }
        }

        throw new InvalidDataException($"Pool species '{id}' is not a ResourcePool slot.");
    }

    private static ReactionEnergyMode ParseEnergy(string? text) => text switch
    {
        "none" => ReactionEnergyMode.None,
        "light" => ReactionEnergyMode.Light,
        "release" => ReactionEnergyMode.Release,
        "cost" => ReactionEnergyMode.Cost,
        _ => throw new InvalidDataException($"Unknown reaction energy mode '{text}'."),
    };

    private static void RequireSchema(JsonElement meta, string schema)
    {
        if (!meta.TryGetProperty("schema", out var value) || !string.Equals(value.GetString(), schema, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Expected metadata.schema '{schema}'.");
        }
    }
}
