using System.Globalization;
using Martis.Contracts;
using Martis.Domain.Common;
using Microsoft.Data.Sqlite;

namespace Martis.Persistence.SQLite;

/// <summary>
/// Proves that persistence can open the pinned SQLite package.
/// Schemas, migrations and save games belong to milestone T07.
/// </summary>
public static class SqliteProbe
{
    /// <summary>Assembly name of <c>Martis.Persistence</c>.</summary>
    public static string PersistenceAssemblyName =>
        typeof(SqliteProbe).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Assembly name of <c>Martis.Domain</c>.
    /// Reading a domain type forces the compiler to emit the project reference.
    /// </summary>
    public static string DomainAssemblyName =>
        typeof(RegionId).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Assembly name of <c>Martis.Contracts</c>.
    /// Reading a contracts type forces the compiler to emit the project reference.
    /// </summary>
    public static string ContractsAssemblyName =>
        typeof(ContractsAssembly).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Assembly name of the SQLite ADO.NET provider.
    /// Reading the connection type forces the compiler to emit the package reference.
    /// </summary>
    public static string SqliteAssemblyName =>
        typeof(SqliteConnection).Assembly.GetName().Name ?? string.Empty;

    /// <summary>
    /// Opens an in-memory database and returns the result of <c>SELECT 1</c>.
    /// The connection is not a save file and holds no simulation state.
    /// </summary>
    /// <returns>The integer <c>1</c> returned by SQLite.</returns>
    public static long SelectOneFromMemory()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1;";
        var scalar = command.ExecuteScalar();
        if (scalar is null)
        {
            throw new InvalidOperationException("SQLite returned no value for SELECT 1.");
        }

        return Convert.ToInt64(scalar, CultureInfo.InvariantCulture);
    }
}
