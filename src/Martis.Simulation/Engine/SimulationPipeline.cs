namespace Martis.Simulation.Engine;

/// <summary>
/// Fixed v0 order of TDD §5.4 (H-28). The id is <see cref="Version"/>.
/// Reordering these steps is a new pipeline version, not a silent edit.
/// <see cref="CreateStubs"/> leaves every step except
/// <see cref="SimulationSteps.ApplyCommands"/> as a stub.
/// <see cref="CreateV0"/> runs <see cref="EnvironmentSystem"/> in the environment step.
/// The other steps stay stubs: diurnal and seasonal cycles are not in this version.
/// </summary>
public static class SimulationPipeline
{
    public const string Version = "pipeline-v0";

    /// <summary>
    /// Pipeline with biology: T04 metabolism (ADR-012), T05 mortality, reproduction and
    /// recycling (ADR-013) and T06 mutation, drift and variant management (ADR-014).
    /// The step order is still §5.4; the id names the model.
    /// </summary>
    public const string BiologyVersion = "pipeline-v3";

    public static IReadOnlyList<string> StepNames { get; } =
    [
        SimulationSteps.ApplyCommands,
        SimulationSteps.UpdateEnvironment,
        SimulationSteps.CalculateResourceAvailability,
        SimulationSteps.CalculateMetabolism,
        SimulationSteps.AllocateSharedResources,
        SimulationSteps.UpdateBiomassAndEnergy,
        SimulationSteps.CalculateMortality,
        SimulationSteps.GenerateReproduction,
        SimulationSteps.ApplyMutations,
        SimulationSteps.ApplySelectionAndDrift,
        SimulationSteps.UpdateEcology,
        SimulationSteps.RecycleDeadBiomass,
        SimulationSteps.ValidateConservation,
        SimulationSteps.UpdateStatistics,
        SimulationSteps.PublishSnapshot,
    ];

    public static IReadOnlyList<ISimulationSystem> CreateStubs()
    {
        var systems = new ISimulationSystem[StepNames.Count];
        for (var i = 0; i < StepNames.Count; i++)
        {
            var name = StepNames[i];
            systems[i] = name == SimulationSteps.ApplyCommands
                ? new ApplyCommandsSystem()
                : new StubSimulationSystem(name);
        }

        return systems;
    }

    /// <summary>
    /// Pipeline used by a loaded scenario. The environment step keeps temperature
    /// constant (D = 0 and S = 0). It does not draw a random number.
    /// </summary>
    public static IReadOnlyList<ISimulationSystem> CreateV0()
    {
        var systems = new ISimulationSystem[StepNames.Count];
        for (var i = 0; i < StepNames.Count; i++)
        {
            var name = StepNames[i];
            systems[i] = name switch
            {
                SimulationSteps.ApplyCommands => new ApplyCommandsSystem(),
                SimulationSteps.UpdateEnvironment => new EnvironmentSystem(),
                _ => new StubSimulationSystem(name),
            };
        }

        return systems;
    }
}

/// <summary>Builds the biology pipeline; later milestones extend <see cref="BiologyPipeline.Systems"/>.</summary>
public static class BiologyPipeline
{
    /// <summary>
    /// Systems of <see cref="SimulationPipeline.BiologyVersion"/> in the §5.4 order. Steps not yet
    /// owned by a milestone stay stubs.
    /// </summary>
    public static IReadOnlyList<ISimulationSystem> Systems(Biology.MetabolismWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var systems = new ISimulationSystem[SimulationPipeline.StepNames.Count];
        for (var i = 0; i < systems.Length; i++)
        {
            var name = SimulationPipeline.StepNames[i];
            systems[i] = name switch
            {
                SimulationSteps.ApplyCommands => new ApplyCommandsSystem(),
                SimulationSteps.UpdateEnvironment => new EnvironmentSystem(),
                SimulationSteps.CalculateResourceAvailability => new Biology.ResourceAvailabilitySystem(workspace),
                SimulationSteps.CalculateMetabolism => new Biology.MetabolismSystem(workspace),
                SimulationSteps.AllocateSharedResources => new Biology.AllocationSystem(workspace),
                SimulationSteps.UpdateBiomassAndEnergy => new Biology.BiomassEnergySystem(workspace),
                SimulationSteps.CalculateMortality => new Biology.MortalitySystem(workspace),
                SimulationSteps.GenerateReproduction => new Biology.ReproductionSystem(workspace),
                SimulationSteps.ApplyMutations => new Biology.MutationSystem(workspace),
                SimulationSteps.ApplySelectionAndDrift => new Biology.SelectionDriftSystem(workspace),
                SimulationSteps.UpdateEcology => new Biology.TransitionSystem(workspace),
                SimulationSteps.RecycleDeadBiomass => new Biology.RecycleSystem(workspace),
                SimulationSteps.ValidateConservation => new Biology.ConservationSystem(workspace),
                _ => new StubSimulationSystem(name),
            };
        }

        return systems;
    }

    public static IReadOnlyList<Commands.ICommandHandler> Handlers(Biology.MetabolismWorkspace workspace)
        => [new Biology.IntroducePopulationHandler(workspace), new Terraforming.SetCellTemperatureHandler()];
}

/// <summary>Ordinal ids for <see cref="SimulationPipeline"/>. The Spanish names live in the data dictionary.</summary>
public static class SimulationSteps
{
    public const string ApplyCommands = "apply-commands";
    public const string UpdateEnvironment = "update-environment";
    public const string CalculateResourceAvailability = "calculate-resource-availability";
    public const string CalculateMetabolism = "calculate-metabolism";
    public const string AllocateSharedResources = "allocate-shared-resources";
    public const string UpdateBiomassAndEnergy = "update-biomass-and-energy";
    public const string CalculateMortality = "calculate-mortality";
    public const string GenerateReproduction = "generate-reproduction";
    public const string ApplyMutations = "apply-mutations";
    public const string ApplySelectionAndDrift = "apply-selection-and-drift";
    public const string UpdateEcology = "update-ecology";
    public const string RecycleDeadBiomass = "recycle-dead-biomass";
    public const string ValidateConservation = "validate-conservation";
    public const string UpdateStatistics = "update-statistics";
    public const string PublishSnapshot = "publish-snapshot";
}

/// <summary>
/// Drains the command queue. With an empty queue it does not change the state.
/// </summary>
public sealed class ApplyCommandsSystem : ISimulationSystem
{
    public string Name => SimulationSteps.ApplyCommands;

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.DrainCommands();
    }
}

/// <summary>
/// Registered step with no model behaviour. Later milestones replace the stub
/// that owns their equations. This one does not draw random numbers.
/// </summary>
public sealed class StubSimulationSystem : ISimulationSystem
{
    public StubSimulationSystem(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A stub step needs a name.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }

    public void Execute(SimulationContext context, double deltaTimeSols)
    {
        ArgumentNullException.ThrowIfNull(context);
    }
}
