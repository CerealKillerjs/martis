using System;
using System.Collections.Generic;
using Godot;

namespace Martis.Game;

/// <summary>
/// «Cargar partida» list shared by the title screen and the Esc menu: one button per saved
/// game (name, last sol, date), newest first, and «Volver».
/// </summary>
public partial class SaveList : VBoxContainer
{
    private const int MaxShown = 8;
    private readonly List<Button> _buttons = [];
    private VBoxContainer _rows = null!;
    private Button _back = null!;
    private Label _empty = null!;

    public event Action<string>? Chosen;

    public event Action? Closed;

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 8);
        var title = new Label { Text = "CARGAR PARTIDA" };
        title.AddThemeFontSizeOverride("font_size", 22);
        title.AddThemeColorOverride("font_color", Hud.Accent);
        AddChild(title);
        _rows = new VBoxContainer();
        _rows.AddThemeConstantOverride("separation", 4);
        AddChild(_rows);
        _empty = new Label { Text = "Todavía no hay partidas guardadas.", Visible = false };
        _empty.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        AddChild(_empty);
        _back = new Button { Text = "Volver", CustomMinimumSize = new Vector2(0, 36) };
        _back.Pressed += () => Closed?.Invoke();
        AddChild(_back);
    }

    /// <summary>Fills the list; returns how many saves it shows.</summary>
    public int Fill(IReadOnlyList<SaveSummary> saves)
    {
        foreach (var child in _rows.GetChildren())
        {
            child.QueueFree();
        }

        _buttons.Clear();

        var shown = Math.Min(saves.Count, MaxShown);
        for (var i = 0; i < shown; i++)
        {
            var save = saves[i];
            var button = new Button { Text = save.Label, Alignment = HorizontalAlignment.Left, CustomMinimumSize = new Vector2(0, 38) };
            button.AddThemeFontSizeOverride("font_size", 16);
            button.Pressed += () => Chosen?.Invoke(save.Id);
            _rows.AddChild(button);
            _buttons.Add(button);
        }

        _empty.Visible = shown == 0;
        return shown;
    }

    /// <summary>Focuses the first save, or «Volver» when there is none.</summary>
    public void FocusFirst()
    {
        (_buttons.Count > 0 ? _buttons[0] : _back).GrabFocus();
    }
}
