using Martis.Contracts.Commands;
using Martis.Domain.World;
using Martis.Simulation.Commands;
using Martis.Simulation.Random;

namespace Martis.Simulation.Engine;

/// <summary>
/// Per-tick view handed to every system (TDD §5.3, ADR-003).
/// The random source is the five-stream bundle, not a single generator.
/// The engine builds one context per tick and discards it afterwards.
/// </summary>
public sealed class SimulationContext
{
    private readonly Queue<ISimulationCommand> _pending;
    private readonly IReadOnlyList<ICommandHandler> _handlers;

    internal SimulationContext(
        SimulationState state,
        RngStreams random,
        SimulationConfiguration configuration,
        double deltaTimeSols,
        Queue<ISimulationCommand> pending,
        IReadOnlyList<ICommandHandler> handlers)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(pending);
        ArgumentNullException.ThrowIfNull(handlers);
        State = state;
        Random = random;
        Configuration = configuration;
        DeltaTimeSols = deltaTimeSols;
        _pending = pending;
        _handlers = handlers;
    }

    public SimulationState State { get; }

    public RngStreams Random { get; }

    public SimulationConfiguration Configuration { get; }

    /// <summary>Same value the engine passes to <see cref="ISimulationSystem.Execute"/>.</summary>
    public double DeltaTimeSols { get; }

    /// <summary>
    /// Applies queued commands in arrival order. Unknown kinds throw and leave
    /// the remaining commands queued. The tick counter is not touched here.
    /// </summary>
    internal void DrainCommands()
    {
        while (_pending.Count > 0)
        {
            var command = _pending.Dequeue();
            FindHandler(command.Kind).Handle(command, State);
        }
    }

    private ICommandHandler FindHandler(string kind)
    {
        foreach (var handler in _handlers)
        {
            if (string.Equals(handler.Kind, kind, StringComparison.Ordinal))
            {
                return handler;
            }
        }

        throw new InvalidOperationException($"No handler is registered for command kind '{kind}'.");
    }
}
