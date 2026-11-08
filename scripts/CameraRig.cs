using Godot;

namespace TwinRealms;

/// <summary>Free third-person camera. Mouse look, scroll to zoom, Esc frees the mouse, click recaptures it.</summary>
public partial class CameraRig : Node3D
{
    public Node3D? Target;
    public float Sensitivity = 0.0025f;
    public float MinPitch = -1.2f, MaxPitch = 0.9f;
    public float Distance = 5.0f;

    Node3D _pivot = null!;
    SpringArm3D _arm = null!;
    Camera3D _cam = null!;
    float _pitch = -0.25f;
    float _shake;

    public void Shake(float amount) => _shake = Mathf.Max(_shake, amount);

    public Camera3D Camera => _cam;
    /// <summary>Horizontal facing of the camera in radians (rig yaw).</summary>
    public float Yaw => Rotation.Y;

    public override void _Ready()
    {
        _pivot = new Node3D { Name = "Pivot" };
        AddChild(_pivot);
        _arm = new SpringArm3D { Name = "Arm", SpringLength = Distance, Margin = 0.3f, CollisionMask = 1 };
        _pivot.AddChild(_arm);
        _cam = new Camera3D { Name = "Camera", Fov = 70f, Far = 2000f, Current = true };
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
            _pitch = Mathf.Clamp(_pitch - m.Relative.Y * Sensitivity, MinPitch, MaxPitch);
            _pivot.Rotation = new Vector3(_pitch, 0, 0);
        }
        else if (e is InputEventMouseButton b && b.Pressed)
        {
            if (b.ButtonIndex == MouseButton.WheelUp) Distance = Mathf.Clamp(Distance - 0.5f, 2f, 12f);
            else if (b.ButtonIndex == MouseButton.WheelDown) Distance = Mathf.Clamp(Distance + 0.5f, 2f, 12f);
            else if (Input.MouseMode != Input.MouseModeEnum.Captured) Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        else if (e.IsActionPressed(Inputs.Release))
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
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
        _arm.SpringLength = Mathf.Lerp(_arm.SpringLength, Distance, 1f - Mathf.Exp(-10f * (float)delta));
        _shake = Mathf.Max(0, _shake - (float)delta * 1.2f);
        _cam.HOffset = (float)GD.RandRange(-1.0, 1.0) * _shake * 0.3f;
        _cam.VOffset = (float)GD.RandRange(-1.0, 1.0) * _shake * 0.3f;
    }
}
