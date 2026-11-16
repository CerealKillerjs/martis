namespace Martis.Contracts.Commands;

/// <summary>
/// Inoculates a cell with an organism from the catalog (TDD §5.1 "introducir organismo").
/// Data only: the handler lives in <c>Martis.Simulation</c>. The biomass is an external
/// input (a laboratory culture) and is recorded as such in the mass balance.
/// <see cref="GeneOverrides"/> is the player's design (T09): genes of the base genome
/// replaced by these expressions, each in [0, 1]. Null keeps the base genome.
/// </summary>
public sealed record IntroducePopulationCommand(
    string OrganismId,
    int CellIndex,
    double BiomassKg,
    IReadOnlyDictionary<string, double>? GeneOverrides = null) : ISimulationCommand
{
    public const string CommandKind = "introduce-population";

    public string Kind => CommandKind;
}
