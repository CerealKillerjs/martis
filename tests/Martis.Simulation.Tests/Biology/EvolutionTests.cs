using System.Text.Json.Nodes;
using Martis.Contracts.Commands;
using Martis.Domain.Biology;
using Martis.Domain.Common;
using Martis.Domain.Genetics;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Experiments;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Biology;

/// <summary>T06: mutation, drift, merging and the variant cap (ADR-006, ADR-009, ADR-014).</summary>
[Trait("Category", "Evolution")]
public sealed class EvolutionTests
{
    private const string Phototroph = "phototroph_a01";

    [Fact]
    public void TheIrwinHallNormalHasMeanZeroAndUnitVariance()
    {
        var random = Pcg32.Seed(11, 2);
        const int count = 40000;
        var sum = 0.0;
        var squares = 0.0;
        for (var i = 0; i < count; i++)
        {
            var value = EvolutionDraws.StandardNormal(random);
            Assert.InRange(value, -6.0, 6.0);
            sum += value;
            squares += value * value;
        }

        var mean = sum / count;
        Assert.InRange(mean, -0.03, 0.03);
        Assert.InRange((squares / count) - (mean * mean), 0.95, 1.05);
    }

    [Theory]
    [InlineData(20L, 0.3)]
    [InlineData(100000L, 0.02)]
    public void BinomialDrawsStayInRangeWithTheRightMean(long trials, double probability)
    {
        var random = Pcg32.Seed(5, 4);
        var sum = 0.0;
        for (var i = 0; i < 4000; i++)
        {
            var hits = EvolutionDraws.Binomial(random, trials, probability);
            Assert.InRange(hits, 0L, trials);
            sum += hits;
        }

        var expected = trials * probability;
        Assert.InRange(sum / 4000, expected * 0.97, expected * 1.03);
        Assert.Equal(0L, EvolutionDraws.Binomial(random, trials, 0.0));
        Assert.Equal(trials, EvolutionDraws.Binomial(random, trials, 1.0));
    }

    [Fact]
    public void ExtremeMutationsKeepEveryGeneInsideTheUnitInterval()
    {
        var catalog = Catalog(("mutation.sigma", 25.0), ("mutation.trait_probability", 1.0), ("mutation.event_rate", 100.0));
        var (engine, workspace) = World(catalog, seed: 1);
        Introduce(engine, 8);

        var created = 0;
        for (var tick = 0; tick < 15; tick++)
        {
            engine.Advance(1);
            created += workspace.Totals.Mutations;
            foreach (var population in engine.State.Populations)
            {
                Assert.All(population.Genome.Expressions, gene => Assert.InRange(gene.Expression, 0.0, 1.0));
            }
        }

        Assert.True(created > 0);
    }

    [Fact]
    public void WithZeroTraitProbabilityNoVariantIsCreated()
    {
        var catalog = Catalog(("mutation.trait_probability", 0.0), ("mutation.event_rate", 100.0));
        var (engine, _) = World(catalog, seed: 2);
        Introduce(engine, 4);

        engine.Advance(20);

        Assert.All(engine.State.Populations, population => Assert.True(population.Id.Value <= 4));
    }

    [Fact]
    public void TheSameSeedGivesTheSameVariantsAndAnotherSeedDoesNot()
    {
        var catalog = Catalog(("mutation.event_rate", 20.0));
        string Run(ulong seed)
        {
            var (engine, _) = World(catalog, seed);
            Introduce(engine, 6);
            engine.Advance(25);
            return System.Text.Encoding.UTF8.GetString(StateSnapshotJson.Write(StateSnapshot.Capture(engine, null)));
        }

        var a = Run(77);
        Assert.Equal(a, Run(77));
        Assert.NotEqual(a, Run(78));
    }

    [Fact]
    public void NoCellEverHoldsMoreVariantsOfASpeciesThanTheCap()
    {
        var catalog = Catalog(("mutation.event_rate", 100.0), ("mutation.trait_probability", 1.0), ("mutation.sigma", 0.3));
        var (engine, workspace) = World(catalog, seed: 3, maxVariants: 3);
        for (var i = 0; i < 5; i++)
        {
            engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 0, 0.01));
        }

        var merges = 0;
        for (var tick = 0; tick < 30; tick++)
        {
            engine.Advance(1);
            merges += workspace.Totals.Merges;
            Assert.True(engine.State.Populations.Count(population => population.CellIndex == 0) <= 3);
            Assert.True(workspace.Totals.MaxElementErrorKg < 1e-9);
        }

        Assert.True(merges > 0);
    }

    [Fact]
    public void EquivalentVariantsMergeAndConserveBiomassAbundanceAndReserve()
    {
        var catalog = Catalog();
        var organism = catalog.Organism(Phototroph);
        var state = Fixture();
        var genome = organism.Definition.BaseGenome.BaseGenome;
        var shifted = new Genome(genome.Expressions.Select(gene => new GeneExpression(gene.GeneId, Math.Min(1.0, gene.Expression + 0.001))));
        state.Populations.Add(Make(1, organism, genome, 0.2, 50.0));
        state.Populations.Add(Make(2, organism, shifted, 0.3, 70.0));
        var workspace = new MetabolismWorkspace(catalog);
        var engine = new SimulationEngine(
            state,
            Configuration(1, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell),
            RngStreams.Create(1),
            [new ResourceAvailabilitySystem(workspace), new SelectionDriftSystem(workspace), new RecycleSystem(workspace)]);

        engine.Advance(1);

        var merged = Assert.Single(engine.State.Populations);
        Assert.Equal(1L, merged.Id.Value);
        Assert.Equal(0.5, merged.BiomassKg, 12);
        Assert.Equal(0.5 / organism.Kinetics.IndividualMassKg, merged.Abundance, 0);
        Assert.Equal(120.0, merged.EnergyReserveJ, 9);
        Assert.Equal(1, workspace.Totals.Merges);
    }

    [Fact]
    public void DriftResamplesSharesButKeepsTheGroupBiomass()
    {
        var catalog = Catalog(("drift.effective_size", 50.0));
        var organism = catalog.Organism(Phototroph);
        var state = Fixture();
        var genome = organism.Definition.BaseGenome.BaseGenome;
        var other = new Genome(genome.Expressions.Select(gene => new GeneExpression(gene.GeneId, Math.Max(0.0, gene.Expression - 0.2))));
        state.Populations.Add(Make(1, organism, genome, 0.5, 0.0));
        state.Populations.Add(Make(2, organism, other, 0.5, 0.0));
        var workspace = new MetabolismWorkspace(catalog);
        var engine = new SimulationEngine(
            state,
            Configuration(4, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell),
            RngStreams.Create(4),
            [new ResourceAvailabilitySystem(workspace), new SelectionDriftSystem(workspace), new RecycleSystem(workspace)]);

        engine.Advance(1);

        var total = engine.State.Populations.Sum(population => population.BiomassKg) + workspace.Totals.BiomassRecycledKg;
        Assert.Equal(1.0, total, 12);
        Assert.NotEqual(0.5, engine.State.Populations[0].BiomassKg);
    }

    [Fact]
    public void TheExperimentWritesOneMutationRowPerNewVariant()
    {
        var root = Repo.Root();
        var request = new ExperimentRequest(PrototypeExperiment.MarsBaselineId, 9UL, 40, 10, new HostInfo("t", "t", "t"))
        {
            Introductions = Enumerable.Range(0, 10).Select(i => new PopulationIntroduction(Phototroph, 64 * (5 + (i * 5)) + 20, 0.5)).ToArray(),
        };

        var result = PrototypeExperiment.Run(request, MarsBaselineScenario.LoadPublished(root), biology: BiologyCatalog.LoadPublished(root));

        Assert.True(result.Succeeded, result.FailureSummary);
        var rows = System.Text.Encoding.UTF8.GetString(result.Files.Single(file => file.Name == PrototypeExperiment.MutationsFile).Content)
            .TrimEnd('\n').Split('\n');
        Assert.Equal("tick,cell,organism,parent_id,child_id,child_biomass_kg,changes", rows[0]);
        Assert.True(rows.Length > 1, "40 sols of ten populations should produce a variant");
        Assert.All(rows.Skip(1), row => Assert.Equal(7, row.Split(',').Length));
    }

    private static Population Make(long id, OrganismModel organism, Genome genome, double biomass, double reserve)
        => new(new PopulationId(id), organism.Definition.Id, 0, genome)
        {
            BiomassKg = biomass,
            Abundance = biomass / organism.Kinetics.IndividualMassKg,
            EnergyReserveJ = reserve,
            AgeDistributionMeanSols = 1.0,
            Viability = 1.0,
        };

    private static void Introduce(SimulationEngine engine, int count)
    {
        for (var i = 0; i < count; i++)
        {
            engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, i, 0.01));
        }
    }

    /// <summary>The published catalog with some organism parameters replaced.</summary>
    private static BiologyCatalog Catalog(params (string Name, double Value)[] overrides)
    {
        var root = Repo.Root();
        var organisms = JsonNode.Parse(File.ReadAllText(Path.Combine(root, "data", "organisms", BiologyCatalog.OrganismsFile)))!;
        foreach (var (name, value) in overrides)
        {
            var parameter = organisms["parameters"]!.AsArray()
                .Single(node => (string?)node!["id"] == $"org.{Phototroph}.{name}")!;
            parameter["value"] = value;
            var range = parameter["range"]!.AsArray();
            range[1] = Math.Max(value, (double)range[1]!);
        }

        return BiologyCatalog.Load(
            File.ReadAllText(Path.Combine(root, "data", "reactions", BiologyCatalog.MetabolismFile)),
            organisms.ToJsonString());
    }

    private static SimulationState Fixture()
    {
        var scenario = MarsBaselineScenario.Load(ScenarioFixtures.Science, ScenarioFixtures.Deposits);
        var state = new SimulationState(8, 8);
        scenario.Apply(state);
        foreach (var environment in state.Environment)
        {
            environment.IonizingEquivalentDoseSvPerSol = 5e-4;
        }

        return state;
    }

    private static SimulationConfiguration Configuration(ulong seed, int maxVariants)
        => new(8, 8, 1.0, seed, maxVariants, true);

    private static (SimulationEngine Engine, MetabolismWorkspace Workspace) World(BiologyCatalog catalog, ulong seed, int maxVariants = SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell)
    {
        var workspace = new MetabolismWorkspace(catalog);
        var engine = new SimulationEngine(
            Fixture(),
            Configuration(seed, maxVariants),
            RngStreams.Create(seed),
            BiologyPipeline.Systems(workspace),
            BiologyPipeline.Handlers(workspace),
            SimulationPipeline.BiologyVersion);
        return (engine, workspace);
    }

}
