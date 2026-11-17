using Martis.Contracts.Commands;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Experiments;
using Martis.Simulation.Missions;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Biology;

/// <summary>T09: the three archetypes, the closed carbon and water cycle and FIRST COLONY.</summary>
[Trait("Category", "Ecosystem")]
public sealed class EcosystemTests
{
    private static readonly HostInfo Host = new("t", "t", "t");

    [Fact]
    public void TheCatalogHasTheMicrobesAndTheChapterThreeOrganisms()
    {
        var catalog = Catalog();

        Assert.Equal(
            new[] { "decomposer", "eukaryote", "extremophile", "herbivore", "multicellular", "phototroph", "predator", "social" },
            catalog.Organisms.Select(organism => organism.Definition.Category).Order(StringComparer.Ordinal));
        var decomposer = catalog.Organism("decomposer_d01");
        Assert.Equal("heterotrophic_assimilation", decomposer.GrowthReaction.Id);
        Assert.Equal("aerobic_respiration", decomposer.EnergyReaction!.Id);
        Assert.True(catalog.Organism("extremophile_e01").Kinetics.TemperatureOptimumKelvin
            < catalog.Organism("phototroph_a01").Kinetics.TemperatureOptimumKelvin);
    }

    [Fact]
    public void TheDecomposerRespiresOrganicCarbonBackToCarbonDioxideAndWater()
    {
        var (engine, workspace) = World(seed: 1);
        var before = engine.State.Resources[2];
        var organic = before.OrganicCarbon;
        var carbonDioxide = before.CarbonDioxide;
        var water = before.Water;
        engine.ApplyCommand(new IntroducePopulationCommand("decomposer_d01", 2, 0.05));

        var respired = 0.0;
        for (var tick = 0; tick < 20; tick++)
        {
            engine.Advance(1);
            respired += workspace.Totals.CarbonRespiredMol;
            Assert.True(workspace.Totals.MaxElementErrorKg < 1e-9);
        }

        var after = engine.State.Resources[2];
        Assert.True(respired > 0.0);
        Assert.True(after.OrganicCarbon < organic);
        Assert.True(after.CarbonDioxide > carbonDioxide);
        Assert.True(after.Water > water);
    }

    [Fact]
    public void APhototrophWithADecomposerKeepsALivingColonyForFourHundredSols()
    {
        var result = Run(5UL, 400, [new PopulationIntroduction("phototroph_a01", 2080, 0.5), new PopulationIntroduction("decomposer_d01", 2080, 0.05)]);
        var alone = Run(5UL, 400, [new PopulationIntroduction("phototroph_a01", 2080, 0.5)]);

        Assert.True(result.Succeeded, result.FailureSummary);
        Assert.True(FinalBiomass(result) > 1.0, $"with decomposer {FinalBiomass(result)}");
        Assert.True(FinalBiomass(alone) < 0.01, $"alone {FinalBiomass(alone)}");
    }

    [Fact]
    public void ADesignReplacesGenesAndBadDesignsAreRejected()
    {
        var (engine, _) = World(seed: 2);
        engine.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", 0, 0.01, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.95 }));
        engine.Advance(1);
        Assert.Equal(0.95, engine.State.Populations[0].Genome[new Martis.Domain.Common.GeneId("UV_PROTECTION")]);

        var (unknown, _) = World(seed: 2);
        unknown.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", 0, 0.01, new Dictionary<string, double> { ["WINGS"] = 0.5 }));
        Assert.Throws<ArgumentException>(() => unknown.Advance(1));

        var (range, _) = World(seed: 2);
        range.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", 0, 0.01, new Dictionary<string, double> { ["UV_PROTECTION"] = 1.5 }));
        Assert.ThrowsAny<ArgumentException>(() => range.Advance(1));
    }

    [Fact]
    public void FirstColonyIsCompletedWithASeedAndADesignedPhototroph()
    {
        var request = new ExperimentRequest(PrototypeExperiment.MarsBaselineId, 5UL, 400, 100, Host)
        {
            Introductions =
            [
                new PopulationIntroduction("phototroph_a01", 2080, 0.5, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.85 }),
                new PopulationIntroduction("decomposer_d01", 2080, 0.05),
            ],
            Mission = MissionDefinition.LoadPublished(Repo.Root()),
            MissionActions = ["analyze-region", "run-experiment"],
        };

        var result = PrototypeExperiment.Run(request, MarsBaselineScenario.LoadPublished(Repo.Root()), biology: Catalog());

        var mission = Text(result, PrototypeExperiment.MissionFile);
        Assert.Contains("\"complete\": true", mission, StringComparison.Ordinal);
        Assert.Contains("mission first_colony complete tick=", Text(result, PrototypeExperiment.LogFile), StringComparison.Ordinal);
    }

    [Fact]
    public void WithoutThePlayerActionsOrADesignTheMissionStaysOpen()
    {
        var catalog = Catalog();
        var tracker = new MissionTracker(MissionDefinition.LoadPublished(Repo.Root()), catalog);
        var (engine, workspace) = World(seed: 3);
        engine.ApplyCommand(new IntroducePopulationCommand("phototroph_a01", 0, 0.01));
        for (var tick = 0; tick < 5; tick++)
        {
            engine.Advance(1);
            tracker.Observe(engine.State, workspace);
        }

        var status = tracker.Status().ToDictionary(item => item.Id);
        Assert.False(tracker.IsComplete);
        Assert.NotNull(status["deploy_colony"].CompletedTick);
        Assert.Null(status["design_phototroph"].CompletedTick);
        Assert.Null(status["analyze_region"].CompletedTick);
        tracker.RecordAction("analyze-region", 5);
        Assert.Equal(5, tracker.Status().Single(item => item.Id == "analyze_region").CompletedTick);
    }

    [Fact]
    public void MissionThresholdsCarryTheirClassification()
    {
        var mission = MissionDefinition.LoadPublished(Repo.Root());
        var sustain = mission.Objectives.Single(objective => objective.Kind == ObjectiveKind.Sustain);

        Assert.Equal("first_colony", mission.Id);
        Assert.Equal(6, mission.Objectives.Count);
        Assert.True(sustain.Sols > 0 && sustain.MinBiomassKg > 0);
    }

    private static double FinalBiomass(ExperimentResult result)
    {
        var last = Text(result, PrototypeExperiment.PopulationHistoryFile).TrimEnd('\n').Split('\n')[^1].Split(',');
        return double.Parse(last[3], System.Globalization.CultureInfo.InvariantCulture);
    }

    private static ExperimentResult Run(ulong seed, int ticks, PopulationIntroduction[] introductions)
        => PrototypeExperiment.Run(
            new ExperimentRequest(PrototypeExperiment.MarsBaselineId, seed, ticks, 100, Host) { Introductions = introductions },
            MarsBaselineScenario.LoadPublished(Repo.Root()),
            biology: Catalog());

    private static string Text(ExperimentResult result, string name)
        => System.Text.Encoding.UTF8.GetString(result.Files.Single(file => file.Name == name).Content);

    private static BiologyCatalog Catalog() => BiologyCatalog.LoadPublished(Repo.Root());

    private static (SimulationEngine Engine, MetabolismWorkspace Workspace) World(ulong seed)
    {
        var scenario = MarsBaselineScenario.Load(ScenarioFixtures.Science, ScenarioFixtures.Deposits);
        var state = new SimulationState(8, 8);
        scenario.Apply(state);
        foreach (var environment in state.Environment)
        {
            environment.IonizingEquivalentDoseSvPerSol = 5e-4;
        }

        var workspace = new MetabolismWorkspace(Catalog());
        var engine = new SimulationEngine(
            state,
            new SimulationConfiguration(8, 8, 1.0, seed, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell, true),
            RngStreams.Create(seed),
            BiologyPipeline.Systems(workspace),
            BiologyPipeline.Handlers(workspace),
            SimulationPipeline.BiologyVersion);
        return (engine, workspace);
    }
}
