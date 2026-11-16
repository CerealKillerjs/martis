namespace Martis.Contracts.Commands;

/// <summary>
/// Inoculates a cell with an organism from the catalog (TDD §5.1 "introducir organismo").
/// Data only: the handler lives in <c>Martis.Simulation</c>. The biomass is an external
/// input (a laboratory culture) and is recorded as such in the mass balance.
/// </summary>
public sealed record IntroducePopulationCommand(string OrganismId, int CellIndex, double BiomassKg) : ISimulationCommand
{
    public const string CommandKind = "introduce-population";

    public string Kind => CommandKind;
}
