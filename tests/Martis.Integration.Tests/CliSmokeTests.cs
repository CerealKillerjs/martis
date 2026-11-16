using System.Diagnostics;
using System.Xml.Linq;

namespace Martis.Integration.Tests;

/// <summary>
/// Runs the built headless executable. The test does not reference the CLI project,
/// which matches the T01 dependency list for the integration tests.
/// </summary>
public sealed class CliSmokeTests
{
    [Fact]
    public async Task VersionReturnsZeroAndPrintsTheEngineVersion()
    {
        var expected = $"MARTIS {EngineVersionFromProps()}";
        var first = await RunCli("--version");
        var second = await RunCli("--version");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(expected, first.Stdout.Trim());
        Assert.Equal(string.Empty, first.Stderr.Trim());
        Assert.Equal(first.Stdout, second.Stdout);
        Assert.Equal(first.ExitCode, second.ExitCode);
    }

    public static IEnumerable<object[]> InvalidArguments()
    {
        yield return [Array.Empty<string>()];
        yield return [new[] { "--Version" }];
        yield return [new[] { "-v" }];
        yield return [new[] { "--version", "extra" }];
        yield return [new[] { "simulate" }];
        yield return [new[] { "simulate", "--ticks", "abc" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "-1", "--ticks", "1", "--output", "out" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--history-interval", "0" }];
        yield return [new[] { "simulate", "--scenario", "olympus", "--seed", "1", "--ticks", "1", "--output", "out" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--introduce", "phototroph_a01@64,0:1" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--introduce", "phototroph_a01@1,1:-1" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--introduce", "nobody@1,1:1" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--introduce", "phototroph_a01@1,1:1+UV_PROTECTION=2" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--introduce", "phototroph_a01@1,1:1+WINGS=0.5" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--mission", "olympus" }];
        yield return [new[] { "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1", "--output", "out", "--mission-action", "analyze-region" }];
    }

    [Fact]
    public async Task SimulateRunsThePrototypeFlowAndWritesTheResults()
    {
        var science = Path.Combine(
            RepoLayout.Root,
            "data",
            "scenarios",
            "MarsBaseline",
            "mars_baseline_gale.v0.json");
        var before = File.ReadAllBytes(science);
        using var output = new TempDirectory();
        var result = await RunCli(
            "simulate",
            "--scenario",
            "mars_baseline",
            "--seed",
            "12345",
            "--ticks",
            "1000",
            "--output",
            output.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1000", result.Stdout.Trim());
        Assert.Equal(string.Empty, result.Stderr.Trim());
        Assert.Equal(before, File.ReadAllBytes(science));
        Assert.Equal(
            ResultFiles.Order(StringComparer.Ordinal),
            Directory.GetFiles(output.Path).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        var snapshot = File.ReadAllText(Path.Combine(output.Path, "final_snapshot.json"));
        Assert.Contains("\"tick\": 1000,", snapshot, StringComparison.Ordinal);
        var metadata = File.ReadAllText(Path.Combine(output.Path, "metadata.json"));
        Assert.Contains("\"status\": \"completed\"", metadata, StringComparison.Ordinal);
    }

    /// <summary>S1-33: same seed, same bytes in every file (same platform and runtime, TDD §6).</summary>
    [Fact]
    [Trait("Category", "Determinism")]
    public async Task TwoRunsWithTheSameSeedWriteIdenticalFiles()
    {
        using var a = new TempDirectory();
        using var b = new TempDirectory();
        string[] common = ["simulate", "--scenario", "mars_baseline", "--seed", "987654321", "--ticks", "300", "--output"];

        Assert.Equal(0, (await RunCli([.. common, a.Path])).ExitCode);
        Assert.Equal(0, (await RunCli([.. common, b.Path])).ExitCode);

        foreach (var name in ResultFiles)
        {
            Assert.Equal(
                File.ReadAllBytes(Path.Combine(a.Path, name)),
                File.ReadAllBytes(Path.Combine(b.Path, name)));
        }
    }

    [Fact]
    public async Task SimulateAcceptsTheFullULongSeed()
    {
        using var output = new TempDirectory();
        var result = await RunCli(
            "simulate",
            "--scenario",
            "mars_baseline",
            "--seed",
            ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ticks",
            "0",
            "--output",
            output.Path);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0", result.Stdout.Trim());
        Assert.Contains(
            $"\"seed\": \"{ulong.MaxValue}\"",
            File.ReadAllText(Path.Combine(output.Path, "metadata.json")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingScenarioDataReturnsOne()
    {
        using var output = new TempDirectory();
        using var emptyRoot = new TempDirectory();
        var result = await RunCli(
            "simulate", "--scenario", "mars_baseline", "--seed", "1", "--ticks", "1",
            "--output", output.Path, "--root", emptyRoot.Path);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Scenario load failed", result.Stderr, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(output.Path));
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidArgumentsReturnTwoAndPrintUsage(string[] args)
    {
        var result = await RunCli(args);
        Assert.Equal(2, result.ExitCode);
        Assert.EndsWith(Normalize(ExpectedUsage), Normalize(result.Stderr), StringComparison.Ordinal);
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    private static readonly string[] ResultFiles =
        ["metadata.json", "final_snapshot.json", "resource_history.csv", "population_history.csv", "mass_balance.csv", "mutations.csv", "simulation.log"];

    [Fact]
    public async Task IntroducedPhototrophGrowsAndTheMassBalanceIsWritten()
    {
        using var output = new TempDirectory();
        var result = await RunCli(
            "simulate", "--scenario", "mars_baseline", "--seed", "3", "--ticks", "60",
            "--output", output.Path, "--introduce", "phototroph_a01@5,7:0.001");

        Assert.Equal(0, result.ExitCode);
        var rows = File.ReadAllLines(Path.Combine(output.Path, "population_history.csv"));
        var last = rows[^1].Split(',');
        Assert.Equal("60", last[0]);
        Assert.True(int.Parse(last[1], System.Globalization.CultureInfo.InvariantCulture) >= 1);
        Assert.True(double.Parse(last[3], System.Globalization.CultureInfo.InvariantCulture) > 0.001);
        Assert.StartsWith("tick,C_kg,", File.ReadAllLines(Path.Combine(output.Path, "mass_balance.csv"))[0], StringComparison.Ordinal);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "martis-cli-" + System.IO.Path.GetRandomFileName());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
                // A leftover temp directory does not change the test result.
            }
        }
    }

    private const string ExpectedUsage =
        "Usage: martis --version\n"
        + "       martis simulate --scenario <id> --seed <ulong> --ticks <int> --output <dir>\n"
        + "                       [--root <repository dir>] [--history-interval <int>]\n"
        + "                       [--introduce <organism>@<x>,<y>:<kg>[+<GENE>=<0..1>]...]...\n"
        + "                       [--mission first_colony [--mission-action <action>]...]\n"
        + "                       [--save <file.db> [--save-name <name>] [--snapshot-interval <int>]]\n"
        + "       martis simulate --scenario <id> --save <file.db> --resume <save id> --ticks <int> --output <dir>";

    private static string Normalize(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();
    }

    private static string EngineVersionFromProps()
    {
        var document = XDocument.Load(Path.Combine(RepoLayout.Root, "Directory.Build.props"));
        return document
            .Descendants()
            .Single(element => element.Name.LocalName == "Version")
            .Value
            .Trim();
    }

    internal static async Task<(int ExitCode, string Stdout, string Stderr)> RunCli(params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = RepoLayout.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(RepoLayout.FindCliDll());
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Failed to start the CLI.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Martis.Cli did not exit within 30 seconds.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }
}
