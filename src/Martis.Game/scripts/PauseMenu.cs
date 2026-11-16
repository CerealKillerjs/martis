using System;
using Godot;
using Martis.Audio;

namespace Martis.Game;

/// <summary>
/// Pause overlay opened with Esc: darkens and blurs the world, stops time and offers
/// Continuar, volume options, Volver al título and Salir.
/// </summary>
public partial class PauseMenu : Control
{
    private PanelContainer _panel = null!;
    private VBoxContainer _options = null!;

    public event Action<bool>? Toggled;

    public bool IsOpen => Visible;

    public override void _Ready()
    {
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        Visible = false;
        MouseFilter = MouseFilterEnum.Stop;
        var shade = new ColorRect { Color = new Color(0.02f, 0.01f, 0.03f, 0.62f), MouseFilter = MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(shade);

        _panel = new PanelContainer { CustomMinimumSize = new Vector2(440, 0) };
        _panel.SetAnchorsPreset(LayoutPreset.Center);
        _panel.GrowHorizontal = GrowDirection.Both;
        _panel.GrowVertical = GrowDirection.Both;
        AddChild(_panel);
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 10);
        _panel.AddChild(box);
        var title = new Label { Text = "PAUSA", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 30);
        title.AddThemeColorOverride("font_color", Hud.Accent);
        box.AddChild(title);
        box.AddChild(Item("Continuar", () => Close()));
        box.AddChild(Item("Opciones de sonido", () => _options.Visible = !_options.Visible));
        _options = new VBoxContainer { Visible = false };
        _options.AddThemeConstantOverride("separation", 8);
        VolumeControls.AddTo(_options);
        box.AddChild(_options);
        box.AddChild(Item("Volver al título", () => GameLaunch.Instance?.ToTitle()));
        box.AddChild(Item("Salir del juego", () => GetTree().Quit()));
    }

    public void Open()
    {
        if (Visible)
        {
            return;
        }

        Visible = true;
        GameAudio.Instance?.Play(Sfx.Pause);
        Modulate = new Color(1, 1, 1, 0);
        CreateTween().TweenProperty(this, "modulate:a", 1f, 0.2);
        UiMotion.Pop(_panel, 0.85f, 0.35);
        Toggled?.Invoke(true);
    }

    public void Close()
    {
        if (!Visible)
        {
            return;
        }

        GameAudio.Instance?.Play(Sfx.Resume);
        var tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, 0.15);
        tween.TweenCallback(Callable.From(() => Visible = false));
        Toggled?.Invoke(false);
    }

    private static Button Item(string text, Action action)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(0, 40) };
        button.AddThemeFontSizeOverride("font_size", 18);
        button.Pressed += action;
        return button;
    }
}
