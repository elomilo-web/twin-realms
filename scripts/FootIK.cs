using Godot;

namespace TwinRealms;

/// <summary>
/// Feet on the ground on slopes, steps and rocks (the clips are made for flat ground).
/// Runs as a skeleton modifier before a TwoBoneIK3D: it reads the animated pose, finds the real ground under each foot,
/// moves the IK targets so each foot keeps the animation's lift above that ground, and lowers the hips when a foot needs
/// to reach down (the downhill leg). The IK then bends the legs. Off in the air, while mounted and when Enabled is false.
/// Numbers: data/feel.json "footIK".
/// </summary>
public partial class FootPlant : SkeletonModifier3D
{
    public Node3D TargetL = null!, TargetR = null!, PoleL = null!, PoleR = null!, Root = null!;
    public TwoBoneIK3D Ik = null!; public Rid Exclude; public bool Enabled = true;
    public float MaxDrop = .4f, MaxLift = .5f, Smooth = 14f, Plant = .09f, BobDamp = .6f;
    int _pel = -1, _fl, _fr, _cl, _cr; float _drop, _w, _ankle = -1, _pelAvg = -99; bool _upL, _upR;
    /// <summary>A foot touched down in the animation (where, on the ground). Footstep sounds hang off this.</summary>
    public System.Action<Vector3>? Stepped;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton(); if (sk == null || Root == null) return;
        if (_pel < 0) { _pel = sk.FindBone("pelvis"); _fl = sk.FindBone("foot_l"); _fr = sk.FindBone("foot_r"); _cl = sk.FindBone("calf_l"); _cr = sk.FindBone("calf_r"); }
        if (_pel < 0 || _fl < 0 || _fr < 0) return;
        float dt = (float)delta;
        _w = Mathf.MoveToward(_w, Enabled ? 1f : 0f, dt * 6f);   // fade in after landing, out when jumping
        Ik.Influence = _w; if (_w <= 0.001f) { _drop = 0; return; }

        var sg = sk.GlobalTransform; float baseY = Root.GlobalPosition.Y;
        var space = sk.GetWorld3D().DirectSpaceState;
        float Ground(Vector3 at)
        {
            var q = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * MaxLift * 2f, at + Vector3.Down * (MaxDrop + .6f), 1);
            if (Exclude.IsValid) q.Exclude = new Godot.Collections.Array<Rid> { Exclude };
            var h = space.IntersectRay(q); return h.Count > 0 ? ((Vector3)h["position"]).Y : baseY;
        }
        Transform3D FootW(int b) => sg * sk.GetBoneGlobalPose(b);
        var fl = FootW(_fl); var fr = FootW(_fr);
        // bounce damping: the jog clip throws the hips 22 cm up each stride (both feet leave the floor). Take most of the rise above the
        // hips' running average off the whole body, feet included, so the hero runs low and the feet come back to the ground sooner.
        float pelY = (sg * sk.GetBoneGlobalPose(_pel)).Origin.Y - baseY;
        _pelAvg = _pelAvg < -50 ? pelY : Mathf.Lerp(_pelAvg, pelY, 1f - Mathf.Exp(-2.5f * dt));
        float bob = -Mathf.Max(0, pelY - _pelAvg) * BobDamp;
        fl.Origin.Y += bob; fr.Origin.Y += bob;
        // ankle height when standing (the rest pose): a foot this close to the floor is on the floor
        if (_ankle < 0) { var rest = sg * sk.GetBoneGlobalRest(_fl); _ankle = Mathf.Clamp(rest.Origin.Y - baseY, .03f, .2f); }
        // each foot: the real ground under it and its animated lift off the flat floor. A foot within Plant of the floor is pinned flat
        // on the ground (the clips wobble a few cm and their stance feet hover); higher up it keeps the lift, less Plant (blended in).
        float grL = Mathf.Clamp(Ground(fl.Origin) - baseY, -MaxDrop, MaxLift), grR = Mathf.Clamp(Ground(fr.Origin) - baseY, -MaxDrop, MaxLift);
        float Lift(float animY) { float l = animY - baseY - _ankle; return l <= Plant ? 0 : l - Plant * Mathf.Clamp(1.5f - l / (Plant * 4f), 0, 1); }
        float tyL = baseY + grL + _ankle + Lift(fl.Origin.Y), tyR = baseY + grR + _ankle + Lift(fr.Origin.Y);
        // footfalls: a foot that was up comes down onto the floor (with a little hysteresis so a hovering foot does not chatter)
        float lL = fl.Origin.Y - baseY - _ankle, lR = fr.Origin.Y - baseY - _ankle;
        if (_upL && lL < Plant * .6f) { _upL = false; Stepped?.Invoke(new Vector3(fl.Origin.X, baseY + grL, fl.Origin.Z)); } else if (lL > Plant * 1.4f) _upL = true;
        if (_upR && lR < Plant * .6f) { _upR = false; Stepped?.Invoke(new Vector3(fr.Origin.X, baseY + grR, fr.Origin.Z)); } else if (lR > Plant * 1.4f) _upR = true;
        float gl = tyL - fl.Origin.Y, gr = tyR - fr.Origin.Y;   // how far each foot target moves from the animated foot
        // the hips go down as far as a foot must reach (never up: the capsule already stands on the highest point)
        float want = Mathf.Max(-MaxDrop, Mathf.Min(0, Mathf.Min(gl, gr)));
        _drop = Mathf.Lerp(_drop, want, 1f - Mathf.Exp(-Smooth * dt));
        var pp = sk.GetBonePosePosition(_pel); int par = sk.GetBoneParent(_pel);
        var parentBasis = (sg.Basis * (par >= 0 ? sk.GetBoneGlobalPose(par).Basis : Basis.Identity));
        sk.SetBonePosePosition(_pel, pp + parentBasis.Inverse() * new Vector3(0, (_drop + bob) * _w, 0));

        TargetL.GlobalTransform = new Transform3D(fl.Basis, fl.Origin + new Vector3(0, gl, 0));
        TargetR.GlobalTransform = new Transform3D(fr.Basis, fr.Origin + new Vector3(0, gr, 0));
        // knees bend forward: poles ahead of each knee in the body's facing
        var fw = -Root.GlobalBasis.Z.Normalized();
        PoleL.GlobalPosition = (sg * sk.GetBoneGlobalPose(_cl)).Origin + fw * .6f;
        PoleR.GlobalPosition = (sg * sk.GetBoneGlobalPose(_cr)).Origin + fw * .6f;
    }

    /// <summary>Sets up foot IK on an avatar's skeleton (call once after Avatar.Build). exclude = the character's own collider.</summary>
    public static FootPlant? Attach(Avatar av, Rid exclude)
    {
        var sk = av.Skel; if (sk == null || sk.FindBone("thigh_l") < 0 || sk.FindBone("calf_l") < 0) return null;
        Node3D Marker(string n) { var m = new Node3D { Name = n, TopLevel = true }; av.AddChild(m); return m; }
        var fp = new FootPlant { Name = "FootPlant", Root = av, Exclude = exclude, TargetL = Marker("IkFootL"), TargetR = Marker("IkFootR"), PoleL = Marker("IkPoleL"), PoleR = Marker("IkPoleR") };
        var ik = new TwoBoneIK3D { Name = "FootIK" };
        sk.AddChild(fp); sk.AddChild(ik);   // child order = process order: FootPlant reads the animation, then the IK solves
        ik.SettingCount = 2;
        foreach (var (i, s, t, p) in new[] { (0, "l", fp.TargetL, fp.PoleL), (1, "r", fp.TargetR, fp.PoleR) })
        {
            ik.SetRootBoneName(i, "thigh_" + s); ik.SetMiddleBoneName(i, "calf_" + s); ik.SetEndBoneName(i, "foot_" + s);
            ik.SetTargetNode(i, ik.GetPathTo(t)); ik.SetPoleNode(i, ik.GetPathTo(p));
        }
        fp.Ik = ik;
        using var doc = JsonUtil.Load("res://data/feel.json"); var cfg = doc.RootElement;
        if (cfg.TryGetProperty("footIK", out var f)) { fp.MaxDrop = JsonUtil.F(f, "maxDrop", fp.MaxDrop); fp.MaxLift = JsonUtil.F(f, "maxLift", fp.MaxLift); fp.Smooth = JsonUtil.F(f, "smooth", fp.Smooth); fp.Plant = JsonUtil.F(f, "plant", fp.Plant); fp.BobDamp = JsonUtil.F(f, "bobDamp", fp.BobDamp); if (f.TryGetProperty("enabled", out var en) && en.ValueKind == System.Text.Json.JsonValueKind.False) { fp.QueueFree(); ik.QueueFree(); return null; } }
        return fp;
    }
}
