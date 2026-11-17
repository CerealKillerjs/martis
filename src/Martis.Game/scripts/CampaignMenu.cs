using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Missions;

namespace Martis.Game;

/// <summary>
/// Title screen of the story mode (campaign «Génesis», ADR-019): one card per chapter with
/// its missions in order. A mission is playable once the one before it is complete; chapters
/// without missions yet are announced as «Próximamente». Mars keeps turning behind it.
/// </summary>
public partial class CampaignMenu : Control
{
    private static readonly Color Dim = new(1, 1, 1, 0.45f);
    private HBoxContainer _chapters = null!;
    private Button _continue = null!;
    private Button _resume = null!;
    private CampaignProgress? _progress;

    public event Action<CampaignMission>? MissionChosen;

    /// <summary>Free play on a map of that many cells per side (H3).</summary>
    public event Action<int>? FreePlayChosen;

    public event Action? LoadChosen;

    public event Action? ResumeChosen;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Stop;

        var shade = new ColorRect { Color = new Color(0.02f, 0.01f, 0.03f, 0.55f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var centre = new CenterContainer();
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        var column = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        column.AddThemeConstantOverride("separation", 18);
        centre.AddChild(column);

        var title = new Label { Text = "MARTIS", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 64);
        title.AddThemeColorOverride("font_color", Hud.Accent);
        title.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, 0.8f));
        title.AddThemeConstantOverride("shadow_offset_y", 3);
        column.AddChild(title);
        var subtitle = new Label { Text = "G É N E S I S   ·   de la primera bacteria a la primera mente", HorizontalAlignment = HorizontalAlignment.Center };
        subtitle.AddThemeFontSizeOverride("font_size", 17);
        subtitle.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.85f, 0.85f));
        column.AddChild(subtitle);

        _chapters = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _chapters.AddThemeConstantOverride("separation", 14);
        column.AddChild(_chapters);

        var actions = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        actions.AddThemeConstantOverride("separation", 12);
        _continue = new Button { CustomMinimumSize = new Vector2(320, 46) };
        _continue.AddThemeFontSizeOverride("font_size", 18);
        _continue.Pressed += () =>
        {
            if (_progress?.Next is { } next)
            {
                MissionChosen?.Invoke(next);
            }
        };
        actions.AddChild(_continue);
        _resume = Button("Volver a la partida", () => ResumeChosen?.Invoke());
        actions.AddChild(_resume);
        actions.AddChild(Button("Cargar partida", () => LoadChosen?.Invoke()));
        var size = new OptionButton { TooltipText = "Tamaño del mapa de la exploración libre", CustomMinimumSize = new Vector2(0, 36) };
        foreach (var grid in new[] { 64, 128, 256 })
        {
            size.AddItem(string.Create(CultureInfo.InvariantCulture, $"{grid}×{grid}"), grid);
        }

        actions.AddChild(Button("Exploración libre", () => FreePlayChosen?.Invoke(size.GetItemId(size.Selected))));
        actions.AddChild(size);
        column.AddChild(actions);
    }

    /// <summary>Rebuilds the cards from the campaign and the player's progress.</summary>
    public void Show(CampaignProgress progress, bool canResume)
    {
        _progress = progress;
        foreach (var child in _chapters.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var chapter in progress.Campaign.Chapters)
        {
            _chapters.AddChild(Card(chapter, progress));
        }

        var next = progress.Next;
        _continue.Text = next is null
            ? "Campaña completada"
            : progress.Completed.Count == 0 ? $"▶  Empezar: {Short(next, progress)}" : $"▶  Continuar: {Short(next, progress)}";
        _continue.Disabled = next is null;
        _resume.Visible = canResume;
        Visible = true;
    }

    private Control Card(CampaignChapter chapter, CampaignProgress progress)
    {
        var open = chapter.Missions.Any(mission => progress.IsUnlocked(mission.Id));
        var done = chapter.Playable && chapter.Missions.All(mission => progress.IsComplete(mission.Id));
        var panel = new PanelContainer { CustomMinimumSize = new Vector2(236, 330) };
        var style = new StyleBoxFlat
        {
            BgColor = open ? new Color(0.07f, 0.05f, 0.06f, 0.88f) : new Color(0.04f, 0.035f, 0.045f, 0.75f),
            BorderColor = done ? Hud.Bio : open ? Hud.Accent : new Color(1, 1, 1, 0.12f),
            ContentMarginLeft = 16,
            ContentMarginRight = 16,
            ContentMarginTop = 14,
            ContentMarginBottom = 14,
            ShadowColor = new Color(0, 0, 0, 0.4f),
            ShadowSize = 10,
        };
        style.SetBorderWidthAll(open ? 2 : 1);
        style.SetCornerRadiusAll(12);
        panel.AddThemeStyleboxOverride("panel", style);

        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 8);
        panel.AddChild(box);
        var number = new Label { Text = string.Create(CultureInfo.InvariantCulture, $"CAPÍTULO {chapter.Number}") };
        number.AddThemeFontSizeOverride("font_size", 12);
        number.AddThemeColorOverride("font_color", done ? Hud.Bio : Hud.Accent);
        box.AddChild(number);
        var name = new Label { Text = chapter.Title };
        name.AddThemeFontSizeOverride("font_size", 23);
        name.AddThemeColorOverride("font_color", open || done ? Colors.White : Dim);
        box.AddChild(name);
        var summary = new Label { Text = chapter.Summary, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(204, 0) };
        summary.AddThemeFontSizeOverride("font_size", 13);
        summary.AddThemeColorOverride("font_color", new Color(1, 1, 1, open ? 0.75f : 0.4f));
        box.AddChild(summary);
        var grid = new Label { Text = string.Create(CultureInfo.InvariantCulture, $"Mapa {chapter.GridSize}×{chapter.GridSize}") };
        grid.AddThemeFontSizeOverride("font_size", 11);
        grid.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1f, 0.7f));
        box.AddChild(grid);
        box.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        if (!chapter.Playable)
        {
            var soon = new Label { Text = "Próximamente", HorizontalAlignment = HorizontalAlignment.Center };
            soon.AddThemeColorOverride("font_color", Dim);
            box.AddChild(soon);
            return panel;
        }

        foreach (var mission in chapter.Missions)
        {
            var complete = progress.IsComplete(mission.Id);
            var unlocked = progress.IsUnlocked(mission.Id);
            var label = string.Create(CultureInfo.InvariantCulture, $"{(complete ? "✔" : unlocked ? "▶" : "🔒")}  {chapter.Number}.{mission.Number}  {Title(mission)}");
            var button = Button(label, () => MissionChosen?.Invoke(mission));
            button.Alignment = HorizontalAlignment.Left;
            button.Disabled = !unlocked;
            if (complete)
            {
                button.AddThemeColorOverride("font_color", Hud.Bio);
            }

            box.AddChild(button);
        }

        return panel;
    }

    private static string Short(CampaignMission mission, CampaignProgress progress)
        => string.Create(CultureInfo.InvariantCulture, $"{progress.Campaign.ChapterOf(mission.Id).Number}.{mission.Number} {Title(mission)}");

    /// <summary>The mission name without its «— 1.1» suffix, in title case.</summary>
    private static string Title(CampaignMission mission) => mission.Id switch
    {
        "awakening" => "Despertar",
        "first_colony" => "Primera colonia",
        "selection" => "Presión selectiva",
        "gradients" => "Gradientes",
        "closed_cycle" => "Ciclo cerrado",
        "cold_frontier" => "Frontera helada",
        "oxygen" => "Atmósfera",
        "symbiosis" => "Simbiosis",
        "complex_cells" => "Células complejas",
        "multicellular" => "Multicelularidad",
        "grazers" => "Herbívoros",
        "food_web" => "Red trófica",
        "predators" => "Depredadores",
        _ => mission.Id.Replace('_', ' '),
    };

    private static Button Button(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 36) };
        button.Pressed += action;
        return button;
    }
}
