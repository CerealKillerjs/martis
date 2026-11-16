using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using Martis.Persistence.SQLite;
using Martis.Simulation.Biology;
using Martis.Simulation.Engine;
using Martis.Simulation.Experiments;
using Martis.Simulation.Missions;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;

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
        + "                       [--root <repository dir>] [--history-interval <int>]\n"
        + "                       [--introduce <organism>@<x>,<y>:<kg>[+<GENE>=<0..1>]...]...\n"
        + "                       [--mission first_colony [--mission-action <action>]...]\n"
        + "                       [--save <file.db> [--save-name <name>] [--snapshot-interval <int>]]\n"
        + "       martis simulate --scenario <id> --save <file.db> --resume <save id> --ticks <int> --output <dir>";

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
        BiologyCatalog biology;
        try
        {
            scenario = MarsBaselineScenario.LoadPublished(root);
            biology = BiologyCatalog.LoadPublished(root);
        }
        catch (Exception exception) when (exception is IOException or ScenarioLoadException or FormatException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
        {
            Console.Error.WriteLine($"Scenario load failed: {exception.Message}");
            return RunFailure;
        }

        MissionDefinition? mission = null;
        if (options.Mission is not null)
        {
            if (!string.Equals(options.Mission, "first_colony", StringComparison.Ordinal))
            {
                Console.Error.WriteLine($"Unknown mission '{options.Mission}'. Known: first_colony.");
                Console.Error.WriteLine(Usage);
                return InvalidArguments;
            }

            try
            {
                mission = MissionDefinition.LoadPublished(root);
            }
            catch (Exception exception) when (exception is IOException or FormatException or InvalidDataException or KeyNotFoundException or InvalidOperationException)
            {
                Console.Error.WriteLine($"Mission load failed: {exception.Message}");
                return RunFailure;
            }
        }

        foreach (var item in options.Introductions)
        {
            if (!biology.TryGetOrganism(item.Organism, out var organism)
                || (item.Genes is not null && item.Genes.Keys.Any(gene => !organism!.HasGene(gene))))
            {
                Console.Error.WriteLine($"Unknown organism or gene in '{item.Organism}'. Known organisms: {string.Join(", ", biology.Organisms.Select(o => o.Id))}.");
                Console.Error.WriteLine(Usage);
                return InvalidArguments;
            }
        }

        SaveGameStore? store = null;
        try
        {
            var seed = options.Seed ?? 0UL;
            StateSnapshot? resume = null;
            string? saveId = null;
            if (options.Save is not null)
            {
                store = SaveGameStore.Open(options.Save);
                if (options.Resume is not null)
                {
                    var save = store.GetSaveGame(options.Resume);
                    if (save is null)
                    {
                        Console.Error.WriteLine($"Save '{options.Resume}' is not in {options.Save}.");
                        return RunFailure;
                    }

                    if (options.Seed is { } given && given != save.WorldSeed)
                    {
                        Console.Error.WriteLine($"--seed {given} does not match the save's seed {save.WorldSeed}.");
                        return InvalidArguments;
                    }

                    seed = save.WorldSeed;
                    var recovery = store.LoadLatestValid(save.Id, stored => TryDecode(stored.Payload, out _));
                    foreach (var tick in recovery.RejectedTicks)
                    {
                        Console.Error.WriteLine($"Snapshot at tick {tick} is corrupt; trying the previous one.");
                    }

                    if (recovery.Snapshot is null || !TryDecode(recovery.Snapshot.Payload, out resume))
                    {
                        Console.Error.WriteLine($"Save '{save.Id}' has no valid snapshot.");
                        return RunFailure;
                    }

                    if (!string.Equals(resume!.PipelineVersion, SimulationPipeline.BiologyVersion, StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"The save was made with {resume.PipelineVersion}; this build runs {SimulationPipeline.BiologyVersion}.");
                        return RunFailure;
                    }

                    saveId = save.Id;
                    Console.Error.WriteLine($"Resuming save {save.Id} from tick {resume.Tick}.");
                }
                else
                {
                    saveId = string.Create(CultureInfo.InvariantCulture, $"save-{store.ListSaveGames().Count + 1}");
                    store.CreateSaveGame(
                        saveId,
                        options.SaveName ?? $"{options.Scenario} seed {seed}",
                        EngineAssembly.Version,
                        DataVersion,
                        seed,
                        options.Scenario,
                        SimulationPipeline.BiologyVersion);
                    Console.Error.WriteLine($"Save id: {saveId}");
                }
            }

            var request = new ExperimentRequest(
                options.Scenario,
                seed,
                options.Ticks,
                options.HistoryInterval,
                new HostInfo(RuntimeInformation.FrameworkDescription, BuildSdk(), RuntimeInformation.RuntimeIdentifier))
            {
                Introductions = options.Introductions
                    .Select(item => new PopulationIntroduction(item.Organism, (item.Y * MarsBaselineWorld.Width) + item.X, item.Kg, item.Genes))
                    .ToArray(),
                Resume = resume,
                SnapshotIntervalTicks = options.SnapshotInterval,
                Mission = mission,
                MissionActions = options.MissionActions,
            };
            var sink = store is not null && saveId is not null ? new SqliteSaveSink(store, saveId) : null;
            var result = PrototypeExperiment.Run(request, scenario, biology: biology, save: sink);
            return Finish(options, result);
        }
        catch (System.Data.Common.DbException exception)
        {
            Console.Error.WriteLine($"Save file error: {exception.Message}");
            return RunFailure;
        }
        catch (InvalidOperationException exception) when (store is not null)
        {
            Console.Error.WriteLine($"Save error: {exception.Message}");
            return RunFailure;
        }
        finally
        {
            store?.Dispose();
        }
    }

    /// <summary>Version of the published biology data (<c>*.v1.json</c>) stored in <c>SaveGame.DataVersion</c>.</summary>
    internal const int DataVersion = 1;

    private static bool TryDecode(byte[] payload, out StateSnapshot? snapshot)
    {
        try
        {
            snapshot = StateSnapshotBinary.Read(payload);
            return true;
        }
        catch (SnapshotCorruptException)
        {
            snapshot = null;
            return false;
        }
    }

    private static int Finish(Options options, ExperimentResult result)
    {
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

    private sealed record Introduction(string Organism, int X, int Y, double Kg)
    {
        public IReadOnlyDictionary<string, double>? Genes { get; init; }
    }

    private sealed record Options(string Scenario, ulong? Seed, int Ticks, string Output, string? Root, int HistoryInterval)
    {
        public IReadOnlyList<Introduction> Introductions { get; init; } = [];

        public string? Save { get; init; }

        public string? SaveName { get; init; }

        public string? Resume { get; init; }

        public int SnapshotInterval { get; init; }

        public string? Mission { get; init; }

        public IReadOnlyList<string> MissionActions { get; init; } = [];
    }

    /// <summary>Writes the run's snapshots and events to the SQLite save (T07).</summary>
    private sealed class SqliteSaveSink(SaveGameStore store, string saveId) : ISaveSink
    {
        public void SaveSnapshot(long tick, string schemaVersion, byte[] payload)
            => store.SaveSnapshot(saveId, tick, schemaVersion, payload);

        public void RecordEvent(long tick, string eventType, string payloadJson)
            => store.RecordEvent(saveId, tick, eventType, payloadJson);
    }

    /// <summary>
    /// Parses <c>organism@x,y:kg</c>, optionally followed by <c>+GENE=value</c> designs.
    /// Coordinates must lie on the 64×64 grid and each gene value in [0, 1].
    /// </summary>
    private static bool TryParseIntroduction(string text, out Introduction introduction)
    {
        introduction = new Introduction(string.Empty, 0, 0, 0);
        var parts = text.Split('+');
        var head = parts[0];
        var at = head.IndexOf('@', StringComparison.Ordinal);
        var colon = head.LastIndexOf(':');
        if (at <= 0 || colon < at)
        {
            return false;
        }

        var coordinates = head[(at + 1)..colon].Split(',');
        if (coordinates.Length != 2
            || !int.TryParse(coordinates[0], NumberStyles.None, CultureInfo.InvariantCulture, out var x)
            || !int.TryParse(coordinates[1], NumberStyles.None, CultureInfo.InvariantCulture, out var y)
            || x >= MarsBaselineWorld.Width || y >= MarsBaselineWorld.Height
            || !double.TryParse(head[(colon + 1)..], NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var kg)
            || !double.IsFinite(kg) || kg <= 0)
        {
            return false;
        }

        Dictionary<string, double>? genes = null;
        foreach (var design in parts.Skip(1))
        {
            var equals = design.IndexOf('=', StringComparison.Ordinal);
            if (equals <= 0
                || !double.TryParse(design[(equals + 1)..], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                || value is < 0.0 or > 1.0)
            {
                return false;
            }

            genes ??= new Dictionary<string, double>(StringComparer.Ordinal);
            if (!genes.TryAdd(design[..equals], value))
            {
                return false;
            }
        }

        introduction = new Introduction(head[..at], x, y, kg) { Genes = genes };
        return true;
    }

    private static bool TryParse(ReadOnlySpan<string> args, out Options options)
    {
        options = new Options(string.Empty, null, 0, string.Empty, null, PrototypeExperiment.DefaultHistoryIntervalTicks);
        string? scenario = null;
        ulong? seed = null;
        int? ticks = null;
        string? output = null;
        string? root = null;
        int? interval = null;
        string? save = null;
        string? saveName = null;
        string? resume = null;
        int? snapshotInterval = null;
        string? mission = null;
        var actions = new List<string>();
        var introductions = new List<Introduction>();

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
                case "--save":
                    if (save is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    save = value;
                    break;
                case "--save-name":
                    if (saveName is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    saveName = value;
                    break;
                case "--resume":
                    if (resume is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    resume = value;
                    break;
                case "--snapshot-interval":
                    if (snapshotInterval is not null
                        || !int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsedSnapshot)
                        || parsedSnapshot <= 0)
                    {
                        return false;
                    }

                    snapshotInterval = parsedSnapshot;
                    break;
                case "--mission":
                    if (mission is not null || string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    mission = value;
                    break;
                case "--mission-action":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        return false;
                    }

                    actions.Add(value);
                    break;
                case "--introduce":
                    if (!TryParseIntroduction(value, out var introduction))
                    {
                        return false;
                    }

                    introductions.Add(introduction);
                    break;
                default:
                    return false;
            }
        }

        if (scenario is null || ticks is null || output is null
            || (seed is null && resume is null)
            || ((resume is not null || saveName is not null || snapshotInterval is not null) && save is null)
            || (resume is not null && saveName is not null)
            || (actions.Count > 0 && mission is null))
        {
            return false;
        }

        options = new Options(scenario, seed, ticks.Value, output, root, interval ?? PrototypeExperiment.DefaultHistoryIntervalTicks)
        {
            Introductions = introductions,
            Save = save,
            SaveName = saveName,
            Resume = resume,
            SnapshotInterval = snapshotInterval ?? 0,
            Mission = mission,
            MissionActions = actions,
        };
        return true;
    }
}
