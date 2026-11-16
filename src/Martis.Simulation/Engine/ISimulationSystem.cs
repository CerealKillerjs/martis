namespace Martis.Simulation.Engine;

/// <summary>
/// One step of a tick (TDD §5.2). The time argument is the configuration step in
/// sols, not in terrestrial days (ADR-011).
/// </summary>
public interface ISimulationSystem
{
    /// <summary>Stable step id. The pipeline compares it ordinally.</summary>
    string Name { get; }

    /// <summary>Runs this step. A v0 stub does not change the state.</summary>
    void Execute(SimulationContext context, double deltaTimeSols);
}
