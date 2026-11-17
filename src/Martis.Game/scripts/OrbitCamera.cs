using System;
using Godot;

namespace Martis.Game;

/// <summary>
/// Orbit camera: right drag rotates, wheel zooms, middle drag or WASD pans, Q/E turn.
/// Moves are eased so the camera glides. Stays above the ground. In chase mode (J1, ADR-026)
/// it rides behind the rover instead: WASD belong to the rover, right drag and Q/E look around
/// it and the view swings back behind it on its own.
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
    private float _orbitDistance = 95f;
    private float _orbitPitch = 0.62f;
    private float _chaseOffset;

    public Terrain? Ground { get; set; }

    /// <summary>Farthest zoom; grows with the map (H3).</summary>
    public float MaxDistance { get; set; } = 260f;

    /// <summary>Raised when the player orbits (<c>orbit-camera</c>) or zooms (<c>zoom-camera</c>); the tutorial listens.</summary>
    public event Action<string>? Used;

    /// <summary>Slow turn when nobody touches the camera (title screen, screenshots).</summary>
    public bool Idle { get; set; }

    /// <summary>True while the camera follows the rover.</summary>
    public bool Chasing { get; private set; }

    public void BeginChase()
    {
        if (Chasing)
        {
            return;
        }

        Chasing = true;
        Idle = false;
        _orbitDistance = _goalDistance;
        _orbitPitch = _goalPitch;
        _goalDistance = 12f;
        _goalPitch = 0.36f;
        _chaseOffset = 0f;
    }

    public void EndChase()
    {
        if (!Chasing)
        {
            return;
        }

        Chasing = false;
        _goalDistance = _orbitDistance;
        _goalPitch = _orbitPitch;
    }

    /// <summary>Called each frame in chase mode with the rover's position and heading (0 = +X, towards +Z).</summary>
    public void Chase(Vector3 target, float heading)
    {
        _goalTarget = target + (Vector3.Up * 0.8f);
        var behind = MathF.Atan2(-MathF.Cos(heading), -MathF.Sin(heading)) + _chaseOffset;
        _goalYaw = _yaw + Mathf.Wrap(behind - _yaw, -MathF.PI, MathF.PI);
    }

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
                    _goalDistance = Math.Max(Chasing ? 5f : 12f, _goalDistance * 0.88f);
                    Used?.Invoke("zoom-camera");
                }
                else if (button.Pressed && button.ButtonIndex == MouseButton.WheelDown)
                {
                    _goalDistance = Math.Min(Chasing ? 45f : MaxDistance, _goalDistance * 1.12f);
                    Used?.Invoke("zoom-camera");
                }

                Idle = false;
                break;
            case InputEventMouseMotion motion when _rotating && Chasing:
                _chaseOffset -= motion.Relative.X * 0.006f;
                _goalPitch = Math.Clamp(_goalPitch + (motion.Relative.Y * 0.005f), 0.08f, 1.3f);
                break;
            case InputEventMouseMotion motion when _rotating:
                _goalYaw -= motion.Relative.X * 0.006f;
                _goalPitch = Math.Clamp(_goalPitch + (motion.Relative.Y * 0.005f), 0.12f, 1.45f);
                Used?.Invoke("orbit-camera");
                break;
            case InputEventMouseMotion motion when _panning && !Chasing:
                Pan(new Vector2(-motion.Relative.X, -motion.Relative.Y) * (_goalDistance * 0.0018f));
                break;
        }
    }

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        if (Chasing)
        {
            if (Input.IsKeyPressed(Key.Q)) _chaseOffset += dt * 1.2f;
            if (Input.IsKeyPressed(Key.E)) _chaseOffset -= dt * 1.2f;
            if (!_rotating && !Input.IsKeyPressed(Key.Q) && !Input.IsKeyPressed(Key.E))
            {
                _chaseOffset *= MathF.Exp(-dt * 0.6f);
            }

            Glide(dt);
            return;
        }

        var move = Vector2.Zero;
        if (Input.IsKeyPressed(Key.W)) move.Y -= 1;
        if (Input.IsKeyPressed(Key.S)) move.Y += 1;
        if (Input.IsKeyPressed(Key.A)) move.X -= 1;
        if (Input.IsKeyPressed(Key.D)) move.X += 1;
        if (Input.IsKeyPressed(Key.Q)) _goalYaw += dt * 1.2f;
        if (Input.IsKeyPressed(Key.E)) _goalYaw -= dt * 1.2f;
        if (Input.IsKeyPressed(Key.Q) || Input.IsKeyPressed(Key.E)) Used?.Invoke("orbit-camera");
        if (move != Vector2.Zero)
        {
            Pan(move.Normalized() * (_goalDistance * 0.9f * dt));
            Idle = false;
        }

        if (Idle)
        {
            _goalYaw += dt * 0.05f;
        }

        Glide(dt);
    }

    private void Glide(float dt)
    {
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
