using Godot;

namespace Martis.Game;

/// <summary>Tween helpers for interface motion (entrances, pops, nudges).</summary>
public static class UiMotion
{
    /// <summary>Slides a control in from <paramref name="offset"/> while fading it in.</summary>
    public static void SlideIn(Control control, Vector2 offset, double delay, double seconds = 0.7)
    {
        var target = control.Position;
        control.Position = target + offset;
        control.Modulate = new Color(control.Modulate, 0f);
        var tween = control.CreateTween().SetParallel().SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "position", target, seconds).SetDelay(delay);
        tween.TweenProperty(control, "modulate:a", 1f, seconds * 0.8).SetDelay(delay);
    }

    /// <summary>Scales a control up from <paramref name="from"/> with a small overshoot.</summary>
    public static void Pop(Control control, float from = 0.7f, double seconds = 0.45)
    {
        control.PivotOffset = control.Size / 2f;
        control.Scale = new Vector2(from, from);
        control.Modulate = new Color(control.Modulate, 0f);
        var tween = control.CreateTween().SetParallel();
        tween.TweenProperty(control, "scale", Vector2.One, seconds).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        tween.TweenProperty(control, "modulate:a", 1f, seconds * 0.6);
    }

    /// <summary>A short vertical drop-in, used for toasts.</summary>
    public static void Drop(Control control, float pixels = 14f, double seconds = 0.35)
    {
        var target = control.Position;
        control.Position = target - new Vector2(0, pixels);
        control.CreateTween().TweenProperty(control, "position", target, seconds).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }

    /// <summary>Horizontal shake for errors.</summary>
    public static void Shake(Control control, float pixels = 8f)
    {
        var origin = control.Position;
        var tween = control.CreateTween();
        foreach (var dx in new[] { pixels, -pixels, pixels * 0.6f, -pixels * 0.6f, 0f })
        {
            tween.TweenProperty(control, "position:x", origin.X + dx, 0.05);
        }
    }
}
