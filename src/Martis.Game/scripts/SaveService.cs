using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Persistence.SQLite;
using Martis.Simulation.Engine;
using Martis.Simulation.Experiments;
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

    public string Save(GameSession session)
    {
        using var store = SaveGameStore.Open(_path);
        if (CurrentId is null || store.GetSaveGame(CurrentId) is null)
        {
            CurrentId = string.Create(CultureInfo.InvariantCulture, $"save-{store.ListSaveGames().Count + 1}");
            store.CreateSaveGame(
                CurrentId,
                string.Create(CultureInfo.InvariantCulture, $"FIRST COLONY semilla {session.Seed}"),
                EngineAssembly.Version,
                DataVersion,
                session.Seed,
                PrototypeExperiment.MarsBaselineId,
                SimulationPipeline.BiologyVersion);
        }

        if (store.SnapshotTicks(CurrentId).Contains(session.Tick))
        {
            return CurrentId;
        }

        var snapshot = session.Capture();
        store.SaveSnapshot(CurrentId, session.Tick, snapshot.SchemaVersion, StateSnapshotBinary.Write(snapshot));
        return CurrentId;
    }

    /// <summary>Loads the newest valid snapshot of the most recent save, or null when there is none.</summary>
    public GameSession? LoadLatest(out string message)
    {
        using var store = SaveGameStore.Open(_path);
        var save = store.ListSaveGames().OrderByDescending(item => item.UpdatedAt).FirstOrDefault();
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
        return GameSession.Resume(_dataRoot, snapshot);
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
