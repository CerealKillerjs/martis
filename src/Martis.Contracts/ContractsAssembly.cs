namespace Martis.Contracts;

/// <summary>
/// Names this assembly and records its reference to the domain.
/// Command, snapshot and event types are intentionally absent. An
/// <c>Apply</c> method that took a state type owned by the simulation
/// would create the cycle described as H-02, so T01 does not declare it.
/// </summary>
public static class ContractsAssembly
{
    /// <summary>Assembly name of <c>Martis.Contracts</c>.</summary>
    public static string Name =>
        typeof(ContractsAssembly).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Assembly name of <c>Martis.Domain</c>.
    /// Reading a domain type forces the compiler to emit the project reference.
    /// </summary>
    public static string DomainAssemblyName =>
        typeof(Martis.Domain.Common.GeneId).Assembly.GetName().Name ?? string.Empty;
}
