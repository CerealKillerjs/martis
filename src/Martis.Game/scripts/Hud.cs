using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Economy;
using Martis.Simulation.Expedition;
using Martis.Simulation.Missions;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// Heads-up display built in code (TDD B.6): top bar, mission, inspector, laboratory,
/// event feed. It raises events; <see cref="GameRoot"/> talks to the simulation.
/// </summary>
public partial class Hud : Control
{
    public static readonly Color Accent = new(1f, 0.55f, 0.25f);
    public static readonly Color Bio = new(0.35f, 1f, 0.65f);

    private readonly Dictionary<string, (HSlider Slider, Label Value)> _genes = new(StringComparer.Ordinal);
    private readonly List<Button> _speedButtons = [];
    private IReadOnlyList<OrganismInfo> _organisms = [];
    private Label _sol = null!;
    private Label _biomass = null!;
    private Label _populations = null!;
    private Label _clock = null!;
    private VBoxContainer _missionRows = null!;
    private Label _missionTitle = null!;
    private Label _missionCaption = null!;
    private HBoxContainer _bannerButtons = null!;
    private RichTextLabel _inspector = null!;
    private RichTextLabel _feed = null!;
    private OptionButton _organism = null!;
    private SpinBox _kg = null!;
    private SpinBox _sols = null!;
    private VBoxContainer _geneBox = null!;
    private Button _deploy = null!;
    private Button _experiment = null!;
    private Label _forecastText = null!;
    private ForecastChart _chart = null!;
    private Label _toast = null!;
    private Label _hint = null!;
    private OptionButton _overlay = null!;
    private PanelContainer _banner = null!;
    private double _toastTime;
    private Label _points = null!;
    private Label _inoculum = null!;
    private ProgressBar _inoculumBar = null!;
    private Button _researchButton = null!;
    private PanelContainer _researchPanel = null!;
    private PanelContainer _missionPanel = null!;
    private PanelContainer _inspectorPanel = null!;
    private PanelContainer _labPanel = null!;
    private PanelContainer _feedPanel = null!;
    private VBoxContainer _researchRows = null!;
    private Label _researchPoints = null!;
    private ResearchState? _limits;

    public event Action<int>? SpeedChanged;

    public event Action<Overlay>? OverlayChanged;

    public event Action? SaveRequested;

    public event Action? LoadRequested;

    public event Action? NewGameRequested;

    public event Action? MenuRequested;

    public event Action? AnalyzeRequested;

    /// <summary>«Enviar el rover»: an order for the rover to drive to the selected cell (V4).</summary>
    public event Action? RoverRequested;

    public event Action? ExperimentRequested;

    public event Action<bool>? DeployToggled;

    public event Action<string>? ResearchChosen;

    public event Action? ResearchRequested;

    public bool ResearchOpen => _researchPanel.Visible;

    public string SelectedOrganism => _organisms.Count == 0 ? string.Empty : _organisms[_organism.Selected].Id;

    public double BiomassKg => _kg.Value;

    public int ForecastSols => (int)_sols.Value;

    public bool DeployMode => _deploy.ButtonPressed;

    /// <summary>The genes the player moved away from the base genome.</summary>
    public IReadOnlyDictionary<string, double>? Design()
    {
        if (_organisms.Count == 0)
        {
            return null;
        }

        var baseGenome = _organisms[_organism.Selected].BaseGenome.ToDictionary(gene => gene.Key, gene => gene.Value, StringComparer.Ordinal);
        var design = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (id, (slider, _)) in _genes)
        {
            if (Math.Abs(slider.Value - baseGenome[id]) > 1e-9)
            {
                design[id] = Math.Round(slider.Value, 3);
            }
        }

        return design.Count == 0 ? null : design;
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Theme = BuildTheme();
        BuildTopBar();
        BuildMission();
        BuildInspector();
        BuildLab();
        BuildFeed();
        BuildToast();
        BuildResearch();
    }

    /// <summary>
    /// Applies the campaign research to the laboratory (H2): locked organisms, gene sliders
    /// limited to the editing range, forecast length and the inoculum the bioreactor holds.
    /// Null (free play) lifts every limit and hides the economy.
    /// </summary>
    public void SetLimits(ResearchState? research)
    {
        _limits = research;
        _points.Visible = _inoculum.Visible = _inoculumBar.Visible = _researchButton.Visible = research is not null;
        for (var i = 0; i < _organisms.Count; i++)
        {
            var locked = research is not null && !research.IsOrganismAvailable(_organisms[i].Id);
            _organism.SetItemDisabled(i, locked);
            _organism.SetItemText(i, $"{(locked ? "🔒 " : string.Empty)}{_organisms[i].Name}  ·  {Category(_organisms[i].Category)}");
        }

        if (_organism.IsItemDisabled(_organism.Selected))
        {
            _organism.Select(0);
        }

        _sols.MaxValue = research?.ForecastSols ?? 200;
        _sols.Value = Math.Min(_sols.Value, _sols.MaxValue);
        _kg.MaxValue = research?.InoculumCapacityKg ?? 5;
        RebuildGenes();
        if (research is not null && _researchPanel.Visible)
        {
            ShowResearch(research);
        }
    }

    public void ShowEconomy(ResearchState? research, double inoculumKg)
    {
        if (research is null)
        {
            return;
        }

        _points.Text = string.Create(CultureInfo.InvariantCulture, $"⚗ {research.Points:0} PI");
        _inoculum.Text = string.Create(CultureInfo.InvariantCulture, $"Inóculo {inoculumKg:0.00}/{research.InoculumCapacityKg:0.00} kg");
        _inoculumBar.MaxValue = research.InoculumCapacityKg;
        _inoculumBar.Value = inoculumKg;
    }

    public void ToggleResearch(ResearchState? research)
    {
        if (research is null)
        {
            _researchPanel.Visible = false;
            return;
        }

        _researchPanel.Visible = !_researchPanel.Visible;
        if (_researchPanel.Visible)
        {
            ShowResearch(research);
        }
    }

    /// <summary>Rebuilds the technology list: researched, available with its cost, or locked by a requirement.</summary>
    public void ShowResearch(ResearchState research)
    {
        _researchPoints.Text = string.Create(CultureInfo.InvariantCulture, $"{research.Points:0} PI disponibles");
        foreach (var child in _researchRows.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var technology in research.Definition.Technologies)
        {
            var done = research.IsUnlocked(technology.Id);
            var blocker = research.Blocker(technology.Id);
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 12);
            var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            var title = new Label { Text = (done ? "✔  " : string.Empty) + technology.Title };
            title.AddThemeFontSizeOverride("font_size", 16);
            title.AddThemeColorOverride("font_color", done ? Bio : Colors.White);
            var description = new Label { Text = technology.Description, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(330, 0) };
            description.AddThemeFontSizeOverride("font_size", 12);
            description.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.65f));
            text.AddChild(title);
            text.AddChild(description);
            row.AddChild(text);
            var id = technology.Id;
            var button = Action(
                done ? "Investigada" : string.Create(CultureInfo.InvariantCulture, $"{technology.Cost:0} PI"),
                () => ResearchChosen?.Invoke(id),
                blocker ?? "Investigar");
            button.CustomMinimumSize = new Vector2(110, 34);
            button.Disabled = blocker is not null;
            row.AddChild(button);
            _researchRows.AddChild(row);
        }
    }

    public void SetOrganisms(IReadOnlyList<OrganismInfo> organisms)
    {
        _organisms = organisms;
        _organism.Clear();
        foreach (var organism in organisms)
        {
            _organism.AddItem($"{organism.Name}  ·  {Category(organism.Category)}");
        }

        _organism.Select(0);
        RebuildGenes();
    }

    public void SetSpeed(int index)
    {
        for (var i = 0; i < _speedButtons.Count; i++)
        {
            _speedButtons[i].SetPressedNoSignal(i == index);
        }
    }

    public void EndDeploy() => _deploy.SetPressedNoSignal(false);

    public void ShowStatus(WorldView view, float timeOfDay)
    {
        _sol.Text = $"SOL {view.Tick}";
        _biomass.Text = $"Biomasa {Mass(view.TotalBiomassKg)}";
        _populations.Text = $"{view.PopulationCount} poblaciones";
        var minutes = (int)(timeOfDay * 24.66f * 60f);
        _clock.Text = $"{minutes / 60:00}:{minutes % 60:00} LMST";
    }

    public void ShowMission(MissionTracker? mission, string? caption = null)
    {
        _missionCaption.Text = caption ?? "MISIÓN";
        foreach (var child in _missionRows.GetChildren())
        {
            child.QueueFree();
        }

        if (mission is null)
        {
            _missionTitle.Text = "Exploración libre";
            return;
        }

        _missionTitle.Text = mission.IsComplete ? $"{mission.Definition.Name}  ✔" : mission.Definition.Name;
        foreach (var status in mission.Status())
        {
            var done = status.CompletedTick is not null;
            var row = new VBoxContainer();
            row.AddThemeConstantOverride("separation", 2);
            var label = new Label
            {
                Text = (done ? "✔  " : "○  ") + status.Title,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                CustomMinimumSize = new Vector2(250, 0),
            };
            label.AddThemeColorOverride("font_color", done ? Bio : new Color(1, 1, 1, 0.85f));
            label.AddThemeFontSizeOverride("font_size", 14);
            row.AddChild(label);
            if (status.Target > 1 && !done)
            {
                var bar = new ProgressBar { MaxValue = status.Target, Value = status.Progress, ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6) };
                row.AddChild(bar);
                var count = new Label { Text = $"{status.Progress:0}/{status.Target:0}" };
                count.AddThemeFontSizeOverride("font_size", 11);
                count.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.5f));
                row.AddChild(count);
            }

            if (status.Active && !string.IsNullOrWhiteSpace(status.Hint))
            {
                var hint = new PanelContainer();
                var style = new StyleBoxFlat { BgColor = new Color(1f, 0.55f, 0.25f, 0.14f), BorderColor = Accent, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 5, ContentMarginBottom = 5 };
                style.BorderWidthLeft = 3;
                style.SetCornerRadiusAll(4);
                hint.AddThemeStyleboxOverride("panel", style);
                var text = new Label { Text = "▸ " + status.Hint, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(240, 0) };
                text.AddThemeFontSizeOverride("font_size", 13);
                text.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.7f));
                hint.AddChild(text);
                row.AddChild(hint);
                label.AddThemeColorOverride("font_color", Colors.White);
            }

            _missionRows.AddChild(row);
        }
    }

    /// <summary>
    /// The inspector shows what the AI knows (V2, ADR-029): nothing for unknown ground, estimates
    /// with their uncertainty for orbital or passing data, exact values once the rover measured it.
    /// </summary>
    public void ShowCell(CellReport? report, KnowledgeLevel level = KnowledgeLevel.Measured)
    {
        if (report is null)
        {
            _inspector.Text = "[color=#ffffff88]Haz clic en el terreno para inspeccionar una celda.[/color]";
            return;
        }

        var text = new System.Text.StringBuilder();
        var (tag, colour) = level switch
        {
            KnowledgeLevel.Measured => ("medida", "#60ffa0"),
            KnowledgeLevel.Estimated => ("estimada", "#ffd060"),
            _ => ("sin datos", "#ff8a70"),
        };
        text.Append(CultureInfo.InvariantCulture, $"[b]Celda ({report.X}, {report.Y})[/b]  [color={colour}]{tag}[/color]\n");
        if (level == KnowledgeLevel.Unknown)
        {
            text.Append("[color=#ffffff99]Nadie ha observado este terreno. Espera a la sonda orbital o conduce el rover hasta aquí y escanea (C).[/color]\n");
            _inspector.Text = text.ToString();
            return;
        }

        var estimated = level == KnowledgeLevel.Estimated;
        var about = estimated ? "≈ " : string.Empty;
        if (report.TemperatureKelvin is { } kelvin)
        {
            var spread = estimated ? "  [color=#ffffff77]± 8 K[/color]" : string.Empty;
            text.Append(CultureInfo.InvariantCulture, $"Temperatura  [color=#ffb070]{about}{kelvin:0.0} K[/color] ({kelvin - 273.15:0} °C){spread}\n");
        }

        if (report.PressurePascal is { } pascal)
        {
            text.Append(CultureInfo.InvariantCulture, $"Presión  {about}{pascal:0} Pa{(estimated ? "  [color=#ffffff77]± 15 %[/color]" : string.Empty)}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"Irradiancia  {about}{report.IrradianceWPerM2:0.0} W/m²\n");
        text.Append(CultureInfo.InvariantCulture, $"Radiación  [color=#d080ff]{about}{report.RadiationSvPerSol * 1000:0.000} mSv/sol[/color]{(estimated ? "  [color=#ffffff77]± 30 %[/color]" : string.Empty)}\n");
        text.Append(estimated ? "\n[b]Recursos[/b]  [color=#ffffff77]± 50 %, escanea para medir[/color]\n" : "\n[b]Recursos[/b]\n");
        foreach (var (name, kg) in report.ResourcesKg.OrderByDescending(item => item.Value).Take(estimated ? 3 : 8))
        {
            text.Append(CultureInfo.InvariantCulture, $"  {Resource(name)}  {about}{Mass(kg)}\n");
        }

        text.Append(CultureInfo.InvariantCulture, $"\n[b]Vida[/b]  {report.Populations.Count} poblaciones\n");
        if (estimated)
        {
            if (report.Populations.Count > 0)
            {
                text.Append("[color=#ffffff99]Genomas y biomasa solo con medida in situ (C con el rover).[/color]\n");
            }

            _inspector.Text = text.ToString();
            return;
        }

        foreach (var population in report.Populations.OrderByDescending(item => item.BiomassKg).Take(5))
        {
            text.Append(CultureInfo.InvariantCulture, $"[color=#60ffa0]#{population.Id} {population.OrganismName}[/color]\n");
            text.Append(CultureInfo.InvariantCulture, $"  {Mass(population.BiomassKg)}  ·  {population.Abundance:0.###e0} células  ·  {population.AgeSols:0.#} soles\n");
            text.Append("  [color=#ffffff99]");
            text.Append(string.Join("  ", population.Genome.Select(gene => string.Create(CultureInfo.InvariantCulture, $"{Gene(gene.Key)} {gene.Value:0.00}"))));
            text.Append("[/color]\n");
        }

        _inspector.Text = text.ToString();
    }

    public void ShowForecast(ExperimentForecast forecast)
    {
        _chart.Plot(forecast.BiomassKg, forecast.Survived);
        if (forecast.Failure is not null && forecast.BiomassKg.Count == 0)
        {
            _forecastText.Text = forecast.Failure;
            _forecastText.AddThemeColorOverride("font_color", new Color(1f, 0.45f, 0.4f));
            return;
        }

        var final = forecast.BiomassKg.Count == 0 ? 0 : forecast.BiomassKg[^1];
        _forecastText.Text = forecast.Survived
            ? $"Sobrevive {forecast.Sols} soles · {Mass(final)}"
            : $"No sobrevive · {forecast.Sols} soles";
        _forecastText.AddThemeColorOverride("font_color", forecast.Survived ? Bio : new Color(1f, 0.45f, 0.4f));
    }

    public void AddEvents(IEnumerable<SessionEvent> events)
    {
        foreach (var item in events)
        {
            var colour = item.Kind switch
            {
                "mutation" => "#ffd060",
                "extinction" => "#ff6a5a",
                "mission" => "#60ffa0",
                "deploy" => "#80d0ff",
                "failure" => "#ff4040",
                "experiment" => "#c0a0ff",
                "survey" => "#9fd8ff",
                "lab" => "#ffc070",
                _ => "#ffffffcc",
            };
            _feed.AppendText($"[color=#ffffff66]sol {item.Tick}[/color]  [color={colour}]{Escape(item.Text)}[/color]\n");
        }
    }

    public void ClearEvents() => _feed.Clear();

    public void Toast(string text, bool bad = false)
    {
        _toast.Text = text;
        _toast.AddThemeColorOverride("font_color", bad ? new Color(1f, 0.5f, 0.45f) : new Color(1f, 0.93f, 0.85f));
        _toast.Visible = true;
        _toastTime = 4.0;
    }

    public bool BannerVisible => _banner.Visible;

    public void CloseBanner() => _banner.Visible = false;

    /// <summary>
    /// Centred message (mission briefing, mission complete). Each button closes the banner and
    /// then runs its action; with no buttons there is a single «Seguir explorando».
    /// </summary>
    public void Banner(string title, string text, params (string Label, Action Action)[] buttons)
    {
        _banner.GetNode<Label>("Box/Title").Text = title;
        _banner.GetNode<Label>("Box/Text").Text = text;
        foreach (var child in _bannerButtons.GetChildren())
        {
            child.QueueFree();
        }

        if (buttons.Length == 0)
        {
            buttons = [("Seguir explorando", () => { })];
        }

        foreach (var (label, action) in buttons)
        {
            _bannerButtons.AddChild(Action(label, () =>
            {
                _banner.Visible = false;
                action();
            }, string.Empty));
        }

        _banner.Visible = true;
    }

    public override void _Process(double delta)
    {
        if (_toastTime > 0)
        {
            _toastTime -= delta;
            _toast.Modulate = new Color(1, 1, 1, (float)Math.Clamp(_toastTime, 0, 1));
            _toast.Visible = _toastTime > 0;
        }

        _hint.Visible = _deploy.ButtonPressed;
    }

    private void BuildTopBar()
    {
        var bar = Panel(new Vector2(0, 0));
        bar.SetAnchorsPreset(LayoutPreset.TopWide);
        bar.OffsetLeft = 12;
        bar.OffsetRight = -12;
        bar.OffsetTop = 10;
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 18);
        bar.AddChild(row);

        var title = new Label { Text = "MARTIS" };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Accent);
        row.AddChild(title);
        _sol = Big(row);
        _clock = Small(row);
        _biomass = Small(row);
        _populations = Small(row);
        _points = Small(row);
        _points.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 1f));
        var inoculum = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        inoculum.AddThemeConstantOverride("separation", 2);
        _inoculum = new Label();
        _inoculum.AddThemeFontSizeOverride("font_size", 13);
        _inoculum.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        _inoculumBar = new ProgressBar { ShowPercentage = false, CustomMinimumSize = new Vector2(130, 5) };
        inoculum.AddChild(_inoculum);
        inoculum.AddChild(_inoculumBar);
        row.AddChild(inoculum);
        row.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        var speeds = new[] { ("❚❚", "Pausa (Espacio)"), ("▶", "1 sol cada 2 s"), ("▶▶", "2 soles/s"), ("▶▶▶", "8 soles/s") };
        for (var i = 0; i < speeds.Length; i++)
        {
            var index = i;
            var button = new Button { Text = speeds[i].Item1, TooltipText = speeds[i].Item2, ToggleMode = true, CustomMinimumSize = new Vector2(44, 0) };
            button.Pressed += () => SpeedChanged?.Invoke(index);
            _speedButtons.Add(button);
            row.AddChild(button);
        }

        _overlay = new OptionButton { TooltipText = "Capa de datos (teclas 0–4)" };
        foreach (var name in new[] { "Capa: ninguna", "Temperatura", "Agua", "Radiación", "Biomasa" })
        {
            _overlay.AddItem(name);
        }

        _overlay.ItemSelected += index => OverlayChanged?.Invoke((Overlay)(int)index);
        row.AddChild(_overlay);
        _researchButton = Action("Investigación", () => ResearchRequested?.Invoke(), "Árbol tecnológico (R)");
        row.AddChild(_researchButton);
        row.AddChild(Action("Guardar", () => SaveRequested?.Invoke(), "Guarda la partida (F5)"));
        row.AddChild(Action("Cargar", () => LoadRequested?.Invoke(), "Carga la última partida (F9)"));
        row.AddChild(Action("Nueva", () => NewGameRequested?.Invoke(), "Reinicia la misión con otra semilla"));
        row.AddChild(Action("Menú", () => MenuRequested?.Invoke(), "Campaña: capítulos y misiones"));
        AddChild(bar);
    }

    public void SelectOverlay(Overlay overlay) => _overlay.Select((int)overlay);

    /// <summary>Which panels a mode shows (V1): each mode is a presentation of the same state.</summary>
    public void SetPanels(bool mission, bool inspector, bool lab, bool feed)
    {
        _missionPanel.Visible = mission;
        _inspectorPanel.Visible = inspector;
        _labPanel.Visible = lab;
        _feedPanel.Visible = feed;
        if (!lab)
        {
            EndDeploy();
        }
    }

    private void BuildMission()
    {
        var panel = _missionPanel = Panel(new Vector2(290, 0));
        panel.SetAnchorsPreset(LayoutPreset.TopLeft);
        panel.OffsetLeft = 12;
        panel.OffsetTop = 70;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        _missionCaption = Caption("MISIÓN");
        box.AddChild(_missionCaption);
        _missionTitle = new Label { Text = "FIRST COLONY" };
        _missionTitle.AddThemeFontSizeOverride("font_size", 19);
        box.AddChild(_missionTitle);
        _missionRows = new VBoxContainer();
        _missionRows.AddThemeConstantOverride("separation", 6);
        box.AddChild(_missionRows);
        AddChild(panel);
    }

    private void BuildInspector()
    {
        var panel = _inspectorPanel = Panel(new Vector2(340, 0));
        panel.SetAnchorsPreset(LayoutPreset.TopRight);
        panel.OffsetLeft = -352;
        panel.OffsetRight = -12;
        panel.OffsetTop = 70;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        box.AddChild(Caption("INSPECTOR"));
        _inspector = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = false,
            ScrollActive = true,
            CustomMinimumSize = new Vector2(316, 250),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _inspector.AddThemeFontSizeOverride("normal_font_size", 13);
        _inspector.AddThemeFontSizeOverride("bold_font_size", 15);
        box.AddChild(_inspector);
        var actions = new HBoxContainer();
        actions.AddThemeConstantOverride("separation", 6);
        var analyze = Action("Analizar región", () => AnalyzeRequested?.Invoke(), "Estudia la celda y sus vecinas (objetivo de misión)");
        analyze.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        var send = Action("Enviar el rover (G)", () => RoverRequested?.Invoke(), "Orden al rover: conducir hasta esta celda");
        send.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        actions.AddChild(analyze);
        actions.AddChild(send);
        box.AddChild(actions);
        ShowCell(null);
        AddChild(panel);
    }

    private void BuildLab()
    {
        var panel = _labPanel = Panel(new Vector2(340, 0));
        panel.SetAnchorsPreset(LayoutPreset.BottomRight);
        panel.GrowVertical = GrowDirection.Begin;
        panel.OffsetLeft = -352;
        panel.OffsetRight = -12;
        panel.OffsetBottom = -12;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 6);
        panel.AddChild(box);
        box.AddChild(Caption("LABORATORIO DE DISEÑO"));
        _organism = new OptionButton();
        _organism.ItemSelected += _ => RebuildGenes();
        box.AddChild(_organism);

        var amounts = new HBoxContainer();
        amounts.AddChild(new Label { Text = "Biomasa kg" });
        _kg = new SpinBox { MinValue = 0.001, MaxValue = 5, Step = 0.001, Value = 0.5, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        amounts.AddChild(_kg);
        amounts.AddChild(new Label { Text = "Soles" });
        _sols = new SpinBox { MinValue = 5, MaxValue = 200, Step = 5, Value = 40 };
        amounts.AddChild(_sols);
        box.AddChild(amounts);

        _geneBox = new VBoxContainer();
        _geneBox.AddThemeConstantOverride("separation", 0);
        box.AddChild(_geneBox);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 6);
        _experiment = Action("Experimentar", () => ExperimentRequested?.Invoke(), "Simula una copia del mundo con este diseño en la celda seleccionada");
        _experiment.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        buttons.AddChild(_experiment);
        _deploy = new Button { Text = "Desplegar", ToggleMode = true, TooltipText = "Haz clic en el terreno para inocular", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _deploy.Toggled += on => DeployToggled?.Invoke(on);
        buttons.AddChild(_deploy);
        buttons.AddChild(Action("↺", RebuildGenes, "Vuelve al genoma base"));
        box.AddChild(buttons);

        _chart = new ForecastChart { CustomMinimumSize = new Vector2(0, 90) };
        box.AddChild(_chart);
        _forecastText = new Label { Text = "Elige una celda y pulsa Experimentar." };
        _forecastText.AddThemeFontSizeOverride("font_size", 13);
        box.AddChild(_forecastText);
        AddChild(panel);
    }

    private void BuildFeed()
    {
        var panel = _feedPanel = Panel(new Vector2(470, 190));
        panel.SetAnchorsPreset(LayoutPreset.BottomLeft);
        panel.GrowVertical = GrowDirection.Begin;
        panel.OffsetLeft = 12;
        panel.OffsetRight = 482;
        panel.OffsetBottom = -12;
        panel.OffsetTop = -202;
        var box = new VBoxContainer();
        panel.AddChild(box);
        box.AddChild(Caption("BITÁCORA"));
        _feed = new RichTextLabel { BbcodeEnabled = true, ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill };
        _feed.AddThemeFontSizeOverride("normal_font_size", 13);
        box.AddChild(_feed);
        AddChild(panel);
    }

    private void BuildToast()
    {
        _toast = new Label { HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _toast.SetAnchorsPreset(LayoutPreset.CenterTop);
        _toast.OffsetLeft = -420;
        _toast.OffsetRight = 420;
        _toast.OffsetTop = 74;
        _toast.OffsetBottom = 104;
        _toast.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        _toast.AddThemeFontSizeOverride("font_size", 17);
        _toast.AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddChild(_toast);

        _hint = new Label { Text = "MODO DESPLIEGUE · clic izquierdo en el terreno para inocular · Esc para salir", HorizontalAlignment = HorizontalAlignment.Center, Visible = false };
        _hint.SetAnchorsPreset(LayoutPreset.CenterBottom);
        _hint.OffsetLeft = -330;
        _hint.OffsetRight = 330;
        _hint.OffsetTop = -44;
        _hint.OffsetBottom = -18;
        _hint.AddThemeColorOverride("font_color", Bio);
        _hint.AddThemeColorOverride("font_shadow_color", Colors.Black);
        AddChild(_hint);

        _banner = Panel(new Vector2(520, 0));
        _banner.SetAnchorsPreset(LayoutPreset.Center);
        _banner.OffsetLeft = -260;
        _banner.OffsetRight = 260;
        _banner.OffsetTop = -100;
        _banner.GrowHorizontal = GrowDirection.Both;
        _banner.GrowVertical = GrowDirection.Both;
        _banner.Visible = false;
        var box = new VBoxContainer { Name = "Box" };
        box.AddThemeConstantOverride("separation", 10);
        var title = new Label { Name = "Title", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", Bio);
        var text = new Label { Name = "Text", HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(480, 0) };
        box.AddChild(title);
        box.AddChild(text);
        _bannerButtons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _bannerButtons.AddThemeConstantOverride("separation", 10);
        box.AddChild(_bannerButtons);
        _banner.AddChild(box);
        AddChild(_banner);
    }

    private void BuildResearch()
    {
        _researchPanel = Panel(new Vector2(500, 0));
        _researchPanel.SetAnchorsPreset(LayoutPreset.Center);
        _researchPanel.OffsetLeft = -250;
        _researchPanel.OffsetRight = 250;
        _researchPanel.OffsetTop = -230;
        _researchPanel.GrowHorizontal = GrowDirection.Both;
        _researchPanel.GrowVertical = GrowDirection.Both;
        _researchPanel.Visible = false;
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _researchPanel.AddChild(box);
        var header = new HBoxContainer();
        var caption = Caption("INVESTIGACIÓN · ÁRBOL TECNOLÓGICO");
        caption.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        header.AddChild(caption);
        header.AddChild(Action("✕", () => _researchPanel.Visible = false, "Cerrar (R)"));
        box.AddChild(header);
        _researchPoints = new Label();
        _researchPoints.AddThemeFontSizeOverride("font_size", 20);
        _researchPoints.AddThemeColorOverride("font_color", new Color(0.65f, 0.85f, 1f));
        box.AddChild(_researchPoints);
        _researchRows = new VBoxContainer();
        _researchRows.AddThemeConstantOverride("separation", 10);
        box.AddChild(_researchRows);
        AddChild(_researchPanel);
    }

    private void RebuildGenes()
    {
        foreach (var child in _geneBox.GetChildren())
        {
            child.QueueFree();
        }

        _genes.Clear();
        if (_organisms.Count == 0)
        {
            return;
        }

        foreach (var (id, value) in _organisms[_organism.Selected].BaseGenome)
        {
            var row = new HBoxContainer();
            var name = new Label { Text = Gene(id), CustomMinimumSize = new Vector2(130, 0), TooltipText = id };
            name.AddThemeFontSizeOverride("font_size", 13);
            var range = _limits?.GeneRange ?? 1.0;
            var slider = new HSlider { MinValue = Math.Max(0, value - range), MaxValue = Math.Min(1, value + range), Step = 0.01, Value = value, SizeFlagsHorizontal = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(120, 22) };
            if (range < 1.0)
            {
                slider.TooltipText = string.Create(CultureInfo.InvariantCulture, $"Edición limitada a ±{range:0.00}: investiga edición génica");
            }
            var shown = new Label { Text = value.ToString("0.00", CultureInfo.InvariantCulture), CustomMinimumSize = new Vector2(38, 0) };
            shown.AddThemeFontSizeOverride("font_size", 13);
            var baseValue = value;
            slider.ValueChanged += changed =>
            {
                shown.Text = changed.ToString("0.00", CultureInfo.InvariantCulture);
                shown.AddThemeColorOverride("font_color", Math.Abs(changed - baseValue) > 1e-9 ? Accent : Colors.White);
            };
            row.AddChild(name);
            row.AddChild(slider);
            row.AddChild(shown);
            _geneBox.AddChild(row);
            _genes[id] = (slider, shown);
        }
    }

    private static Theme BuildTheme()
    {
        var theme = new Theme { DefaultFontSize = 14 };
        var panel = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.04f, 0.06f, 0.78f),
            BorderColor = new Color(1f, 0.55f, 0.25f, 0.35f),
            ContentMarginLeft = 14,
            ContentMarginRight = 14,
            ContentMarginTop = 10,
            ContentMarginBottom = 10,
            ShadowColor = new Color(0, 0, 0, 0.35f),
            ShadowSize = 8,
        };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(10);
        theme.SetStylebox("panel", "PanelContainer", panel);

        StyleBoxFlat Button(Color colour)
        {
            var box = new StyleBoxFlat { BgColor = colour, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 5, ContentMarginBottom = 5 };
            box.SetCornerRadiusAll(6);
            return box;
        }

        theme.SetStylebox("normal", "Button", Button(new Color(1, 1, 1, 0.07f)));
        theme.SetStylebox("hover", "Button", Button(new Color(1f, 0.55f, 0.25f, 0.25f)));
        theme.SetStylebox("pressed", "Button", Button(new Color(1f, 0.55f, 0.25f, 0.55f)));
        theme.SetStylebox("hover_pressed", "Button", Button(new Color(1f, 0.55f, 0.25f, 0.65f)));
        theme.SetStylebox("focus", "Button", new StyleBoxEmpty());
        theme.SetStylebox("normal", "OptionButton", Button(new Color(1, 1, 1, 0.07f)));
        theme.SetStylebox("hover", "OptionButton", Button(new Color(1f, 0.55f, 0.25f, 0.25f)));
        theme.SetStylebox("pressed", "OptionButton", Button(new Color(1f, 0.55f, 0.25f, 0.4f)));
        var fill = new StyleBoxFlat { BgColor = Bio };
        fill.SetCornerRadiusAll(3);
        var back = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.1f) };
        back.SetCornerRadiusAll(3);
        theme.SetStylebox("fill", "ProgressBar", fill);
        theme.SetStylebox("background", "ProgressBar", back);
        var grabber = new StyleBoxFlat { BgColor = Accent, ContentMarginTop = 2, ContentMarginBottom = 2 };
        grabber.SetCornerRadiusAll(3);
        theme.SetStylebox("grabber_area", "HSlider", grabber);
        theme.SetStylebox("grabber_area_highlight", "HSlider", grabber);
        var slider = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.12f), ContentMarginTop = 2, ContentMarginBottom = 2 };
        slider.SetCornerRadiusAll(3);
        theme.SetStylebox("slider", "HSlider", slider);
        theme.SetColor("font_color", "Label", new Color(1f, 0.95f, 0.9f));
        theme.SetColor("default_color", "RichTextLabel", new Color(1f, 0.95f, 0.9f));
        return theme;
    }

    private static PanelContainer Panel(Vector2 minimum)
        => new() { CustomMinimumSize = minimum, MouseFilter = MouseFilterEnum.Stop };

    private static Label Caption(string text)
    {
        var label = new Label { Text = text };
        label.AddThemeFontSizeOverride("font_size", 11);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.55f, 0.25f, 0.9f));
        return label;
    }

    private static Label Big(Container parent)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", 20);
        parent.AddChild(label);
        return label;
    }

    private static Label Small(Container parent)
    {
        var label = new Label();
        label.AddThemeFontSizeOverride("font_size", 14);
        label.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.75f));
        parent.AddChild(label);
        return label;
    }

    private static Button Action(string text, Action action, string tooltip)
    {
        var button = new Button { Text = text, TooltipText = tooltip };
        button.Pressed += action;
        return button;
    }

    public static string Mass(double kg) => kg switch
    {
        >= 1 => string.Create(CultureInfo.InvariantCulture, $"{kg:0.00} kg"),
        >= 1e-3 => string.Create(CultureInfo.InvariantCulture, $"{kg * 1e3:0.0} g"),
        >= 1e-6 => string.Create(CultureInfo.InvariantCulture, $"{kg * 1e6:0.0} mg"),
        > 0 => string.Create(CultureInfo.InvariantCulture, $"{kg * 1e9:0.0} µg"),
        _ => "0",
    };

    private static string Category(string category) => category switch
    {
        "phototroph" => "fotótrofo",
        "decomposer" => "descomponedor",
        "extremophile" => "extremófilo",
        "eukaryote" => "eucariota",
        "multicellular" => "multicelular",
        "herbivore" => "herbívoro",
        "predator" => "depredador",
        "social" => "social",
        _ => category,
    };

    private static string Gene(string id) => id switch
    {
        "PHOTOSYNTHESIS" => "Fotosíntesis",
        "UV_PROTECTION" => "Protección UV",
        "THERMAL_TOLERANCE" => "Tolerancia térmica",
        "DESICCATION_TOLERANCE" => "Tolerancia sequía",
        "REPAIR_CAPACITY" => "Reparación ADN",
        "REPRODUCTION_RATE" => "Reproducción",
        "BIOFILM_PRODUCTION" => "Biopelícula",
        _ => id.Replace('_', ' ').ToLowerInvariant(),
    };

    private static string Resource(string name) => name switch
    {
        "CarbonDioxide" => "CO₂",
        "Water" => "Agua",
        "OrganicCarbon" => "Carbono orgánico",
        "Oxygen" => "O₂",
        "Nitrogen" => "Nitrógeno",
        "Phosphorus" => "Fósforo",
        "MineralCarbon" => "Carbono mineral",
        "Iron" => "Hierro",
        "Sulfur" => "Azufre",
        _ => name,
    };

    private static string Escape(string text) => text.Replace("[", "[lb]", StringComparison.Ordinal);
}
