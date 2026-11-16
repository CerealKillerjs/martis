using System;
using Godot;

namespace Martis.Game;

/// <summary>
/// Orbit camera: right drag rotates, wheel zooms, middle drag or WASD pans, Q/E turn.
/// Moves are eased so the camera glides. Stays above the ground.
/// </summary>
public partial class OrbitCamera : Camera3D
{
    private Vector3 _target;
    private Vector3 _goalTarget;
    private float _yaw = 0.7f;
    private float _goalYaw = 0.7f;
    private float _pitch = 0.62f;
    private float _goalPitch = 0.62f;
    private float _distance = 95f;
    private float _goalDistance = 95f;
    private bool _rotating;
    private bool _panning;

    public Terrain? Ground { get; set; }

    /// <summary>Slow turn when nobody touches the camera (title screen, screenshots).</summary>
    public bool Idle { get; set; }

    public void Frame(Vector3 target, float distance, float yaw, float pitch, bool snap = false)
    {
        _goalTarget = target;
        _goalDistance = distance;
        _goalYaw = yaw;
        _goalPitch = pitch;
        if (snap)
        {
            _target = target;
            _distance = distance;
            _yaw = yaw;
            _pitch = pitch;
            Place();
        }
    }

    public void FocusOn(Vector3 target) => _goalTarget = target;

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton button:
                if (button.ButtonIndex == MouseButton.Right)
                {
                    _rotating = button.Pressed;
                }
                else if (button.ButtonIndex == MouseButton.Middle)
                {
                    _panning = button.Pressed;
                }
                else if (button.Pressed && button.ButtonIndex == MouseButton.WheelUp)
                {
                    _goalDistance = Math.Max(12f, _goalDistance * 0.88f);
                }
                else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                {
                    _goalDistance = Math.Min(260f, _goalDistance * 1.12f);
                }

                Idle = false;
                break;
            case InputEventMouseMotion motion when _rotating:
                _goalYaw -= motion.Relative.X * 0.006f;
                _goalPitch = Math.Clamp(_goalPitch + (motion.Relative.Y * 0.005f), 0.12f, 1.45f);
                break;
            case InputEventMouseMotion motion when _panning:
                Pan(new Vector2(-motion.Relative.X, -motion.Relative.Y) * (_goalDistance * 0.0018f));
                break;
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D)) move.X += 1;
        if (Input.IsKeyPressed(Key.Q)) _goalYaw += dt * 1.2f;
        if (Input.IsKeyPressed(Key.E)) _goalYaw -= dt * 1.2f;
        if (move != Vector2.Zero)
        {
            Pan(move.Normalized() * (_goalDistance * 0.9f * dt));
            Idle = false;
        }

        if (Idle)
        {
            _goalYaw += dt * 0.05f;
        }

        var k = 1f - MathF.Exp(-dt * 6f);
        _target = _target.Lerp(_goalTarget, k);
        _yaw = Mathf.Lerp(_yaw, _goalYaw, k);
        _pitch = Mathf.Lerp(_pitch, _goalPitch, k);
        _distance = Mathf.Lerp(_distance, _goalDistance, k);
        Place();
    }

    private void Pan(Vector2 amount)
    {
        var forward = new Vector3(-MathF.Sin(_yaw), 0, -MathF.Cos(_yaw));
        var right = new Vector3(MathF.Cos(_yaw), 0, -MathF.Sin(_yaw));
        _goalTarget += (right * amount.X) - (forward * amount.Y);
        if (Ground is not null)
        {
            _goalTarget.X = Math.Clamp(_goalTarget.X, -Ground.SizeX / 2f, Ground.SizeX / 2f);
            _goalTarget.Z = Math.Clamp(_goalTarget.Z, -Ground.SizeZ / 2f, Ground.SizeZ / 2f);
            _goalTarget.Y = Ground.HeightAt(_goalTarget.X, _goalTarget.Z);
        }
    }

    private void Place()
    {
        var offset = new Vector3(
            MathF.Sin(_yaw) * MathF.Cos(_pitch),
            MathF.Sin(_pitch),
            MathF.Cos(_yaw) * MathF.Cos(_pitch)) * _distance;
        var position = _target + offset;
        if (Ground is not null)
        {
            position.Y = Math.Max(position.Y, Ground.HeightAt(position.X, position.Z) + 2.5f);
        }

        Position = position;
        LookAt(_target, Vector3.Up);
    }
}
