using System;
using Godot;
using Martis.Application;
using Martis.Audio;

namespace Martis.Game;

/// <summary>
/// Menu opened with Esc: darkens the world, stops time and gathers what used to crowd the top
/// bar. A row with the four modes (F1–F4, V1), then Continuar, Guardar, Cargar partida,
/// Nueva partida, Campaña, sound options, Volver al título and Salir.
/// </summary>
public partial class PauseMenu : Control
{
    /// <summary>The four modes of presence (Documento Maestro v2.0 §3.1) with their keys.</summary>
    public static readonly (GameMode Mode, string Label, string Key, string Tip)[] Modes =
    [
        (GameMode.Exploration, "EXPLORACIÓN", "F1", "Conduce el rover en tercera persona (Tab)"),
        (GameMode.Strategic, "ESTRATÉGICO", "F2", "Capas del mapa, inspector y órdenes"),
        (GameMode.Laboratory, "LABORATORIO", "F3", "Diseños, experimentos y análisis"),
        (GameMode.Planetary, "PLANETARIO", "F4", "El globo y los balances globales"),
    ];

    private readonly Button[] _modeButtons = new Button[Modes.Length];
    private PanelContainer _panel = null!;
    private VBoxContainer _main = null!;
    private VBoxContainer _options = null!;
    private SaveList _saves = null!;
    private Button _continue = null!;
    private bool _open;

    public event Action<bool>? Toggled;

    public event Action<GameMode>? ModeChosen;

    public event Action? SaveChosen;

    public event Action<string>? LoadChosen;

    public event Action? NewGameChosen;

    public event Action? CampaignChosen;

    public bool IsOpen => _open;

    /// <summary>Database the save list reads (the game's <c>--db</c>), or null for <c>user://martis.db</c>.</summary>
    public string? SavePath { get; set; }

    /// <summary>Texts of the main column, top to bottom, for the smoke check.</summary>
    public string[] Items()
    {
        var items = new System.Collections.Generic.List<string>();
        foreach (var button in _modeButtons)
        {
            items.Add(button.Text);
        }

        foreach (var child in _main.GetChildren())
        {
            if (child is Button button)
            {
                items.Add(button.Text);
            }
        }

        return [.. items];
    }

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.02f, 0.01f, 0.03f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        var centre = new CenterContainer { MouseFilter = MouseFilterEnum.Ignore };
        centre.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        AddChild(centre);
        _panel = new PanelContainer { CustomMinimumSize = new Vector2(600, 0) };
        centre.AddChild(_panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        _panel.AddChild(box);
        var title = new Label { Text = "PAUSA", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", Hud.Accent);
        box.AddChild(title);

        var modes = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        modes.AddThemeConstantOverride("separation", 4);
        box.AddChild(modes);
        for (var i = 0; i < Modes.Length; i++)
        {
            var (mode, label, key, tip) = Modes[i];
            var button = new Button
            {
                Text = $"{label}  {key}",
                ToggleMode = true,
                TooltipText = tip,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 40),
            };
            button.AddThemeFontSizeOverride("font_size", 13);
            button.Pressed += () =>
            {
                Close();
                ModeChosen?.Invoke(mode);
            };
            modes.AddChild(button);
            _modeButtons[i] = button;
        }

        _main = new VBoxContainer();
        _main.AddThemeConstantOverride("separation", 8);
        box.AddChild(_main);
        _continue = Item("Continuar", Close);
        Item("Guardar partida  (F5)", () => SaveChosen?.Invoke());
        Item("Cargar partida", () => ShowSaves(true));
        Item("Nueva partida", () =>
        {
            Close();
            NewGameChosen?.Invoke();
        });
        Item("Campaña: capítulos y misiones", () =>
        {
            Close();
            CampaignChosen?.Invoke();
        });
        Item("Opciones de sonido", () => _options.Visible = !_options.Visible);
        _options = new VBoxContainer { Visible = false };
        _options.AddThemeConstantOverride("separation", 8);
        VolumeControls.AddTo(_options);
        _main.AddChild(_options);
        Item("Volver al título", () => GameLaunch.Instance?.ToTitle());
        Item("Salir del juego", () => GetTree().Quit());

        _saves = new SaveList { Visible = false };
        box.AddChild(_saves);
        _saves.Closed += () => ShowSaves(false);
        _saves.Chosen += id =>
        {
            Close();
            LoadChosen?.Invoke(id);
        };
    }

    /// <summary>Opens the menu with the current mode marked.</summary>
    public void Open(GameMode current)
    {
        if (_open)
        {
            return;
        }

        _open = true;
        for (var i = 0; i < Modes.Length; i++)
        {
            _modeButtons[i].SetPressedNoSignal(Modes[i].Mode == current);
        }

        ShowSaves(false);
        Visible = true;
        GameAudio.Instance?.Play(Sfx.Pause);
        Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(this, "modulate:a", 1f, 0.2);
        UiMotion.Pop(_panel, 0.85f, 0.35);
        _continue.GrabFocus();
        Toggled?.Invoke(true);
    }

    public void Close()
    {
        if (!_open)
        {
            return;
        }

        _open = false;
        GameAudio.Instance?.Play(Sfx.Resume);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.15);
        tween.TweenCallback(Callable.From(() => Visible = _open));
        Toggled?.Invoke(false);
    }

    /// <summary>Esc inside the menu: leaves the save list first, then closes.</summary>
    public void Back()
    {
        if (_saves.Visible)
        {
            ShowSaves(false);
        }
        else
        {
            Close();
        }
    }

    private void ShowSaves(bool show)
    {
        if (show)
        {
            _saves.Fill(SaveService.List(SavePath));
        }

        _saves.Visible = show;
        _main.Visible = !show;
        if (show)
        {
            _saves.FocusFirst();
        }
    }

    private Button Item(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += action;
        _main.AddChild(button);
        return button;
    }
}
