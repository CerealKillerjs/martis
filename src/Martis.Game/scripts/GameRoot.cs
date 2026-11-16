using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Audio;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// Entry point of the game (T08, ADR-017). Owns the <see cref="GameSession"/>, steps it
/// on the main thread (at most one sol per frame, ADR-000 R4 keeps threads out of src)
/// and wires the 3D world and the HUD. Command-line options after <c>--</c>:
/// <c>--seed=N</c>, <c>--shot=path.png</c> (scripted demo, screenshot, quit),
/// <c>--smoke</c> (headless check: play 30 sols, save, load, quit with code 0/1).
/// </summary>
public partial class GameRoot : Node
{
    /// <summary>Real seconds per sol for each speed button.</summary>
    private static readonly double[] SecondsPerSol = [double.PositiveInfinity, 2.0, 0.5, 0.125];

    private string _dataRoot = string.Empty;
    private GameSession _session = null!;
    private World3D _world = null!;
    private Hud _hud = null!;
    private SaveService _saves = null!;
    private int _speed = 1;
    private double _solProgress;
    private long _seenEvents;
    private (int X, int Y) _selected = (-1, -1);
    private bool _celebrated;
    private Dictionary<string, string> _options = new(StringComparer.Ordinal);

    public override void _Ready()
    {
        _options = OS.GetCmdlineUserArgs()
            .Select(arg => arg.TrimStart('-').Split('=', 2))
            .ToDictionary(parts => parts[0], parts => parts.Length > 1 ? parts[1] : string.Empty, StringComparer.Ordinal);
        try
        {
            _dataRoot = DataRoot.Find();
        }
        catch (Exception exception)
        {
            GD.PushError(exception.Message);
            GetTree().Quit(1);
            return;
        }

        _saves = new SaveService(_dataRoot, _options.TryGetValue("db", out var db) ? db : null);
        var seed = _options.TryGetValue("seed", out var text) && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 5UL;
        StartSession(GameSession.NewGame(_dataRoot, seed));

        if (_options.ContainsKey("smoke"))
        {
            CallDeferred(MethodName.Smoke);
        }
        else if (_options.TryGetValue("shot", out var shot))
        {
            CallDeferred(MethodName.Showcase, shot);
        }
        else if (GameLaunch.Instance?.Mode == GameLaunch.LoadLatest)
        {
            // Title screen «Cargar partida».
            Load();
        }
        else
        {
            _hud.Toast("Bienvenido a Marte. Analiza una región, diseña un microbio y despliega tu primera colonia.");
        }
    }

    public override void _Process(double delta)
    {
        if (_session is null)
        {
            return;
        }

        var seconds = SecondsPerSol[_speed];
        if (_session.Failure is null && !double.IsInfinity(seconds))
        {
            _solProgress += delta / seconds;
            if (_solProgress >= 1.0)
            {
                _solProgress -= 1.0;
                _session.Advance(1);
                Refresh();
            }

            _solProgress = Math.Min(_solProgress, 1.0);
        }

        _world.TimeOfDay = (float)((_solProgress + 0.25) % 1.0);
        _hud.ShowStatus(_view ?? _session.View(), _world.TimeOfDay);
        GameAudio.Instance?.Observe(new MusicContext
        {
            TimeOfDay = _world.TimeOfDay,
            BiomassKg = (_view ?? _session.View()).TotalBiomassKg,
            BiomassGrowthPerSol = _growth,
            SecondsPerSol = SecondsPerSol[_speed],
            Failure = _session.Failure is not null,
        });
    }

    private WorldView? _view;

    /// <summary>Smoothed relative biomass change per sol, for the music director (ADR-018).</summary>
    private double _growth;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Space:
                    GameAudio.Instance?.Play(_speed == 0 ? Sfx.Resume : Sfx.Pause);
                    SetSpeed(_speed == 0 ? 1 : 0);
                    break;
                case Key.Escape:
                    _hud.EndDeploy();
                    break;
                case Key.F5:
                    Save();
                    break;
                case Key.F9:
                    Load();
                    break;
                case >= Key.Key0 and <= Key.Key4:
                    var overlay = (Overlay)(key.Keycode - Key.Key0);
                    _hud.SelectOverlay(overlay);
                    SetOverlay(overlay);
                    break;
            }
        }

        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } click)
        {
            var camera = _world.Camera;
            var origin = camera.ProjectRayOrigin(click.Position);
            var direction = camera.ProjectRayNormal(click.Position);
            if (_world.Ground.Raycast(origin, direction, out var hit) && _world.Ground.CellAt(hit, out var x, out var y))
            {
                if (_hud.DeployMode)
                {
                    Deploy(x, y);
                }

                Select(x, y);
            }
        }
    }

    private void StartSession(GameSession session)
    {
        _session = session;
        _seenEvents = 0;
        _celebrated = session.Mission?.IsComplete ?? false;
        if (_world is null)
        {
            _world = new World3D { Name = "World" };
            AddChild(_world);
            _world.Build(session.Width, session.Height, session.Seed);
            var layer = new CanvasLayer { Name = "UI" };
            AddChild(layer);
            _hud = new Hud { Name = "Hud" };
            layer.AddChild(_hud);
            _hud.SpeedChanged += SetSpeed;
            _hud.OverlayChanged += SetOverlay;
            _hud.SaveRequested += Save;
            _hud.LoadRequested += Load;
            _hud.NewGameRequested += NewGame;
            _hud.AnalyzeRequested += Analyze;
            _hud.ExperimentRequested += Experiment;
            _hud.DeployToggled += on => _hud.Toast(on ? "Elige una celda para la colonia." : "Despliegue cancelado.");
            _hud.SetOrganisms(session.Organisms);
            SetSpeed(1);
        }

        Select(session.Width / 2, session.Height / 2);
        Refresh();
        var colony = Array.FindIndex(_view!.BiomassKg, kg => kg > 0f);
        if (colony >= 0)
        {
            _world.PlaceLander(colony % session.Width, colony / session.Width);
        }
    }

    private void Refresh()
    {
        var previous = _view;
        _view = _session.View();
        if (previous is not null && _view.Tick > previous.Tick)
        {
            var growth = MusicDirector.Growth(previous.TotalBiomassKg, _view.TotalBiomassKg, (int)(_view.Tick - previous.Tick));
            _growth += 0.2 * (growth - _growth);
        }

        _world.Show(_view);
        _hud.ShowMission(_session.Mission);
        if (_selected.X >= 0)
        {
            _hud.ShowCell(_session.Inspect(_selected.X, _selected.Y));
        }

        var fresh = (int)Math.Min(_session.TotalEvents - _seenEvents, _session.Events.Count);
        _seenEvents = _session.TotalEvents;
        var events = _session.Events.Skip(_session.Events.Count - fresh).ToArray();
        _hud.AddEvents(events);
        foreach (var item in events)
        {
            var tint = item.Kind switch
            {
                "mutation" => new Color(1f, 0.82f, 0.3f),
                "deploy" => new Color(0.4f, 0.85f, 1f),
                _ => Colors.Transparent,
            };
            if (tint.A > 0)
            {
                _world.Pulse(item.Cell, _session.Width, tint);
            }
        }

        PlayEventSound(events);

        if (_session.Failure is not null)
        {
            SetSpeed(0);
            _hud.Toast($"Simulación detenida: {_session.Failure}. Carga una partida para seguir.", bad: true);
        }

        if (!_celebrated && _session.Mission is { IsComplete: true } mission)
        {
            _celebrated = true;
            GameAudio.Instance?.Play(Sfx.Victory);
            GameAudio.Instance?.Celebrate();
            _hud.Banner("¡PRIMERA COLONIA!", $"{mission.Definition.Name} completada en el sol {_session.Tick}. La vida terrestre diseñada por ti se sostiene en Marte.");
        }
    }

    private void SetSpeed(int index)
    {
        _speed = index;
        _hud.SetSpeed(index);
    }

    private void SetOverlay(Overlay overlay)
    {
        _world.SetOverlay(overlay);
        if (_view is not null)
        {
            _world.Show(_view);
        }
    }

    private void Select(int x, int y)
    {
        _selected = (x, y);
        _world.Highlight(x, y);
        _hud.ShowCell(_session.Inspect(x, y));
    }

    private void Deploy(int x, int y)
    {
        if (_session.Introduce(_hud.SelectedOrganism, x, y, _hud.BiomassKg, _hud.Design(), out var error))
        {
            _hud.EndDeploy();
            _hud.Toast($"Inoculación programada en ({x}, {y}). Llegará al empezar el próximo sol.");
            if (!_world.HasLander)
            {
                _world.PlaceLander(x, y);
            }

            _world.Camera.FocusOn(_world.Ground.CellCenter(x, y));
            Refresh();
        }
        else
        {
            _hud.Toast(error ?? "Despliegue rechazado.", bad: true);
        }
    }

    private void Analyze()
    {
        if (_selected.X < 0)
        {
            return;
        }

        _session.RecordAction("analyze-region");
        GameAudio.Instance?.Play(Sfx.Analyze);
        var (x, y) = _selected;
        var report = _session.Inspect(x, y);
        var neighbours = Enumerable.Range(-2, 5)
            .SelectMany(dy => Enumerable.Range(-2, 5).Select(dx => (X: x + dx, Y: y + dy)))
            .Where(cell => cell.X >= 0 && cell.Y >= 0 && cell.X < _session.Width && cell.Y < _session.Height)
            .Select(cell => _session.Inspect(cell.X, cell.Y))
            .ToArray();
        var water = neighbours.Average(cell => cell.ResourcesKg["Water"]);
        var radiation = neighbours.Average(cell => cell.RadiationSvPerSol);
        _hud.Toast(string.Create(CultureInfo.InvariantCulture, $"Región ({x}, {y}) analizada: {report.TemperatureKelvin:0} K, agua media {Hud.Mass(water)}, radiación {radiation * 1000:0.00} mSv/sol."));
        _world.SetOverlay(Overlay.Temperature);
        _hud.SelectOverlay(Overlay.Temperature);
        Refresh();
    }

    private void Experiment()
    {
        if (_selected.X < 0)
        {
            return;
        }

        var forecast = _session.Experiment(_hud.SelectedOrganism, _selected.X, _selected.Y, _hud.BiomassKg, _hud.Design(), _hud.ForecastSols);
        _hud.ShowForecast(forecast);
        Refresh();
    }

    private void Save()
    {
        try
        {
            var id = _saves.Save(_session);
            GameAudio.Instance?.Play(Sfx.Save);
            _hud.Toast($"Partida guardada ({id}, sol {_session.Tick}).");
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or System.IO.IOException or InvalidOperationException)
        {
            _hud.Toast($"No se pudo guardar: {exception.Message}", bad: true);
        }
    }

    private void Load()
    {
        try
        {
            var loaded = _saves.LoadLatest(out var message);
            if (loaded is not null)
            {
                GameAudio.Instance?.Play(Sfx.Load);
                StartSession(loaded);
            }

            _hud.Toast(message, bad: loaded is null);
        }
        catch (Exception exception) when (exception is System.Data.Common.DbException or System.IO.IOException or InvalidOperationException or ArgumentException)
        {
            _hud.Toast($"No se pudo cargar: {exception.Message}", bad: true);
        }
    }

    private void NewGame()
    {
        var seed = (_session.Seed * 6364136223846793005UL) + 1442695040888963407UL;
        _saves.CurrentId = null;
        StartSession(GameSession.NewGame(_dataRoot, seed % 1_000_000UL));
        _hud.Toast($"Nueva partida, semilla {_session.Seed}.");
    }

    /// <summary>One sound per refresh: the most important event of the batch (ADR-018).</summary>
    private static void PlayEventSound(IEnumerable<SessionEvent> events)
    {
        Sfx[] priority = [Sfx.Mission, Sfx.Extinction, Sfx.Error, Sfx.Mutation, Sfx.Deploy, Sfx.Experiment];
        var sounds = events.Select(item => SfxSynth.ForEvent(item.Kind)).OfType<Sfx>().ToHashSet();
        foreach (var sfx in priority)
        {
            if (sounds.Contains(sfx))
            {
                GameAudio.Instance?.Play(sfx);
                return;
            }
        }
    }

    /// <summary>Scripted showcase used for screenshots: a thriving colony at dusk.</summary>
    private async void Showcase(string path)
    {
        SetSpeed(0);
        _celebrated = !_options.ContainsKey("banner");
        var design = new Dictionary<string, double>(StringComparer.Ordinal) { ["UV_PROTECTION"] = 0.85 };
        _session.RecordAction("analyze-region");
        _session.Experiment("phototroph_a01", 32, 32, 0.5, design, 20);
        foreach (var (x, y) in new[] { (32, 32), (30, 35), (35, 30), (27, 29), (38, 36) })
        {
            _session.Introduce("phototroph_a01", x, y, 0.5, design, out _);
            _session.Introduce("decomposer_d01", x, y, 0.05, null, out _);
        }

        _session.Introduce("extremophile_e01", 24, 38, 0.2, null, out _);
        var sols = _options.TryGetValue("sols", out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : 220;
        _session.Advance(sols);
        _world.PlaceLander(32, 32);
        Select(32, 32);
        Refresh();
        _hud.ShowForecast(_session.Experiment("phototroph_a01", 32, 32, 0.5, design, 40));
        if (_options.TryGetValue("overlay", out var overlay))
        {
            SetOverlay((Overlay)int.Parse(overlay, CultureInfo.InvariantCulture));
            _hud.SelectOverlay(_world.Overlay);
        }

        var hour = _options.TryGetValue("hour", out var h) ? float.Parse(h, CultureInfo.InvariantCulture) : 0.7f;
        _solProgress = (hour - 0.25 + 1.0) % 1.0;
        var centre = _world.Ground.CellCenter(32, 32);
        var distance = _options.TryGetValue("distance", out var d) ? float.Parse(d, CultureInfo.InvariantCulture) : 48f;
        var yaw = _options.TryGetValue("yaw", out var w) ? float.Parse(w, CultureInfo.InvariantCulture) : 0.9f;
        var pitch = _options.TryGetValue("pitch", out var p) ? float.Parse(p, CultureInfo.InvariantCulture) : 0.42f;
        _world.Camera.Frame(centre, distance, yaw, pitch, snap: true);
        _hud.Toast("Colonia FIRST COLONY · demostración");
        for (var i = 0; i < 30; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        GD.Print($"MARTIS screenshot {path} {error} tick={_session.Tick} biomass={_session.View().TotalBiomassKg:0.###}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    /// <summary>Headless check for CI: plays, saves, loads and compares.</summary>
    private void Smoke()
    {
        SetSpeed(0);
        var ok = _session.Introduce("phototroph_a01", 32, 32, 0.5, new Dictionary<string, double> { ["UV_PROTECTION"] = 0.85 }, out var error);
        ok &= _session.Introduce("decomposer_d01", 32, 32, 0.05, null, out _);
        _session.Advance(30);
        Refresh();
        var biomass = _session.View().TotalBiomassKg;
        _saves.CurrentId = null;
        var id = _saves.Save(_session);
        var loaded = _saves.LoadLatest(out var message);
        ok &= loaded is not null && loaded.Tick == 30 && Math.Abs(loaded.View().TotalBiomassKg - biomass) < 1e-12;
        GD.Print($"MARTIS smoke ok={ok} save={id} tick={_session.Tick} biomass={biomass:0.######} {message} {error}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
