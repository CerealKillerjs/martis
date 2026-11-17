using System;
using Godot;
using Martis.Application;

namespace Martis.Game;

/// <summary>The four modes of presence as tabs under the top bar (V1, Documento Maestro v2.0 §3.1).</summary>
public partial class ModeBar : PanelContainer
{
    private static readonly (GameMode Mode, string Label, string Key, string Tip)[] Modes =
    [
        (GameMode.Exploration, "EXPLORACIÓN", "F1", "Conduce el rover en tercera persona (Tab)"),
        (GameMode.Strategic, "ESTRATÉGICO", "F2", "Capas del mapa, inspector y órdenes"),
        (GameMode.Laboratory, "LABORATORIO", "F3", "Diseños, experimentos y análisis"),
        (GameMode.Planetary, "PLANETARIO", "F4", "El globo y los balances globales"),
    ];

    private readonly Button[] _buttons = new Button[Modes.Length];

    public event Action<GameMode>? ModeChosen;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.CenterTop);
        OffsetLeft = -300;
        OffsetRight = 300;
        OffsetTop = 64;
        OffsetBottom = 96;
        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.05f, 0.07f, 0.72f) };
        style.SetCornerRadiusAll(16);
        style.SetContentMarginAll(3);
        AddThemeStyleboxOverride("panel", style);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        row.AddThemeConstantOverride("separation", 2);
        AddChild(row);
        for (var i = 0; i < Modes.Length; i++)
        {
            var (mode, label, key, tip) = Modes[i];
            var button = new Button
            {
                Text = $"{label}  {key}",
                ToggleMode = true,
                FocusMode = FocusModeEnum.None,
                TooltipText = tip,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
            };
            button.AddThemeFontSizeOverride("font_size", 12);
            button.Pressed += () => ModeChosen?.Invoke(mode);
            row.AddChild(button);
            _buttons[i] = button;
        }
    }

    public void Show(GameMode mode)
    {
        for (var i = 0; i < Modes.Length; i++)
        {
            _buttons[i].SetPressedNoSignal(Modes[i].Mode == mode);
        }
    }
}
