using Martis.Contracts.Commands;
using Martis.Domain.World;

namespace Martis.Simulation.Commands;

/// <summary>
/// Applies one command kind to <see cref="SimulationState"/>.
/// Handlers live here, not in Contracts (ADR-000).
/// </summary>
public interface ICommandHandler
{
    /// <summary>Matches <see cref="ISimulationCommand.Kind"/> with an ordinal comparison.</summary>
    string Kind { get; }

    /// <summary>Mutates <paramref name="state"/>. Must not draw from a stream it does not own.</summary>
    void Handle(ISimulationCommand command, SimulationState state);
}
