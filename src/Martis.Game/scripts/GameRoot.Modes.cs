using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Application;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// V1 (Documento Maestro v2.0 §3): the AI is present in one world through several modes:
/// exploration (the rover, F1 or Tab), strategic (layers and orders, F2), laboratory (F3) and
/// planetary (F4). Switching only changes camera and panels; the simulation, the selection and
/// the layer stay as they are. Esc goes back to the previous mode. Orders go to a register.
/// </summary>
public partial class GameRoot
{
    private readonly ModeController _modes = new();
    private readonly TimeController _time = new();
    private readonly OrderBook _orders = new();
    private ModeBar? _modeBar;
    private PlanetaryPanel? _planetary;
    private double _planetaryClock;

    private bool Exploring => _modes.Current == GameMode.Exploration;

    /// <summary>Mode keys are read before the interface so a focused button cannot swallow them.</summary>
    public override void _Input(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key || _session is null || _menu.Visible)
        {
            return;
        }

        GameMode? target = key.Keycode switch
        {
            Key.F1 => GameMode.Exploration,
            Key.F2 => GameMode.Strategic,
            Key.F3 => GameMode.Laboratory,
            Key.F4 => GameMode.Planetary,
            _ => null,
        };
        if (key.Keycode == Key.Tab)
        {
            Changed(_modes.Toggle());
            GetViewport().SetInputAsHandled();
        }
        else if (target is { } mode)
        {
            Changed(_modes.Switch(mode));
            GetViewport().SetInputAsHandled();
        }
    }

    private void SwitchMode(GameMode mode) => Changed(_modes.Switch(mode));

    private void BackFromMode()
    {
        if (_modes.Current is GameMode.Laboratory or GameMode.Planetary)
        {
            Changed(_modes.Back());
        }
    }

    private void Changed(ModeChange? change)
    {
        if (change is not { } done)
        {
            return;
        }

        ApplyMode();
        Act(done.To switch
        {
            GameMode.Exploration => "rover-view",
            GameMode.Strategic => "orbital-view",
            GameMode.Laboratory => "laboratory-view",
            _ => "planetary-view",
        });
    }

    /// <summary>Camera, panels and time limit of the current mode.</summary>
    private void ApplyMode()
    {
        EnsureModeUi();
        var mode = _modes.Current;
        _time.Cap(ModeController.MaxSpeed(mode));
        _hud.SetSpeed(_time.EffectiveSpeed);
        _modeBar!.Show(mode);
        _hud.SetSamplesVisible(mode == GameMode.Laboratory);
        _planetary!.Visible = mode == GameMode.Planetary;
        if (_gauge is not null)
        {
            _gauge.Visible = mode is GameMode.Exploration or GameMode.Strategic;
        }
        switch (mode)
        {
            case GameMode.Exploration:
                _hud.SetPanels(mission: true, inspector: false, lab: false, feed: false);
                _world.Camera.BeginChase();
                break;
            case GameMode.Strategic:
                _hud.SetPanels(mission: true, inspector: true, lab: true, feed: true);
                _world.Camera.EndChase();
                if (_rover is not null)
                {
                    _world.Camera.FocusOn(_world.Rover.Position);
                }

                break;
            case GameMode.Laboratory:
                _hud.SetPanels(mission: false, inspector: true, lab: true, feed: false);
                _world.Camera.EndChase();
                if (_selected.X >= 0)
                {
                    _world.Camera.FocusOn(_world.Ground.CellCenter(_selected.X, _selected.Y));
                }

                break;
            case GameMode.Planetary:
                _hud.SetPanels(mission: false, inspector: false, lab: false, feed: false);
                _world.Camera.EndChase();
                UpdatePlanetary(force: true);
                break;
        }

        if (_menu.Visible)
        {
            SetModeUiVisible(false);
        }
    }

    private void EnsureModeUi()
    {
        if (_modeBar is not null)
        {
            return;
        }

        var layer = _hud.GetParent();
        _planetary = new PlanetaryPanel { Name = "Planetary", Visible = false };
        layer.AddChild(_planetary);
        layer.MoveChild(_planetary, 0); // behind the HUD: the top bar stays usable
        _modeBar = new ModeBar { Name = "ModeBar" };
        layer.AddChild(_modeBar);
        _modeBar.ModeChosen += SwitchMode;
    }

    /// <summary>The campaign menu covers the mode bar, the rover gauge and the planetary view.</summary>
    private void SetModeUiVisible(bool visible)
    {
        if (_modeBar is null)
        {
            return;
        }

        _modeBar.Visible = visible;
        if (visible)
        {
            ApplyMode();
        }
        else
        {
            _planetary!.Visible = false;
            _hud.SetSamplesVisible(false);
            if (_gauge is not null)
            {
                _gauge.Visible = false;
            }
        }
    }

    /// <summary>F in exploration: seeds the laboratory's design under the rover.</summary>
    private void SeedHere()
    {
        var (x, y) = _rover.Cell;
        Select(x, y);
        Deploy(x, y);
        Act("seed-from-rover");
    }

    private void Resolve(Order order)
    {
        var verb = order.Kind switch
        {
            "deploy" => "Siembra",
            "analyze" => "Análisis",
            "scan" => "Escaneo",
            "sample" => "Muestreo",
            "move" => "Desplazamiento",
            "build" => "Montaje",
            "repair" => "Reparación",
            _ => order.Kind,
        };
        var state = order.Status == OrderStatus.Done ? "✓" : "✗";
        _hud.AddEvents([new SessionEvent(order.ResolvedTick ?? order.IssuedTick, order.Status == OrderStatus.Done ? "order" : "failure", $"Orden #{order.Id} {state} {verb} {order.Target} ({order.Actor}): {order.Result}")]);
    }

    /// <summary>Global balances for the planetary view, refreshed a few times per second.</summary>
    private void UpdatePlanetary(bool force = false)
    {
        if (_planetary is null || !_planetary.Visible || _view is null)
        {
            return;
        }

        var now = Time.GetTicksMsec() / 1000.0;
        if (!force && now - _planetaryClock < 0.25)
        {
            return;
        }

        _planetaryClock = now;
        var view = _view;
        var living = view.BiomassKg.Count(kg => kg > 1e-7f);
        _planetary.Show(new PlanetaryData(
            view.Tick,
            view.TotalBiomassKg,
            view.PopulationCount,
            living / (double)view.BiomassKg.Length,
            view.TemperatureKelvin.Average(k => (double)k),
            view.TemperatureKelvin.Min(),
            view.TemperatureKelvin.Max(),
            view.WaterKg.Sum(kg => (double)kg),
            view.RadiationSvPerSol.Average(sv => (double)sv) * 1000.0,
            _session.Research?.Points,
            _orders.All.Count,
            _orders.All.Count(order => order.Status == OrderStatus.Failed),
            _session.Intelligence,
            _knowledge.Coverage(Martis.Simulation.Expedition.KnowledgeLevel.Estimated),
            _knowledge.Coverage(Martis.Simulation.Expedition.KnowledgeLevel.Measured),
            _network.Stations.Count,
            _network.Stations.Count(station => !station.Broken),
            _rover.Wear,
            _pois.Pois.Count(poi => poi.State == Martis.Simulation.Expedition.PoiState.Discovered),
            _pois.Pois.Count));
    }
}
