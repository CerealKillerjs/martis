namespace Martis.Persistence.Migrations;

/// <summary>One forward-only schema step. <see cref="Version"/> is stored in <c>SchemaMigration</c> and <c>PRAGMA user_version</c>.</summary>
public sealed record SchemaMigration(int Version, string Name, string Sql);

/// <summary>
/// The save schema, in order (TDD §9.2, ADR-015). A published migration is never edited;
/// a change is a new migration with the next version.
/// </summary>
public static class SchemaMigrations
{
    /// <summary>TDD §9.2 verbatim: the first migration, not the final schema.</summary>
    public static readonly SchemaMigration Initial = new(
        1,
        "tdd-9.2-initial",
        """
        CREATE TABLE SaveGame (
            Id TEXT PRIMARY KEY,
            Name TEXT NOT NULL,
            CreatedAt TEXT NOT NULL,
            UpdatedAt TEXT NOT NULL,
            EngineVersion TEXT NOT NULL,
            DataVersion INTEGER NOT NULL,
            WorldSeed INTEGER NOT NULL,
            CurrentTick INTEGER NOT NULL
        );

        CREATE TABLE Snapshot (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SaveGameId TEXT NOT NULL,
            Tick INTEGER NOT NULL,
            CreatedAt TEXT NOT NULL,
            Payload BLOB NOT NULL,
            FOREIGN KEY (SaveGameId) REFERENCES SaveGame(Id),
            UNIQUE (SaveGameId, Tick)
        );

        CREATE TABLE SimulationEvent (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SaveGameId TEXT NOT NULL,
            Tick INTEGER NOT NULL,
            EventType TEXT NOT NULL,
            Payload TEXT NOT NULL,
            FOREIGN KEY (SaveGameId) REFERENCES SaveGame(Id)
        );

        CREATE INDEX IX_Snapshot_SaveGame_Tick
        ON Snapshot(SaveGameId, Tick);

        CREATE INDEX IX_Event_SaveGame_Tick
        ON SimulationEvent(SaveGameId, Tick);
        """);

    /// <summary>
    /// Integrity and identity of each snapshot (ADR-004): the SHA-256 of the payload, so
    /// corruption is found before decoding, and the state schema id. The save game keeps the
    /// scenario and the pipeline it was started with.
    /// </summary>
    public static readonly SchemaMigration SnapshotChecksum = new(
        2,
        "snapshot-checksum-and-versions",
        """
        ALTER TABLE Snapshot ADD COLUMN StateSchema TEXT NOT NULL DEFAULT '';
        ALTER TABLE Snapshot ADD COLUMN Checksum TEXT NOT NULL DEFAULT '';
        ALTER TABLE SaveGame ADD COLUMN ScenarioId TEXT NOT NULL DEFAULT '';
        ALTER TABLE SaveGame ADD COLUMN PipelineVersion TEXT NOT NULL DEFAULT '';
        """);

    public static IReadOnlyList<SchemaMigration> All { get; } = [Initial, SnapshotChecksum];

    public static int LatestVersion => All[^1].Version;
}
