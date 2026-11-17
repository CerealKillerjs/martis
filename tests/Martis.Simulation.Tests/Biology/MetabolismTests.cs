using Martis.Contracts.Commands;
using Martis.Domain.Chemistry;
using Martis.Domain.Environment;
using Martis.Domain.Validation;
using Martis.Domain.World;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;
using Martis.Simulation.Tests.Scenarios;

namespace Martis.Simulation.Tests.Biology;

/// <summary>T04: reactions, shared allocation and the VAL-006/007/008 account.</summary>
[Trait("Category", "Metabolism")]
public sealed class MetabolismTests
{
    private const string Phototroph = "phototroph_a01";

    [Fact]
    public void EveryPublishedReactionIsElementBalanced()
    {
        var catalog = Catalog();
        var formulas = catalog.Species.ToDictionary(species => species.Id, species => species.Formula, StringComparer.Ordinal);

        Assert.NotEmpty(catalog.Reactions);
        foreach (var reaction in catalog.Reactions)
        {
            Assert.Empty(ElementalBalance.Residuals(reaction.Definition, formulas));
            var inKg = reaction.Inputs.Sum(term => term.KgPerExtent);
            var outKg = reaction.Outputs.Sum(term => term.KgPerExtent);
            Assert.Equal(inKg, outKg, 9);
        }
    }

    [Fact]
    public void AnUnbalancedReactionFailsToLoad()
    {
        var root = Repo.Root();
        var metabolism = File.ReadAllText(Path.Combine(root, "data", "reactions", BiologyCatalog.MetabolismFile))
            .Replace("\"Oxygen\": 106", "\"Oxygen\": 105", StringComparison.Ordinal);
        var organisms = File.ReadAllText(Path.Combine(root, "data", "organisms", BiologyCatalog.OrganismsFile));

        var error = Assert.Throws<InvalidDataException>(() => BiologyCatalog.Load(metabolism, organisms));
        Assert.Contains("VAL-007", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BiomassIsRedfieldOrganicMatter()
    {
        var biomass = Catalog().Biomass;
        Assert.Equal(106, biomass.Formula.CountOf("C"));
        Assert.Equal(16, biomass.Formula.CountOf("N"));
        Assert.Equal(1, biomass.Formula.CountOf("P"));
    }

    [Fact]
    public void APhototrophGrowsFromCarbonDioxideAndReleasesOxygen()
    {
        var (engine, _) = World(seed: 1);
        var before = Copy(engine.State.Resources[9]);
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 9, 1e-3));

        engine.Advance(20);

        var population = Assert.Single(engine.State.Populations);
        var after = engine.State.Resources[9];
        Assert.True(population.BiomassKg > 1e-3);
        Assert.True(after.CarbonDioxide < before.CarbonDioxide);
        Assert.True(after.Oxygen > before.Oxygen);
        Assert.True(population.EnergyReserveJ > 0);
        Assert.Equal(population.BiomassKg / Catalog().Organism(Phototroph).Kinetics.IndividualMassKg, population.Abundance, 6);
    }

    [Fact]
    public void ElementsAreConservedInEveryCellEveryTick()
    {
        var (engine, workspace) = World(seed: 2);
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 3, 0.01));
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 3, 0.02));

        for (var tick = 0; tick < 100; tick++)
        {
            engine.Advance(1);
            Assert.True(workspace.Totals.MaxElementErrorKg < 1e-12, $"tick {tick}: {workspace.Totals.MaxElementErrorKg}");
        }
    }

    [Fact]
    public void WithoutPhosphorusNothingGrows()
    {
        var (engine, workspace) = World(seed: 3);
        engine.State.Resources[0].Phosphorus = 0;
        engine.State.MassLedgers[0].RestoreRecorded(CellMassLedger.Read(engine.State.Resources[0], engine.State.Environment[0].WaterIce));
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 0, 1e-3));

        engine.Advance(1);

        // Recycled necromass returns phosphate later; on the first tick there is none.
        Assert.Equal(0.0, workspace.Totals.CarbonFixedMol);
        Assert.True(Assert.Single(engine.State.Populations).BiomassKg < 1e-3);
    }

    [Fact]
    public void ScarceSlotsAreSharedInProportionAndNeverOverdrawn()
    {
        var (engine, workspace) = World(seed: 4);
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 1, 5000));
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 1, 15000));

        engine.Advance(1);

        var fluxes = workspace.Fluxes;
        Assert.Equal(2, fluxes.Count);
        Assert.True(fluxes[0].AllocatedExtent < fluxes[0].PlannedExtent, "the slot should be scarce");
        Assert.Equal(fluxes[0].AllocatedExtent / fluxes[0].PlannedExtent, fluxes[1].AllocatedExtent / fluxes[1].PlannedExtent, 12);
        foreach (var quantity in engine.State.Resources[1].Quantities)
        {
            Assert.True(quantity.Value >= 0.0, quantity.Name);
        }
    }

    [Fact]
    public void PopulationOrderDoesNotChangeTheShares()
    {
        var a = Shares(5000, 15000);
        var b = Shares(15000, 5000);

        Assert.Equal(a.Small, b.Small, 12);
        Assert.Equal(a.Large, b.Large, 12);
    }

    [Fact]
    public void CreatedMassIsCaughtAsVal007AndTheTickRollsBack()
    {
        var catalog = Catalog();
        var workspace = new MetabolismWorkspace(catalog);
        var systems = BiologyPipeline.Systems(workspace).ToList();
        var index = systems.FindIndex(system => system.Name == SimulationSteps.UpdateBiomassAndEnergy);
        systems.Insert(index + 1, new MassFromNothing());
        var engine = Engine(systems, workspace, seed: 5);
        engine.Advance(3);

        var error = Assert.Throws<SimulationInvariantException>(() => engine.Advance(1));

        Assert.Contains(error.Violations, violation => violation.InvariantId == InvariantIds.ElementalMassConserved && violation.CellIndex == 7);
        Assert.Equal(SimulationSteps.ValidateConservation, engine.FailedStep);
        Assert.Equal(3, engine.CurrentTick);
    }

    [Fact]
    public void OverdrawnSlotIsCaughtAsVal006()
    {
        var catalog = Catalog();
        var workspace = new MetabolismWorkspace(catalog);
        var state = Fixture();
        workspace.Open(state);
        workspace.Consumed[ResourceColumns.Slot("Water")] = workspace.StartPool[ResourceColumns.Slot("Water")] * 2;

        var error = Assert.Throws<SimulationInvariantException>(() =>
            new ConservationSystem(workspace).Execute(Context(state), 1.0));

        Assert.Contains(error.Violations, violation => violation.InvariantId == InvariantIds.ConsumptionWithinAvailability && violation.Quantity == "Water");
    }

    [Fact]
    public void SpendingMoreEnergyThanHeldIsCaughtAsVal008()
    {
        var (engine, workspace) = World(seed: 6);
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 0, 1e-3));
        engine.Advance(1);
        workspace.Open(engine.State);
        workspace.Spend(engine.State.Populations[0].Id.Value, 1e30);

        var error = Assert.Throws<SimulationInvariantException>(() =>
            new ConservationSystem(workspace).Execute(Context(engine.State), 1.0));

        Assert.Contains(error.Violations, violation => violation.InvariantId == InvariantIds.EnergyWithinAvailability);
    }

    [Fact]
    public void IntroductionRejectsUnknownOrganismsAndBadBiomass()
    {
        var (engine, _) = World(seed: 7);
        engine.ApplyCommand(new IntroducePopulationCommand("nobody", 0, 1));
        Assert.Throws<KeyNotFoundException>(() => engine.Advance(1));

        var (second, _) = World(seed: 7);
        second.ApplyCommand(new IntroducePopulationCommand(Phototroph, 0, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => second.Advance(1));
    }

    [Fact]
    public void SnapshotRestoreContinuesTheBiologyRunIdentically()
    {
        var (original, _) = World(seed: 8);
        original.ApplyCommand(new IntroducePopulationCommand(Phototroph, 2, 0.01));
        original.Advance(10);
        var bytes = StateSnapshotJson.Write(StateSnapshot.Capture(original, null));
        var (restored, _) = World(seed: 8);
        StateSnapshotJson.Read(bytes).Restore(restored);

        original.Advance(40);
        restored.Advance(40);

        Assert.Equal(
            StateSnapshotJson.Write(StateSnapshot.Capture(original, null)),
            StateSnapshotJson.Write(StateSnapshot.Capture(restored, null)));
        Assert.Equal(SimulationPipeline.BiologyVersion, StateSnapshot.Capture(original, null).PipelineVersion);
    }

    private static (double Small, double Large) Shares(double first, double second)
    {
        var (engine, workspace) = World(seed: 9);
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 1, first));
        engine.ApplyCommand(new IntroducePopulationCommand(Phototroph, 1, second));
        engine.Advance(1);
        // Ids follow introduction order: id 1 got `first`.
        var smallId = first < second ? 1L : 2L;
        var small = workspace.Fluxes.Single(flux => flux.PopulationId == smallId);
        var large = workspace.Fluxes.Single(flux => flux.PopulationId != smallId);
        return (small.AllocatedExtent, large.AllocatedExtent);
    }

    private static BiologyCatalog Catalog() => BiologyCatalog.LoadPublished(Repo.Root());

    private static SimulationState Fixture()
    {
        var scenario = MarsBaselineScenario.Load(ScenarioFixtures.Science, ScenarioFixtures.Deposits);
        var state = new SimulationState(8, 8);
        scenario.Apply(state);
        foreach (var environment in state.Environment)
        {
            // The fixture's 0.5 Sv/sol is a stress value; biology tests use a Mars-like 0.5 mSv/sol.
            environment.IonizingEquivalentDoseSvPerSol = 5e-4;
        }

        return state;
    }

    private static (SimulationEngine Engine, MetabolismWorkspace Workspace) World(ulong seed)
    {
        var workspace = new MetabolismWorkspace(Catalog());
        return (Engine(BiologyPipeline.Systems(workspace), workspace, seed), workspace);
    }

    private static SimulationEngine Engine(IReadOnlyList<ISimulationSystem> systems, MetabolismWorkspace workspace, ulong seed)
    {
        var state = Fixture();
        var configuration = new SimulationConfiguration(8, 8, 1.0, seed, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell, true);
        return new SimulationEngine(state, configuration, RngStreams.Create(seed), systems, BiologyPipeline.Handlers(workspace), SimulationPipeline.BiologyVersion);
    }

    private static SimulationContext Context(SimulationState state)
    {
        var configuration = new SimulationConfiguration(state.Width, state.Height, 1.0, 1, SimulationConfiguration.DefaultMaxVariantsPerSpeciesPerCell, true);
        var engine = new SimulationEngine(state, configuration, RngStreams.Create(1), [new ContextGrabber()]);
        engine.Advance(1);
        state.Tick--;
        return ContextGrabber.Last!;
    }

    private static ResourcePool Copy(ResourcePool pool)
    {
        var copy = new ResourcePool();
        ResourceColumns.WriteDeposits(copy, pool.Quantities.Select(quantity => quantity.Value).ToArray());
        return copy;
    }

    /// <summary>Test-only fault: adds 1 kg of water to cell 7 without a source.</summary>
    private sealed class MassFromNothing : ISimulationSystem
    {
        public string Name => "test-mass-from-nothing";

        public void Execute(SimulationContext context, double deltaTimeSols)
        {
            if (context.State.Tick >= 3)
            {
                context.State.Resources[7].Water += 1.0;
                context.State.WaterBudgets[7].RecordedTotalKg += 1.0;
            }
        }
    }

    private sealed class ContextGrabber : ISimulationSystem
    {
        public static SimulationContext? Last { get; private set; }

        public string Name => "test-context";

        public void Execute(SimulationContext context, double deltaTimeSols) => Last = context;
    }
}
