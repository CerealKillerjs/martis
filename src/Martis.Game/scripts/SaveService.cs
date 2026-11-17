using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Persistence.SQLite;
using Martis.Simulation.Engine;
using Martis.Simulation.Experiments;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;
using Martis.Simulation.Snapshots;

namespace Martis.Game;

/// <summary>
/// Saves and loads the game with the same SQLite store and binary snapshot format as
/// the CLI (ADR-015). The database lives in <c>user://martis.db</c>.
/// </summary>
public sealed class SaveService
{
    public const int DataVersion = 1;
    private readonly string _path;
    private readonly string _dataRoot;

    public SaveService(string dataRoot, string? path = null)
    {
        _dataRoot = dataRoot;
        _path = path ?? ProjectSettings.GlobalizePath("user://martis.db");
    }

    /// <summary>Id of the save game this run writes to, created on the first save.</summary>
    public string? CurrentId { get; set; }

    /// <summary>Event type of the section map (M4, ADR-038): which save holds each open section.</summary>
    public const string WorldEventType = "section-world";

    /// <summary>The section map stored with the snapshot the last load read, or null.</summary>
    public string? LastWorld { get; private set; }

    /// <summary>
    /// Stores a snapshot and, for a campaign mission, its progress as a
    /// <see cref="MissionSaveRecord.EventType"/> event at the same tick (H-31).
    /// </summary>
    public string Save(GameSession session, string? missionFile = null, string? expeditionJson = null, string? worldJson = null)
    {
        using var store = SaveGameStore.Open(_path);
        if (CurrentId is null || store.GetSaveGame(CurrentId) is null)
        {
            CurrentId = string.Create(CultureInfo.InvariantCulture, $"save-{store.ListSaveGames().Count + 1}");
            store.CreateSaveGame(
                CurrentId,
                string.Create(CultureInfo.InvariantCulture, $"{session.Mission?.Definition.Name ?? "Exploración libre"} semilla {session.Seed}"),
                EngineAssembly.Version,
                DataVersion,
                session.Seed,
                PrototypeExperiment.MarsBaselineId,
                SimulationPipeline.BiologyVersion);
        }

        if (!store.SnapshotTicks(CurrentId).Contains(session.Tick))
        {
            var snapshot = session.Capture();
            store.SaveSnapshot(CurrentId, session.Tick, snapshot.SchemaVersion, StateSnapshotBinary.Write(snapshot));
        }

        if (missionFile is not null && session.CaptureMission() is { } progress)
        {
            store.RecordEvent(CurrentId, session.Tick, MissionSaveRecord.EventType, new MissionSaveRecord(missionFile, progress, session.CaptureEconomy()).ToJson());
        }

        if (expeditionJson is not null)
        {
            // V5 (ADR-032): rover, knowledge, samples, stations and places, next to the snapshot.
            store.RecordEvent(CurrentId, session.Tick, Martis.Simulation.Expedition.ExpeditionState.EventType, expeditionJson);
        }

        if (worldJson is not null)
        {
            store.RecordEvent(CurrentId, session.Tick, WorldEventType, worldJson);
        }

        return CurrentId;
    }

    /// <summary>Loads the newest valid snapshot of one save game (a section of the world, M4), or null.</summary>
    public GameSession? LoadById(string id, out string message, out MissionSaveRecord? mission, Func<int, WorldOptions?>? worldFor, out string? expedition)
    {
        using var store = SaveGameStore.Open(_path);
        var save = store.GetSaveGame(id);
        return Load(store, save, out message, out mission, worldFor, out expedition);
    }

    /// <summary>Loads the newest valid snapshot of the most recent save, or null when there is none.</summary>
    /// <summary>
    /// <paramref name="mission"/> is the campaign mission the save belongs to, with its progress
    /// and economy, or null for an older save or free play, which load as before.
    /// </summary>
    public GameSession? LoadLatest(out string message, out MissionSaveRecord? mission, Func<int, WorldOptions?>? worldFor = null)
        => LoadLatest(out message, out mission, worldFor, out _);

    /// <summary>As above, plus the expedition saved at the snapshot's tick (V5), or null for an older save.</summary>
    public GameSession? LoadLatest(out string message, out MissionSaveRecord? mission, Func<int, WorldOptions?>? worldFor, out string? expedition)
    {
        using var store = SaveGameStore.Open(_path);
        var save = store.ListSaveGames().OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
        return Load(store, save, out message, out mission, worldFor, out expedition);
    }

    private GameSession? Load(SaveGameStore store, SaveGameRecord? save, out string message, out MissionSaveRecord? mission, Func<int, WorldOptions?>? worldFor, out string? expedition)
    {
        mission = null;
        expedition = null;
        LastWorld = null;
        if (save is null)
        {
            message = "No hay partidas guardadas.";
            return null;
        }

        var recovery = store.LoadLatestValid(save.Id, stored => TryRead(stored.Payload) is not null);
        if (recovery.Snapshot is null)
        {
            message = $"La partida {save.Name} no tiene ninguna instantánea válida.";
            return null;
        }

        var snapshot = TryRead(recovery.Snapshot.Payload)!;
        CurrentId = save.Id;
        var skipped = recovery.RejectedTicks.Count == 0 ? string.Empty : $" ({recovery.RejectedTicks.Count} instantáneas dañadas descartadas)";
        message = $"Cargada «{save.Name}», sol {snapshot.Tick}{skipped}.";
        var world = worldFor?.Invoke(snapshot.Configuration.MapWidth);
        var events = store.Events(save.Id, snapshot.Tick - 1).Where(item => item.Tick == snapshot.Tick).ToArray();
        expedition = events.LastOrDefault(item => item.EventType == Martis.Simulation.Expedition.ExpeditionState.EventType)?.Payload;
        LastWorld = events.LastOrDefault(item => item.EventType == WorldEventType)?.Payload;
        var saved = events.LastOrDefault(item => item.EventType == MissionSaveRecord.EventType);
        if (saved is not null)
        {
            try
            {
                var record = MissionSaveRecord.FromJson(saved.Payload);
                mission = record;
                return GameSession.Resume(_dataRoot, snapshot, record.MissionFile, record.Progress, world);
            }
            catch (System.IO.InvalidDataException exception)
            {
                message += $" El progreso de la misión no se pudo leer ({exception.Message}) y empieza de cero.";
            }
        }

        return GameSession.Resume(_dataRoot, snapshot, withMission: true, world);
    }

    private static StateSnapshot? TryRead(byte[] payload)
    {
        try
        {
            return StateSnapshotBinary.Read(payload);
        }
        catch (SnapshotCorruptException)
        {
            return null;
        }
    }
}
