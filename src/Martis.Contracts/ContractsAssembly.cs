namespace Martis.Contracts;

/// <summary>
/// Names this assembly and records its only project reference, <c>Martis.Domain</c>.
/// Contracts hold data: commands as records without an apply method, snapshots and events.
/// Handlers that apply a command to simulation state live in <c>Martis.Simulation</c>.
/// The command marker lives in <c>Commands</c>. Handlers do not.
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
