using Godot;

namespace Martis.Game;

/// <summary>A fixed sensor station (V4): a mast with a small solar panel and a light that tells its state.</summary>
public partial class StationModel : Node3D
{
    private static readonly Color Working = new(0.35f, 1f, 0.75f);
    private static readonly Color Worn = new(1f, 0.75f, 0.3f);
    private static readonly Color Dead = new(1f, 0.25f, 0.2f);
    private StandardMaterial3D _lamp = null!;
    private OmniLight3D _light = null!;
    private float _clock;
    private float _wear;

    public override void _Ready()
    {
        var metal = new StandardMaterial3D { AlbedoColor = new Color(0.8f, 0.8f, 0.78f), Metallic = 0.7f, Roughness = 0.35f };
        var cells = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.12f, 0.35f), Metallic = 0.8f, Roughness = 0.15f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.5f, BottomRadius = 0.6f, Height = 0.15f }, MaterialOverride = metal, Position = new Vector3(0, 0.07f, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.05f, BottomRadius = 0.07f, Height = 2.4f }, MaterialOverride = metal, Position = new Vector3(0, 1.2f, 0) });
        var panel = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.1f, 0.04f, 0.7f) }, MaterialOverride = cells, Position = new Vector3(0, 1.3f, 0) };
        panel.RotationDegrees = new Vector3(25, 0, 0);
        AddChild(panel);
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.35f, 0.25f, 0.35f) }, MaterialOverride = metal, Position = new Vector3(0, 0.5f, 0) });
        for (var i = 0; i < 3; i++)
        {
            var cup = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.09f, Height = 0.18f }, MaterialOverride = metal };
            var a = i * Mathf.Tau / 3f;
            cup.Position = new Vector3(Mathf.Cos(a) * 0.3f, 2.35f, Mathf.Sin(a) * 0.3f);
            AddChild(cup);
        }

        _lamp = new StandardMaterial3D { EmissionEnabled = true, Emission = Working, EmissionEnergyMultiplier = 5f, AlbedoColor = Colors.Black };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.1f, Height = 0.2f }, MaterialOverride = _lamp, Position = new Vector3(0, 2.5f, 0) });
        _light = new OmniLight3D { LightColor = Working, OmniRange = 5f, LightEnergy = 1.2f, Position = new Vector3(0, 2.55f, 0) };
        AddChild(_light);
    }

    public void ShowWear(double wear) => _wear = (float)wear;

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        var colour = _wear >= 1f ? Dead : _wear > 0.6f ? Worn : Working;
        var on = _wear >= 1f ? Mathf.Sin(_clock * 6f) > 0.3f : Mathf.Sin(_clock * 1.6f) > -0.6f;
        _lamp.Emission = colour;
        _lamp.EmissionEnergyMultiplier = on ? 5f : 0.3f;
        _light.LightColor = colour;
        _light.LightEnergy = on ? 1.2f : 0.1f;
    }
}
