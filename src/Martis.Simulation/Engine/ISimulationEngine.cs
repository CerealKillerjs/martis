using Martis.Contracts.Commands;
using Martis.Domain.World;
using Martis.Simulation.Random;

namespace Martis.Simulation.Engine;

/// <summary>
/// Tick driver (TDD §5.1). Snapshot create/restore is not on this contract yet:
/// H-03 is still open and the full snapshot belongs to a later task.
/// Commands are data. <see cref="ApplyCommand"/> only enqueues them.
/// </summary>
public interface ISimulationEngine
{
    long CurrentTick { get; }

    SimulationState State { get; }

    SimulationConfiguration Configuration { get; }

    RngStreams Random { get; }

    /// <summary>Step ids in execution order.</summary>
    IReadOnlyList<string> SystemNames { get; }

    /// <summary>
    /// Validates <paramref name="configuration"/> and keeps it when it matches the
    /// current grid. Does not rebuild the world and does not reseed the streams.
    /// </summary>
    void Initialize(SimulationConfiguration configuration);

    /// <summary>Runs <paramref name="steps"/> ticks. Zero does nothing. Negative throws.</summary>
    void Advance(int steps);

    /// <summary>Queues a command for the first step of a later tick.</summary>
    void ApplyCommand(ISimulationCommand command);
}
