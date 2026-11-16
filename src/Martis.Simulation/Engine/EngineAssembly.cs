namespace Martis.Simulation.Engine;

/// <summary>
/// Names this assembly and records the project references the engine is allowed to keep:
/// <c>Martis.Domain</c> and <c>Martis.Contracts</c>.
/// Contracts stay data-only. Command handlers that change simulation state live here,
/// not in Contracts.
/// Later milestones must not call System.Random, GetHashCode(), Parallel.*, AsParallel(),
/// Task.Run, DateTime.Now, DateTime.UtcNow or Guid.NewGuid() from this project.
/// </summary>
public static class EngineAssembly
{
    /// <summary>Assembly name of the simulation engine.</summary>
    public static string Name =>
        typeof(EngineAssembly).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Product version of the engine (<c>Version</c> in <c>Directory.Build.props</c>),
    /// without a <c>+</c> source-revision suffix. Stored beside snapshots and run metadata.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    /// <summary>
    /// Assembly name of <c>Martis.Domain</c>.
    /// Reading a domain type forces the compiler to emit the project reference.
    /// An unused project reference would be missing from <c>GetReferencedAssemblies</c>.
    /// </summary>
    public static string DomainAssemblyName =>
        typeof(Martis.Domain.Common.RegionId).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Assembly name of <c>Martis.Contracts</c>.
    /// Reading a contracts type forces the compiler to emit the approved project reference.
    /// </summary>
    public static string ContractsAssemblyName =>
        typeof(Martis.Contracts.ContractsAssembly).Assembly.GetName().Name ?? string.Empty;

    private static string ReadVersion()
    {
        var informational = typeof(EngineAssembly).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(informational))
        {
            return "0.0.0";
        }

        var plus = informational.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? informational : informational[..plus];
    }
}
