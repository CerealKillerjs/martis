using System;
using System.Globalization;
using System.Linq;
using Godot;
using Martis.Simulation.Sessions;

namespace Martis.Game;

/// <summary>
/// M2 (Documento Maestro v2.0, ADR-035): Jezero at real scale. In exploration the scaled map is
/// swapped for <see cref="RealTerrain"/>, one unit per metre, streamed around the rover; the
/// rover drives at the region's real speed (20 m/s) and a move order (G) travels 30× faster. The
/// rover keeps living in cell units, so knowledge, samples, stations and saves are unchanged:
/// only the view and the speed scale change. <c>--real-shot=x.png [--at=x,y] [--look=deg] [--section=id]</c>
/// renders the real view from a point of the region (metres from its north-west corner).
/// </summary>
public partial class GameRoot
{
    private bool _realShowcase;
    private Node3D? _real;
    private RealTerrain? _realTerrain;
    private RoverModel? _realRover;
    private Node3D? _realLander;
    private DirectionalLight3D? _realSun;
    private Camera3D? _realCamera;
    private Vector3 _realEye;
    private bool _realActive;

    /// <summary>True while exploration shows the real-scale ground.</summary>
    private bool RealView => _realActive;

    /// <summary>Metres of the real region per map cell.</summary>
    private double MetresPerCell => _realTerrain is null ? 1.0 : _realTerrain.SizeM / _session.Width;

    /// <summary>Builds the real-scale view once (only when the real relief is loaded).</summary>
    private bool EnsureReal()
    {
        if (_real is not null)
        {
            return true;
        }

        if (Relief() is not { } relief)
        {
            return false;
        }

        _real = new Node3D { Name = "RealView", Visible = false };
        AddChild(_real);
        var environment = (Godot.Environment)_world.Environment3D.Duplicate();
        environment.FogDensity = 0.000032f; // metres, not map units: haze over kilometres
        environment.FogAerialPerspective = 0.6f;
        environment.AmbientLightEnergy = 0.45f; // relief reads from the sun at this scale
        _real.AddChild(new WorldEnvironment { Environment = environment });
        _realSun = new DirectionalLight3D { ShadowEnabled = true, DirectionalShadowMaxDistance = 1500f, LightAngularDistance = 0.4f };
        _real.AddChild(_realSun);

        var albedo = Image.LoadFromFile(relief.Definition.AlbedoFile);
        albedo.GenerateMipmaps();
        _realTerrain = new RealTerrain { Name = "RealTerrain" };
        _realTerrain.Setup(relief, ImageTexture.CreateFromImage(albedo));
        foreach (var insert in relief.Inserts.Take(1))
        {
            var detail = Image.LoadFromFile(insert.Detail.Definition.AlbedoFile);
            detail.GenerateMipmaps();
            _realTerrain.SetDetail(insert, ImageTexture.CreateFromImage(detail));
        }
        _real.AddChild(_realTerrain);
        _realRover = new RoverModel { Name = "Rover" };
        _real.AddChild(_realRover);
        _realLander = (Node3D)_world.LanderNode.Duplicate();
        _realLander.Scale = Vector3.One * 1.6f;
        _real.AddChild(_realLander);
        _realCamera = new Camera3D { Fov = 62f, Near = 0.3f, Far = 70000f };
        _real.AddChild(_realCamera);
        return true;
    }

    /// <summary>Region metres of a point in cell units.</summary>
    private Vector3 RealPoint(double x, double y) => _realTerrain!.FromRegion(x * MetresPerCell, y * MetresPerCell);

    /// <summary>Every frame: which view is shown, the rover's speed scale, the rover and camera at real scale.</summary>
    private void SyncReal(double delta, float drivenCells)
    {
        var want = Driving && !_options.ContainsKey("procedural") && EnsureReal();
        if (want != _realActive)
        {
            _realActive = want;
            _real!.Visible = want;
            _world.Visible = !want;
            if (want)
            {
                _realEye = Vector3.Zero;
                _realCamera!.MakeCurrent();
            }
            else
            {
                _world.Camera.MakeCurrent();
            }
        }

        // Real speed while driving; fast travel on a move order; the map's own speed otherwise.
        _rover.SpeedScale = !want
            ? 1.0
            : _realTerrain!.Relief.Definition.RoverSpeedMs / MetresPerCell / _rover.Definition.SpeedCellsPerSecond
                * (_moveOrder is not null ? _realTerrain.Relief.Definition.TravelFactor : 1.0);
        if (!want)
        {
            return;
        }

        _realSun!.GlobalTransform = _world.Sun.GlobalTransform;
        _realSun.LightEnergy = _world.Sun.LightEnergy;
        _realSun.LightColor = _world.Sun.LightColor;

        var position = RealPoint(_rover.X, _rover.Y);
        var heading = (float)_rover.Heading;
        _realRover!.Pose(_realTerrain!.HeightAt, position, heading, (float)(drivenCells * MetresPerCell), _landed && _rover.Battery < 0.15, _rover.Charging);
        _realTerrain.Focus(position);
        if (_rover.Lander is { } lander)
        {
            var p = RealPoint(lander.X + 0.5, lander.Y + 0.5);
            _realLander!.Position = p;
            _realLander.Visible = true;
        }

        var forward = new Vector3(MathF.Cos(heading), 0, MathF.Sin(heading));
        var eye = position - (forward * 11f) + new Vector3(0, 3.6f, 0);
        eye.Y = MathF.Max(eye.Y, _realTerrain.HeightAt(eye.X, eye.Z) + 2.5f);
        _realEye = _realEye == Vector3.Zero ? eye : _realEye.Lerp(eye, 1f - MathF.Exp(-6f * (float)delta));
        _realCamera!.Position = _realEye;
        _realCamera.LookAt(position + (forward * 40f) + new Vector3(0, 2.6f, 0), Vector3.Up);
    }

    private async void RealShowcase(string path)
    {
        if (Relief() is null)
        {
            GD.PushError("--real-shot necesita el relieve real.");
            GetTree().Quit(1);
            return;
        }

        if (_options.TryGetValue("section", out var sectionId) && _planet is not null)
        {
            // M4: show another section of the mosaic (its relief and places), on a fresh session.
            UseSection(_planet.Section(sectionId));
            StartSession(GameSession.NewGame(_dataRoot, _session.Seed, withMission: false, _session.World));
        }

        _realShowcase = true;
        SetSpeed(0);
        _world.TimeOfDay = _options.TryGetValue("hour", out var sky) ? float.Parse(sky, CultureInfo.InvariantCulture) : 0.36f;
        if (!_options.ContainsKey("hud") && _hud.GetParent() is CanvasLayer layer)
        {
            layer.Visible = false;
        }

        SwitchMode(Martis.Application.GameMode.Exploration);
        var at = _options.TryGetValue("at", out var a) ? a.Split(',') : ["41500", "44500"];
        var cells = MetresPerCellFor(_session.Width);
        var look = Mathf.DegToRad(_options.TryGetValue("look", out var l) ? float.Parse(l, CultureInfo.InvariantCulture) - 90f : 200f);
        _rover.Restore(double.Parse(at[0], CultureInfo.InvariantCulture) / cells, double.Parse(at[1], CultureInfo.InvariantCulture) / cells, look, _rover.Battery, _rover.Wear, _rover.Lander);
        for (var i = 0; i < 3; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        _realTerrain!.BuildAll(RealPoint(_rover.X, _rover.Y));
        for (var i = 0; i < 40; i++)
        {
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        }

        var error = GetViewport().GetTexture().GetImage().SavePng(path);
        GD.Print($"MARTIS real-shot {path} {error} chunks={_realTerrain.ChunkCount} ground={RealPoint(_rover.X, _rover.Y).Y:0.0} m");
        GetTree().Quit(error == Error.Ok ? 0 : 1);
    }

    private double MetresPerCellFor(int width) => Relief() is { } relief ? relief.Definition.SizeM / width : 1.0;
}
