using Martis.Contracts.Commands;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Biology;

/// <summary>T05: mortality, starvation, carrying capacity, extinction and recycling.</summary>
[Trait("Category", "Population")]
public sealed class PopulationTests
{
    private const string Phototroph = "phototroph_a01";

    [Fact]
    public void CombinedMortalityIsOneMinusTheProductOfSurvivals()
    {
        Assert.Equal(1.0 - (0.9 * 0.8 * 0.5), MortalityModel.Combine([0.1, 0.2, 0.5]), 15);
        Assert.Equal(0.0, MortalityModel.Combine([0.0, 0.0]));
        Assert.Equal(1.0, MortalityModel.Combine([0.3, 1.0]));
        Assert.Throws<InvalidOperationException>(() => MortalityModel.Combine([1.5]));
        Assert.Throws<InvalidOperationException>(() => MortalityModel.Combine([double.NaN]));
    }

    [Fact]
    public void ATermIsZeroWithoutStressAndGrowsWithTime()
    {
        Assert.Equal(0.0, MortalityModel.Term(0.2, 0.0, 1.0));
        var one = MortalityModel.Term(0.2, 1.0, 1.0);
        var two = MortalityModel.Term(0.2, 1.0, 2.0);
        Assert.Equal(1.0 - Math.Exp(-0.2), one, 15);
        Assert.True(two > one && two < 1.0);
    }

    [Fact]
    public void BiomassAboveTheCarryingCapacityIsCutBackWithoutCreatingMatter()
    {
        var (engine, workspace) = World(seed: 1);
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 4, 50.0));

        engine.Advance(1);

        var population = Assert.Single(engine.State.Populations);
        var capacity = PopulationParameters.From(Catalog().Organism(Phototroph)).CarryingCapacityKg;
        Assert.True(population.BiomassKg <= capacity * 1.01, $"biomass {population.BiomassKg}");
        Assert.True(workspace.Totals.BiomassDiedKg > 30.0);
        Assert.Equal(workspace.Totals.BiomassDiedKg, workspace.Totals.BiomassRecycledKg, 9);
        Assert.True(workspace.Totals.MaxElementErrorKg < 1e-9);
    }

    [Fact]
    public void InTheDarkTheReserveRunsOutAndThePopulationStarvesToExtinction()
    {
        var (engine, workspace) = World(seed: 2);
        foreach (var environment in engine.State.Environment)
        {
            environment.SolarIrradiance = 0.0;
        }

        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 5, 1e-6));
        engine.Advance(1);
        Assert.True(workspace.Totals.UnpaidMaintenanceJ > 0.0);

        var extinctions = 0;
        for (var tick = 0; tick < 400 && engine.State.Populations.Count > 0; tick++)
        {
            engine.Advance(1);
            extinctions += workspace.Totals.Extinctions;
        }

        Assert.Empty(engine.State.Populations);
        Assert.Equal(1, extinctions);
    }

    [Fact]
    public void DeadBiomassReturnsNitrogenAndPhosphorusToThePool()
    {
        var (engine, _) = World(seed: 3);
        foreach (var environment in engine.State.Environment)
        {
            environment.SolarIrradiance = 0.0;
        }

        var nitrogen = engine.State.Resources[6].Nitrogen;
        var organic = engine.State.Resources[6].OrganicCarbon;
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 6, 0.5));

        engine.Advance(50);

        Assert.True(engine.State.Resources[6].Nitrogen > nitrogen);
        Assert.True(engine.State.Resources[6].OrganicCarbon > organic);
    }

    [Fact]
    public void MeanAgeGrowsOneSolPerTickWithoutBirthsAndLessWithThem()
    {
        var (dark, _) = World(seed: 4);
        foreach (var environment in dark.State.Environment)
        {
            environment.SolarIrradiance = 0.0;
        }

        dark.ApplyCommand(new IntroducePopulationCommand(Phototroph, 0, 0.1));
        dark.Advance(10);
        Assert.Equal(10.0, Assert.Single(dark.State.Populations).AgeDistributionMeanSols, 9);

        var (lit, workspace) = World(seed: 4);
        lit.ApplyCommand(new IntroducePopulationCommand(Phototroph, 0, 1e-3));
        lit.Advance(10);
        Assert.True(Assert.Single(lit.State.Populations).AgeDistributionMeanSols < 10.0);
        Assert.True(workspace.Totals.NewIndividuals > 0.0);
    }

    [Fact]
    public void AbundanceBiomassAndViabilityStayInRangeForManyInoculations()
    {
        var random = new System.Random(5);
        for (var run = 0; run < 12; run++)
        {
            var (engine, workspace) = World(seed: (ulong)run + 10);
            for (var i = 0; i < 6; i++)
            {
                engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, random.Next(64), Math.Pow(10, random.Next(-9, 2))));
            }

            for (var tick = 0; tick < 40; tick++)
            {
                engine.Advance(1);
                Assert.True(workspace.Totals.MaxElementErrorKg < 1e-9);
                foreach (var population in engine.State.Populations)
                {
                    Assert.True(population.BiomassKg >= 0.0 && population.Abundance >= 1.0);
                    Assert.InRange(population.Viability, 0.0, 1.0);
                }
            }
        }
    }

    [Fact]
    public void AMissingT05ParameterIsReported()
    {
        var organism = Catalog().Organism(Phototroph);
        var trimmed = organism with
        {
            Kinetics = organism.Kinetics with
            {
                Extra = organism.Kinetics.Extra
                    .Where(pair => pair.Key != "carrying_capacity")
                    .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal),
            },
        };

        var error = Assert.Throws<KeyNotFoundException>(() => PopulationParameters.From(trimmed));
        Assert.Contains("carrying_capacity", error.Message, StringComparison.Ordinal);
    }

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
        var configuration = new SimulationConfiguration(8, 8, 1.0, seed, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell, true);
        var engine = new SimulationEngine(
            state,
            configuration,
            RngStreams.Create(seed),
            BiologyPipeline.Systems(workspace),
            BiologyPipeline.Handlers(workspace),
            SimulationPipeline.BiologyVersion);
        return (engine, workspace);
    }
}
