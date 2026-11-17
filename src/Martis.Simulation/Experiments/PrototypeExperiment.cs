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
    HostInfo Host)
{
    /// <summary>Populations inoculated before the first tick, applied in list order (TDD §5.1).</summary>
    public IReadOnlyList<PopulationIntroduction> Introductions { get; init; } = [];

    /// <summary>
    /// Saved state to continue from (T07). The run restores it before any introduction and
    /// then advances <see cref="Ticks"/> more ticks. Its world seed must be <see cref="Seed"/>.
    /// </summary>
    public StateSnapshot? Resume { get; init; }

    /// <summary>Ticks between saved snapshots when a save sink is given; 0 saves only the final state.</summary>
    public int SnapshotIntervalTicks { get; init; }

    /// <summary>Mission to follow during the run (T09); null runs without one.</summary>
    public Missions.MissionDefinition? Mission { get; init; }

    /// <summary>Player actions reported to the mission before the first tick (for example <c>analyze-region</c>).</summary>
    public IReadOnlyList<string> MissionActions { get; init; } = [];
}

/// <summary>
/// Where a run puts its save data (T07, TDD §9.2): full snapshots every interval and the
/// events between them. The CLI writes them to SQLite; Simulation does not know the store.
/// </summary>
public interface ISaveSink
{
    /// <summary>A binary payload from <see cref="StateSnapshotBinary.Write"/>.</summary>
    void SaveSnapshot(long tick, string schemaVersion, byte[] payload);

    /// <summary>One event of the tick, as a JSON object.</summary>
    void RecordEvent(long tick, string eventType, string payloadJson);
}

/// <summary>
/// One inoculation for <see cref="ExperimentRequest"/>: <paramref name="BiomassKg"/> of
/// <paramref name="OrganismId"/> placed in <paramref name="CellIndex"/> before tick 1.
/// Hosts use this instead of the Contracts command, so they depend on Simulation only.
/// </summary>
public sealed record PopulationIntroduction(
    string OrganismId,
    int CellIndex,
    double BiomassKg,
    IReadOnlyDictionary<string, double>? GeneOverrides = null);

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
    public const string MassBalanceFile = "mass_balance.csv";
    public const string MutationsFile = "mutations.csv";

    /// <summary>Written only when the request names a mission.</summary>
    public const string MissionFile = "mission.json";

    /// <summary>Files every run produces, in write order.</summary>
    public static IReadOnlyList<string> FileNames { get; } =
        [MetadataFile, SnapshotFile, ResourceHistoryFile, PopulationHistoryFile, MassBalanceFile, MutationsFile, LogFile];

    /// <summary>
    /// Runs the flow. <paramref name="engineFactory"/> defaults to
    /// <see cref="MarsBaselineWorld.Create(MarsBaselineScenario, ulong, int, int)"/>;
    /// tests pass another one to inject a fault.
    /// </summary>
    public static ExperimentResult Run(
        ExperimentRequest request,
        MarsBaselineScenario scenario,
        Func<MarsBaselineScenario, ulong, SimulationEngine>? engineFactory = null,
        Biology.BiologyCatalog? biology = null,
        ISaveSink? save = null)
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
        SimulationEngine engine;
        Biology.MetabolismWorkspace? workspace = null;
        if (engineFactory is not null)
        {
            engine = engineFactory(scenario, request.Seed);
        }
        else if (biology is not null)
        {
            (engine, workspace) = MarsBaselineWorld.CreateWithBiology(scenario, biology, request.Seed);
        }
        else
        {
            engine = MarsBaselineWorld.Create(scenario, request.Seed);
        }

        if (request.Resume is { } resume)
        {
            if (resume.Configuration.WorldSeed != request.Seed)
            {
                throw new ArgumentException("The saved state was made with another seed.", nameof(request));
            }

            resume.Restore(engine);
        }

        foreach (var introduction in request.Introductions)
        {
            engine.ApplyCommand(new Martis.Contracts.Commands.IntroducePopulationCommand(
                introduction.OrganismId, introduction.CellIndex, introduction.BiomassKg, introduction.GeneOverrides));
        }

        Missions.MissionTracker? mission = null;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        if (request.Mission is { } definition)
        {
            mission = new Missions.MissionTracker(definition, biology ?? workspace?.Catalog
                ?? throw new ArgumentException("A mission needs the biology catalog.", nameof(request)));
            foreach (var action in request.MissionActions)
            {
                mission.RecordAction(action, engine.CurrentTick);
            }
        }

        var balance = new MassBalanceWriter(biology ?? workspace?.Catalog);
        var log = new Log();
        var resources = new StringBuilder();
        var populations = new StringBuilder();
        var mutations = new StringBuilder("tick,cell,organism,parent_id,child_id,child_biomass_kg,changes\n");
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

        log.Line($"seed {Num(request.Seed)} prng {Pcg32.AlgorithmId} pipeline {engine.PipelineVersion}");
        foreach (var introduction in request.Introductions)
        {
            log.Line($"introduce {introduction.OrganismId} cell={Num(introduction.CellIndex)} biomass_kg={Num(introduction.BiomassKg)}");
        }

        log.Line($"world {Num(engine.State.Width)}x{Num(engine.State.Height)} cells={Num(engine.State.CellCount)} delta_time_sols={Num(engine.Configuration.DeltaTimeSols)} validation={Bool(engine.Configuration.EnableValidation)}");
        log.Line($"steps {string.Join(' ', engine.SystemNames)}");
        log.Line($"requested_ticks {Num(request.Ticks)} history_interval {Num(request.HistoryIntervalTicks)}");

        var initialViolations = Domain.Validation.SimulationValidator.Validate(engine.State);
        string? failure = null;
        long? lastSaved = null;
        if (initialViolations.Count > 0)
        {
            failure = $"initial state failed validation ({Num(initialViolations.Count)} violations)";
            LogViolations(log, request.Seed, engine.State, SimulationEngine.ValidationStepName, initialViolations);
        }
        else
        {
            AppendHistory(resources, populations, engine.State);
            balance.Row(engine.State, workspace);
            if (save is not null && request.Resume is null && engine.PendingCommandCount == 0)
            {
                Save(save, engine, identity);
                lastSaved = engine.CurrentTick;
            }

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
                catch (Exception exception) when (exception is ArithmeticException or ArgumentException or InvalidOperationException or KeyNotFoundException)
                {
                    failure = $"tick {Num(engine.CurrentTick)} failed in {engine.FailedStep ?? "unknown"}: {exception.GetType().Name}";
                    log.Line($"ERROR seed={Num(request.Seed)} tick={Num(engine.CurrentTick)} system={engine.FailedStep ?? "unknown"} error={exception.GetType().Name} message={exception.Message}");
                    break;
                }

                balance.Accumulate(workspace);
                AppendMutations(mutations, workspace);
                var tick = engine.CurrentTick;
                if (mission is not null)
                {
                    mission.Observe(engine.State, workspace);
                    LogMission(log, mission, reported);
                }

                if (save is not null)
                {
                    RecordEvents(save, tick, workspace);
                    if (request.SnapshotIntervalTicks > 0 && tick % request.SnapshotIntervalTicks == 0)
                    {
                        Save(save, engine, identity);
                        lastSaved = tick;
                    }
                }

                if (tick % request.HistoryIntervalTicks == 0 || step + 1 == request.Ticks)
                {
                    AppendHistory(resources, populations, engine.State);
                    balance.Row(engine.State, workspace);
                }
            }
        }

        var discarded = engine.DiscardPendingCommands();
        if (discarded > 0)
        {
            log.Line($"discarded {Num(discarded)} queued commands");
        }

        var snapshot = StateSnapshot.Capture(engine, identity);
        if (save is not null && failure is null && lastSaved != engine.CurrentTick)
        {
            save.SaveSnapshot(engine.CurrentTick, snapshot.SchemaVersion, StateSnapshotBinary.Write(snapshot));
        }

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
            new ExperimentFile(MassBalanceFile, utf8.GetBytes(balance.ToString())),
            new ExperimentFile(MutationsFile, utf8.GetBytes(mutations.ToString())),
            new ExperimentFile(LogFile, utf8.GetBytes(log.ToString())),
        };
        if (mission is not null)
        {
            files = [.. files.Take(files.Length - 1), new ExperimentFile(MissionFile, MissionJson(mission)), files[^1]];
        }

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
            writer.WriteString("pipeline_version", engine.PipelineVersion);
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

    private static void LogMission(Log log, Missions.MissionTracker mission, HashSet<string> reported)
    {
        foreach (var status in mission.Status())
        {
            if (status.CompletedTick is { } tick && reported.Add(status.Id))
            {
                log.Line($"mission {mission.Definition.Id} objective {status.Id} completed tick={Num(tick)}");
            }
        }

        if (mission.IsComplete && reported.Add("*"))
        {
            log.Line($"mission {mission.Definition.Id} complete tick={Num(mission.CompletedTick!.Value)}");
        }
    }

    private static byte[] MissionJson(Missions.MissionTracker mission)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            writer.WriteString("mission", mission.Definition.Id);
            writer.WriteString("name", mission.Definition.Name);
            writer.WriteBoolean("complete", mission.IsComplete);
            if (mission.CompletedTick is { } done)
            {
                writer.WriteNumber("completed_tick", done);
            }
            else
            {
                writer.WriteNull("completed_tick");
            }

            writer.WriteStartArray("objectives");
            foreach (var status in mission.Status())
            {
                writer.WriteStartObject();
                writer.WriteString("id", status.Id);
                writer.WriteString("title", status.Title);
                if (status.CompletedTick is { } tick)
                {
                    writer.WriteNumber("completed_tick", tick);
                }
                else
                {
                    writer.WriteNull("completed_tick");
                }

                writer.WriteNumber("progress", status.Progress);
                writer.WriteNumber("target", status.Target);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void Save(ISaveSink save, SimulationEngine engine, ScenarioIdentity identity)
    {
        var snapshot = StateSnapshot.Capture(engine, identity);
        save.SaveSnapshot(engine.CurrentTick, snapshot.SchemaVersion, StateSnapshotBinary.Write(snapshot));
    }

    /// <summary>Mutation and extinction events of the tick, as compact JSON objects.</summary>
    private static void RecordEvents(ISaveSink save, long tick, Biology.MetabolismWorkspace? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        foreach (var item in workspace.MutationEvents)
        {
            save.RecordEvent(tick, "mutation", Json(writer =>
            {
                writer.WriteNumber("cell", item.CellIndex);
                writer.WriteString("organism", item.OrganismId);
                writer.WriteNumber("parent_id", item.ParentId);
                writer.WriteNumber("child_id", item.ChildId);
                writer.WriteNumber("child_biomass_kg", item.ChildBiomassKg);
                writer.WriteStartArray("changes");
                foreach (var change in item.Changes)
                {
                    writer.WriteStartObject();
                    writer.WriteString("gene", change.GeneId);
                    writer.WriteNumber("before", change.Before);
                    writer.WriteNumber("after", change.After);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }));
        }

        foreach (var id in workspace.Extinct)
        {
            save.RecordEvent(tick, "extinction", Json(writer => writer.WriteNumber("population_id", id)));
        }
    }

    private static string Json(Action<Utf8JsonWriter> body)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            body(writer);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>One row per new variant; <c>changes</c> is <c>GENE:before&gt;after</c> joined by ';'.</summary>
    private static void AppendMutations(StringBuilder text, Biology.MetabolismWorkspace? workspace)
    {
        if (workspace is null)
        {
            return;
        }

        foreach (var item in workspace.MutationEvents)
        {
            text.Append(Num(item.Tick)).Append(',')
                .Append(Num(item.CellIndex)).Append(',')
                .Append(item.OrganismId).Append(',')
                .Append(Num(item.ParentId)).Append(',')
                .Append(Num(item.ChildId)).Append(',')
                .Append(Num(item.ChildBiomassKg)).Append(',')
                .Append(string.Join(';', item.Changes.Select(change => $"{change.GeneId}:{Num(change.Before)}>{Num(change.After)}")))
                .Append('\n');
        }
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

    /// <summary>
    /// mass_balance.csv: element masses summed over the world (kg), the cumulative external
    /// inflow, and the worst per-cell element error seen since the previous row (VAL-007).
    /// Without a biology catalog the file has only its header.
    /// </summary>
    private sealed class MassBalanceWriter
    {
        private readonly Biology.BiologyCatalog? _catalog;
        private readonly StringBuilder _text = new();
        private double _inflow;
        private double _worst;
        private double _fixed;
        private double _light;
        private double _died;
        private double _recycled;
        private double _maintenance;
        private double _unpaid;
        private int _extinctions;

        public MassBalanceWriter(Biology.BiologyCatalog? catalog)
        {
            _catalog = catalog;
            _text.Append("tick");
            foreach (var element in catalog?.Elements ?? [])
            {
                _text.Append(',').Append(element).Append("_kg");
            }

            _text.Append(",total_kg,external_inflow_kg,carbon_fixed_mol,light_energy_j,maintenance_j,unpaid_maintenance_j,biomass_died_kg,biomass_recycled_kg,extinctions,max_element_error_kg\n");
        }

        public void Accumulate(Biology.MetabolismWorkspace? workspace)
        {
            if (workspace is null)
            {
                return;
            }

            _inflow += workspace.Totals.ExternalInflowKg;
            _worst = Math.Max(_worst, workspace.Totals.MaxElementErrorKg);
            _fixed += workspace.Totals.CarbonFixedMol;
            _light += workspace.Totals.LightEnergyJ;
            _maintenance += workspace.Totals.MaintenanceJ;
            _unpaid += workspace.Totals.UnpaidMaintenanceJ;
            _died += workspace.Totals.BiomassDiedKg;
            _recycled += workspace.Totals.BiomassRecycledKg;
            _extinctions += workspace.Totals.Extinctions;
        }

        public void Row(SimulationState state, Biology.MetabolismWorkspace? workspace)
        {
            if (_catalog is null || workspace is null)
            {
                return;
            }

            var perCell = new double[state.CellCount * _catalog.Elements.Count];
            workspace.ElementTotals(state, perCell);
            var totals = new double[_catalog.Elements.Count];
            for (var i = 0; i < perCell.Length; i++)
            {
                totals[i % totals.Length] += perCell[i];
            }

            _text.Append(Num(state.Tick));
            var sum = 0.0;
            foreach (var total in totals)
            {
                _text.Append(',').Append(Num(total));
                sum += total;
            }

            _text.Append(',').Append(Num(sum))
                .Append(',').Append(Num(_inflow))
                .Append(',').Append(Num(_fixed))
                .Append(',').Append(Num(_light))
                .Append(',').Append(Num(_maintenance))
                .Append(',').Append(Num(_unpaid))
                .Append(',').Append(Num(_died))
                .Append(',').Append(Num(_recycled))
                .Append(',').Append(Num(_extinctions))
                .Append(',').Append(Num(_worst)).Append('\n');
            _worst = 0;
        }

        public override string ToString() => _text.ToString();
    }
}
