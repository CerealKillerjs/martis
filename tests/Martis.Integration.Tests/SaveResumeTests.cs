using Martis.Persistence.SQLite;
using Martis.Simulation.Snapshots;
using Microsoft.Data.Sqlite;

namespace Martis.Integration.Tests;

/// <summary>
/// T07 acceptance (ROADMAP): save, close, reopen, load and continue gives the state of an
/// uninterrupted run (VAL-009 + VAL-010); a corrupt latest snapshot falls back to the previous one.
/// </summary>
[Trait("Category", "Integration")]
public sealed class SaveResumeTests
{
    private static readonly string[] Inoculation = ["--introduce", "phototroph_a01@32,32:0.5", "--introduce", "phototroph_a01@12,40:0.5"];

    [Fact]
    public async Task ResumingASaveMatchesTheUninterruptedRun()
    {
        using var work = new Work();
        var full = await Simulate(work, "full", ["--seed", "18446744073709551615", "--ticks", "50", .. Inoculation]);
        Assert.Equal(0, full.ExitCode);

        var first = await Simulate(work, "first", ["--seed", "18446744073709551615", "--ticks", "30", "--save", work.Db, "--snapshot-interval", "10", .. Inoculation]);
        Assert.Equal(0, first.ExitCode);
        var id = SaveId(first.Stderr);

        var resumed = await Simulate(work, "resumed", ["--ticks", "20", "--save", work.Db, "--resume", id]);

        Assert.Equal(0, resumed.ExitCode);
        Assert.Contains("from tick 30", resumed.Stderr, StringComparison.Ordinal);
        Assert.Equal(work.Snapshot("full"), work.Snapshot("resumed"));
        using var store = SaveGameStore.Open(work.Db);
        Assert.Equal(new long[] { 10, 20, 30, 50 }, store.SnapshotTicks(id));
        Assert.Equal(18446744073709551615UL, store.GetSaveGame(id)!.WorldSeed);
    }

    [Fact]
    public async Task ACorruptLatestSnapshotIsSkippedAndThePreviousOneContinues()
    {
        using var work = new Work();
        var full = await Simulate(work, "full", ["--seed", "4", "--ticks", "40", .. Inoculation]);
        Assert.Equal(0, full.ExitCode);
        var first = await Simulate(work, "first", ["--seed", "4", "--ticks", "30", "--save", work.Db, "--snapshot-interval", "10", .. Inoculation]);
        var id = SaveId(first.Stderr);
        Corrupt(work.Db, tick: 30);

        var resumed = await Simulate(work, "resumed", ["--ticks", "20", "--save", work.Db, "--resume", id]);

        Assert.Equal(0, resumed.ExitCode);
        Assert.Contains("Snapshot at tick 30 is corrupt", resumed.Stderr, StringComparison.Ordinal);
        Assert.Contains("from tick 20", resumed.Stderr, StringComparison.Ordinal);
        Assert.Equal(work.Snapshot("full"), work.Snapshot("resumed"));
    }

    [Fact]
    public async Task ResumeArgumentsAreChecked()
    {
        using var work = new Work();
        Assert.Equal(2, (await Simulate(work, "a", ["--ticks", "5", "--resume", "x"])).ExitCode);
        Assert.Equal(2, (await Simulate(work, "b", ["--ticks", "5", "--save", work.Db, "--resume", "x", "--save-name", "n"])).ExitCode);
        Assert.Equal(2, (await Simulate(work, "c", ["--seed", "1", "--ticks", "5", "--snapshot-interval", "0", "--save", work.Db])).ExitCode);
        var missing = await Simulate(work, "d", ["--ticks", "5", "--save", work.Db, "--resume", "nope"]);
        Assert.Equal(1, missing.ExitCode);
        Assert.Contains("'nope' is not in", missing.Stderr, StringComparison.Ordinal);
    }

    private static Task<(int ExitCode, string Stdout, string Stderr)> Simulate(Work work, string name, string[] extra)
        => CliSmokeTests.RunCli(["simulate", "--scenario", "mars_baseline", "--output", work.Output(name), .. extra]);

    private static string SaveId(string stderr)
    {
        const string prefix = "Save id: ";
        var line = stderr.Split('\n').Single(text => text.StartsWith(prefix, StringComparison.Ordinal));
        return line[prefix.Length..].Trim();
    }

    private static void Corrupt(string db, long tick)
    {
        using var connection = new SqliteConnection($"Data Source={db};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE Snapshot SET Payload = CAST(substr(Payload, 1, length(Payload) - 1) || X'00' AS BLOB) WHERE Tick = $tick;";
        command.Parameters.AddWithValue("$tick", tick);
        Assert.Equal(1, command.ExecuteNonQuery());
    }

    private sealed class Work : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "martis-save-" + Path.GetRandomFileName());

        public Work() => Directory.CreateDirectory(_root);

        public string Db => Path.Combine(_root, "game.db");

        public string Output(string name) => Path.Combine(_root, name);

        public string Snapshot(string name)
            => StateSnapshotJson.Sha256Hex(StateSnapshotJson.Read(File.ReadAllBytes(Path.Combine(Output(name), "final_snapshot.json"))));

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(_root, recursive: true);
        }
    }
}
