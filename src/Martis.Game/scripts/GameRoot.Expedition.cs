using System;
using Godot;
using Martis.Simulation.Expedition;

namespace Martis.Game;

/// <summary>
/// «Expedición» J1 (ADR-026): the player drives a seeding rover over the relief. Tab swaps the
/// orbital view for a chase camera behind it; colonies can only be seeded within its reach. The
/// rover is part of the controls, never of the simulation: it only decides which clicks become
/// <c>Introduce</c> commands, so the run stays deterministic and saves are unchanged.
/// </summary>
public partial class GameRoot
{
    private RoverDefinition? _roverDefinition;
    private Rover _rover = null!;
    private RoverGauge? _gauge;
    private bool _landed;
    private bool _droveOnce;

    /// <summary>True while the chase camera follows the rover (exploration mode).</summary>
    private bool Driving => Exploring;

    /// <summary>A new session gets a fresh rover: at the lander when colonies exist, else at the map centre.</summary>
    private void ResetRover(int landerCell)
    {
        _roverDefinition ??= RoverDefinition.LoadPublished(_dataRoot);
        _rover = new Rover(_roverDefinition, _session.Width, _session.Height);
        _landed = landerCell >= 0;
        if (_landed)
        {
            var x = landerCell % _session.Width;
            var y = landerCell / _session.Width;
            _rover.Place(Math.Max(0, x - 2), y, 0);
            _rover.Landed(x, y);
        }

        if (_gauge is null)
        {
            _gauge = new RoverGauge { Name = "RoverGauge" };
            _hud.GetParent().AddChild(_gauge);
        }

        ApplyMode();
        PoseRover(0f);
    }

    private void RoverLanded(int x, int y)
    {
        _landed = true;
        _rover.Landed(x, y);
    }

    /// <summary>A click in deploy mode becomes a deployment only within the rover's reach.</summary>
    private bool RoverAllows(int x, int y)
    {
        if (_rover.CanReach(x, y))
        {
            return true;
        }

        var distance = _rover.DistanceTo(x, y);
        _hud.Toast(
            FormattableString.Invariant($"Fuera del alcance del rover ({distance:0} casillas, alcance {_rover.Definition.ReachCells:0}). Pulsa Tab, acércate con WASD y siembra con F."),
            bad: true);
        return false;
    }

    private void UpdateRover(double delta)
    {
        var throttle = 0.0;
        var steer = 0.0;
        if (Driving && !_menu.Visible && GetViewport().GuiGetFocusOwner() is not LineEdit)
        {
            if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) throttle += 1;
            if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) throttle -= 1;
            if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) steer += 1;
            if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) steer -= 1;
        }

        var driven = _rover.Drive(throttle, steer, delta);
        if (driven > 0 && !_droveOnce)
        {
            _droveOnce = true;
            Act("drive-rover");
        }

        PoseRover((float)(driven * Terrain.CellSize * Math.Sign(throttle)));
    }

    private void PoseRover(float drivenMetres)
    {
        var position = _world.CellPoint(_rover.X, _rover.Y);
        var heading = (float)_rover.Heading;
        _world.Rover.Pose(_world.Ground, position, heading, drivenMetres, _landed && _rover.Battery < 0.15, _rover.Charging);
        if (Driving)
        {
            _world.Camera.Chase(position, heading);
        }

        var ring = Driving || _hud.DeployMode;
        _world.ShowReach(_rover.X, _rover.Y, ring ? _rover.Definition.ReachCells : -1);
        _gauge?.Show(_rover, Driving, _landed);
    }
}
