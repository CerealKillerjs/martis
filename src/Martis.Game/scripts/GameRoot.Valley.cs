using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Expedition;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// V5 (Documento Maestro v2.0 §32, ADR-032): the parametrized valley. Its points of interest appear
/// as anomalies once the region is estimated and reveal their finding, and research, when the rover
/// or a station measures them. The whole expedition (rover, knowledge, samples, stations, places)
/// is saved with the game, so a load restores the run exactly.
/// </summary>
public partial class GameRoot
{
    private readonly List<PoiMarker> _poiMarkers = [];
    private ValleyDefinition? _valleyDefinition;
    private PoiField _pois = null!;

    private void ResetValley()
    {
        _valleyDefinition ??= ValleyDefinition.LoadPublished(_dataRoot);
        _pois = new PoiField(_valleyDefinition, _session.Width, _session.Height);
        foreach (var marker in _poiMarkers)
        {
            marker.QueueFree();
        }

        _poiMarkers.Clear();
        foreach (var poi in _pois.Pois)
        {
            var marker = new PoiMarker { Name = $"Poi_{poi.Definition.Id}" };
            _world.AddChild(marker);
            marker.Position = _world.CellPoint(poi.X + 0.5, poi.Y + 0.5);
            _poiMarkers.Add(marker);
        }

        _pois.Update(_knowledge); // what the probe saw before the session started is not news
        ShowPois();
    }

    /// <summary>New knowledge may reveal anomalies and places.</summary>
    private void UpdatePois(bool announce = true)
    {
        var changed = _pois.Update(_knowledge);
        foreach (var poi in changed.Where(_ => announce))
        {
            var cell = (poi.Y * _session.Width) + poi.X;
            if (poi.State == PoiState.Sighted)
            {
                _hud.AddEvents([new SessionEvent(_session.Tick, "survey", string.Create(CultureInfo.InvariantCulture, $"Anomalía en ({poi.X}, {poi.Y}): los datos estimados no encajan. Merece una visita del rover."), cell)]);
                continue;
            }

            _session.Research?.Earn(poi.Definition.Research);
            var reward = _session.Research is null ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" +{poi.Definition.Research:0} PI.");
            _hud.AddEvents([new SessionEvent(_session.Tick, "mission", $"Descubierto: {poi.Definition.Name}. {poi.Definition.Finding}{reward}", cell)]);
            _hud.Toast($"Descubierto: {poi.Definition.Name}.{reward}");
            _world.Pulse(cell, _session.Width, new Color(0.45f, 1f, 0.6f));
            Act("discover-poi");
        }

        if (changed.Count > 0)
        {
            ShowPois();
        }
    }

    private void ShowPois()
    {
        for (var i = 0; i < _poiMarkers.Count; i++)
        {
            _poiMarkers[i].Show(_pois.Pois[i]);
        }
    }

    private string CaptureExpedition() => ExpeditionState.Capture(_rover, _knowledge, _samples, _network, _pois, _climate).ToJson();

    /// <summary>After a load: the saved expedition replaces the reconstructed one.</summary>
    private void RestoreExpedition(string? json)
    {
        if (json is null)
        {
            return;
        }

        try
        {
            var state = ExpeditionState.FromJson(json);
            state.Apply(_rover, _knowledge, _samples, _network, _pois);
            if (state.Climate is { } climate)
            {
                ResetClimate(climate);
            }
        }
        catch (System.IO.InvalidDataException exception)
        {
            _hud.Toast($"La expedición guardada no se pudo leer ({exception.Message}): se reconstruye desde la sonda.", bad: true);
            return;
        }

        if (_rover.Lander is { } lander)
        {
            _landed = true;
            _world.PlaceLander(lander.X, lander.Y);
        }

        foreach (var model in _stationModels.Values)
        {
            model.QueueFree();
        }

        _stationModels.Clear();
        ShowStations();
        ShowPois();
        _hud.ShowSamples(_samples, _session.Tick);
        _shownKnowledge = -1;
        ShowKnowledge();
        PoseRover(0f);
    }
}
