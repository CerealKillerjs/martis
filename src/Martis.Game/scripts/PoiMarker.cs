using Godot;
using Martis.Simulation.Expedition;

namespace Martis.Game;

/// <summary>
/// A point of interest of the valley (V5): a faint pulsing beam marked «?» when an estimate shows
/// an anomaly, a coloured beam with its name once the rover or a station has measured it.
/// </summary>
public partial class PoiMarker : Node3D
{
    private StandardMaterial3D _beam = null!;
    private MeshInstance3D _column = null!;
    private Label3D _label = null!;
    private Color _colour;
    private PoiState _state;
    private float _clock;

    public Poi? Poi { get; set; }

    public override void _Ready()
    {
        _beam = new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            EmissionEnabled = true,
        };
        _column = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.15f, BottomRadius = 0.6f, Height = 18f }, MaterialOverride = _beam, Position = new Vector3(0, 9f, 0), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_column);
        _label = new Label3D
        {
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FixedSize = true,
            FontSize = 40,
            PixelSize = 0.00035f,
            OutlineSize = 12,
            Position = new Vector3(0, 19.5f, 0),
            NoDepthTest = true,
        };
        AddChild(_label);
        Visible = false;
    }

    public void Show(Poi poi)
    {
        Poi = poi;
        _state = poi.State;
        _colour = poi.Definition.Kind switch
        {
            PoiKind.Ice => new Color(0.55f, 0.85f, 1f),
            PoiKind.Brine => new Color(0.35f, 0.6f, 1f),
            PoiKind.Shelter => new Color(0.75f, 0.6f, 1f),
            PoiKind.Meteorite => new Color(1f, 0.75f, 0.35f),
            _ => new Color(0.45f, 1f, 0.6f),
        };
        Visible = poi.State != PoiState.Hidden;
        _label.Text = poi.State == PoiState.Discovered ? poi.Definition.Name : "? anomalía";
        _label.Modulate = poi.State == PoiState.Discovered ? _colour : new Color(0.75f, 0.9f, 1f, 0.85f);
        _column.Scale = poi.State == PoiState.Discovered ? Vector3.One : new Vector3(0.5f, 1f, 0.5f);
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        var pulse = 0.5f + (0.5f * Mathf.Sin(_clock * (_state == PoiState.Discovered ? 1.2f : 3f)));
        var colour = _state == PoiState.Discovered ? _colour : new Color(0.6f, 0.85f, 1f);
        var alpha = _state == PoiState.Discovered ? 0.45f + (0.15f * pulse) : 0.18f + (0.17f * pulse);
        _beam.AlbedoColor = new Color(colour, alpha);
        _beam.Emission = colour * 0.8f;
    }
}
