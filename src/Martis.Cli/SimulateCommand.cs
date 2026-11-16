using System.Globalization;
using Martis.Simulation.Engine;

namespace Martis.Cli;

/// <summary>
/// <c>simulate</c> runs the stub engine on an empty 64 by 64 world (S1-23).
/// <c>--scenario</c> is accepted and not opened. <c>--output</c> is accepted and
/// not written. Scenario files and result files belong to a later task.
/// </summary>
internal static class SimulateCommand
{
    internal const string Usage =
        "Usage: martis --version\n       martis simulate --scenario <id> --seed <ulong> --ticks <int> --output <dir>";

    internal static int Run(ReadOnlySpan<string> args)
    {
        if (!TryParse(args, out var scenario, out var seed, out var ticks, out var output))
        {
            Console.Error.WriteLine(Usage);
            return 1;
        }

        // The names are required by the CLI shape. Nothing is loaded or written.
        _ = scenario;
        _ = output;

        var engine = PrototypeWorld.Create(seed);
        engine.Advance(ticks);
        Console.WriteLine(engine.CurrentTick.ToString(CultureInfo.InvariantCulture));
        return 0;
    }

    private static bool TryParse(
        ReadOnlySpan<string> args,
        out string scenario,
        out ulong seed,
        out int ticks,
        out string output)
    {
        scenario = string.Empty;
        seed = 0;
        ticks = 0;
        output = string.Empty;
        var seenScenario = false;
        var seenSeed = false;
        var seenTicks = false;
        var seenOutput = false;

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
                    if (seenScenario || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    scenario = value;
                    seenScenario = true;
                    break;
                case "--seed":
                    if (seenSeed || !ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out seed))
                    {
                        return false;
                    }

                    seenSeed = true;
                    break;
                case "--ticks":
                    if (seenTicks || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ticks) || ticks < 0)
                    {
                        return false;
                    }

                    seenTicks = true;
                    break;
                case "--output":
                    if (seenOutput || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    output = value;
                    seenOutput = true;
                    break;
                default:
                    return false;
            }
        }

        return seenScenario && seenSeed && seenTicks && seenOutput;
    }
}
