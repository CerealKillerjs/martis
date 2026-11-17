using System;
using System.Globalization;
using Godot;
using Martis.Audio;

namespace Martis.Game;

/// <summary>Volume sliders, mute and fullscreen switches shared by the title options and the pause menu.</summary>
public static class VolumeControls
{
    public static void AddTo(VBoxContainer box)
    {
        var settings = GameAudio.Instance?.Settings ?? new AudioSettings();
        foreach (var (bus, name) in new[] { (AudioBus.Master, "General"), (AudioBus.Music, "Música"), (AudioBus.Effects, "Efectos"), (AudioBus.Ambience, "Ambiente") })
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = name, CustomMinimumSize = new Vector2(110, 0) });
            var slider = new HSlider { MinValue = 0, MaxValue = 1, Step = 0.01, Value = settings[bus], SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 24) };
            var value = new Label { Text = Percent(settings[bus]), CustomMinimumSize = new Vector2(52, 0), HorizontalAlignment = HorizontalAlignment.Right };
            slider.ValueChanged += v =>
            {
                value.Text = Percent((float)v);
                Change(s => s[bus] = (float)v);
            };
            row.AddChild(slider);
            row.AddChild(value);
            box.AddChild(row);
        }

        var mute = new CheckButton { Text = "Silenciar todo", ButtonPressed = settings.Muted };
        mute.Toggled += on => Change(s => s.Muted = on);
        box.AddChild(mute);
        var fullscreen = new CheckButton { Text = "Pantalla completa", ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen };
        fullscreen.Toggled += on => DisplayServer.WindowSetMode(on ? DisplayServer.WindowMode.Fullscreen : DisplayServer.WindowMode.Windowed);
        box.AddChild(fullscreen);
    }

    private static void Change(Action<AudioSettings> edit)
    {
        var audio = GameAudio.Instance;
        if (audio is null)
        {
            return;
        }

        var copy = audio.Settings with { };
        edit(copy);
        audio.Apply(copy);
        audio.SaveSettings();
    }

    private static string Percent(float value) => string.Create(CultureInfo.InvariantCulture, $"{value * 100:0} %");
}
