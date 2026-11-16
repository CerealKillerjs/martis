using Martis.Contracts.Commands;
using Martis.Domain.World;
using Martis.Simulation.Commands;
using Martis.Simulation.Random;

namespace Martis.Simulation.Engine;

/// <summary>
/// Runs a fixed list of systems once per tick (TDD §5.5, ADR-003, ADR-000).
/// The tick counter advances only after every system returns. Systems must not
/// change <see cref="SimulationState.Tick"/> themselves.
/// <see cref="ApplyCommand"/> does not touch the state. The apply-commands step
/// of the following tick does, in queue order.
/// </summary>
public sealed class SimulationEngine : ISimulationEngine
{
    private readonly IReadOnlyList<ISimulationSystem> _systems;
    private readonly IReadOnlyList<ICommandHandler> _handlers;
    private readonly Queue<ISimulationCommand> _commands = new();
    private readonly RngStreams _random;
    private SimulationConfiguration _configuration;

    public SimulationEngine(
        SimulationState state,
        SimulationConfiguration configuration,
        RngStreams random,
        IReadOnlyList<ISimulationSystem> systems,
        IReadOnlyList<ICommandHandler>? handlers = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(random);
        ArgumentNullException.ThrowIfNull(systems);
        configuration.Validate();
        if (state.Width != configuration.MapWidth || state.Height != configuration.MapHeight)
        {
            throw new InvalidOperationException("The state dimensions must match the configuration.");
        }

        State = state;
        _configuration = configuration;
        _random = random;
        _systems = CopySystems(systems);
        _handlers = CopyHandlers(handlers ?? []);
        SystemNames = _systems.Select(system => system.Name).ToArray();
    }

    public SimulationState State { get; }

    public long CurrentTick => State.Tick;

    public SimulationConfiguration Configuration => _configuration;

    public RngStreams Random => _random;

    public IReadOnlyList<string> SystemNames { get; }

    public void Initialize(SimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        configuration.Validate();
        if (configuration.MapWidth != State.Width || configuration.MapHeight != State.Height)
        {
            throw new InvalidOperationException(
                "Initialize cannot resize the world. Build a new state for a different grid.");
        }

        _configuration = configuration;
    }

    public void Advance(int steps)
    {
        if (steps < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(steps),
                steps,
                "The number of steps must be greater than or equal to 0.");
        }

        for (var step = 0; step < steps; step++)
        {
            var context = new SimulationContext(
                State,
                _random,
                _configuration,
                _configuration.DeltaTimeSols,
                _commands,
                _handlers);
            foreach (var system in _systems)
            {
                system.Execute(context, _configuration.DeltaTimeSols);
            }

            checked
            {
                State.Tick++;
            }
        }
    }

    public void ApplyCommand(ISimulationCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.IsNullOrWhiteSpace(command.Kind))
        {
            throw new ArgumentException("A command needs a kind.", nameof(command));
        }

        _commands.Enqueue(command);
    }

    private static ISimulationSystem[] CopySystems(IReadOnlyList<ISimulationSystem> systems)
    {
        var copy = new ISimulationSystem[systems.Count];
        for (var i = 0; i < systems.Count; i++)
        {
            copy[i] = systems[i] ?? throw new ArgumentException("The system list contains a null entry.", nameof(systems));
            if (string.IsNullOrWhiteSpace(copy[i].Name))
            {
                throw new ArgumentException("A system needs a name.", nameof(systems));
            }
        }

        return copy;
    }

    private static ICommandHandler[] CopyHandlers(IReadOnlyList<ICommandHandler> handlers)
    {
        var copy = new ICommandHandler[handlers.Count];
        var seen = new List<string>(handlers.Count);
        for (var i = 0; i < handlers.Count; i++)
        {
            var handler = handlers[i] ?? throw new ArgumentException("The handler list contains a null entry.", nameof(handlers));
            if (string.IsNullOrWhiteSpace(handler.Kind))
            {
                throw new ArgumentException("A handler needs a kind.", nameof(handlers));
            }

            if (seen.Contains(handler.Kind, StringComparer.Ordinal))
            {
                throw new InvalidOperationException($"Two handlers are registered for '{handler.Kind}'.");
            }

            seen.Add(handler.Kind);
            copy[i] = handler;
        }

        return copy;
    }
}
