using System.Globalization;
using System.Security.Cryptography;
using Martis.Persistence.Migrations;
using Microsoft.Data.Sqlite;

namespace Martis.Persistence.SQLite;

/// <summary>
/// H-14: SQLite INTEGER is a signed 64-bit value. A <see cref="ulong"/> seed is stored by
/// its bits (two's complement), so every seed round-trips, including those above
/// <see cref="long.MaxValue"/>.
/// </summary>
public static class SeedCodec
{
    public static long ToStorage(ulong seed) => unchecked((long)seed);

    public static ulong FromStorage(long stored) => unchecked((ulong)stored);
}

/// <summary>Identity and versions of one save game (TDD §9.2 <c>SaveGame</c>).</summary>
public sealed record SaveGameRecord(
    string Id,
    string Name,
    string EngineVersion,
    int DataVersion,
    ulong WorldSeed,
    long CurrentTick,
    string ScenarioId,
    string PipelineVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>One stored snapshot row. <see cref="Payload"/> is opaque to persistence.</summary>
public sealed record StoredSnapshot(long Id, string SaveGameId, long Tick, string StateSchema, string Checksum, byte[] Payload);

/// <summary>One stored event row (TDD §9.2 <c>SimulationEvent</c>).</summary>
public sealed record StoredEvent(long Id, long Tick, string EventType, string Payload);

/// <summary>
/// Result of <see cref="SaveGameStore.LoadLatestValid"/>: the newest snapshot that passed
/// every check, and the ticks of the newer ones that were rejected, newest first.
/// </summary>
public sealed record SnapshotRecovery(StoredSnapshot? Snapshot, IReadOnlyList<long> RejectedTicks);

/// <summary>
/// SQLite save store (T07, TDD §9). Opening a file applies the pending migrations in one
/// transaction. Snapshots are written with the SHA-256 of their payload; loading walks
/// back from the newest snapshot until one has a matching checksum and is accepted by
/// the caller's decoder (TDD §9.3: «si un snapshot está corrupto, intentar recuperar el anterior»).
/// </summary>
public sealed class SaveGameStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly TimeProvider _clock;

    private SaveGameStore(SqliteConnection connection, TimeProvider clock)
    {
        _connection = connection;
        _clock = clock;
    }

    /// <summary>Schema version after opening (<see cref="SchemaMigrations.LatestVersion"/>).</summary>
    public int SchemaVersion { get; private set; }

    public static SaveGameStore Open(string path, TimeProvider? clock = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
            ForeignKeys = true,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            var store = new SaveGameStore(connection, clock ?? TimeProvider.System);
            store.Migrate();
            return store;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public void Dispose() => _connection.Dispose();

    /// <summary>Versions recorded in <c>SchemaMigration</c>, ascending.</summary>
    public IReadOnlyList<int> AppliedMigrations()
    {
        using var command = Command("SELECT Version FROM SchemaMigration ORDER BY Version;");
        using var reader = command.ExecuteReader();
        var versions = new List<int>();
        while (reader.Read())
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    public SaveGameRecord CreateSaveGame(
        string id,
        string name,
        string engineVersion,
        int dataVersion,
        ulong worldSeed,
        string scenarioId,
        string pipelineVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var now = _clock.GetUtcNow();
        using var command = Command(
            """
            INSERT INTO SaveGame (Id, Name, CreatedAt, UpdatedAt, EngineVersion, DataVersion, WorldSeed, CurrentTick, ScenarioId, PipelineVersion)
            VALUES ($id, $name, $now, $now, $engine, $data, $seed, 0, $scenario, $pipeline);
            """);
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$name", name);
        command.Parameters.AddWithValue("$now", Time(now));
        command.Parameters.AddWithValue("$engine", engineVersion);
        command.Parameters.AddWithValue("$data", dataVersion);
        command.Parameters.AddWithValue("$seed", SeedCodec.ToStorage(worldSeed));
        command.Parameters.AddWithValue("$scenario", scenarioId);
        command.Parameters.AddWithValue("$pipeline", pipelineVersion);
        command.ExecuteNonQuery();
        return GetSaveGame(id) ?? throw new InvalidOperationException("The new save game was not stored.");
    }

    public SaveGameRecord? GetSaveGame(string id)
    {
        using var command = Command(
            """
            SELECT Id, Name, EngineVersion, DataVersion, WorldSeed, CurrentTick, ScenarioId, PipelineVersion, CreatedAt, UpdatedAt
            FROM SaveGame WHERE Id = $id;
            """);
        command.Parameters.AddWithValue("$id", id);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadSaveGame(reader) : null;
    }

    public IReadOnlyList<SaveGameRecord> ListSaveGames()
    {
        using var command = Command(
            """
            SELECT Id, Name, EngineVersion, DataVersion, WorldSeed, CurrentTick, ScenarioId, PipelineVersion, CreatedAt, UpdatedAt
            FROM SaveGame ORDER BY CreatedAt, Id;
            """);
        using var reader = command.ExecuteReader();
        var saves = new List<SaveGameRecord>();
        while (reader.Read())
        {
            saves.Add(ReadSaveGame(reader));
        }

        return saves;
    }

    /// <summary>Stores one snapshot and moves the save game's current tick, in one transaction.</summary>
    public StoredSnapshot SaveSnapshot(string saveGameId, long tick, string stateSchema, byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        var checksum = Checksum(payload);
        using var transaction = _connection.BeginTransaction();
        long id;
        using (var insert = Command(
            """
            INSERT INTO Snapshot (SaveGameId, Tick, CreatedAt, Payload, StateSchema, Checksum)
            VALUES ($save, $tick, $now, $payload, $schema, $checksum);
            SELECT last_insert_rowid();
            """,
            transaction))
        {
            insert.Parameters.AddWithValue("$save", saveGameId);
            insert.Parameters.AddWithValue("$tick", tick);
            insert.Parameters.AddWithValue("$now", Time(_clock.GetUtcNow()));
            insert.Parameters.AddWithValue("$payload", payload);
            insert.Parameters.AddWithValue("$schema", stateSchema);
            insert.Parameters.AddWithValue("$checksum", checksum);
            id = Convert.ToInt64(insert.ExecuteScalar(), CultureInfo.InvariantCulture);
        }

        using (var update = Command(
            "UPDATE SaveGame SET CurrentTick = MAX(CurrentTick, $tick), UpdatedAt = $now WHERE Id = $save;",
            transaction))
        {
            update.Parameters.AddWithValue("$save", saveGameId);
            update.Parameters.AddWithValue("$tick", tick);
            update.Parameters.AddWithValue("$now", Time(_clock.GetUtcNow()));
            update.ExecuteNonQuery();
        }

        transaction.Commit();
        return new StoredSnapshot(id, saveGameId, tick, stateSchema, checksum, payload);
    }

    public void RecordEvent(string saveGameId, long tick, string eventType, string payloadJson)
    {
        using var command = Command(
            "INSERT INTO SimulationEvent (SaveGameId, Tick, EventType, Payload) VALUES ($save, $tick, $type, $payload);");
        command.Parameters.AddWithValue("$save", saveGameId);
        command.Parameters.AddWithValue("$tick", tick);
        command.Parameters.AddWithValue("$type", eventType);
        command.Parameters.AddWithValue("$payload", payloadJson);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<StoredEvent> Events(string saveGameId, long fromTickExclusive = long.MinValue)
    {
        using var command = Command(
            "SELECT Id, Tick, EventType, Payload FROM SimulationEvent WHERE SaveGameId = $save AND Tick > $from ORDER BY Tick, Id;");
        command.Parameters.AddWithValue("$save", saveGameId);
        command.Parameters.AddWithValue("$from", fromTickExclusive);
        using var reader = command.ExecuteReader();
        var events = new List<StoredEvent>();
        while (reader.Read())
        {
            events.Add(new StoredEvent(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetString(3)));
        }

        return events;
    }

    /// <summary>Ticks of every stored snapshot of a save, ascending.</summary>
    public IReadOnlyList<long> SnapshotTicks(string saveGameId)
    {
        using var command = Command("SELECT Tick FROM Snapshot WHERE SaveGameId = $save ORDER BY Tick;");
        command.Parameters.AddWithValue("$save", saveGameId);
        using var reader = command.ExecuteReader();
        var ticks = new List<long>();
        while (reader.Read())
        {
            ticks.Add(reader.GetInt64(0));
        }

        return ticks;
    }

    /// <summary>
    /// Newest snapshot whose payload matches its checksum and that <paramref name="accept"/>
    /// takes (the caller decodes and validates it). Rejected ticks are reported, newest first.
    /// </summary>
    public SnapshotRecovery LoadLatestValid(string saveGameId, Func<StoredSnapshot, bool>? accept = null)
    {
        using var command = Command(
            "SELECT Id, Tick, StateSchema, Checksum, Payload FROM Snapshot WHERE SaveGameId = $save ORDER BY Tick DESC;");
        command.Parameters.AddWithValue("$save", saveGameId);
        using var reader = command.ExecuteReader();
        var rejected = new List<long>();
        while (reader.Read())
        {
            if (reader.GetValue(4) is not byte[] payload)
            {
                // A payload that is no longer a BLOB (for example rewritten as text) is corrupt.
                rejected.Add(reader.GetInt64(1));
                continue;
            }

            var snapshot = new StoredSnapshot(reader.GetInt64(0), saveGameId, reader.GetInt64(1), reader.GetString(2), reader.GetString(3), payload);
            if (!string.Equals(Checksum(payload), snapshot.Checksum, StringComparison.Ordinal) || (accept is not null && !accept(snapshot)))
            {
                rejected.Add(snapshot.Tick);
                continue;
            }

            return new SnapshotRecovery(snapshot, rejected);
        }

        return new SnapshotRecovery(null, rejected);
    }

    /// <summary>Lower-case hex SHA-256 of a payload.</summary>
    public static string Checksum(byte[] payload)
        => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    private void Migrate()
    {
        using (var create = Command(
            "CREATE TABLE IF NOT EXISTS SchemaMigration (Version INTEGER PRIMARY KEY, Name TEXT NOT NULL, AppliedAt TEXT NOT NULL);"))
        {
            create.ExecuteNonQuery();
        }

        var applied = AppliedMigrations().ToHashSet();
        var unknown = applied.Where(version => version > SchemaMigrations.LatestVersion).ToArray();
        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                $"The save file has schema version {unknown.Max()}, newer than this build ({SchemaMigrations.LatestVersion}).");
        }

        foreach (var migration in SchemaMigrations.All)
        {
            if (applied.Contains(migration.Version))
            {
                continue;
            }

            using var transaction = _connection.BeginTransaction();
            using (var step = Command(migration.Sql, transaction))
            {
                step.ExecuteNonQuery();
            }

            using (var record = Command(
                "INSERT INTO SchemaMigration (Version, Name, AppliedAt) VALUES ($version, $name, $now);",
                transaction))
            {
                record.Parameters.AddWithValue("$version", migration.Version);
                record.Parameters.AddWithValue("$name", migration.Name);
                record.Parameters.AddWithValue("$now", Time(_clock.GetUtcNow()));
                record.ExecuteNonQuery();
            }

            using (var pragma = Command(
                string.Create(CultureInfo.InvariantCulture, $"PRAGMA user_version = {migration.Version};"),
                transaction))
            {
                pragma.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        SchemaVersion = SchemaMigrations.LatestVersion;
    }

    private SqliteCommand Command(string sql, SqliteTransaction? transaction = null)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        command.Transaction = transaction;
        return command;
    }

    private static SaveGameRecord ReadSaveGame(SqliteDataReader reader)
        => new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetInt32(3),
            SeedCodec.FromStorage(reader.GetInt64(4)),
            reader.GetInt64(5),
            reader.GetString(6),
            reader.GetString(7),
            DateTimeOffset.Parse(reader.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind));

    private static string Time(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
