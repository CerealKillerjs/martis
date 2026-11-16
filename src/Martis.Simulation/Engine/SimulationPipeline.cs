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
