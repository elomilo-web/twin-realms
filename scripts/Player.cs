using Godot;

namespace TwinRealms;

/// <summary>Walk, sprint, jump and roll. Movement is relative to the camera.</summary>
public partial class Player : CharacterBody3D
{
    public CameraRig? Rig;

    [Export] public float WalkSpeed = 5.2f;
    [Export] public float SprintSpeed = 8.4f;
    [Export] public float JumpVelocity = 6.0f;
    [Export] public float RollSpeed = 11f;
    [Export] public float RollTime = 0.45f;
    [Export] public float RollCooldown = 0.6f;
    const float Accel = 40f, AirAccel = 10f;

    Node3D _model = null!;
    float _gravity;
    float _rollT, _rollCd;
    Vector3 _rollDir;

    public override void _Ready()
    {
        _gravity = (float)ProjectSettings.GetSetting("physics/3d/default_gravity");
        var shape = new CapsuleShape3D { Radius = 0.4f, Height = 1.8f };
        AddChild(new CollisionShape3D { Shape = shape, Position = new Vector3(0, 0.9f, 0) });

        // placeholder model: capsule body plus a nose block so you can see which way it faces
        _model = new Node3D { Name = "Model" };
        AddChild(_model);
        var mat = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.2f, 0.2f), Roughness = 0.8f };
        _model.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = 0.4f, Height = 1.8f }, MaterialOverride = mat, Position = new Vector3(0, 0.9f, 0) });
        var noseMat = new StandardMaterial3D { AlbedoColor = new Color(0.95f, 0.85f, 0.4f) };
        _model.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(0.2f, 0.2f, 0.4f) }, MaterialOverride = noseMat, Position = new Vector3(0, 1.4f, -0.4f) });
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        var v = Velocity;
        if (!IsOnFloor()) v.Y -= _gravity * dt;
        _rollCd = Mathf.Max(0, _rollCd - dt);

        var input = Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back);
        float yaw = Rig?.Yaw ?? 0f;
        var dir = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, yaw);

        if (_rollT > 0)
        {
            _rollT -= dt;
            v.X = _rollDir.X * RollSpeed; v.Z = _rollDir.Z * RollSpeed;
        }
        else
        {
            if (Input.IsActionJustPressed(Inputs.Roll) && IsOnFloor() && _rollCd <= 0)
            {
                _rollDir = dir.LengthSquared() > 0.01f ? dir.Normalized() : -_model.GlobalTransform.Basis.Z;
                _rollT = RollTime; _rollCd = RollCooldown;
            }
            if (Input.IsActionJustPressed(Inputs.Jump) && IsOnFloor()) v.Y = JumpVelocity;

            float speed = Input.IsActionPressed(Inputs.Sprint) ? SprintSpeed : WalkSpeed;
            var target = dir * speed;
            float a = (IsOnFloor() ? Accel : AirAccel) * dt;
            v.X = Mathf.MoveToward(v.X, target.X, a);
            v.Z = Mathf.MoveToward(v.Z, target.Z, a);
        }

        // face the direction of travel
        var flat = new Vector3(v.X, 0, v.Z);
        if (flat.LengthSquared() > 0.5f)
        {
            float want = Mathf.Atan2(-flat.X, -flat.Z);
            _model.Rotation = new Vector3(0, Mathf.LerpAngle(_model.Rotation.Y, want, 1f - Mathf.Exp(-14f * dt)), 0);
        }

        Velocity = v;
        MoveAndSlide();
    }
}
