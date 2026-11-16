using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Economy;
using Martis.Simulation.Missions;
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
    private CampaignProgress _campaign = null!;
    private CampaignMission? _mission;
    private CampaignMenu _menu = null!;
    private bool _playing;
    private double _savedPoints = double.NaN;

    /// <summary>Chapter of the mission being played (<c>ch1</c>…<c>ch5</c>), null in free play. Music picks its mood from it.</summary>
    public string? ChapterId => _mission?.ChapterId;

    public event Action<string?>? ChapterChanged;

    private string ProgressPath => _options.TryGetValue("campaign", out var path) ? path : ProjectSettings.GlobalizePath("user://campaign.json");

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
        _campaign = LoadCampaign();
        StartSession(GameSession.NewGame(_dataRoot, seed));

        if (_options.ContainsKey("smoke"))
        {
            CallDeferred(MethodName.Smoke);
        }
        else if (_options.TryGetValue("shot", out var shot))
        {
            _mission = _campaign.Campaign.Mission("first_colony");
            CallDeferred(MethodName.Showcase, shot);
        }
        else if (_options.TryGetValue("menu-shot", out var menuShot))
        {
            CallDeferred(MethodName.MenuShowcase, menuShot);
        }
        else if (_options.TryGetValue("mission", out var missionId))
        {
            StartMission(_campaign.Campaign.Mission(missionId));
        }
        else
        {
            Launch();
        }
    }

    /// <summary>
    /// Follows the title screen's choice when there is one (autoload <c>/root/GameLaunch</c>,
    /// string properties <c>Mode</c> = Campaign | Sandbox | Load and optional <c>ChapterId</c>);
    /// otherwise opens the campaign menu.
    /// </summary>
    private void Launch()
    {
        var launch = GetNodeOrNull("/root/GameLaunch");
        var mode = launch?.Get("Mode").AsString() ?? string.Empty;
        var chapterId = launch?.Get("ChapterId").AsString() ?? string.Empty;
        switch (mode)
        {
            case "Sandbox":
                FreePlay();
                return;
            case "Load":
                Load();
                if (!_playing)
                {
                    OpenMenu();
                }

                return;
            case "Campaign" when chapterId.Length > 0:
                var chapter = _campaign.Campaign.Chapters.FirstOrDefault(item => item.Id == chapterId);
                var next = chapter?.Missions.FirstOrDefault(item => _campaign.IsUnlocked(item.Id) && !_campaign.IsComplete(item.Id))
                    ?? chapter?.Missions.LastOrDefault(item => _campaign.IsUnlocked(item.Id));
                if (next is not null)
                {
                    StartMission(next);
                    return;
                }

                break;
        }

        OpenMenu();
    }

    private CampaignProgress LoadCampaign()
    {
        var campaign = CampaignDefinition.LoadPublished(_dataRoot);
        var economy = EconomyDefinition.LoadPublished(_dataRoot);
        try
        {
            return System.IO.File.Exists(ProgressPath)
                ? CampaignProgress.FromJson(campaign, System.IO.File.ReadAllText(ProgressPath), economy)
                : new CampaignProgress(campaign) { Research = new ResearchState(economy) };
        }
        catch (Exception exception) when (exception is System.IO.IOException or System.IO.InvalidDataException or UnauthorizedAccessException)
        {
            GD.PushWarning($"Campaign progress ignored: {exception.Message}");
            return new CampaignProgress(campaign) { Research = new ResearchState(economy) };
        }
    }

    private void SaveCampaign()
    {
        try
        {
            System.IO.File.WriteAllText(ProgressPath, _campaign.ToJson());
            _savedPoints = _campaign.Research?.Points ?? 0;
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            _hud.Toast($"No se pudo guardar el progreso de la campaña: {exception.Message}", bad: true);
        }
    }

    private void OpenMenu()
    {
        SetSpeed(0);
        _hud.Visible = false;
        _world.Camera.Idle = true;
        _menu.Show(_campaign, _playing);
    }

    private void CloseMenu()
    {
        _menu.Visible = false;
        _hud.Visible = true;
        _world.Camera.Idle = false;
    }

    private ulong NextSeed() => ((_session.Seed * 6364136223846793005UL) + 1442695040888963407UL) % 1_000_000UL;

    private void StartMission(CampaignMission mission, ulong? seed = null)
    {
        _mission = mission;
        _saves.CurrentId = null;
        _hud.ClearEvents();
        var session = GameSession.NewGame(_dataRoot, seed ?? _session.Seed, mission.File);
        session.AttachEconomy(_campaign.Research!);
        StartSession(session);
        _playing = true;
        CloseMenu();
        SetSpeed(0);
        ChapterChanged?.Invoke(ChapterId);
        var chapter = _campaign.Campaign.ChapterOf(mission.Id);
        var definition = _session.Mission!.Definition;
        _hud.Banner(
            definition.Name,
            string.Create(CultureInfo.InvariantCulture, $"Capítulo {chapter.Number} · {chapter.Title}\n\n{definition.Briefing}"),
            ("Empezar", () => _hud.Toast(definition.Objectives[0].Hint ?? definition.Objectives[0].Title)));
    }

    private void FreePlay()
    {
        _mission = null;
        _saves.CurrentId = null;
        _hud.ClearEvents();
        StartSession(GameSession.NewGame(_dataRoot, _session.Seed, withMission: false));
        _playing = true;
        CloseMenu();
        SetSpeed(1);
        ChapterChanged?.Invoke(null);
        _hud.Toast("Exploración libre: sin objetivos, todo el laboratorio a tu disposición.");
    }

    private void Act(string action)
    {
        if (_session.Mission is not { } mission || mission.IsComplete || !_playing)
        {
            return;
        }

        var before = mission.ActiveObjective;
        _session.RecordAction(action);
        if (!ReferenceEquals(before, mission.ActiveObjective))
        {
            Refresh();
        }
    }

    private string? MissionCaption()
        => _mission is null ? null : string.Create(CultureInfo.InvariantCulture, $"CAPÍTULO {_campaign.Campaign.ChapterOf(_mission.Id).Number} · MISIÓN {_mission.Number}");

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
    }

    private WorldView? _view;

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            switch (key.Keycode)
            {
                case Key.Space:
                    SetSpeed(_speed == 0 ? 1 : 0);
                    break;
                case Key.Escape:
                    _hud.EndDeploy();
                    break;
                case Key.F5:
                    Save();
                    break;
                case Key.R:
                    _hud.ToggleResearch(_session.Research);
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
                Act("inspect-cell");
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
            _hud.MenuRequested += OpenMenu;
            _hud.ResearchRequested += () => _hud.ToggleResearch(_session.Research);
            _hud.ResearchChosen += Research;
            _world.Camera.Used += Act;
            _menu = new CampaignMenu { Name = "CampaignMenu", Visible = false };
            layer.AddChild(_menu);
            _menu.MissionChosen += mission => StartMission(mission);
            _menu.FreePlayChosen += FreePlay;
            _menu.LoadChosen += Load;
            _menu.ResumeChosen += CloseMenu;
            _hud.AnalyzeRequested += Analyze;
            _hud.ExperimentRequested += Experiment;
            _hud.DeployToggled += on => _hud.Toast(on ? "Elige una celda para la colonia." : "Despliegue cancelado.");
            _hud.SetOrganisms(session.Organisms);
            SetSpeed(1);
        }

        _hud.SetLimits(session.Research);
        if (session.Research is null)
        {
            _hud.ToggleResearch(null);
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
        _view = _session.View();
        _world.Show(_view);
        _hud.ShowMission(_session.Mission, MissionCaption());
        _hud.ShowEconomy(_session.Research, _session.InoculumKg);
        if (_session.Research is { } research && research.Points != _savedPoints)
        {
            SaveCampaign();
            if (_hud.ResearchOpen)
            {
                _hud.ShowResearch(research);
            }
        }
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

        if (_session.Failure is not null)
        {
            SetSpeed(0);
            _hud.Toast($"Simulación detenida: {_session.Failure}. Carga una partida para seguir.", bad: true);
        }

        if (!_celebrated && _session.Mission is { IsComplete: true } mission)
        {
            _celebrated = true;
            Celebrate(mission);
        }
    }

    private void Celebrate(MissionTracker mission)
    {
        SetSpeed(0);
        if (_mission is null)
        {
            _hud.Banner("¡MISIÓN COMPLETADA!", $"{mission.Definition.Name} completada en el sol {_session.Tick}.");
            return;
        }

        _campaign.Complete(_mission.Id);
        SaveCampaign();
        var title = _mission.Id == "first_colony" ? "¡PRIMERA COLONIA!" : "¡MISIÓN COMPLETADA!";
        var done = $"{mission.Definition.Name} completada en el sol {_session.Tick}.";
        if (_campaign.Next is not { } next)
        {
            _hud.Banner(title, done + " Has terminado todas las misiones disponibles: los próximos capítulos llegarán pronto.", ("Seguir explorando", () => { }), ("Menú", OpenMenu));
            return;
        }

        var chapter = _campaign.Campaign.ChapterOf(next.Id);
        var teaser = chapter.Id == _mission.ChapterId
            ? $"Siguiente: misión {chapter.Number}.{next.Number}."
            : $"Se abre el capítulo {chapter.Number}: {chapter.Title}.";
        _hud.Banner(title, $"{done} {teaser}", ("Siguiente misión", () => StartMission(next)), ("Seguir explorando", () => SetSpeed(1)));
    }

    private void Research(string technologyId)
    {
        if (_session.ResearchTechnology(technologyId, out var error))
        {
            var technology = _session.Research!.Definition.Technology(technologyId);
            _hud.Toast($"Investigado: {technology.Title}. {technology.Description}");
            _hud.SetLimits(_session.Research);
            SaveCampaign();
            Refresh();
        }
        else
        {
            _hud.Toast(error ?? "No se puede investigar.", bad: true);
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
        if (overlay != Overlay.None)
        {
            Act("select-overlay");
        }

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
            var id = _saves.Save(_session, _mission?.File);
            SaveCampaign();
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
            var loaded = _saves.LoadLatest(out var message, out var record);
            if (loaded is not null)
            {
                _mission = _campaign.Campaign.Missions.FirstOrDefault(item => item.File == record?.MissionFile);
                if (_mission is not null)
                {
                    loaded.AttachEconomy(_campaign.Research!, record!.Economy);
                }

                _hud.ClearEvents();
                StartSession(loaded);
                _playing = true;
                CloseMenu();
                SetSpeed(0);
                ChapterChanged?.Invoke(ChapterId);
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
        if (_mission is not null)
        {
            StartMission(_mission, NextSeed());
            return;
        }

        _saves.CurrentId = null;
        _hud.ClearEvents();
        StartSession(GameSession.NewGame(_dataRoot, NextSeed(), withMission: false));
        _hud.Toast($"Nueva partida, semilla {_session.Seed}.");
    }

    /// <summary>Screenshot of the campaign menu with some progress, for docs and review.</summary>
    private async void MenuShowcase(string path)
    {
        if (_options.ContainsKey("progress"))
        {
            _campaign.Complete("awakening");
            _campaign.Complete("first_colony");
        }

        _playing = true;
        OpenMenu();
        if (_options.TryGetValue("briefing", out var id))
        {
            StartMission(_campaign.Campaign.Mission(id));
        }

        if (_options.ContainsKey("research"))
        {
            _hud.Banner(string.Empty, string.Empty);
            _hud.CloseBanner();
            _campaign.Research!.Earn(62);
            _session.ResearchTechnology("gene_editing_1", out _);
            _session.ResearchTechnology("long_forecast", out _);
            _hud.SetLimits(_session.Research);
            Refresh();
            _hud.ToggleResearch(_session.Research);
        }

        for (var i = 0; i < 40; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(path);
        GD.Print($"MARTIS menu screenshot {path} {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
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
        _session.RecordAction("analyze-region");
        _saves.CurrentId = null;
        var id = _saves.Save(_session, MissionDefinition.FirstColonyFile);
        var loaded = _saves.LoadLatest(out var message, out var record);
        ok &= loaded is not null && loaded.Tick == 30 && Math.Abs(loaded.View().TotalBiomassKg - biomass) < 1e-12;
        ok &= record?.MissionFile == MissionDefinition.FirstColonyFile
            && loaded!.Mission!.Status().Single(status => status.Id == "analyze_region").CompletedTick is not null;
        GD.Print($"MARTIS smoke ok={ok} save={id} tick={_session.Tick} biomass={biomass:0.######} {message} {error}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
