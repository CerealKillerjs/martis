using Martis.Simulation.Engine;

namespace Martis.Simulation.Tests;

/// <summary>
/// Smoke test for the simulation project. The engine itself arrives in a later milestone.
/// </summary>
public sealed class EngineAssemblyTests
{
    [Fact]
    public void NamesTheEngineAndTheDomainAssembly()
    {
        Assert.Equal("Martis.Simulation", EngineAssembly.Name);
        Assert.Equal("Martis.Domain", EngineAssembly.DomainAssemblyName);
    }
}
