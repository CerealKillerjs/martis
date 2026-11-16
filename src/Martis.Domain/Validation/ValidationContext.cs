namespace Martis.Domain.Validation;

/// <summary>
/// Whether <see cref="SimulationValidator"/> is looking at an empty world or at a loaded scenario.
/// Unset temperature, unset pressure and a missing <c>Scope</c> are legal on a world
/// and violations once a scenario has been loaded.
/// </summary>
public enum ValidationContext
{
    World,
    LoadedScenario,
}
