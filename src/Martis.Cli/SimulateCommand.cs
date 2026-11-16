using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Martis.Simulation.Experiments;
using Martis.Simulation.Scenarios;

namespace Martis.Cli;

/// <summary>
/// <c>simulate</c> runs the prototype flow of TDD §19 (S1-32): load MarsBaseline,
/// build the 64×64 world, advance the ticks with validation on, take the full
/// snapshot and write the result files to <c>--output</c>.
/// Exit codes: 0 success, 1 run failure (invariant violation, missing data, I/O),
/// 2 invalid arguments (SPRINT_01 convention).
/// </summary>
internal static class SimulateCommand
{
    internal const int Success = 0;
    internal const int RunFailure = 1;
    internal const int InvalidArguments = 2;

    internal const string Usage =
        "Usage: martis --version\n"
        + "       martis simulate --scenario <id> --seed <ulong> --ticks <int> --output <dir>\n"
        + "                       [--root <repository dir>] [--history-interval <int>]";

    internal static int Run(ReadOnlySpan<string> args)
    {
        if (!TryParse(args, out var options))
        {
            Console.Error.WriteLine(Usage);
            return InvalidArguments;
        }

        if (!string.Equals(options.Scenario, PrototypeExperiment.MarsBaselineId, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"Unknown scenario '{options.Scenario}'. Known: {PrototypeExperiment.MarsBaselineId}.");
            Console.Error.WriteLine(Usage);
            return InvalidArguments;
        }

        var root = options.Root ?? FindRepositoryRoot();
        if (root is null)
        {
            Console.Error.WriteLine(
                "Scenario data not found: no data/scenarios/MarsBaseline above the current directory or the executable. Pass --root.");
            return RunFailure;
        }

        MarsBaselineScenario scenario;
        try
        {
            scenario = MarsBaselineScenario.LoadPublished(root);
        }
        catch (Exception exception) when (exception is IOException or ScenarioLoadException or FormatException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Scenario load failed: {exception.Message}");
            return RunFailure;
        }

        var request = new ExperimentRequest(
            options.Scenario,
            options.Seed,
            options.Ticks,
            options.HistoryInterval,
            new HostInfo(RuntimeInformation.FrameworkDescription, BuildSdk(), RuntimeInformation.RuntimeIdentifier));
        var result = PrototypeExperiment.Run(request, scenario);

        try
        {
            Directory.CreateDirectory(options.Output);
            foreach (var file in result.Files)
            {
                File.WriteAllBytes(Path.Combine(options.Output, file.Name), file.Content);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not write results to '{options.Output}': {exception.Message}");
            return RunFailure;
        }

        Console.WriteLine(result.FinalTick.ToString(CultureInfo.InvariantCulture));
        if (!result.Succeeded)
        {
            Console.Error.WriteLine($"Simulation failed: {result.FailureSummary}");
            Console.Error.WriteLine($"Details: {Path.Combine(options.Output, PrototypeExperiment.LogFile)}");
            return RunFailure;
        }

        return Success;
    }

    /// <summary>
    /// Walks up from the working directory, then from the executable, to the first
    /// directory that holds the MarsBaseline data files.
    /// </summary>
    private static string? FindRepositoryRoot()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "data", "scenarios", "MarsBaseline", MarsBaselineScenario.ScienceFileName);
                if (File.Exists(candidate))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }
        }

        return null;
    }

    private static string? BuildSdk()
    {
        return typeof(SimulateCommand).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(attribute.Key, "BuildSdkVersion", StringComparison.Ordinal))
            ?.Value;
    }

    private sealed record Options(string Scenario, ulong Seed, int Ticks, string Output, string? Root, int HistoryInterval);

    private static bool TryParse(ReadOnlySpan<string> args, out Options options)
    {
        options = new Options(string.Empty, 0, 0, string.Empty, null, PrototypeExperiment.DefaultHistoryIntervalTicks);
        string? scenario = null;
        ulong? seed = null;
        int? ticks = null;
        string? output = null;
        string? root = null;
        int? interval = null;

        for (var i = 0; i < args.Length; i++)
        {
            var flag = args[i];
            if (i + 1 >= args.Length || !flag.StartsWith("--", StringComparison.Ordinal) || flag.Contains('=', StringComparison.Ordinal))
            {
                return false;
            }

            var value = args[++i];
            switch (flag)
            {
                case "--scenario":
                    if (scenario is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    scenario = value;
                    break;
                case "--seed":
                    if (seed is not null || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSeed))
                    {
                        return false;
                    }

                    seed = parsedSeed;
                    break;
                case "--ticks":
                    if (ticks is not null || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedTicks))
                    {
                        return false;
                    }

                    ticks = parsedTicks;
                    break;
                case "--output":
                    if (output is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    output = value;
                    break;
                case "--root":
                    if (root is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    root = value;
                    break;
                case "--history-interval":
                    if (interval is not null
                        || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedInterval)
                        || parsedInterval <= 0)
                    {
                        return false;
                    }

                    interval = parsedInterval;
                    break;
                default:
                    return false;
            }
        }

        if (scenario is null || seed is null || ticks is null || output is null)
        {
            return false;
        }

        options = new Options(scenario, seed.Value, ticks.Value, output, root, interval ?? PrototypeExperiment.DefaultHistoryIntervalTicks);
        return true;
    }
}
