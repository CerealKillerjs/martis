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
/// <c>--smoke</c> (headless check: play 30 sols, save, load, quit with code 0/1),
/// <c>--shot=path.png --rover</c> (the same demo seen from the rover's chase camera, J1),
/// <c>--shot=path.png --mode=planetary|laboratory|strategic|exploration</c> (V1 modes).
/// </summary>
public partial class GameRoot : Node
{
    private string _dataRoot = string.Empty;
    private GameSession _session = null!;
    private World3D _world = null!;
    private Hud _hud = null!;
    private SaveService _saves = null!;
    private long _seenEvents;
    private (int X, int Y) _selected = (-1, -1);
    private bool _celebrated;

    /// <summary>When the HUD shows hints in the current campaign mission (H8); null in free play.</summary>
    private MissionGuidance? _guidance;
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
        var startGrid = _options.TryGetValue("grid", out var gridText) ? int.Parse(gridText, CultureInfo.InvariantCulture) : 64;
        StartSession(GameSession.NewGame(_dataRoot, seed, true, _campaign.Campaign.WorldFor(startGrid) ?? WorldOptions.Default));

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
        SetModeUiVisible(false);
        _world.Camera.Idle = true;
        _menu.Show(_campaign, _playing);
    }

    private void CloseMenu()
    {
        _menu.Visible = false;
        _hud.Visible = true;
        SetModeUiVisible(true);
        _world.Camera.Idle = false;
    }

    private ulong NextSeed() => ((_session.Seed * 6364136223846793005UL) + 1442695040888963407UL) % 1_000_000UL;

    private void StartMission(CampaignMission mission, ulong? seed = null)
    {
        _mission = mission;
        _saves.CurrentId = null;
        _hud.ClearEvents();
        var session = GameSession.NewGame(_dataRoot, seed ?? _session.Seed, mission.File, _campaign.Campaign.ChapterOf(mission.Id).World);
        session.AttachEconomy(_campaign.Research!);
        StartSession(session);
        _playing = true;
        CloseMenu();
        SetSpeed(0);
        ChapterChanged?.Invoke(ChapterId);
        var chapter = _campaign.Campaign.ChapterOf(mission.Id);
        var definition = _session.Mission!.Definition;
        var concepts = string.Join(" · ", mission.Concepts.Select(id => _campaign.Campaign.Concept(id).Title));
        var learn = concepts.Length > 0 ? $"\n\nAprenderás: {concepts}" : string.Empty;
        var first = _guidance?.ShowsHint ?? true ? definition.Objectives[0].Hint ?? definition.Objectives[0].Title : definition.Objectives[0].Title;
        _hud.Banner(
            definition.Name,
            string.Create(CultureInfo.InvariantCulture, $"Era {Eras.Roman(chapter.Era)} · {Eras.Name(chapter.Era)}  ·  Capítulo {chapter.Number} · {chapter.Title}\n{Kind(mission.Kind)} · {Guidance(chapter.Guidance)}\n\n{definition.Briefing}{learn}"),
            ("Empezar", () => _hud.Toast(first)));
    }

    /// <summary>Mission types of the master document v2.0, §11, as the briefing names them.</summary>
    internal static string Kind(MissionKind kind) => kind switch
    {
        MissionKind.Reconnaissance => "Reconocimiento",
        MissionKind.Sampling => "Muestreo",
        MissionKind.Experiment => "Experimento",
        MissionKind.Colonization => "Colonización",
        MissionKind.Restoration => "Restauración",
        MissionKind.Terraforming => "Terraformación",
        _ => "Expedición",
    };

    /// <summary>How the briefing names the help of a chapter (H8).</summary>
    private static string Guidance(GuidanceLevel level) => level switch
    {
        GuidanceLevel.Tutorial => "Tutorial guiado",
        GuidanceLevel.Hints => "Con pistas",
        GuidanceLevel.OnRequest => "Pistas si las pides",
        GuidanceLevel.Minimal => "Ayuda mínima",
        _ => "Sin ayuda",
    };

    private void FreePlay() => FreePlay(_options.TryGetValue("grid", out var grid) ? int.Parse(grid, CultureInfo.InvariantCulture) : 64);

    private void FreePlay(int gridSize)
    {
        _mission = null;
        _saves.CurrentId = null;
        _hud.ClearEvents();
        StartSession(GameSession.NewGame(_dataRoot, _session.Seed, withMission: false, _campaign.Campaign.WorldFor(gridSize) ?? WorldOptions.Default));
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

        if (_session.Failure is null && _time.Advance(delta) > 0)
        {
            _session.Advance(1);
            OrbitalPass();
            StationsTick();
            AnalyzeSamples();
            Refresh();
        }

        _world.TimeOfDay = (float)((_time.SolProgress + 0.25) % 1.0);
        UpdatePlanetary();
        UpdateRover(delta);
        ShowKnowledge();
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
                    _time.TogglePause();
                    _hud.SetSpeed(_time.EffectiveSpeed);
                    break;
                case Key.Escape:
                    _hud.EndDeploy();
                    BackFromMode();
                    break;
                case Key.F when Exploring:
                    SeedHere();
                    break;
                case Key.C when Exploring:
                    ScanHere();
                    break;
                case Key.M when Exploring:
                    CollectHere();
                    break;
                case Key.B when Exploring:
                    BuildStation();
                    break;
                case Key.X when Exploring:
                    RepairStation();
                    break;
                case Key.G when !Exploring:
                    SendRover();
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
                if (_hud.DeployMode && RoverAllows(x, y))
                {
                    Deploy(x, y);
                }

                Select(x, y);
                Act("inspect-cell");
            }
        }
    }

    private World3D BuildWorld(GameSession session)
    {
        var world = new World3D { Name = "World" };
        AddChild(world);
        world.Build(session.Width, session.Height, session.Seed);
        world.Camera.Used += Act;
        return world;
    }

    private void StartSession(GameSession session)
    {
        _session = session;
        _seenEvents = 0;
        _guidance = _mission is not null && session.Mission is not null
            ? new MissionGuidance(_campaign.Campaign.ChapterOf(_mission.Id).Guidance, _campaign.Campaign.Guidance, session.Tick)
            : null;
        _celebrated = session.Mission?.IsComplete ?? false;
        if (_world is not null && (_world.Ground.CellsX != session.Width || _world.Ground.CellsY != session.Height))
        {
            // A chapter on another map size (H3): the 3D world is built again for it.
            var overlay = _world.Overlay;
            RemoveChild(_world);
            _world.QueueFree();
            _world = BuildWorld(session);
            MoveChild(_world, 0);
            _world.SetOverlay(overlay);
        }

        if (_world is null)
        {
            _world = BuildWorld(session);
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
            _menu = new CampaignMenu { Name = "CampaignMenu", Visible = false };
            layer.AddChild(_menu);
            _menu.MissionChosen += mission => StartMission(mission);
            _menu.FreePlayChosen += FreePlay;
            _menu.LoadChosen += Load;
            _menu.ResumeChosen += CloseMenu;
            _hud.AnalyzeRequested += Analyze;
            _hud.HintRequested += () =>
            {
                if (_guidance?.Request() == true)
                {
                    Refresh();
                }
            };
            _hud.GenomeRequested += DesignFromSample;
            _hud.RoverRequested += SendRover;
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

        ResetKnowledge();
        ResetSamples();
        ResetFacilities();
        ResetValley();
        Select(session.Width / 2, session.Height / 2);
        Refresh();
        var colony = Array.FindIndex(_view!.BiomassKg, kg => kg > 0f);
        if (colony >= 0)
        {
            _world.PlaceLander(colony % session.Width, colony / session.Width);
        }

        ResetRover(colony);
    }

    private void Refresh()
    {
        _view = _session.View();
        _world.Show(_view);
        if (_session.Mission is { } tracked)
        {
            _guidance?.Observe(tracked, _session.Tick);
        }

        _hud.ShowMission(_session.Mission, MissionCaption(), _guidance);
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
            ShowSelected();
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
        var learned = string.Join(" · ", _mission.Concepts.Select(id => _campaign.Campaign.Concept(id).Title));
        var debrief = mission.Definition.Debrief is { } text ? $"\n\n{text}" : string.Empty;
        var codex = learned.Length > 0 ? $"\n\nNuevo en el códice: {learned}." : string.Empty;
        var done = $"{mission.Definition.Name} completada en el sol {_session.Tick}.{debrief}{codex}\n\n";
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
        _time.Request(index);
        _hud.SetSpeed(_time.EffectiveSpeed);
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
        ShowSelected();
    }

    private void Deploy(int x, int y)
    {
        var order = _orders.Issue("deploy", FormattableString.Invariant($"({x}, {y})"), Exploring ? "rover" : "estación", _session.Tick, _hud.BiomassKg);
        if (!KnowledgeAllows(x, y, out var blind))
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, "celda sin datos"));
            _hud.Toast(blind!, bad: true);
            return;
        }

        if (_session.Introduce(_hud.SelectedOrganism, x, y, _hud.BiomassKg, _hud.Design(), out var error))
        {
            Resolve(_orders.Complete(order.Id, _session.Tick, FormattableString.Invariant($"{_hud.BiomassKg:0.###} kg programados")));
            _hud.EndDeploy();
            _hud.Toast($"Inoculación programada en ({x}, {y}). Llegará al empezar el próximo sol.");
            if (!_world.HasLander)
            {
                _world.PlaceLander(x, y);
                RoverLanded(x, y);
            }

            _world.Camera.FocusOn(_world.Ground.CellCenter(x, y));
            Refresh();
        }
        else
        {
            Resolve(_orders.Fail(order.Id, _session.Tick, error ?? "Despliegue rechazado"));
            _hud.Toast(error ?? "Despliegue rechazado.", bad: true);
        }
    }

    private void Analyze()
    {
        if (_selected.X < 0)
        {
            return;
        }

        if (!KnowledgeAllows(_selected.X, _selected.Y, out var blind))
        {
            _hud.Toast(blind!, bad: true);
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
            var id = _saves.Save(_session, _mission?.File, CaptureExpedition());
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
            var loaded = _saves.LoadLatest(out var message, out var record, _campaign.Campaign.WorldFor, out var expedition);
            if (loaded is not null)
            {
                _mission = _campaign.Campaign.Missions.FirstOrDefault(item => item.File == record?.MissionFile);
                if (_mission is not null)
                {
                    loaded.AttachEconomy(_campaign.Research!, record!.Economy);
                }

                _hud.ClearEvents();
                StartSession(loaded);
                RestoreExpedition(expedition);
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
        StartSession(GameSession.NewGame(_dataRoot, NextSeed(), withMission: false, _session.World));
        _hud.Toast($"Nueva partida, semilla {_session.Seed}.");
    }

    /// <summary>Screenshot of the campaign menu with some progress, for docs and review.</summary>
    private async void MenuShowcase(string path)
    {
        if (_options.TryGetValue("progress", out var upTo))
        {
            // --progress completes the first two missions; --progress=<id> every mission up to that one.
            var last = string.IsNullOrEmpty(upTo) || upTo == "true" ? "first_colony" : upTo;
            foreach (var mission in _campaign.Campaign.Missions)
            {
                _campaign.Complete(mission.Id);
                if (mission.Id == last)
                {
                    break;
                }
            }
        }

        _playing = true;
        OpenMenu();
        if (_options.ContainsKey("codex"))
        {
            _menu.ToggleCodex();
        }

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
        _session.Experiment("phototroph_a01", 32 + ((_session.Width / 2) - 32), 32 + ((_session.Width / 2) - 32), 0.5, design, 20);
        // Centred on the map whatever its size (H3); on a big map a few more colonies.
        var o = (_session.Width / 2) - 32;
        var sites = new List<(int X, int Y)> { (32, 32), (30, 35), (35, 30), (27, 29), (38, 36) };
        if (_session.Width > 64)
        {
            sites.AddRange([(10, 12), (58, 50), (-20, 40), (70, -10), (45, 80)]);
        }

        foreach (var (x, y) in sites)
        {
            _session.Introduce("phototroph_a01", x + o, y + o, 0.5, design, out _);
            _session.Introduce("decomposer_d01", x + o, y + o, 0.05, null, out _);
        }

        _session.Introduce("extremophile_e01", 24 + o, 38 + o, 0.2, null, out _);
        var sols = _options.TryGetValue("sols", out var text) ? int.Parse(text, CultureInfo.InvariantCulture) : 220;
        _session.Advance(sols);
        _world.PlaceLander(32 + o, 32 + o);
        ShowcaseKnowledge(o);
        if (_options.ContainsKey("samples"))
        {
            ShowcaseSamples(o);
        }

        if (_options.ContainsKey("stations"))
        {
            ShowcaseStations(o);
        }

        Select(32 + o, 32 + o);
        Refresh();
        _hud.ShowForecast(_session.Experiment("phototroph_a01", 32 + o, 32 + o, 0.5, design, 40));
        if (_options.TryGetValue("overlay", out var overlay))
        {
            SetOverlay((Overlay)int.Parse(overlay, CultureInfo.InvariantCulture));
            _hud.SelectOverlay(_world.Overlay);
        }

        var hour = _options.TryGetValue("hour", out var h) ? float.Parse(h, CultureInfo.InvariantCulture) : 0.7f;
        _time.SetProgress(hour - 0.25 + 1.0);
        var centre = _world.Ground.CellCenter(32 + o, 32 + o);
        var distance = _options.TryGetValue("distance", out var d) ? float.Parse(d, CultureInfo.InvariantCulture) : 48f;
        var yaw = _options.TryGetValue("yaw", out var w) ? float.Parse(w, CultureInfo.InvariantCulture) : 0.9f;
        var pitch = _options.TryGetValue("pitch", out var p) ? float.Parse(p, CultureInfo.InvariantCulture) : 0.42f;
        _world.Camera.Frame(centre, distance, yaw, pitch, snap: true);
        _hud.Toast("Colonia FIRST COLONY · demostración");
        if (_options.ContainsKey("rover"))
        {
            // Expedición (J1): the rover at the edge of the colony, chase camera behind it.
            _rover.Place(27 + o, 34 + o, -0.5);
            RoverLanded(32 + o, 32 + o);
            _rover.Drive(1, 0, 0.1);
            var heading = (float)_rover.Heading;
            var at = _world.CellPoint(_rover.X, _rover.Y);
            var behind = MathF.Atan2(-MathF.Cos(heading), -MathF.Sin(heading));
            _world.Camera.Frame(at + (Vector3.Up * 0.8f), 12f, behind + 0.5f, 0.3f, snap: true);
            SwitchMode(Martis.Application.GameMode.Exploration);
            _hud.Toast("Expedición · conduce el rover y siembra dentro del anillo");
        }
        if (_options.TryGetValue("mode", out var modeName) && Enum.TryParse<Martis.Application.GameMode>(modeName, ignoreCase: true, out var shotMode))
        {
            SwitchMode(shotMode);
        }

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
        // Expedición (J1): the rover loaded its data, drives, and gates deployments by reach.
        var start = _rover.X;
        var roverOk = _rover.CanReach(_session.Width / 2, _session.Height / 2) && !_rover.CanReach(0, 0) && _rover.Drive(1, 0, 0.5) > 0 && _rover.X > start;
        // V1: switching modes never touches the simulation; orders are registered.
        var tick = _session.Tick;
        foreach (var mode in Enum.GetValues<Martis.Application.GameMode>())
        {
            SwitchMode(mode);
        }

        SwitchMode(Martis.Application.GameMode.Strategic);
        Deploy(_session.Width / 2, _session.Height / 2);
        roverOk &= _session.Tick == tick && _orders.All.Count == 1;
        // V2: no seeding blind; the scanner measures the ground under the rover.
        Deploy(0, 0);
        var blindRefused = _orders.All[^1].Status == Martis.Application.OrderStatus.Failed;
        SwitchMode(Martis.Application.GameMode.Exploration);
        ScanHere();
        var (rx, ry) = _rover.Cell;
        var knowledgeOk = blindRefused && _knowledge.Level(rx, ry) == Martis.Simulation.Expedition.KnowledgeLevel.Measured && _orders.All.Count == 3;
        GD.Print($"MARTIS smoke knowledge={knowledgeOk} estimated={_knowledge.Coverage(Martis.Simulation.Expedition.KnowledgeLevel.Estimated):0.###}");
        roverOk &= knowledgeOk;
        // V3: a sample of measured ground is analysed in the lab after a few sols.
        CollectHere();
        var collected = _samples.All.Count == 1 && _samples.All[0].State == Martis.Simulation.Expedition.SampleState.Analyzing;
        _session.Advance(_samples.Definition.AnalysisSols);
        AnalyzeSamples();
        var samplesOk = collected && _samples.All[0].State == Martis.Simulation.Expedition.SampleState.Analyzed;
        GD.Print($"MARTIS smoke samples={samplesOk} kind={_samples.All.FirstOrDefault()?.Kind}");
        roverOk &= samplesOk;
        // V4: a station measures every sol; an order sends the rover somewhere and resolves on arrival.
        BuildStation();
        StationsTick();
        SwitchMode(Martis.Application.GameMode.Strategic);
        var (sx, sy) = _rover.Cell;
        Select(Math.Max(0, sx - 6), sy);
        SendRover();
        var move = _moveOrder;
        for (var i = 0; i < 2000 && _moveOrder is not null; i++)
        {
            UpdateRover(0.05);
        }

        var robotsOk = _network.Stations.Count == 1 && move is not null && _orders.All.Single(order => order.Id == move.Id).Status == Martis.Application.OrderStatus.Done && _rover.Wear > 0;
        GD.Print($"MARTIS smoke robots={robotsOk} wear={_rover.Wear:0.###}");
        roverOk &= robotsOk;
        // V5: measuring a place of the valley reveals it; the whole expedition survives save and load.
        var delta = _pois.Pois[0];
        _knowledge.Scan(delta.X + 0.5, delta.Y + 0.5);
        ShowKnowledge();
        var expedition = CaptureExpedition();
        _saves.Save(_session, MissionDefinition.FirstColonyFile, expedition);
        var reloaded = _saves.LoadLatest(out _, out _, null, out var savedExpedition);
        var (roverX, roverY) = (_rover.X, _rover.Y);
        _rover.Place(0, 0, 0);
        RestoreExpedition(savedExpedition);
        var valleyOk = delta.State == Martis.Simulation.Expedition.PoiState.Discovered && reloaded is not null && savedExpedition == expedition
            && _rover.X == roverX && _rover.Y == roverY && _network.Stations.Count == 1 && _samples.All.Count == 1;
        GD.Print($"MARTIS smoke valley={valleyOk} places={_pois.Pois.Count(poi => poi.State != Martis.Simulation.Expedition.PoiState.Hidden)} bytes={expedition.Length}");
        roverOk &= valleyOk;
        ok &= roverOk;
        GD.Print($"MARTIS smoke rover={roverOk} battery={_rover.Battery:0.##}");
        GD.Print($"MARTIS smoke ok={ok} save={id} tick={_session.Tick} biomass={biomass:0.######} {message} {error}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
