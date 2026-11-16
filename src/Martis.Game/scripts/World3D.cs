using System;
using System.Collections.Generic;
using Godot;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>Map overlays (TDD B.6 capas de datos).</summary>
public enum Overlay
{
    None = 0,
    Temperature = 1,
    Water = 2,
    Radiation = 3,
    Biomass = 4,
}

/// <summary>
/// The 3D scene (ADR-017): sky, sun with a day/night cycle, terrain with data overlays,
/// glowing colony domes, the lander, dust and event pulses. Reads only <see cref="WorldView"/>.
/// </summary>
public partial class World3D : Node3D
{
    private const int MaxDomesPerCell = 3;
    private readonly List<(MeshInstance3D Mesh, ShaderMaterial Material, float Age)> _pulses = [];
    private ShaderMaterial _terrainMaterial = null!;
    private ShaderMaterial _colonyMaterial = null!;
    private ShaderMaterial _skyMaterial = null!;
    private ShaderMaterial _pulseShaderTemplate = null!;
    private Image _cellImage = null!;
    private ImageTexture _cellTexture = null!;
    private Image _glowImage = null!;
    private ImageTexture _glowTexture = null!;
    private MultiMeshInstance3D _domes = null!;
    private DirectionalLight3D _sun = null!;
    private DirectionalLight3D _moon = null!;
    private Godot.Environment _environment = null!;
    private OmniLight3D _beacon = null!;
    private CpuParticles3D _dust = null!;
    private Node3D _lander = null!;
    private float _clock;

    public Terrain Ground { get; private set; } = null!;

    public OrbitCamera Camera { get; private set; } = null!;

    /// <summary>Fraction of the current sol, 0 = midnight, 0.5 = noon.</summary>
    public float TimeOfDay { get; set; } = 0.38f;

    public Overlay Overlay { get; private set; } = Overlay.None;

    public bool HasLander => _lander.Visible;

    public void Build(int width, int height, ulong seed)
    {
        Ground = new Terrain(width, height, seed);
        BuildEnvironment();
        BuildTerrain(seed);
        BuildDomes(width * height);
        BuildLander();
        BuildDust();
        _pulseShaderTemplate = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/pulse.gdshader") };

        // Bigger maps (H3) open further out and let the camera pull back to see all of it.
        var scale = MathF.Sqrt(Ground.CellsX / 64f);
        Camera = new OrbitCamera { Ground = Ground, Fov = 50f, Far = 2000f + Ground.SizeX, Near = 0.2f, MaxDistance = MathF.Max(260f, Ground.SizeX * 1.1f) };
        AddChild(Camera);
        Camera.Frame(new Vector3(0, Ground.HeightAt(0, 0), 0), 110f * scale, 0.75f, 0.68f, snap: true);
        Camera.MakeCurrent();
    }

    public void SetOverlay(Overlay overlay)
    {
        Overlay = overlay;
        _terrainMaterial.SetShaderParameter("overlay_mode", (int)overlay);
    }

    public void Highlight(int x, int y)
        => _terrainMaterial.SetShaderParameter("highlight_cell", x < 0 ? new Vector2(-1, -1) : new Vector2(x, y));

    public void PlaceLander(int x, int y)
    {
        var p = Ground.CellCenter(x, y) + new Vector3(Terrain.CellSize * 2.5f, 0, -Terrain.CellSize * 1.5f);
        p.Y = Ground.HeightAt(p.X, p.Z);
        _lander.Position = p;
        _lander.Visible = true;
    }

    /// <summary>Copies the simulation view into the data texture and the dome MultiMesh.</summary>
    public void Show(WorldView view)
    {
        var cells = view.Width * view.Height;
        var water = Range(view.WaterKg);
        var multimesh = _domes.Multimesh;
        var visible = 0;
        for (var i = 0; i < cells; i++)
        {
            var photo = LogScale(view.PhototrophBiomassKg[i]);
            var decomp = LogScale(view.DecomposerBiomassKg[i]);
            var overlay = Overlay switch
            {
                Overlay.Temperature => Mathf.InverseLerp(185f, 255f, view.TemperatureKelvin[i]),
                Overlay.Water => water.Max > water.Min ? Mathf.InverseLerp(water.Min, water.Max, view.WaterKg[i]) : 0.5f,
                Overlay.Radiation => Mathf.InverseLerp(0f, 0.0015f, view.RadiationSvPerSol[i]),
                Overlay.Biomass => LogScale(view.BiomassKg[i]),
                _ => 0f,
            };
            _cellImage.SetPixel(i % view.Width, i / view.Width, new Color(photo, decomp, view.VariantHue[i], Math.Clamp(overlay, 0f, 1f)));

            if (view.BiomassKg[i] <= 1e-7f)
            {
                continue;
            }

            var total = view.BiomassKg[i];
            var share = view.DecomposerBiomassKg[i] / total;
            var vigour = LogScale(total);
            var green = Color.FromHsv(0.27f + (view.VariantHue[i] * 0.2f), 0.85f, 1f);
            var violet = new Color(0.72f, 0.3f, 1f);
            var colour = green.Lerp(violet, share);
            colour.A = vigour;
            var domes = Math.Clamp(view.VariantCount[i], 1, MaxDomesPerCell);
            var centre = Ground.CellCenter(i % view.Width, i / view.Width);
            for (var d = 0; d < domes && visible < multimesh.InstanceCount; d++)
            {
                var angle = (i * 2.399f) + (d * 2.094f);
                var offset = d == 0 ? Vector3.Zero : new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * 0.55f;
                var position = centre + offset;
                position.Y = Ground.HeightAt(position.X, position.Z) - 0.05f;
                var size = (0.3f + (0.85f * vigour)) * (d == 0 ? 1f : 0.6f);
                var basis = Basis.Identity.Scaled(new Vector3(size, size * 0.75f, size));
                multimesh.SetInstanceTransform(visible, new Transform3D(basis, position));
                multimesh.SetInstanceColor(visible, colour);
                visible++;
            }
        }

        multimesh.VisibleInstanceCount = visible;
        _cellTexture.Update(_cellImage);
        var photoGlow = Blur(view.PhototrophBiomassKg, view.Width, view.Height);
        var decompGlow = Blur(view.DecomposerBiomassKg, view.Width, view.Height);
        for (var i = 0; i < cells; i++)
        {
            _glowImage.SetPixel(i % view.Width, i / view.Width, new Color(photoGlow[i], decompGlow[i], 0, 1));
        }

        _glowTexture.Update(_glowImage);
    }

    /// <summary>An expanding ring at a cell: green for deploys, gold for mutations, red for losses.</summary>
    public void Pulse(int cell, int width, Color tint)
    {
        if (cell < 0)
        {
            return;
        }

        var material = (ShaderMaterial)_pulseShaderTemplate.Duplicate();
        material.SetShaderParameter("tint", tint);
        var mesh = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(9, 9) },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        var position = Ground.CellCenter(cell % width, cell / width);
        position.Y += 0.25f;
        mesh.Position = position;
        AddChild(mesh);
        _pulses.Add((mesh, material, 0f));
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        _clock += dt;
        UpdateSun();
        for (var i = _pulses.Count - 1; i >= 0; i--)
        {
            var (mesh, material, age) = _pulses[i];
            age += dt / 2.2f;
            if (age >= 1f)
            {
                mesh.QueueFree();
                _pulses.RemoveAt(i);
                continue;
            }

            material.SetShaderParameter("age", age);
            _pulses[i] = (mesh, material, age);
        }

        if (_lander.Visible)
        {
            _beacon.LightEnergy = (MathF.Sin(_clock * 4f) > 0.6f) ? 3f : 0.2f;
            var panels = _lander.GetNode<Node3D>("Panels");
            panels.Rotation = new Vector3(0, 0, Mathf.DegToRad(-20f + (40f * TimeOfDay)));
        }

        if (Camera is not null)
        {
            var target = Camera.GlobalPosition + (-Camera.GlobalBasis.Z * 30f);
            _dust.GlobalPosition = new Vector3(target.X, Ground.HeightAt(target.X, target.Z) + 6f, target.Z);
        }
    }

    private void UpdateSun()
    {
        // Sun path: rises in the east (+X), noon high in the north-tilted sky.
        var angle = (TimeOfDay - 0.25f) * MathF.Tau; // 0 at sunrise
        var elevation = MathF.Sin(angle);
        var direction = new Vector3(-MathF.Cos(angle), -elevation, -0.35f).Normalized();
        _sun.LookAtFromPosition(Vector3.Zero, direction, Vector3.Up);
        var day = Mathf.SmoothStep(-0.12f, 0.25f, elevation);
        var night = 1f - day;
        var warm = Mathf.SmoothStep(0.0f, 0.45f, elevation);
        _sun.LightEnergy = 1.25f * day;
        _sun.LightColor = new Color(1f, 0.62f, 0.42f).Lerp(new Color(1f, 0.92f, 0.82f), warm);
        _sun.Visible = day > 0.01f;
        _moon.LightEnergy = 0.18f * night;
        _environment.AmbientLightEnergy = 0.18f + (0.32f * day);
        _environment.AmbientLightColor = new Color(0.25f, 0.3f, 0.5f).Lerp(new Color(0.85f, 0.62f, 0.48f), day);
        _environment.FogLightColor = new Color(0.08f, 0.07f, 0.1f).Lerp(new Color(0.82f, 0.56f, 0.38f), day);
        _skyMaterial.SetShaderParameter("night", night);
        _terrainMaterial.SetShaderParameter("night", night);
        _colonyMaterial.SetShaderParameter("night", night);
    }

    private void BuildEnvironment()
    {
        _skyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/sky.gdshader") };
        _environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _skyMaterial, RadianceSize = Sky.RadianceSizeEnum.Size256 },
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.85f, 0.62f, 0.48f),
            AmbientLightEnergy = 0.8f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled,
            TonemapMode = Godot.Environment.ToneMapper.Aces,
            TonemapExposure = 0.95f,
            TonemapWhite = 6f,
            GlowEnabled = true,
            GlowIntensity = 0.7f,
            GlowStrength = 1.1f,
            GlowBloom = 0f,
            GlowHdrThreshold = 1.0f,
            GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Screen,
            FogEnabled = true,
            FogLightColor = new Color(0.82f, 0.56f, 0.38f),
            FogDensity = 0.0012f,
            FogSkyAffect = 0.25f,
            FogAerialPerspective = 0.15f,
            AdjustmentEnabled = true,
            AdjustmentSaturation = 1.12f,
            AdjustmentContrast = 1.06f,
        };
        _environment.SetGlowLevel(1, 1f);
        _environment.SetGlowLevel(3, 0.8f);
        _environment.SetGlowLevel(5, 0.6f);
        AddChild(new WorldEnvironment { Environment = _environment });

        _sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            DirectionalShadowMaxDistance = 320f,
            LightAngularDistance = 0.4f,
        };
        AddChild(_sun);
        _moon = new DirectionalLight3D { LightColor = new Color(0.5f, 0.6f, 1f), LightEnergy = 0f, ShadowEnabled = false };
        _moon.RotationDegrees = new Vector3(-55, 140, 0);
        AddChild(_moon);
    }

    private void BuildTerrain(ulong seed)
    {
        _cellImage = Image.CreateEmpty(Ground.CellsX, Ground.CellsY, false, Image.Format.Rgbaf);
        _cellTexture = ImageTexture.CreateFromImage(_cellImage);
        _glowImage = Image.CreateEmpty(Ground.CellsX, Ground.CellsY, false, Image.Format.Rgbaf);
        _glowTexture = ImageTexture.CreateFromImage(_glowImage);
        _terrainMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
        _terrainMaterial.SetShaderParameter("cell_data", _cellTexture);
        _terrainMaterial.SetShaderParameter("glow_data", _glowTexture);
        _terrainMaterial.SetShaderParameter("world_size", Ground.SizeX);
        _terrainMaterial.SetShaderParameter("cells", (float)Ground.CellsX);
        _terrainMaterial.SetShaderParameter("highlight_cell", new Vector2(-1, -1));
        var terrain = new Node3D { Name = "Terrain" };
        AddChild(terrain);
        foreach (var chunk in Ground.BuildChunks())
        {
            terrain.AddChild(new MeshInstance3D { Mesh = chunk, MaterialOverride = _terrainMaterial });
        }

        var skirtMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.55f, 0.28f, 0.16f),
            Roughness = 1f,
        };
        AddChild(new MeshInstance3D { Mesh = Ground.BuildSkirt(MathF.Max(700f, Ground.SizeX * 1.4f), 240, seed), MaterialOverride = skirtMaterial, Name = "Skirt", Position = new Vector3(0, -0.02f, 0) });
    }

    private void BuildDomes(int cells)
    {
        _colonyMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/colony.gdshader") };
        var mesh = new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 20, Rings = 10, IsHemisphere = true };
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = true,
            Mesh = mesh,
            InstanceCount = cells * MaxDomesPerCell,
            VisibleInstanceCount = 0,
        };
        _domes = new MultiMeshInstance3D { Multimesh = multimesh, MaterialOverride = _colonyMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Name = "Colonies" };
        AddChild(_domes);
    }

    private void BuildLander()
    {
        _lander = new Node3D { Name = "Lander", Visible = false };
        var hull = new StandardMaterial3D { AlbedoColor = new Color(0.86f, 0.86f, 0.84f), Metallic = 0.6f, Roughness = 0.35f };
        var gold = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.68f, 0.2f), Metallic = 1f, Roughness = 0.3f };
        var cellsMaterial = new StandardMaterial3D { AlbedoColor = new Color(0.08f, 0.12f, 0.35f), Metallic = 0.8f, Roughness = 0.15f, EmissionEnabled = true, Emission = new Color(0.05f, 0.1f, 0.3f) };
        _lander.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.1f, BottomRadius = 1.4f, Height = 1.3f }, MaterialOverride = hull, Position = new Vector3(0, 1.3f, 0) });
        _lander.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1.05f, Height = 1.4f, IsHemisphere = true }, MaterialOverride = gold, Position = new Vector3(0, 1.95f, 0) });
        for (var i = 0; i < 4; i++)
        {
            var a = (i * MathF.Tau / 4f) + 0.785f;
            var leg = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.07f, BottomRadius = 0.07f, Height = 1.6f }, MaterialOverride = hull };
            leg.Position = new Vector3(MathF.Cos(a) * 1.3f, 0.6f, MathF.Sin(a) * 1.3f);
            leg.Rotation = new Vector3(MathF.Sin(a) * 0.45f, 0, -MathF.Cos(a) * 0.45f);
            _lander.AddChild(leg);
            _lander.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.25f, BottomRadius = 0.3f, Height = 0.08f }, MaterialOverride = hull, Position = new Vector3(MathF.Cos(a) * 1.65f, 0.04f, MathF.Sin(a) * 1.65f) });
        }

        var panels = new Node3D { Name = "Panels", Position = new Vector3(0, 1.5f, 0) };
        panels.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.2f, 0.05f, 1.2f) }, MaterialOverride = cellsMaterial, Position = new Vector3(2.7f, 0, 0) });
        panels.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.2f, 0.05f, 1.2f) }, MaterialOverride = cellsMaterial, Position = new Vector3(-2.7f, 0, 0) });
        _lander.AddChild(panels);
        _lander.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0.02f, BottomRadius = 0.03f, Height = 2.2f }, MaterialOverride = hull, Position = new Vector3(0.4f, 3.3f, 0) });
        var dish = new MeshInstance3D { Mesh = new SphereMesh { Radius = 0.5f, Height = 0.4f, IsHemisphere = true }, MaterialOverride = hull, Position = new Vector3(-0.5f, 2.7f, 0.2f) };
        dish.RotationDegrees = new Vector3(150, 30, 0);
        _lander.AddChild(dish);
        _beacon = new OmniLight3D { LightColor = new Color(1f, 0.25f, 0.15f), OmniRange = 6f, Position = new Vector3(0.4f, 4.45f, 0) };
        _lander.AddChild(_beacon);
        _lander.AddChild(new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f },
            MaterialOverride = new StandardMaterial3D { EmissionEnabled = true, Emission = new Color(1f, 0.3f, 0.2f), EmissionEnergyMultiplier = 6f, AlbedoColor = Colors.Black },
            Position = new Vector3(0.4f, 4.42f, 0),
        });
        var floodlight = new SpotLight3D { LightColor = new Color(1f, 0.85f, 0.65f), LightEnergy = 2.5f, SpotRange = 22f, SpotAngle = 50f, Position = new Vector3(0, 3.2f, 0) };
        floodlight.RotationDegrees = new Vector3(-60, 200, 0);
        _lander.AddChild(floodlight);
        AddChild(_lander);
    }

    private void BuildDust()
    {
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/dust.gdshader") };
        _dust = new CpuParticles3D
        {
            Amount = 900,
            Lifetime = 9f,
            Preprocess = 9f,
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Box,
            EmissionBoxExtents = new Vector3(70, 7, 70),
            Direction = new Vector3(1, 0.05f, 0.3f),
            Spread = 12f,
            Gravity = new Vector3(0, -0.05f, 0),
            InitialVelocityMin = 2f,
            InitialVelocityMax = 6f,
            ScaleAmountMin = 0.08f,
            ScaleAmountMax = 0.5f,
            Mesh = new QuadMesh { Size = new Vector2(1, 1), Material = material },
            LocalCoords = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_dust);
    }

    /// <summary>Log biomass spread over a few cells: the visual halo of a mat (not simulated dispersal).</summary>
    private static float[] Blur(float[] kg, int width, int height)
    {
        const int radius = 3;
        var source = new float[kg.Length];
        for (var i = 0; i < kg.Length; i++)
        {
            source[i] = LogScale(kg[i]);
        }

        var weights = new float[(2 * radius) + 1];
        for (var k = -radius; k <= radius; k++)
        {
            weights[k + radius] = MathF.Exp(-(k * k) / 4.5f);
        }

        var horizontal = new float[kg.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var xx = x + k;
                    if (xx >= 0 && xx < width)
                    {
                        value = MathF.Max(value, source[(y * width) + xx] * weights[k + radius]);
                    }
                }

                horizontal[(y * width) + x] = value;
            }
        }

        var result = new float[kg.Length];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var value = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var yy = y + k;
                    if (yy >= 0 && yy < height)
                    {
                        value = MathF.Max(value, horizontal[(yy * width) + x] * weights[k + radius]);
                    }
                }

                result[(y * width) + x] = value;
            }
        }

        return result;
    }

    private static float LogScale(float kg)
        => kg <= 1e-7f ? 0f : Math.Clamp((MathF.Log10(kg) + 6f) / 6.5f, 0.05f, 1f);

    private static (float Min, float Max) Range(float[] values)
    {
        var min = float.MaxValue;
        var max = float.MinValue;
        foreach (var value in values)
        {
            min = MathF.Min(min, value);
            max = MathF.Max(max, value);
        }

        return (min, max);
    }
}
