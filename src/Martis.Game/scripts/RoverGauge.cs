using Godot;
using Martis.Simulation.Expedition;

namespace Martis.Game;

/// <summary>Bottom-centre readout of the rover (J1, ADR-026): battery, state and the keys that drive it.</summary>
public partial class RoverGauge : PanelContainer
{
    private static readonly Color Bio = new(0.45f, 1f, 0.7f);
    private Label _title = null!;
    private ProgressBar _battery = null!;
    private Label _state = null!;
    private Label _keys = null!;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        // Centred above the deploy hint: the log owns the bottom-left corner.
        SetAnchorsPreset(LayoutPreset.CenterBottom);
        OffsetLeft = -150;
        OffsetTop = -166;
        OffsetRight = 150;
        OffsetBottom = -54;
        GrowVertical = GrowDirection.Begin;
        var style = new StyleBoxFlat { BgColor = new Color(0.04f, 0.05f, 0.07f, 0.72f), BorderColor = new Color(Bio, 0.35f) };
        style.SetBorderWidthAll(1);
        style.SetCornerRadiusAll(8);
        style.SetContentMarginAll(10);
        AddThemeStyleboxOverride("panel", style);

        var column = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore };
        column.AddThemeConstantOverride("separation", 4);
        AddChild(column);
        _title = new Label { Text = "ROVER CIENTÍFICO" };
        _title.AddThemeFontSizeOverride("font_size", 12);
        _title.AddThemeColorOverride("font_color", Bio);
        column.AddChild(_title);
        _battery = new ProgressBar { ShowPercentage = false, MaxValue = 1, CustomMinimumSize = new Vector2(250, 6) };
        column.AddChild(_battery);
        _state = new Label();
        _state.AddThemeFontSizeOverride("font_size", 13);
        column.AddChild(_state);
        _keys = new Label();
        _keys.AddThemeFontSizeOverride("font_size", 11);
        _keys.AddThemeColorOverride("font_color", new Color(1, 1, 1, 0.6f));
        column.AddChild(_keys);
    }

    public void Show(Rover rover, bool driving, bool landed, KnowledgeMap? knowledge = null, SampleStore? samples = null)
    {
        _battery.Value = rover.Battery;
        var percent = (int)System.Math.Round(rover.Battery * 100);
        _state.Text = !landed
            ? "Batería sin límite hasta el aterrizaje"
            : rover.Charging
                ? $"Cargando junto al módulo · {percent} %"
                : rover.Battery <= 0
                    ? "Batería agotada: avance lento · vuelve al módulo"
                    : $"Batería {percent} %";
        _state.AddThemeColorOverride("font_color", landed && rover.Battery <= 0 ? new Color(1f, 0.55f, 0.4f) : new Color(1, 1, 1, 0.85f));
        var mapped = knowledge is null
            ? string.Empty
            : System.FormattableString.Invariant($"Mapa {knowledge.Coverage(KnowledgeLevel.Estimated) * 100:0} % estimado · {knowledge.Coverage(KnowledgeLevel.Measured) * 100:0.#} % medido");
        if (samples is not null)
        {
            mapped += System.FormattableString.Invariant($" · muestras {samples.Carried}/{samples.Definition.Capacity}");
        }

        mapped += "\n";
        _keys.Text = mapped + (driving
            ? "WASD conducir · C escanear · M muestrear · F sembrar · Tab estratégico"
            : "Tab o F1 conducir el rover · siembra dentro del anillo");
    }
}
