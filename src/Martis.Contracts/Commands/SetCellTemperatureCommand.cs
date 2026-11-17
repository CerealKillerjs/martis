namespace Martis.Contracts.Commands;

/// <summary>
/// Sets the temperature of one cell (Fase E, regional terraforming, ADR-033). Data only: the
/// handler lives in <c>Martis.Simulation</c>. It is the only way an intervention changes the
/// climate, so the change happens at a tick boundary and every replay sees the same sequence.
/// </summary>
public sealed record SetCellTemperatureCommand(int CellIndex, double TemperatureKelvin) : ISimulationCommand
{
    public const string CommandKind = "set-cell-temperature";

    public string Kind => CommandKind;
}
