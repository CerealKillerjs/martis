using System;
using Godot;

namespace Martis.Game;

/// <summary>
/// The seeding rover of the «Expedición» view (J1, ADR-026): a six-wheeled body with a solar
/// deck, a mast with a headlamp and the inoculum tank. It sits on the relief, leans with the
/// slope, spins its wheels with the distance driven and kicks up dust while it moves.
/// </summary>
public partial class RoverModel : Node3D
{
    private const float WheelRadius = 0.26f;
    private readonly Node3D[] _wheels = new Node3D[6];
    private CpuParticles3D _trail = null!;
    private OmniLight3D _status = null!;
    private StandardMaterial3D _statusMaterial = null!;
    private Node3D _body = null!;
    private float _spin;

    public override void _Ready()
    {
        // Built at real size (about 1.5 m) and scaled up so it reads at colony scale.
        _body = new Node3D { Name = "Body", Scale = Vector3.One * 1.7f };
        AddChild(_body);
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.88f, 0.84f), Metallic = 0.5f, Roughness = 0.4f };
        var dark = new StandardMaterial3D { AlbedoColor = new Color(0.12f, 0.12f, 0.13f), Roughness = 0.85f };
        var gold = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.68f, 0.2f), Metallic = 1f, Roughness = 0.3f };
        var solar = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.12f, 0.35f), Metallic = 0.8f, Roughness = 0.15f, EmissionEnabled = true, Emission = new Color(0.04f, 0.08f, 0.25f) };
        var tank = new StandardMaterial3D { AlbedoColor = new Color(0.1f, 0.5f, 0.3f), Metallic = 0.2f, Roughness = 0.2f, EmissionEnabled = true, Emission = new Color(0.1f, 0.9f, 0.5f), EmissionEnergyMultiplier = 1.2f };

        // Body: forward is +X so the rover's heading maps straight onto Rotation.Y.
        _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.5f, 0.32f, 0.95f) }, MaterialOverride = hull, Position = new Vector3(0, 0.62f, 0) });
        _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.7f, 0.04f, 1.25f) }, MaterialOverride = solar, Position = new Vector3(-0.08f, 0.82f, 0) });
        _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.5f, 0.2f, 0.7f) }, MaterialOverride = gold, Position = new Vector3(0.45f, 0.42f, 0) });
        _body.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.16f, BottomRadius = 0.16f, Height = 0.5f }, MaterialOverride = tank, Position = new Vector3(-0.45f, 0.98f, 0.28f), RotationDegrees = new Vector3(90, 0, 0) });

        // Mast with the camera head and headlamp.
        _body.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.05f, Height = 0.75f }, MaterialOverride = hull, Position = new Vector3(0.55f, 1.18f, -0.3f) });
        _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.18f, 0.14f, 0.3f) }, MaterialOverride = dark, Position = new Vector3(0.58f, 1.58f, -0.3f) });
        var lamp = new SpotLight3D { LightColor = new Color(1f, 0.92f, 0.78f), LightEnergy = 3f, SpotRange = 16f, SpotAngle = 32f, Position = new Vector3(0.7f, 1.55f, -0.3f) };
        lamp.RotationDegrees = new Vector3(-14, -90, 0);
        _body.AddChild(lamp);
        _statusMaterial = new StandardMaterial3D { AlbedoColor = Colors.Black, EmissionEnabled = true, Emission = new Color(0.3f, 1f, 0.6f), EmissionEnergyMultiplier = 5f };
        _body.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.05f, Height = 0.1f }, MaterialOverride = _statusMaterial, Position = new Vector3(0.55f, 1.68f, -0.3f) });
        _status = new OmniLight3D { LightColor = new Color(0.3f, 1f, 0.6f), LightEnergy = 0.8f, OmniRange = 2.5f, Position = new Vector3(0.55f, 1.75f, -0.3f) };
        _body.AddChild(_status);

        // Six wheels on rocker arms.
        for (var i = 0; i < 6; i++)
        {
            var side = i < 3 ? -1f : 1f;
            var x = -0.6f + (0.6f * (i % 3));
            var hub = new Node3D { Position = new Vector3(x, WheelRadius, side * 0.62f) };
            hub.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = WheelRadius, BottomRadius = WheelRadius, Height = 0.2f, RadialSegments = 14 }, MaterialOverride = dark, RotationDegrees = new Vector3(90, 0, 0) });
            hub.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.3f, 0.06f, 0.21f) }, MaterialOverride = hull });
            _body.AddChild(hub);
            _body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.08f, 0.4f, 0.06f) }, MaterialOverride = hull, Position = new Vector3(x, 0.45f, side * 0.52f) });
            _wheels[i] = hub;
        }

        _trail = new CpuParticles3D
        {
            Amount = 60,
            Lifetime = 1.4f,
            Emitting = false,
            Mesh = new SphereMesh { Radius = 0.12f, Height = 0.24f, RadialSegments = 6, Rings = 3 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.78f, 0.5f, 0.32f, 0.35f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded },
            Direction = new Vector3(-1, 0.6f, 0),
            Spread = 35f,
            InitialVelocityMin = 0.4f,
            InitialVelocityMax = 1.2f,
            Gravity = new Vector3(0, -0.4f, 0),
            ScaleAmountMin = 0.6f,
            ScaleAmountMax = 1.8f,
            Position = new Vector3(-0.8f, 0.1f, 0),
        };
        _body.AddChild(_trail);
    }

    /// <summary>Places the rover on the ground, leaning with the slope under its wheels.</summary>
    public void Pose(Terrain ground, Vector3 position, float heading, float drivenMetres, bool lowBattery, bool charging)
        => Pose(ground.HeightAt, position, heading, drivenMetres, lowBattery, charging);

    /// <summary>Same, on any ground given by its height function (the real-scale terrain, M2).</summary>
    public void Pose(Func<float, float, float> height, Vector3 position, float heading, float drivenMetres, bool lowBattery, bool charging)
    {
        var forward = new Vector3(MathF.Cos(heading), 0, MathF.Sin(heading));
        var right = new Vector3(-forward.Z, 0, forward.X);
        float H(Vector3 p) => height(p.X, p.Z);
        var front = position + (forward * 1.2f);
        var back = position - (forward * 1.2f);
        var left = position - (right * 1f);
        var rightSide = position + (right * 1f);
        var pitch = MathF.Atan2(H(front) - H(back), 2.4f);
        var roll = MathF.Atan2(H(rightSide) - H(left), 2f);
        position.Y = (H(front) + H(back) + H(left) + H(rightSide)) / 4f;

        Position = position;
        Rotation = Vector3.Zero;
        RotateY(-heading);
        RotateObjectLocal(Vector3.Forward, -pitch);
        RotateObjectLocal(Vector3.Right, -roll);

        _spin += drivenMetres / (WheelRadius * 1.7f);
        foreach (var wheel in _wheels)
        {
            wheel.Rotation = new Vector3(0, 0, -_spin);
        }

        _trail.Emitting = MathF.Abs(drivenMetres) > 1e-4f;
        var colour = charging ? new Color(0.4f, 0.75f, 1f) : lowBattery ? new Color(1f, 0.45f, 0.2f) : new Color(0.3f, 1f, 0.6f);
        _status.LightColor = colour;
        _statusMaterial.Emission = colour;
    }
}
