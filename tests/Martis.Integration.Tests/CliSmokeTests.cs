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
    }

    [Fact]
    public async Task SimulatePrintsTheFinalTickAndReturnsZero()
    {
        var result = await RunCli(
            "simulate",
            "--scenario",
            "mars_baseline",
            "--seed",
            "12345",
            "--ticks",
            "1000",
            "--output",
            "synthetic-output");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1000", result.Stdout.Trim());
        Assert.Equal(string.Empty, result.Stderr.Trim());
        Assert.False(Directory.Exists(Path.Combine(RepoLayout.Root, "synthetic-output")));
        Assert.Empty(Directory.EnumerateFiles(
            Path.Combine(RepoLayout.Root, "data", "scenarios"),
            "*.json",
            SearchOption.AllDirectories));
    }

    [Fact]
    public async Task SimulateAcceptsTheFullULongSeed()
    {
        var result = await RunCli(
            "simulate",
            "--scenario",
            "mars_baseline",
            "--seed",
            ulong.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ticks",
            "0",
            "--output",
            "synthetic-output");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0", result.Stdout.Trim());
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public async Task InvalidArgumentsReturnOneAndPrintUsage(string[] args)
    {
        var result = await RunCli(args);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal(Normalize(ExpectedUsage), Normalize(result.Stderr));
        Assert.Equal(string.Empty, result.Stdout.Trim());
    }

    private const string ExpectedUsage =
        "Usage: martis --version\n       martis simulate --scenario <id> --seed <ulong> --ticks <int> --output <dir>";

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

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunCli(params string[] args)
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
