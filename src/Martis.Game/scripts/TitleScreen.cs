using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Audio;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// Animated title screen: a cinematic Martian dusk over glowing colonies, the MARTIS logo
/// revealed letter by letter, and the main menu: Continuar (the newest save, when there is one),
/// Nueva partida (the expedition), Campaña, Cargar partida (pick a save), Opciones and Salir.
/// Options: <c>--title-shot=path.png</c> (with <c>--title-time=s</c>, <c>--title-load</c> to open the
/// save list) takes a screenshot and quits;
/// <c>--title-smoke</c> checks the menu headless and quits with 0/1.
/// </summary>
public partial class TitleScreen : Node
{
    private const int Size = 64;
    private const ulong WorldSeed = 21;

    private static readonly (int X, int Y, float Radius, float Decomposers)[] Patches =
    [
        (32, 32, 4.5f, 0.15f), (26, 36, 3f, 0.6f), (37, 27, 3.5f, 0.3f), (40, 38, 2.5f, 0.8f),
        (22, 26, 2.5f, 0.2f), (31, 42, 2f, 0.5f), (45, 31, 2f, 0.1f),
    ];

    private readonly List<Control> _menuItems = [];
    private readonly Dictionary<string, string> _options = new(StringComparer.Ordinal);
    private World3D _world = null!;
    private Control _ui = null!;
    private HBoxContainer _logo = null!;
    private Label _tagline = null!;
    private ColorRect _rule = null!;
    private VBoxContainer _menu = null!;
    private PanelContainer _settings = null!;
    private PanelContainer _loadPanel = null!;
    private SaveList _saveList = null!;
    private IReadOnlyList<SaveSummary> _saves = [];
    private ColorRect _barTop = null!;
    private ColorRect _barBottom = null!;
    private Label _footer = null!;
    private double _time;
    private double _nextPulse = 3.0;
    private int _pulseIndex;
    private bool _introDone;
    private Vector3 _focus;
    private Tween? _intro;

    public override void _Ready()
    {
        foreach (var arg in OS.GetCmdlineUserArgs())
        {
            var parts = arg.TrimStart('-').Split('=', 2);
            _options[parts[0]] = parts.Length > 1 ? parts[1] : string.Empty;
        }

        if (GameLaunch.ScriptedRun())
        {
            // CI and screenshots go straight to the game, as before the title screen existed.
            CallDeferred(MethodName.SkipToGame);
            return;
        }

        BuildWorld();
        BuildUi();
        PlayIntro();
        if (_options.ContainsKey("title-smoke"))
        {
            CallDeferred(MethodName.Smoke);
        }
        else if (_options.TryGetValue("title-shot", out var shot))
        {
            Screenshot(shot);
        }
    }

    public override void _Process(double delta)
    {
        if (_world is null)
        {
            return;
        }

        _time += delta;
        if (_time <= FlightSeconds + 0.1)
        {
            Fly((float)(_time / FlightSeconds));
        }
        else
        {
            _world.Camera.Idle = true;
        }

        // A slow dusk: blue Martian sunset fading into a starry night and back, every three minutes.
        var cycle = (float)(0.5 - (0.5 * Math.Cos(_time * Math.Tau / 180.0)));
        _world.TimeOfDay = 0.715f + (0.2f * cycle);
        GameAudio.Instance?.Observe(new MusicContext { OnTitle = true });

        if (_time >= _nextPulse)
        {
            var patch = Patches[_pulseIndex % Patches.Length];
            _pulseIndex++;
            var tint = _pulseIndex % 3 == 0 ? new Color(1f, 0.82f, 0.3f) : new Color(0.4f, 1f, 0.7f);
            _world.Pulse((patch.Y * Size) + patch.X, Size, tint);
            _nextPulse = _time + 2.2 + (_pulseIndex % 4 * 0.7);
        }

        // Logo breathing glow.
        var glow = 0.55f + (0.25f * MathF.Sin((float)_time * 1.3f));
        foreach (var letter in _logo.GetChildren().OfType<Label>())
        {
            letter.AddThemeColorOverride("font_shadow_color", new Color(1f, 0.45f, 0.15f, glow));
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        var skip = @event is InputEventKey { Pressed: true, Echo: false } or InputEventMouseButton { Pressed: true };
        if (skip && !_introDone && _intro is not null)
        {
            // Any key finishes the intro at once.
            _intro.CustomStep(60);
            _time = Math.Max(_time, FlightSeconds);
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventKey { Pressed: true, Keycode: Key.Escape })
        {
            if (_settings.Visible)
            {
                ShowPanel(_settings, false);
            }
            else if (_loadPanel.Visible)
            {
                ShowPanel(_loadPanel, false);
            }
        }
    }

    private const double FlightSeconds = 7.0;

    /// <summary>Opening camera flight: from high above the plain down to a low pass over the colonies.</summary>
    private void Fly(float progress)
    {
        var t = Math.Clamp(progress, 0f, 1f);
        var ease = 1f - MathF.Pow(1f - t, 3f);
        var distance = Mathf.Lerp(240f, 64f, ease);
        var pitch = Mathf.Lerp(1.15f, 0.2f, ease);
        var yaw = Mathf.Lerp(2.5f, 1.55f, ease);
        var lift = new Vector3(0, Mathf.Lerp(30f, 0f, ease), 0);
        _world.Camera.Frame(_focus + lift, distance, yaw, pitch, snap: true);
    }

    private void SkipToGame()
    {
        GameLaunch.Instance!.Mode = GameLaunch.Sandbox;
        GetTree().ChangeSceneToFile(GameLaunch.GameScene);
    }

    private void BuildWorld()
    {
        _world = new World3D { Name = "World" };
        AddChild(_world);
        _world.Build(Size, Size, WorldSeed);
        _world.Show(ShowcaseView());
        _world.PlaceLander(29, 30);
        _focus = _world.Ground.CellCenter(32, 33) + new Vector3(0, 0, 16f);
        Fly(0f);
        _world.Camera.SetProcessUnhandledInput(false);
    }

    /// <summary>A hand-made world view: patches of microbial mats around the lander.</summary>
    private static WorldView ShowcaseView()
    {
        var cells = Size * Size;
        var biomass = new float[cells];
        var photo = new float[cells];
        var decomp = new float[cells];
        var hue = new float[cells];
        var variants = new int[cells];
        var random = new AudioRandom(WorldSeed);
        foreach (var (px, py, radius, share) in Patches)
        {
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var d = MathF.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
                    if (d > radius || random.Chance(0.25f * d / radius))
                    {
                        continue;
                    }

                    var i = (y * Size) + x;
                    var kg = 0.4f * MathF.Exp(-d * 1.2f) * random.Range(0.4f, 1.2f);
                    biomass[i] += kg;
                    decomp[i] += kg * share;
                    photo[i] += kg * (1f - share);
                    hue[i] = random.NextFloat();
                    variants[i] = 1 + random.Next(3);
                }
            }
        }

        var zeros = new float[cells];
        return new WorldView(0, Size, Size, zeros, zeros, zeros, zeros, biomass, photo, decomp, hue, variants, biomass.Sum(), Patches.Length);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer { Name = "UI" };
        AddChild(layer);
        _ui = new Control { Theme = TitleTheme(), MouseFilter = Control.MouseFilterEnum.Ignore };
        _ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_ui);

        // Vignette on the left so the menu reads over the bright sky.
        var shade = new TextureRect
        {
            Texture = new GradientTexture2D
            {
                Gradient = new Gradient { Colors = [new Color(0.02f, 0.01f, 0.02f, 0.82f), new Color(0.02f, 0.01f, 0.02f, 0f)], Offsets = [0f, 1f] },
                FillFrom = new Vector2(0, 0.5f),
                FillTo = new Vector2(0.62f, 0.5f),
                Width = 256,
                Height = 16,
            },
            StretchMode = TextureRect.StretchModeEnum.Scale,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _ui.AddChild(shade);

        _barTop = Bar(top: true);
        _barBottom = Bar(top: false);

        var column = new VBoxContainer { Name = "Column" };
        column.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
        column.OffsetLeft = 110;
        column.OffsetRight = 760;
        column.OffsetTop = 150;
        column.AddThemeConstantOverride("separation", 10);
        _ui.AddChild(column);

        _logo = new HBoxContainer { Name = "Logo" };
        _logo.AddThemeConstantOverride("separation", 6);
        foreach (var ch in "MARTIS")
        {
            var letter = new Label { Text = ch.ToString(), Modulate = new Color(1, 1, 1, 0) };
            letter.AddThemeFontSizeOverride("font_size", 132);
            letter.AddThemeColorOverride("font_color", new Color(1f, 0.93f, 0.86f));
            letter.AddThemeColorOverride("font_outline_color", new Color(1f, 0.5f, 0.2f, 0.9f));
            letter.AddThemeConstantOverride("outline_size", 3);
            letter.AddThemeConstantOverride("shadow_outline_size", 26);
            letter.AddThemeConstantOverride("shadow_offset_x", 0);
            letter.AddThemeConstantOverride("shadow_offset_y", 0);
            _logo.AddChild(letter);
        }

        column.AddChild(_logo);
        _rule = new ColorRect { Color = Hud.Accent, CustomMinimumSize = new Vector2(0, 3), Scale = new Vector2(0, 1) };
        column.AddChild(_rule);
        _tagline = new Label { Text = "Diseña vida. Siémbrala en Marte. Mírala evolucionar.", VisibleRatio = 0 };
        _tagline.AddThemeFontSizeOverride("font_size", 22);
        _tagline.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.7f, 0.9f));
        column.AddChild(_tagline);
        column.AddChild(new Control { CustomMinimumSize = new Vector2(0, 46) });

        _menu = new VBoxContainer { Name = "Menu" };
        _menu.AddThemeConstantOverride("separation", 6);
        column.AddChild(_menu);
        _saves = SaveService.List(_options.TryGetValue("db", out var db) ? db : null);
        if (_saves.Count > 0)
        {
            var latest = _saves[0];
            AddItem("Continuar", latest.Label, () => GameLaunch.Instance?.StartGame(GameLaunch.LoadLatest, saveId: latest.Id));
        }

        AddItem("Nueva partida", "Expedición a Jezero: el rover, el laboratorio y la primera colonia", () => GameLaunch.Instance?.StartGame(GameLaunch.Sandbox));
        AddItem("Campaña", "Génesis: capítulos y misiones guiadas, de la primera bacteria a la mente", () => GameLaunch.Instance?.StartGame(GameLaunch.Campaign));
        AddItem("Cargar partida", _saves.Count == 0 ? "Todavía no hay partidas guardadas" : $"Elige entre tus {_saves.Count} partidas guardadas", () => ShowPanel(_loadPanel, true));
        AddItem("Opciones", "Volumen de música, efectos y ambiente", () => ShowPanel(_settings, true));
        AddItem("Salir", string.Empty, Quit);

        _footer = new Label
        {
            Text = "MARTIS v0.1.0  ·  música y sonido generados por procedimiento  ·  pulsa cualquier tecla para saltar la intro",
            Modulate = new Color(1, 1, 1, 0),
        };
        _footer.AddThemeFontSizeOverride("font_size", 13);
        _footer.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.55f));
        _footer.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        _footer.OffsetLeft = 110;
        _footer.OffsetTop = -44;
        _footer.OffsetBottom = -20;
        _footer.GrowVertical = Control.GrowDirection.Begin;
        _ui.AddChild(_footer);

        _settings = BuildSettings();
        _ui.AddChild(_settings);
        _loadPanel = SidePanel();
        _saveList = new SaveList();
        _loadPanel.AddChild(_saveList);
        _ui.AddChild(_loadPanel);
        _saveList.Fill(_saves);
        _saveList.Chosen += id => GameLaunch.Instance?.StartGame(GameLaunch.LoadLatest, saveId: id);
        _saveList.Closed += () => ShowPanel(_loadPanel, false);
    }

    private ColorRect Bar(bool top)
    {
        var bar = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.SetAnchorsPreset(top ? Control.LayoutPreset.TopWide : Control.LayoutPreset.BottomWide);
        if (top)
        {
            bar.OffsetBottom = 110;
        }
        else
        {
            bar.OffsetTop = -110;
        }

        _ui.AddChild(bar);
        return bar;
    }

    private void AddItem(string text, string hint, Action action)
    {
        var row = new VBoxContainer { Modulate = new Color(1, 1, 1, 0) };
        row.AddThemeConstantOverride("separation", 0);
        var button = new Button { Text = text, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(420, 52) };
        button.AddThemeFontSizeOverride("font_size", 28);
        var detail = new Label { Text = hint, Modulate = new Color(1, 1, 1, 0) };
        detail.AddThemeFontSizeOverride("font_size", 14);
        detail.AddThemeColorOverride("font_color", new Color(1f, 0.75f, 0.55f));
        button.MouseEntered += () => Hover(button, detail, true);
        button.MouseExited += () => Hover(button, detail, false);
        button.FocusEntered += () => Hover(button, detail, true);
        button.FocusExited += () => Hover(button, detail, false);
        button.Pressed += action;
        row.AddChild(button);
        row.AddChild(detail);
        _menu.AddChild(row);
        _menuItems.Add(row);
    }

    private void Hover(Button button, Label detail, bool on)
    {
        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(button, "position:x", on ? 18f : 0f, 0.18);
        tween.TweenProperty(detail, "modulate:a", on ? 1f : 0f, 0.2);
        tween.TweenProperty(detail, "position:x", on ? 22f : 0f, 0.2);
    }

    private void PlayIntro()
    {
        _intro = CreateTween();
        _intro.SetParallel();
        _intro.TweenProperty(_barTop, "offset_bottom", 0f, 2.2).SetDelay(0.4).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _intro.TweenProperty(_barBottom, "offset_top", 0f, 2.2).SetDelay(0.4).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
        _intro.TweenCallback(Callable.From(() => GameAudio.Instance?.Play(Sfx.TitleSting))).SetDelay(1.2);
        var letters = _logo.GetChildren().OfType<Label>().ToArray();
        for (var i = 0; i < letters.Length; i++)
        {
            var delay = 1.4 + (i * 0.14);
            letters[i].PivotOffset = new Vector2(40, 90);
            letters[i].Scale = new Vector2(1.6f, 1.6f);
            _intro.TweenProperty(letters[i], "modulate:a", 1f, 0.6).SetDelay(delay);
            _intro.TweenProperty(letters[i], "scale", Vector2.One, 0.9).SetDelay(delay).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        }

        _intro.TweenProperty(_rule, "scale:x", 1f, 1.0).SetDelay(2.4).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        _intro.TweenProperty(_tagline, "visible_ratio", 1f, 1.4).SetDelay(2.8);
        for (var i = 0; i < _menuItems.Count; i++)
        {
            var item = _menuItems[i];
            var delay = 3.6 + (i * 0.11);
            item.Position += new Vector2(-60, 0);
            _intro.TweenProperty(item, "modulate:a", 1f, 0.5).SetDelay(delay);
            _intro.TweenProperty(item, "position:x", item.Position.X + 60, 0.6).SetDelay(delay).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        }

        _intro.TweenProperty(_footer, "modulate:a", 1f, 1.0).SetDelay(4.4);
        _intro.Finished += () =>
        {
            _introDone = true;
            _footer.Text = "MARTIS v0.1.0  ·  música y sonido generados por procedimiento";
            _menuItems[0].GetChild<Button>(0).GrabFocus();
        };
    }

    /// <summary>Panel on the right of the title for Opciones and Cargar partida.</summary>
    private static PanelContainer SidePanel()
    {
        var panel = new PanelContainer { Visible = false, CustomMinimumSize = new Vector2(520, 0) };
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        panel.OffsetLeft = -620;
        panel.OffsetRight = -100;
        panel.OffsetTop = -200;
        panel.GrowVertical = Control.GrowDirection.Both;
        return panel;
    }

    private PanelContainer BuildSettings()
    {
        var panel = SidePanel();
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 12);
        panel.AddChild(box);
        var title = new Label { Text = "OPCIONES" };
        title.AddThemeFontSizeOverride("font_size", 24);
        title.AddThemeColorOverride("font_color", Hud.Accent);
        box.AddChild(title);

        VolumeControls.AddTo(box);
        var back = new Button { Text = "Volver" };
        back.Pressed += () => ShowPanel(_settings, false);
        box.AddChild(back);
        return panel;
    }

    private void ShowPanel(PanelContainer panel, bool show)
    {
        if (show == panel.Visible)
        {
            return;
        }

        if (show)
        {
            foreach (var other in new[] { _settings, _loadPanel })
            {
                if (other != panel && other.Visible)
                {
                    other.Visible = false;
                }
            }

            panel.Visible = true;
            panel.Modulate = new Color(1, 1, 1, 0);
            panel.Position += new Vector2(40, 0);
            if (panel == _loadPanel)
            {
                _saveList.FocusFirst();
            }
        }

        var tween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(panel, "modulate:a", show ? 1f : 0f, 0.3);
        tween.TweenProperty(panel, "position:x", panel.Position.X + (show ? -40 : 40), 0.3);
        tween.TweenProperty(_menu, "modulate:a", show ? 0.35f : 1f, 0.3);
        if (!show)
        {
            tween.Chain().TweenCallback(Callable.From(() =>
            {
                panel.Visible = false;
                panel.Position += new Vector2(-40, 0);
            }));
        }
    }

    private async void Quit()
    {
        var tween = CreateTween();
        tween.TweenProperty(_ui, "modulate:a", 0f, 0.5);
        await ToSignal(tween, Tween.SignalName.Finished);
        GetTree().Quit();
    }

    private static Theme TitleTheme()
    {
        var theme = new Theme { DefaultFontSize = 16 };
        var font = new SystemFont { FontNames = ["Bahnschrift", "Segoe UI", "Noto Sans", "DejaVu Sans"], FontWeight = 600 };
        theme.DefaultFont = font;
        StyleBoxFlat Box(Color background, Color border, int left)
        {
            var box = new StyleBoxFlat { BgColor = background, BorderColor = border, ContentMarginLeft = 18, ContentMarginRight = 12, ContentMarginTop = 6, ContentMarginBottom = 6 };
            box.BorderWidthLeft = left;
            return box;
        }

        theme.SetStylebox("normal", "Button", Box(new Color(0, 0, 0, 0), Colors.Transparent, 0));
        theme.SetStylebox("hover", "Button", Box(new Color(1f, 0.5f, 0.2f, 0.16f), Hud.Accent, 4));
        theme.SetStylebox("focus", "Button", Box(new Color(1f, 0.5f, 0.2f, 0.10f), Hud.Accent, 4));
        theme.SetStylebox("pressed", "Button", Box(new Color(1f, 0.5f, 0.2f, 0.32f), Hud.Accent, 6));
        theme.SetColor("font_color", "Button", new Color(1f, 0.93f, 0.86f, 0.92f));
        theme.SetColor("font_hover_color", "Button", Colors.White);
        theme.SetColor("font_focus_color", "Button", Colors.White);
        theme.SetColor("font_color", "Label", new Color(1f, 0.95f, 0.9f));
        theme.SetColor("font_color", "CheckButton", new Color(1f, 0.95f, 0.9f));
        var panel = new StyleBoxFlat { BgColor = new Color(0.05f, 0.04f, 0.06f, 0.88f), BorderColor = new Color(1f, 0.55f, 0.25f, 0.4f), ContentMarginLeft = 24, ContentMarginRight = 24, ContentMarginTop = 20, ContentMarginBottom = 20 };
        panel.SetBorderWidthAll(1);
        panel.SetCornerRadiusAll(12);
        theme.SetStylebox("panel", "PanelContainer", panel);
        var grabber = new StyleBoxFlat { BgColor = Hud.Accent, ContentMarginTop = 3, ContentMarginBottom = 3 };
        grabber.SetCornerRadiusAll(3);
        theme.SetStylebox("grabber_area", "HSlider", grabber);
        theme.SetStylebox("grabber_area_highlight", "HSlider", grabber);
        var track = new StyleBoxFlat { BgColor = new Color(1, 1, 1, 0.14f), ContentMarginTop = 3, ContentMarginBottom = 3 };
        track.SetCornerRadiusAll(3);
        theme.SetStylebox("slider", "HSlider", track);
        return theme;
    }

    private async void Screenshot(string path)
    {
        var seconds = _options.TryGetValue("title-time", out var t) ? double.Parse(t, CultureInfo.InvariantCulture) : 7.0;
        while (_time < seconds)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        if (_options.ContainsKey("title-load"))
        {
            // «Cargar partida» open, for review.
            ShowPanel(_loadPanel, true);
            for (var i = 0; i < 30; i++)
            {
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
        }

        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"MARTIS title screenshot {path} {error}");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private async void Smoke()
    {
        _intro?.CustomStep(60);
        for (var i = 0; i < 10; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var buttons = _menuItems.Select(item => item.GetChild<Button>(0).Text).ToArray();
        string[] expected = ["Nueva partida", "Campaña", "Cargar partida", "Opciones", "Salir"];
        var ok = _introDone
            && buttons.SequenceEqual(_saves.Count > 0 ? ["Continuar", .. expected] : expected)
            && _logo.GetChildren().OfType<Label>().All(letter => letter.Modulate.A > 0.99f)
            && GameAudio.Instance is not null
            && GameLaunch.Instance is not null;
        GD.Print($"MARTIS title smoke ok={ok} menu={string.Join("|", buttons)} mood={GameAudio.Instance?.Mood}");
        GetTree().Quit(ok ? 0 : 1);
    }
}
