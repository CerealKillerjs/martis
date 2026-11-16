using System;
using System.Linq;
using Godot;

namespace Martis.Game;

/// <summary>Interface animation for the HUD (entrance, toasts, banner). Kept apart from the layout code.</summary>
public partial class Hud
{
    private Vector2 _toastHome;
    private Tween? _bannerGlow;

    /// <summary>Panels fly in from the edge they are anchored to, one after another.</summary>
    public void Entrance()
    {
        var panels = GetChildren().OfType<PanelContainer>().Where(panel => panel.Visible).ToArray();
        for (var i = 0; i < panels.Length; i++)
        {
            var panel = panels[i];
            var offset = panel.AnchorTop >= 0.99f ? new Vector2(0, 60)
                : panel.AnchorLeft >= 0.99f ? new Vector2(80, 0)
                : panel.AnchorRight <= 0.01f ? new Vector2(-80, 0)
                : new Vector2(0, -50);
            UiMotion.SlideIn(panel, offset, 0.15 + (i * 0.12));
        }
    }

    private void AnimateToast(bool bad)
    {
        if (_toastHome == Vector2.Zero)
        {
            _toastHome = _toast.Position;
        }

        _toast.Position = _toastHome;
        if (bad)
        {
            UiMotion.Shake(_toast);
        }
        else
        {
            UiMotion.Drop(_toast);
        }
    }

    private void AnimateBanner()
    {
        UiMotion.Pop(_banner, 0.6f, 0.6);
        var title = _banner.GetNode<Label>("Box/Title");
        _bannerGlow?.Kill();
        _bannerGlow = title.CreateTween().SetLoops();
        _bannerGlow.TweenProperty(title, "modulate", new Color(1.5f, 1.5f, 1.5f), 0.8).SetTrans(Tween.TransitionType.Sine);
        _bannerGlow.TweenProperty(title, "modulate", Colors.White, 0.8).SetTrans(Tween.TransitionType.Sine);
        _banner.VisibilityChanged += StopGlow;
    }

    private void StopGlow()
    {
        if (!_banner.Visible)
        {
            _bannerGlow?.Kill();
            _banner.VisibilityChanged -= StopGlow;
        }
    }
}
