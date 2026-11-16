using System.Globalization;
using System.Text;
using System.Text.Json;
using Martis.Domain.World;
using Martis.Simulation.Engine;
using Martis.Simulation.Random;
using Martis.Simulation.Scenarios;
using Martis.Simulation.Snapshots;

namespace Martis.Simulation.Experiments;

/// <summary>
/// Build facts the host knows and the engine must not read itself (runtime, SDK, OS).
/// They are written to <c>metadata.json</c> (ADR-002). None of them is a model value.
/// </summary>
public sealed record HostInfo(string Runtime, string? BuildSdk, string OperatingSystem);

/// <summary>Parameters of one headless run (TDD §11.1, §19).</summary>
public sealed record ExperimentRequest(
    string ScenarioId,
    ulong Seed,
    int Ticks,
    int HistoryIntervalTicks,
    HostInfo Host);

/// <summary>One output file, as bytes. The host decides where to write it.</summary>
public sealed record ExperimentFile(string Name, byte[] Content);

/// <summary>
/// Outcome of <see cref="PrototypeExperiment.Run"/>. <see cref="Files"/> is written in
/// both cases: after a failure the snapshot is the last committed tick.
/// </summary>
public sealed record ExperimentResult(bool Succeeded, long FinalTick, string? FailureSummary, IReadOnlyList<ExperimentFile> Files);

/// <summary>
/// The prototype flow of TDD §19 and S1-32, without touching the disk:
/// load MarsBaseline → 64×64 world → environment → <c>pcg32-v1</c> streams →
/// advance N ticks with validation on → snapshot → result files.
/// Every file is a pure function of the request, the scenario files and the build:
/// there is no clock, no machine name and no path in the output, so the same request
/// on the same platform and runtime gives the same bytes (VAL-010, §6).
/// </summary>
public static class PrototypeExperiment
{
    /// <summary>Only scenario id the prototype knows (H-16).</summary>
    public const string MarsBaselineId = "mars_baseline";

    /// <summary>
    /// Default spacing of history rows. TDD §8.1 lists 30 ticks as the indicative
    /// period of the scientific history; this is an engineering default, not a model value.
    /// </summary>
    public const int DefaultHistoryIntervalTicks = 30;

    public const string MetadataSchema = "martis-run-metadata-v1";

    public const string MetadataFile = "metadata.json";
    public const string SnapshotFile = "final_snapshot.json";
    public const string ResourceHistoryFile = "resource_history.csv";
    public const string PopulationHistoryFile = "population_history.csv";
    public const string LogFile = "simulation.log";

    /// <summary>Files every run produces, in write order.</summary>
    public static IReadOnlyList<string> FileNames { get; } =
        [MetadataFile, SnapshotFile, ResourceHistoryFile, PopulationHistoryFile, LogFile];

    /// <summary>
    /// Runs the flow. <paramref name="engineFactory"/> defaults to
    /// <see cref="MarsBaselineWorld.Create(MarsBaselineScenario, ulong, int, int)"/>;
    /// tests pass another one to inject a fault.
    /// </summary>
    public static ExperimentResult Run(
        ExperimentRequest request,
        MarsBaselineScenario scenario,
        Func<MarsBaselineScenario, ulong, SimulationEngine>? engineFactory = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(request.Host);
        if (!string.Equals(request.ScenarioId, MarsBaselineId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unknown scenario '{request.ScenarioId}'.", nameof(request));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(request.Ticks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(request.HistoryIntervalTicks);

        var identity = ScenarioIdentity.From(request.ScenarioId, scenario);
        var engine = (engineFactory ?? ((s, seed) => MarsBaselineWorld.Create(s, seed)))(scenario, request.Seed);
        var log = new Log();
        var resources = new StringBuilder();
        var populations = new StringBuilder();
        resources.Append("tick");
        foreach (var name in ResourceColumns.Names)
        {
            resources.Append(',').Append(name).Append("_kg");
        }

        resources.Append(",WaterIce_kg\n");
        populations.Append("tick,population_count,total_abundance,total_biomass_kg\n");

        log.Line($"MARTIS engine {EngineAssembly.Version}");
        log.Line($"scenario {identity.Id} name={identity.Name} data_version={identity.DataVersion} sep_enabled={Bool(identity.SepEnabled)}");
        foreach (var warning in scenario.Warnings)
        {
            log.Line($"scenario warning {warning}");
        }

        log.Line($"seed {Num(request.Seed)} prng {Pcg32.AlgorithmId} pipeline {SimulationPipeline.Version}");
        log.Line($"world {Num(engine.State.Width)}x{Num(engine.State.Height)} cells={Num(engine.State.CellCount)} delta_time_sols={Num(engine.Configuration.DeltaTimeSols)} validation={Bool(engine.Configuration.EnableValidation)}");
        log.Line($"steps {string.Join(' ', engine.SystemNames)}");
        log.Line($"requested_ticks {Num(request.Ticks)} history_interval {Num(request.HistoryIntervalTicks)}");

        var initialViolations = Domain.Validation.SimulationValidator.Validate(engine.State);
        string? failure = null;
        if (initialViolations.Count > 0)
        {
            failure = $"initial state failed validation ({Num(initialViolations.Count)} violations)";
            LogViolations(log, request.Seed, engine.State, SimulationEngine.ValidationStepName, initialViolations);
        }
        else
        {
            AppendHistory(resources, populations, engine.State);
            for (var step = 0; step < request.Ticks; step++)
            {
                try
                {
                    engine.Advance(1);
                }
                catch (SimulationInvariantException exception)
                {
                    failure = $"tick {Num(exception.Tick)} failed validation in {engine.FailedStep ?? "unknown"}";
                    LogViolations(log, request.Seed, engine.State, engine.FailedStep, exception.Violations);
                    break;
                }
                catch (Exception exception) when (exception is ArithmeticException or ArgumentException or InvalidOperationException)
                {
                    failure = $"tick {Num(engine.CurrentTick)} failed in {engine.FailedStep ?? "unknown"}: {exception.GetType().Name}";
                    log.Line($"ERROR seed={Num(request.Seed)} tick={Num(engine.CurrentTick)} system={engine.FailedStep ?? "unknown"} error={exception.GetType().Name} message={exception.Message}");
                    break;
                }

                var tick = engine.CurrentTick;
                if (tick % request.HistoryIntervalTicks == 0 || step + 1 == request.Ticks)
                {
                    AppendHistory(resources, populations, engine.State);
                }
            }
        }

        var snapshot = StateSnapshot.Capture(engine, identity);
        var snapshotBytes = StateSnapshotJson.Write(snapshot);
        var snapshotHash = StateSnapshotJson.Sha256Hex(snapshot);
        var succeeded = failure is null;
        log.Line($"final_tick {Num(engine.CurrentTick)}");
        log.Line($"final_snapshot_sha256 {snapshotHash}");
        log.Line(succeeded ? "status completed" : $"status failed: {failure}");

        var metadata = Metadata(request, identity, engine, succeeded, failure, snapshotHash);
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var files = new[]
        {
            new ExperimentFile(MetadataFile, metadata),
            new ExperimentFile(SnapshotFile, snapshotBytes),
            new ExperimentFile(ResourceHistoryFile, utf8.GetBytes(resources.ToString())),
            new ExperimentFile(PopulationHistoryFile, utf8.GetBytes(populations.ToString())),
            new ExperimentFile(LogFile, utf8.GetBytes(log.ToString())),
        };
        return new ExperimentResult(succeeded, engine.CurrentTick, failure, files);
    }

    private static void LogViolations(
        Log log,
        ulong seed,
        SimulationState state,
        string? system,
        IReadOnlyList<Domain.Validation.InvariantViolation> violations)
    {
        foreach (var violation in violations)
        {
            var cell = violation.CellIndex is { } index
                ? $"{Num(index)} (x={Num(index % state.Width)}, y={Num(index / state.Width)})"
                : "-";
            var population = violation.Population is { } id ? Num(id.Value) : "-";
            log.Line(
                $"VIOLATION {violation.InvariantId} seed={Num(seed)} tick={Num(violation.Tick)} cell={cell} population={population} system={system ?? "unknown"} quantity={violation.Quantity} value={Num(violation.Value)}");
        }
    }

    private static void AppendHistory(StringBuilder resources, StringBuilder populations, SimulationState state)
    {
        // Sums run over cells in ascending index order, so the float result is fixed.
        var totals = new double[ResourceColumns.Names.Count];
        var ice = 0.0;
        for (var cell = 0; cell < state.CellCount; cell++)
        {
            var quantities = state.Resources[cell].Quantities;
            for (var i = 0; i < totals.Length; i++)
            {
                totals[i] += quantities[i].Value;
            }

            ice += state.Environment[cell].WaterIce;
        }

        resources.Append(Num(state.Tick));
        foreach (var total in totals)
        {
            resources.Append(',').Append(Num(total));
        }

        resources.Append(',').Append(Num(ice)).Append('\n');

        var abundance = 0.0;
        var biomass = 0.0;
        foreach (var population in state.Populations)
        {
            abundance += population.Abundance;
            biomass += population.BiomassKg;
        }

        populations
            .Append(Num(state.Tick)).Append(',')
            .Append(Num(state.Populations.Count)).Append(',')
            .Append(Num(abundance)).Append(',')
            .Append(Num(biomass)).Append('\n');
    }

    private static byte[] Metadata(
        ExperimentRequest request,
        ScenarioIdentity identity,
        SimulationEngine engine,
        bool succeeded,
        string? failure,
        string snapshotHash)
    {
        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true, NewLine = "\n" }))
        {
            writer.WriteStartObject();
            writer.WriteString("schema", MetadataSchema);
            writer.WriteString("status", succeeded ? "completed" : "failed");
            if (failure is null)
            {
                writer.WriteNull("failure");
            }
            else
            {
                writer.WriteString("failure", failure);
            }

            writer.WriteString("scenario_id", identity.Id);
            writer.WriteString("scenario_name", identity.Name);
            writer.WriteString("scenario_data_version", identity.DataVersion);
            writer.WriteString("seed", Num(request.Seed));
            writer.WriteNumber("requested_ticks", request.Ticks);
            writer.WriteNumber("final_tick", engine.CurrentTick);
            writer.WriteNumber("history_interval_ticks", request.HistoryIntervalTicks);
            writer.WriteNumber("map_width", engine.State.Width);
            writer.WriteNumber("map_height", engine.State.Height);
            writer.WriteNumber("delta_time_sols", engine.Configuration.DeltaTimeSols);
            writer.WriteString("engine_version", EngineAssembly.Version);
            writer.WriteString("snapshot_schema", StateSnapshot.CurrentSchemaVersion);
            writer.WriteString("pipeline_version", SimulationPipeline.Version);
            writer.WriteString("prng_algorithm", Pcg32.AlgorithmId);
            writer.WriteString("runtime", request.Host.Runtime);
            if (request.Host.BuildSdk is null)
            {
                writer.WriteNull("build_sdk");
            }
            else
            {
                writer.WriteString("build_sdk", request.Host.BuildSdk);
            }

            writer.WriteString("operating_system", request.Host.OperatingSystem);
            writer.WriteString("final_snapshot_sha256", snapshotHash);
            writer.WriteStartArray("files");
            foreach (var name in FileNames)
            {
                writer.WriteStringValue(name);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        var bytes = new byte[buffer.WrittenCount + 1];
        buffer.WrittenSpan.CopyTo(bytes);
        bytes[^1] = (byte)'\n';
        return bytes;
    }

    private static string Num(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(ulong value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Num(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private static string Bool(bool value) => value ? "true" : "false";

    private sealed class Log
    {
        private readonly StringBuilder _text = new();

        public void Line(string line) => _text.Append(line).Append('\n');

        public override string ToString() => _text.ToString();
    }
}
