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
    Vector3 _lead, _lastTp; float _lockB, _camLen = -1f; Node3D? _interpFor;
    Vector3 _frame; float _frameDist, _framePitch, _flickX, _switchT; double _flickT0, _flickCool;
    public void StepBob() { if (Sprinting && Game.Set.Shake > 0) _bob = .03f; }

    public Camera3D Camera => _cam;
    /// <summary>
    /// First person (Settings.FirstPerson, toggle_view key): the camera sits just in front of the hero's face at eye height and follows the
    /// head bone's bob lightly; the hero's yaw follows the camera (Player). Head, hair, helm and race pieces are on <see cref="HeadLayer"/>,
    /// which this camera does not draw (their shadows stay). Mounted or dead the view drops back to third person.
    /// </summary>
    public const uint HeadLayer = 1u << 19;
    public bool FirstPerson => (Game.Set.FirstPerson || AutoFirstPerson) && Target is Player p && p == Player.Instance && !p.Mounted && !p.Dead;
    /// <summary>A-09, Settings > Game "First person while building": build mode (the pickaxe out) switches to first person by itself and back when it is put away.</summary>
    public static bool AutoFirstPerson => Game.Set.FirstPersonBuild && Player.BuildMode && BlockWorld.Enabled && BlockWorld.I?.Area != null;
    Vector3 _fpRel; bool _fpWas; float _sneakY;
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
            if (Targeting.Current != null)
            {
                // flick detection: horizontal travel beyond 90 px within 0.15 s switches the lock to that side
                double now = Time.GetTicksMsec() / 1000.0;
                if (now - _flickT0 > .15 || Mathf.Sign(m.Relative.X) != Mathf.Sign(_flickX)) { _flickT0 = now; _flickX = 0; }
                _flickX += m.Relative.X;
                if (Mathf.Abs(_flickX) > 90f && now > _flickCool)
                {
                    if (Targeting.SwitchSide(_flickX > 0 ? 1 : -1)) { _flickCool = now + .45; _switchT = .35f; }
                    _flickX = 0;
                }
            }
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

    // controller look: right stick, radial deadzone (Settings > Controller), quadratic response, smoothed, with a turn boost when held at full tilt
    Vector2 _padV; float _padAcc; bool _padFlick = true;
    void PadLook(float dt)
    {
        if (!Inputs.HasPad || Ui.AnyOpen || Input.MouseMode != Input.MouseModeEnum.Captured) { _padV = Vector2.Zero; _padAcc = 0; return; }
        var rs = Inputs.Stick(JoyAxis.RightX, JoyAxis.RightY);
        if (Targeting.Current != null)
        {
            // locked on: a sideways flick of the stick switches to the next target on that side; yaw follows the lock
            if (Mathf.Abs(rs.X) > .7f && _padFlick) { if (Targeting.SwitchSide(rs.X > 0 ? 1 : -1)) _switchT = .35f; _padFlick = false; }
            else if (Mathf.Abs(rs.X) < .3f) _padFlick = true;
            rs.X = 0;
        }
        float len = rs.Length(); var goal = rs * len;   // fine aim near the centre, full speed at the edge
        _padAcc = len > .92f ? Mathf.MoveToward(_padAcc, 1f, dt / .45f) : Mathf.MoveToward(_padAcc, 0f, dt / .12f);
        _padV = _padV.Lerp(goal, 1f - Mathf.Exp(-16f * dt));
        if (_padV.LengthSquared() < 1e-6f) return;
        float sp = Mathf.Max(.05f, Game.Set.PadSens) * (1f + .7f * _padAcc);
        RotateY(-_padV.X * 2.8f * sp * dt);
        _pitch = Mathf.Clamp(_pitch - _padV.Y * 1.8f * sp * dt * (Game.Set.InvertY ? -1f : 1f), MinPitch, MaxPitch);
        _pivot.Rotation = new Vector3(_pitch, 0, 0);
    }

    /// <summary>
    /// First-person eye: the hero's (interpolated) position plus the head bone's offset, smoothed so the head bob comes through lightly,
    /// a little above the bone and 0.16 m ahead of it along the view, which puts the camera just in front of the face (never inside it).
    /// </summary>
    void FirstPersonEye(Vector3 tp, float dt)
    {
        var rel = new Vector3(0, 1.62f, 0);
        if (Target is Player p && p.Av?.Skel is { } sk)
        {
            int hb = sk.FindBone("Head");
            if (hb >= 0) { var hp = (sk.GlobalTransform * sk.GetBoneGlobalPose(hb)).Origin - p.GlobalPosition; if (hp.Y > .5f && hp.Length() < 2.6f) rel = hp + new Vector3(0, .07f, 0); }
        }
        if (_fpRel == Vector3.Zero) _fpRel = rel;
        // horizontal: follow tightly (a lag would pull the camera back into the head); vertical: softer, so steps only bob it a little
        _fpRel = new Vector3(Mathf.Lerp(_fpRel.X, rel.X, 1f - Mathf.Exp(-30f * dt)), Mathf.Lerp(_fpRel.Y, rel.Y, 1f - Mathf.Exp(-9f * dt)), Mathf.Lerp(_fpRel.Z, rel.Z, 1f - Mathf.Exp(-30f * dt)));
        var fwd = new Vector3(-Mathf.Sin(Rotation.Y), 0, -Mathf.Cos(Rotation.Y));
        GlobalPosition = tp + _fpRel + fwd * .16f;
        _pivot.Rotation = new Vector3(_pitch, 0, 0);
    }

    public override void _Process(double delta)
    {
        Follow(delta);
        // A-01: the camera is now where this frame draws it: solve the block aim through the reticle once, for the outline, the ghost,
        // digging and placing alike (PlayerBlocks.BlockAimFrame)
        if (Target is Player bp && bp == Player.Instance && IsInstanceValid(bp)) bp.BlockAimFrame();
    }
    void Follow(double delta)
    {
        PadLook((float)delta);
        if (Target != null)
        {
            // follow the head height, smoothed
            // look-ahead: lead the camera along the horizontal motion (0.8 m run, 1.2 m sprint), tau 0.4 s, none while locked on
            // physics interpolation (project.godot): the hero's body moves on 60 Hz physics ticks but is drawn interpolated every frame,
            // so its animation, the camera and the world stay in step at any frame rate; the camera itself is not interpolated (it moves here)
            if (_interpFor != Target)
            {
                _interpFor = Target; GetTree().Root.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off; PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
                if (Target is CharacterBody3D) { Target.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.On; Target.ResetPhysicsInterpolation(); }
            }
            var tp = Target.PhysicsInterpolationMode == PhysicsInterpolationModeEnum.On ? Target.GetGlobalTransformInterpolated().Origin : Target.GlobalPosition; float dt = Mathf.Max((float)delta, .0001f);
            var hv = new Vector3(tp.X - _lastTp.X, 0, tp.Z - _lastTp.Z) / dt; _lastTp = tp;
            if (hv.Length() > 30f) { hv = Vector3.Zero; if (Target.PhysicsInterpolationMode == PhysicsInterpolationModeEnum.On) Target.ResetPhysicsInterpolation(); }   // teleport / respawn guard (no streak)
            if (FirstPerson) { FirstPersonEye(tp, (float)delta); goto lockTurn; }
            bool locked = Targeting.Current is Enemy le && IsInstanceValid(le);
            _lockB = Mathf.MoveToward(_lockB, locked ? 1f : 0f, (float)delta / .3f);
            float maxLead = Sprinting ? 1.2f : 0.8f;
            var leadGoal = locked ? Vector3.Zero : hv.LimitLength(6f) / 6f * maxLead;
            _lead = _lead.Lerp(leadGoal, 1f - Mathf.Exp(-(float)delta / .4f));
            if (locked) _lead = _lead.Lerp(Vector3.Zero, 1f - Mathf.Exp(-12f * (float)delta));
            // lock framing: look point = lerp(hero, target chest, 0.35); farther apart = farther back; tall targets tilt the view down; tau 0.25 s
            Vector3 frameGoal = Vector3.Zero; float distGoal = 0f, pitchGoal = 0f;
            if (locked && Targeting.Current is Enemy fe)
            {
                var chest = fe.GlobalPosition + Vector3.Up * fe.HeightM() * .6f; var hero = tp + new Vector3(0, 1.6f, 0);
                frameGoal = (chest - hero) * .35f;
                float sep = (fe.GlobalPosition - tp).Length();
                distGoal = Mathf.Clamp(.15f * (sep - 4f), 0f, 3f);
                pitchGoal = -.12f * Mathf.Clamp((fe.HeightM() - 2.5f) / 3f, 0f, 1f);
            }
            float kf = 1f - Mathf.Exp(-(float)delta / .25f);
            _frame = _frame.Lerp(frameGoal, kf); _frameDist = Mathf.Lerp(_frameDist, distGoal, kf); _framePitch = Mathf.Lerp(_framePitch, pitchGoal, kf);
            _pivot.Rotation = new Vector3(Mathf.Clamp(_pitch + _framePitch, MinPitch, MaxPitch), 0, 0);
            _sneakY = Mathf.Lerp(_sneakY, Target is Player { Sneaking: true } ? -.28f : 0f, 1f - Mathf.Exp(-6f * (float)delta));   // A-10: the view sinks with the crouch
            var goal = tp + new Vector3(0, 1.6f + _sneakY, 0) + _lead + _frame;
            GlobalPosition = GlobalPosition.Lerp(goal, 1f - Mathf.Exp(-14f * (float)delta));
        }
        lockTurn:
        // lock-on: swing the camera so the target stays in view (mouse still controls pitch)
        if (Targeting.Current is Enemy t && IsInstanceValid(t) && Target != null)
        {
            var d = t.GlobalPosition - Target.GlobalPosition; d.Y = 0;
            _switchT = Mathf.Max(0, _switchT - (float)delta);
            float turn = _switchT > 0 ? Mathf.Min(Feel.LockTurn, 7f) : Feel.LockTurn;   // short ease after a target switch
            if (d.LengthSquared() > .5f) Rotation = new Vector3(0, Mathf.LerpAngle(Rotation.Y, Util.YawTo(d.X, d.Z), 1f - Mathf.Exp(-turn * (float)delta)), 0);
        }
        float pulse = 0f; if (_fovT > 0) { _fovT = Mathf.Max(0, _fovT - (float)delta); float u = _fovT / Mathf.Max(.01f, Feel.FovPulseTime); pulse = Feel.FovPulse * Mathf.Sin(u * Mathf.Pi); }
        _spr = Mathf.MoveToward(_spr, Sprinting ? 1f : 0f, (float)delta / .35f); float sE = _spr * _spr * (3f - 2f * _spr);
        bool fp = FirstPerson;
        if (fp != _fpWas)
        {
            _fpWas = fp; _cam.CullMask = fp ? 0xFFFFFu & ~HeadLayer : 0xFFFFFu; _cam.Near = fp ? .03f : .05f;
            _frame = Vector3.Zero; _frameDist = _framePitch = 0f; _lead = Vector3.Zero; _fpRel = Vector3.Zero;
            if (!fp) _camLen = -1f;
        }
        _cam.Fov = (fp ? Game.Set.Fov + 5f : Game.Set.Fov) - pulse + (fp ? 3f : 6f) * sE;
        _kick *= Mathf.Exp(-16f * (float)delta); _bob *= Mathf.Exp(-9f * (float)delta);
        _arm.SpringLength = Mathf.Lerp(_arm.SpringLength, Distance + .4f * sE + _frameDist,1f - Mathf.Exp(-(10f - 4f * sE) * (float)delta));
        _shake = Mathf.Max(0, _shake - (float)delta * 1.2f);
        if (fp)
        {
            // first person: no arm, no shoulder offset; shake and swing kicks stay (smaller)
            _arm.SpringLength = 0f; _camLen = 0f; _cam.Position = Vector3.Zero;
            _cam.HOffset = (float)GD.RandRange(-1.0, 1.0) * _shake * 0.12f + _kick.X * .4f;
            _cam.VOffset = (float)GD.RandRange(-1.0, 1.0) * _shake * 0.12f + _kick.Y * .4f + _bob * .5f;
            return;
        }
        // over the right shoulder, so the aim point at screen centre is not hidden behind the hero (data/feel.json camera.shoulder)
        // collision smoothing: pull in instantly, ease back out over ~0.35 s (camera local Z, applied after the arm's own placement)
        float hit = _arm.GetHitLength();
        if (_camLen < 0f || hit < _camLen) _camLen = hit; else _camLen = Mathf.Lerp(_camLen, hit, 1f - Mathf.Exp(-(float)delta / .35f));
        _cam.Position = new Vector3(_cam.Position.X, _cam.Position.Y, _camLen);
        float shoulderX = .35f * (1f - sE) * (1f - _lockB);
        _cam.HOffset = Feel.Shoulder + shoulderX + (float)GD.RandRange(-1.0, 1.0) * _shake * 0.3f + _kick.X;
        _cam.VOffset = Feel.ShoulderUp + (float)GD.RandRange(-1.0, 1.0) * _shake * 0.3f + _kick.Y + _bob;
    }
}
