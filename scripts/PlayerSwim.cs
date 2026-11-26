using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// The hero in water (F-20) and in a boat (F-21).
/// Swimming: in water deeper than the hero's chest the body floats with the head just above the surface; Jump swims up (and climbs out
/// onto a bank), Dive (Ctrl, or Roll: the pad's B / Circle) swims down; let go and the hero floats back up. Breath (12 s) drains while the head
/// is under, refills at the surface; empty breath drowns (8% of max health a second). The body lies forward while swimming along
/// (a procedural breaststroke, AvatarSwim.cs) and treads water upright when still. Water blocks and the smooth lakes both count (Water.cs).
/// Boats: BoardBoat / BoatRide / LeaveBoat; placing one from the pack's boats with Interact while looking at open water.
/// </summary>
public partial class Player
{
    public const float MaxBreath = 12f;
    public float Breath = MaxBreath;
    /// <summary>Swimming (not standing on the bed), and the head under the surface.</summary>
    public bool Swimming, HeadUnder;
    float _drownT, _swimPh, _swimTilt;
    public Boat? InBoat;

    bool SwimMove(float dt, ref Vector3 v)
    {
        Swimming = false; HeadUnder = false;
        if (InBoat != null) { BreathTick(dt); return false; }
        var p = GlobalPosition;
        float? s = Water.SurfaceAt(p + Vector3.Up * .9f);
        bool inW = s != null && p.Y + .9f < s.Value;
        if (inW && !_wasWater) Sfx.Play("splash", .8f, 1f);
        _wasWater = inW;
        if (!inW) { BreathTick(dt); return false; }
        float surf = s!.Value, depth = surf - p.Y; const float Float = 1.35f;
        HeadUnder = p.Y + 1.55f < surf;
        bool cap = Input.MouseMode == Input.MouseModeEnum.Captured || DevSwim != 0;
        bool up = (cap && Input.IsActionPressed(Inputs.Jump)) || DevSwim > 0, down = (cap && (Input.IsActionPressed(Inputs.Dive) || Input.IsActionPressed(Inputs.Roll))) || DevSwim < 0;
        if (depth > 1.05f && (!IsOnFloor() || depth > Float || down))
        {
            Swimming = true;
            if (!IsOnFloor()) v.Y += _gravity * dt;   // the water holds us up: undo this frame's gravity
            float target = down ? -2.6f : up ? (depth > Float + .2f ? 3f : 0f) : Mathf.Clamp((depth - Float) * 2.5f, -1.5f, 1.2f);
            if (!down && !up && Mathf.Abs(depth - Float) < .3f) target += Mathf.Sin(_idleT * 2.1f) * .12f;   // bobbing at the surface
            v.Y = Mathf.MoveToward(v.Y, target, 10f * dt);
            if (up && !HeadUnder && IsOnWall()) v.Y = Mathf.Max(v.Y, JumpVelocity * .8f);   // climb out onto the bank
            _fluidSpeed = Blocks.F("fluids.swim.speed", .6f) * (HeadUnder ? .9f : 1f);
            float hv = new Vector2(v.X, v.Z).Length();
            _swimPh += dt * (hv > .5f ? 2.6f : 1.3f);
        }
        else _fluidSpeed = .75f;   // wading
        BreathTick(dt);
        return true;
    }
    /// <summary>DevCapture: +1 hold "swim up", -1 hold "dive" without input (0 = off).</summary>
    public static int DevSwim;

    void BreathTick(float dt)
    {
        if (HeadUnder && !Dead)
        {
            Breath = Mathf.Max(0, Breath - dt);
            if (Breath <= 0)
            {
                _drownT -= dt;
                if (_drownT <= 0)
                {
                    _drownT = 1f; int n = Mathf.Max(1, Mathf.RoundToInt(Game.D.MaxHp() * .08f)); Hp -= n;
                    FloatText.Spawn(this, GlobalPosition + new Vector3(0, 2.0f, 0), "-" + n, new Color(.5f, .75f, 1f), .7f); Rig?.Shake(.03f);
                    if (Hp <= 0 && !Dead && !DuelSave()) Die();
                }
            }
        }
        else { Breath = Mathf.Min(MaxBreath, Breath + dt * 4f); _drownT = .5f; }
    }

    /// <summary>The swimming body: lie forward along the stroke (0..1), lifted about the hips so the back stays at the surface.</summary>
    float SwimTilt(float dt, float hv)
    {
        float want = Swimming ? (hv > .8f ? 1f : .12f) : 0f;
        _swimTilt = Mathf.MoveToward(_swimTilt, want, dt * 2.5f); return _swimTilt;
    }

    // ---------------------------------------------------------------- boats (F-21)
    public void BoardBoat(Boat b)
    {
        if (b.Rider != null || Dead) return; if (Mounted) ToggleMount(true);
        Flying = false; InBoat = b; b.Rider = this; Sfx.Play("land_thud", .4f, 1.3f);
        Ui.Note($"In the boat: {Inputs.KeyOf(Inputs.Forward)}/{Inputs.KeyOf(Inputs.Back)} row, {Inputs.KeyOf(Inputs.Left)}/{Inputs.KeyOf(Inputs.Right)} turn, {Inputs.KeyOf(Inputs.Use)} get out", new Color(.8f, .9f, 1f));
    }

    /// <summary>Riding a boat: steering, the hero in the seat, Interact to get out. True while in a boat (the rest of the frame is skipped).</summary>
    bool BoatRide(float dt)
    {
        var b = InBoat; if (b == null) return false;
        if (!IsInstanceValid(b) || b.IsQueuedForDeletion()) { InBoat = null; return false; }
        bool cap = Input.MouseMode == Input.MouseModeEnum.Captured;
        var input = cap ? Input.GetVector(Inputs.Left, Inputs.Right, Inputs.Forward, Inputs.Back) : Vector2.Zero;
        if (DevBoatThrottle != 0) input = new Vector2(input.X, -DevBoatThrottle);
        b.Steer(-input.Y, input.X, dt);
        GlobalPosition = b.Seat; Velocity = Vector3.Zero; _walkV = Vector3.Zero; _kb = Vector3.Zero; _hitKb = Vector3.Zero;
        _modelYaw = b.Yaw;
        Prompt = cap ? "[" + Inputs.KeyOf(Inputs.Use) + "] Get out of the boat" : "";
        if (Rig != null) Rig.Sprinting = Mathf.Abs(b.Speed) > 5f;
        if (cap && Input.IsActionJustPressed(Inputs.Use)) LeaveBoat();
        Animate(dt);
        return true;
    }
    /// <summary>DevCapture: rows forward at this throttle without input.</summary>
    public static float DevBoatThrottle;

    /// <summary>Out of the boat: onto the nearest dry land within 4.5 m, else into the water beside it.</summary>
    public void LeaveBoat()
    {
        var b = InBoat; InBoat = null; if (b == null || !IsInstanceValid(b)) return;
        b.Rider = null; var c = b.GlobalPosition; Vector3? best = null; float bd = 1e9f;
        for (int r = 2; r <= 4; r++)
            for (int k = 0; k < 12; k++)
            {
                float a = k * Mathf.Tau / 12f; float x = c.X + Mathf.Cos(a) * r * 1.1f, z = c.Z + Mathf.Sin(a) * r * 1.1f;
                if (Water.SurfaceAt(new Vector3(x, c.Y + .5f, z)) != null) continue;
                var g = World.OnGround(x, z, .3f); if (g.Y > c.Y + 2.5f || g.Y < c.Y - 1.5f) continue;
                float d = new Vector2(x - c.X, z - c.Z).Length(); if (d < bd) { bd = d; best = g; }
            }
        var side = new Vector3(Mathf.Cos(b.Yaw), 0, -Mathf.Sin(b.Yaw)) * 1.3f;
        Teleport(best ?? c + side + Vector3.Up * .2f, _modelYaw);
        Ui.Note(best != null ? "You step ashore" : "You slip into the water", Ui.Dim);
    }

    /// <summary>With a boat in the pack: Interact while looking at open water 2-5 m ahead sets it there.</summary>
    void BoatPlaceScan(bool captured)
    {
        if (!captured || InBoat != null || Prompt != "" || Game.D.Mats.GetValueOrDefault("boat") <= 0 || Dead) return;
        var cam = GetViewport().GetCamera3D(); var fw = cam != null ? -cam.GlobalBasis.Z : Facing(); fw.Y = 0; if (fw.LengthSquared() < .01f) return; fw = fw.Normalized();
        for (float d = 2.5f; d <= 5f; d += .5f)
        {
            var at = GlobalPosition + fw * d;
            if (Water.SurfaceAt(at + Vector3.Up * .5f) is not { } s || !Water.Floatable(at.X, at.Z, s)) continue;
            Prompt = "[" + Inputs.KeyOf(Inputs.Use) + "] Put the boat on the water";
            if (Input.IsActionJustPressed(Inputs.Use))
            {
                Game.D.Mats["boat"]--; Boat.Place(new Vector3(at.X, s, at.Z), Util.YawTo(fw.X, fw.Z)); Sfx.Play("splash", .7f, 1.1f); Game.Notify();
            }
            return;
        }
    }
}
