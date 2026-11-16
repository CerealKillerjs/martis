namespace Martis.Simulation.Engine;

/// <summary>
/// Names this assembly and records its only allowed project reference.
/// The tick pipeline, systems and the deterministic generator are later milestones.
/// T01 does not simulate. Later milestones must not call System.Random,
/// GetHashCode(), Parallel.*, AsParallel(), Task.Run, DateTime.Now,
/// DateTime.UtcNow or Guid.NewGuid() from this project.
/// </summary>
public static class EngineAssembly
{
    /// <summary>Assembly name of the simulation engine.</summary>
    public static string Name =>
        typeof(EngineAssembly).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Assembly name of <c>Martis.Domain</c>.
    /// Reading a domain type forces the compiler to emit the project reference.
    /// An unused project reference would be missing from <c>GetReferencedAssemblies</c>.
    /// </summary>
    public static string DomainAssemblyName =>
        typeof(Martis.Domain.Common.RegionId).Assembly.GetName().Name ?? string.Empty;
}
