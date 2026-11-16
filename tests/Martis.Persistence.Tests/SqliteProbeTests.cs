using Martis.Persistence.SQLite;

namespace Martis.Persistence.Tests;

/// <summary>
/// Smoke test for the persistence project. Save format and migrations are T07.
/// </summary>
public sealed class SqliteProbeTests
{
    [Fact]
    public void NamesTheAllowedDependencies()
    {
        Assert.Equal("Martis.Persistence", SqliteProbe.PersistenceAssemblyName);
        Assert.Equal("Martis.Domain", SqliteProbe.DomainAssemblyName);
        Assert.Equal("Martis.Contracts", SqliteProbe.ContractsAssemblyName);
        Assert.StartsWith("Microsoft.Data.Sqlite", SqliteProbe.SqliteAssemblyName, StringComparison.Ordinal);
    }

    [Fact]
    public void InMemoryDatabaseReturnsOne()
    {
        Assert.Equal(1L, SqliteProbe.SelectOneFromMemory());
    }
}
