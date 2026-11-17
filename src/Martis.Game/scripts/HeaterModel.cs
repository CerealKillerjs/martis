using Godot;

namespace Martis.Game;

/// <summary>
/// A ground heater (E1): a squat radiator whose fins glow with the heat it gives, a warm light on
/// the soil, and a red blink once it breaks.
/// </summary>
public partial class HeaterModel : Node3D
{
    private readonly StandardMaterial3D[] _fins = new StandardMaterial3D[5];
    private StandardMaterial3D _lamp = null!;
    private OmniLight3D _glow = null!;
    private float _clock;
    private float _wear;
    private float _heat;

    /// <summary>Whether the last state shown was broken (used to pulse a breakdown once).</summary>
    public bool ShownBroken { get; private set; }

    public override void _Ready()
    {
        var body = new StandardMaterial3D { AlbedoColor = new Color(0.22f, 0.21f, 0.2f), Metallic = 0.6f, Roughness = 0.45f };
        var trim = new StandardMaterial3D { AlbedoColor = new Color(0.85f, 0.83f, 0.78f), Metallic = 0.7f, Roughness = 0.3f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.75f, BottomRadius = 0.85f, Height = 0.12f }, MaterialOverride = trim, Position = new Vector3(0, 0.06f, 0) });
        AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.1f, 0.5f, 0.6f) }, MaterialOverride = body, Position = new Vector3(0, 0.37f, 0) });
        for (var i = 0; i < _fins.Length; i++)
        {
            _fins[i] = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.1f, 0.08f), EmissionEnabled = true, Emission = new Color(1f, 0.35f, 0.08f), EmissionEnergyMultiplier = 0f, Metallic = 0.4f, Roughness = 0.5f };
            AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.42f, 0.7f) }, MaterialOverride = _fins[i], Position = new Vector3(-0.4f + (i * 0.2f), 0.75f, 0) });
        }

        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.04f, Height = 0.9f }, MaterialOverride = trim, Position = new Vector3(0.5f, 0.95f, 0.25f) });
        _lamp = new StandardMaterial3D { EmissionEnabled = true, Emission = new Color(1f, 0.6f, 0.2f), EmissionEnergyMultiplier = 4f, AlbedoColor = Colors.Black };
        AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f }, MaterialOverride = _lamp, Position = new Vector3(0.5f, 1.43f, 0.25f) });
        _glow = new OmniLight3D { LightColor = new Color(1f, 0.45f, 0.15f), OmniRange = 6f, LightEnergy = 0f, Position = new Vector3(0, 0.9f, 0) };
        AddChild(_glow);
    }

    public void ShowState(double wear, double heat)
    {
        _wear = (float)wear;
        _heat = Mathf.Clamp((float)heat, 0f, 1f);
        ShownBroken = wear >= 1.0;
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        var broken = _wear >= 1f;
        var pulse = 0.85f + (0.15f * Mathf.Sin(_clock * 2.4f));
        var fin = broken ? 0f : (0.6f + (3.4f * _heat)) * pulse;
        foreach (var material in _fins)
        {
            material.EmissionEnergyMultiplier = fin;
        }

        _glow.LightEnergy = broken ? 0f : (0.4f + (2.2f * _heat)) * pulse;
        var on = broken ? Mathf.Sin(_clock * 6f) > 0.3f : true;
        _lamp.Emission = broken ? new Color(1f, 0.25f, 0.2f) : _wear > 0.6f ? new Color(1f, 0.75f, 0.3f) : new Color(0.4f, 1f, 0.7f);
        _lamp.EmissionEnergyMultiplier = on ? 4f : 0.2f;
    }
}
