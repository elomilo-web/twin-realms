using Godot;

namespace TwinRealms;

/// <summary>Free third-person camera. Mouse look, scroll to zoom, Esc frees the mouse, click recaptures it.</summary>
public partial class CameraRig : Node3D
{
    public Node3D? Target;
    public float Sensitivity => 0.0025f * Game.Set.Sens;
    public float MinPitch = -1.2f, MaxPitch = 0.9f;
    public float Distance = 5.0f;

    Node3D _pivot = null!;
    SpringArm3D _arm = null!;
    Camera3D _cam = null!;
    float _pitch = -0.25f;
    float _shake;

    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount * Game.Set.Shake);

    Vector2 _kick; float _fovT;
    /// <summary>Short punch of the camera along a world-space swing direction, optional tiny FOV pulse. Off when Shake setting is 0.</summary>
    public void Kick(Vector3 dir, float dist, bool fovPulse)
    {
        float s = Game.Set.Shake; if (s <= 0 || dir.LengthSquared() < .0001f) return;
        var right = GlobalTransform.Basis.X; dir = dir.Normalized();
        _kick += new Vector2(right.Dot(dir), -.4f) * dist * s;
        if (fovPulse) _fovT = Feel.FovPulseTime;
    }

    // sprint feel: FOV +6 deg and +0.4 m arm (0.35 s ease), footstep-synced 0.03 m vertical bob (bob off when Shake = 0)
    public bool Sprinting; float _spr, _bob;
    public void StepBob() { if (Sprinting && Game.Set.Shake > 0) _bob = .03f; }

    public Camera3D Camera => _cam;
    /// <summary>Horizontal facing of the camera in radians (rig yaw).</summary>
    public float Yaw => Rotation.Y;

    public override void _Ready()
    {
        _pivot = new Node3D { Name = "Pivot" };
        AddChild(_pivot);
        _arm = new SpringArm3D { Name = "Arm", SpringLength = Distance, Margin = 0.3f, CollisionMask = 1 };
        _pivot.AddChild(_arm);
        _cam = new Camera3D { Name = "Camera", Fov = 70f, Far = 4500f, Current = true };
        _arm.AddChild(_cam);
        _pivot.Rotation = new Vector3(_pitch, 0, 0);
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void ExcludeFromCollision(CollisionObject3D body) => _arm.AddExcludedObject(body.GetRid());

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion m && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-m.Relative.X * Sensitivity);
            _pitch = Mathf.Clamp(_pitch - m.Relative.Y * Sensitivity * (Game.Set.InvertY ? -1f : 1f), MinPitch, MaxPitch);
            _pivot.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (e is InputEventMouseButton b && b.Pressed)
        {
            if (Player.BuildMode && b.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown && Player.Instance != null) Player.Instance.CycleBlock(b.ButtonIndex == MouseButton.WheelUp ? -1 : 1);   // build mode: the wheel picks the block
            else if (b.ButtonIndex == MouseButton.WheelUp) Distance = Mathf.Clamp(Distance - 0.5f, 2f, 12f);
            else if (b.ButtonIndex == MouseButton.WheelDown) Distance = Mathf.Clamp(Distance + 0.5f, 2f, 12f);
            else if (Input.MouseMode != Input.MouseModeEnum.Captured && !Ui.AnyOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
        }
    }

    public override void _Process(double delta)
    {
        if (Target != null)
        {
            // follow the head height, smoothed
            var goal = Target.GlobalPosition + new Vector3(0, 1.6f, 0);
            GlobalPosition = GlobalPosition.Lerp(goal, 1f - Mathf.Exp(-14f * (float)delta));
        }
        // lock-on: swing the camera so the target stays in view (mouse still controls pitch)
        if (Targeting.Current is Enemy t && IsInstanceValid(t) && Target != null)
        {
            var d = t.GlobalPosition - Target.GlobalPosition; d.Y = 0;
            if (d.LengthSquared() > .5f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(d.X, d.Z), 1f - Mathf.Exp(-Feel.LockTurn * (float)delta)), 0);
        }
        float pulse = 0f; if (_fovT > 0) { _fovT = Mathf.Max(0, _fovT - (float)delta); float u = _fovT / Mathf.Max(.01f, Feel.FovPulseTime); pulse = Feel.FovPulse * Mathf.Sin(u * Mathf.Pi); }
        _spr = Mathf.MoveToward(_spr, Sprinting ? 1f : 0f, (float)delta / .35f); float sE = _spr * _spr * (3f - 2f * _spr);
        _cam.Fov = Game.Set.Fov - pulse + 6f * sE;
        _kick *= Mathf.Exp(-16f * (float)delta); _bob *= Mathf.Exp(-9f * (float)delta);
        _arm.SpringLength = Mathf.Lerp(_arm.SpringLength, Distance + .4f * sE, 1f - Mathf.Exp(-(10f - 4f * sE) * (float)delta));
        _shake = Mathf.Max(0, _shake - (float)delta * 1.2f);
        // over the right shoulder, so the aim point at screen centre is not hidden behind the hero (data/feel.json camera.shoulder)
        _cam.HOffset = Feel.Shoulder + (float)GD.RandRange(-1.0, 1.0) * _shake * 0.3f + _kick.X;
        _cam.VOffset = Feel.ShoulderUp + (float)GD.RandRange(-1.0, 1.0) * _shake * 0.3f + _kick.Y + _bob;
    }
}
