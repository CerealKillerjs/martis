using Martis.Persistence.Migrations;
using Martis.Persistence.SQLite;
using Microsoft.Data.Sqlite;

namespace Martis.Persistence.Tests;

/// <summary>T07: migrations, H-14 seeds, snapshots, events and recovery from corruption.</summary>
public sealed class SaveGameStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "martis-store-" + Path.GetRandomFileName());

    public SaveGameStoreTests() => Directory.CreateDirectory(_directory);

    private string DbPath => Path.Combine(_directory, "save.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void OpeningAppliesEveryMigrationOnceAndReopeningChangesNothing()
    {
        using (var store = SaveGameStore.Open(DbPath))
        {
            Assert.Equal(SchemaMigrations.LatestVersion, store.SchemaVersion);
            Assert.Equal(SchemaMigrations.All.Select(migration => migration.Version), store.AppliedMigrations());
        }

        using var again = SaveGameStore.Open(DbPath);
        Assert.Equal(SchemaMigrations.All.Select(migration => migration.Version), again.AppliedMigrations());
        Assert.Equal(SchemaMigrations.LatestVersion, UserVersion());
    }

    [Fact]
    public void AFileFromANewerBuildIsRefused()
    {
        using (SaveGameStore.Open(DbPath))
        {
        }

        Execute("INSERT INTO SchemaMigration (Version, Name, AppliedAt) VALUES (999, 'future', 'x');");

        var error = Assert.Throws<InvalidOperationException>(() => SaveGameStore.Open(DbPath));
        Assert.Contains("999", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0UL)]
    [InlineData(12345UL)]
    [InlineData(9223372036854775807UL)]
    [InlineData(9223372036854775808UL)]
    [InlineData(18446744073709551615UL)]
    public void EverySeedRoundTripsThroughTheSignedIntegerColumn(ulong seed)
    {
        Assert.Equal(seed, SeedCodec.FromStorage(SeedCodec.ToStorage(seed)));
        using var store = SaveGameStore.Open(DbPath);
        store.CreateSaveGame("s1", "name", "0.1.0", 1, seed, "mars_baseline", "pipeline-v3");

        Assert.Equal(seed, store.GetSaveGame("s1")!.WorldSeed);
    }

    [Fact]
    public void SnapshotsMoveTheCurrentTickAndTheNewestValidOneIsLoaded()
    {
        using var store = Create();
        store.SaveSnapshot("s1", 10, "schema", [1, 2, 3]);
        store.SaveSnapshot("s1", 20, "schema", [4, 5, 6]);

        var recovery = store.LoadLatestValid("s1");

        Assert.Equal(20, recovery.Snapshot!.Tick);
        Assert.Equal(new byte[] { 4, 5, 6 }, recovery.Snapshot.Payload);
        Assert.Empty(recovery.RejectedTicks);
        Assert.Equal(20, store.GetSaveGame("s1")!.CurrentTick);
        Assert.Equal(new long[] { 10, 20 }, store.SnapshotTicks("s1"));
    }

    [Fact]
    public void ACorruptLatestSnapshotFallsBackToThePreviousOne()
    {
        using var store = Create();
        store.SaveSnapshot("s1", 10, "schema", [1, 2, 3]);
        store.SaveSnapshot("s1", 20, "schema", [4, 5, 6]);
        Execute("UPDATE Snapshot SET Payload = X'040507' WHERE Tick = 20;");

        var recovery = store.LoadLatestValid("s1");

        Assert.Equal(10, recovery.Snapshot!.Tick);
        Assert.Equal(new long[] { 20 }, recovery.RejectedTicks);
    }

    [Fact]
    public void APayloadRewrittenAsTextIsTreatedAsCorrupt()
    {
        using var store = Create();
        store.SaveSnapshot("s1", 10, "schema", [1, 2, 3]);
        store.SaveSnapshot("s1", 20, "schema", [4, 5, 6]);
        Execute("UPDATE Snapshot SET Payload = 'text' WHERE Tick = 20;");

        Assert.Equal(10, store.LoadLatestValid("s1").Snapshot!.Tick);
    }

    [Fact]
    public void ASnapshotTheDecoderRejectsAlsoFallsBack()
    {
        using var store = Create();
        store.SaveSnapshot("s1", 10, "schema", [1]);
        store.SaveSnapshot("s1", 20, "schema", [2]);
        store.SaveSnapshot("s1", 30, "schema", [3]);

        var recovery = store.LoadLatestValid("s1", snapshot => snapshot.Payload[0] == 1);

        Assert.Equal(10, recovery.Snapshot!.Tick);
        Assert.Equal(new long[] { 30, 20 }, recovery.RejectedTicks);
        Assert.Null(store.LoadLatestValid("s1", _ => false).Snapshot);
    }

    [Fact]
    public void TheSameTickCannotBeSavedTwice()
    {
        using var store = Create();
        store.SaveSnapshot("s1", 10, "schema", [1]);

        Assert.Throws<SqliteException>(() => store.SaveSnapshot("s1", 10, "schema", [2]));
        Assert.Throws<SqliteException>(() => store.SaveSnapshot("missing", 5, "schema", [2]));
    }

    [Fact]
    public void EventsComeBackInTickOrderAfterTheGivenTick()
    {
        using var store = Create();
        store.RecordEvent("s1", 5, "mutation", "{\"a\":1}");
        store.RecordEvent("s1", 3, "extinction", "{\"b\":2}");
        store.RecordEvent("s1", 9, "mutation", "{\"c\":3}");

        Assert.Equal(new long[] { 3, 5, 9 }, store.Events("s1").Select(item => item.Tick));
        Assert.Equal(new[] { "mutation" }, store.Events("s1", fromTickExclusive: 5).Select(item => item.EventType));
    }

    [Fact]
    public void SaveGamesAreListedWithTheirVersions()
    {
        using var store = Create();
        var save = Assert.Single(store.ListSaveGames());
        Assert.Equal("0.1.0", save.EngineVersion);
        Assert.Equal(1, save.DataVersion);
        Assert.Equal("mars_baseline", save.ScenarioId);
        Assert.Equal("pipeline-v3", save.PipelineVersion);
    }

    private SaveGameStore Create()
    {
        var store = SaveGameStore.Open(DbPath);
        store.CreateSaveGame("s1", "Partida", "0.1.0", 1, 7UL, "mars_baseline", "pipeline-v3");
        return store;
    }

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private long UserVersion()
    {
        using var connection = new SqliteConnection($"Data Source={DbPath};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return (long)command.ExecuteScalar()!;
    }
}
