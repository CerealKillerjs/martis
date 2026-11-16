namespace Martis.Contracts.Commands;

/// <summary>
/// Data describing a change requested by the host (ADR-000).
/// There is no Apply method on this type. A handler in Martis.Simulation applies
/// the command to simulation state. The engine queues the command and the first
/// step of the next tick drains the queue in arrival order (TDD §5.4).
/// </summary>
public interface ISimulationCommand
{
    /// <summary>Handler key, compared ordinally. It is not a hash.</summary>
    string Kind { get; }
}
