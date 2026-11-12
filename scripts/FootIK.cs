using Godot;

namespace TwinRealms;

/// <summary>
/// Feet on the ground on slopes, steps and rocks (the clips are made for flat ground).
/// Runs as a skeleton modifier: it reads the animated pose, finds the real ground under each foot (heel and toes),
/// aims each foot so its lowest part keeps the animation's lift above that ground, tilts planted feet to the slope, lowers the hips
/// when a foot needs to reach down (the downhill leg), and bends the legs itself (two-bone IK in Solve). Off in the air, while mounted and when Enabled is false.
/// Numbers: data/feel.json "footIK".
/// </summary>
public partial class FootPlant : SkeletonModifier3D
{
    public Node3D TargetL = null!, TargetR = null!, PoleL = null!, PoleR = null!, Root = null!;
    public TwoBoneIK3D Ik = null!; public Rid Exclude; public bool Enabled = true;
    public float MaxDrop = .4f, MaxLift = .5f, Smooth = 14f, Plant = .09f, BobDamp = .6f;
    int _pel = -1, _fl, _fr, _cl, _cr, _tl, _tr, _bl = -1, _br = -1; float _drop, _w, _ankle = -1, _ball = -1, _pelAvg = -99; bool _upL, _upR;
    /// <summary>A foot touched down in the animation (where, on the ground). Footstep sounds hang off this.</summary>
    public System.Action<Vector3>? Stepped;
    /// <summary>For the test bot: where the feet were aimed last frame.</summary>
    public Vector3 AimL => TargetL.GlobalPosition; public Vector3 AimR => TargetR.GlobalPosition;
    /// <summary>For the test bot: where the heels and toes really ended up after the legs were bent (world), last frame.</summary>
    public Vector3 HeelL, ToeL, HeelR, ToeR; public float AnkleRest => _ankle; public float BallRest => _ball;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton(); if (sk == null || Root == null) return;
        if (_pel < 0) { _pel = sk.FindBone("pelvis"); _fl = sk.FindBone("foot_l"); _fr = sk.FindBone("foot_r"); _cl = sk.FindBone("calf_l"); _cr = sk.FindBone("calf_r"); _tl = sk.FindBone("thigh_l"); _tr = sk.FindBone("thigh_r"); _bl = sk.FindBone("ball_l"); _br = sk.FindBone("ball_r"); }
        if (_pel < 0 || _fl < 0 || _fr < 0) return;
        float dt = (float)delta;
        _w = Mathf.MoveToward(_w, Enabled ? 1f : 0f, dt * 6f);   // fade in after landing, out when jumping
        if (Ik != null) Ik.Influence = 0; if (_w <= 0.001f) { _drop = 0; return; }

        var sg = sk.GlobalTransform; float baseY = Root.GlobalPosition.Y;
        var space = sk.GetWorld3D().DirectSpaceState;
        Vector3 nL = Vector3.Up, nR = Vector3.Up, lastN = Vector3.Up;
        float Ground(Vector3 at)
        {
            var q = PhysicsRayQueryParameters3D.Create(at + Vector3.Up * MaxLift * 2f, at + Vector3.Down * (MaxDrop + .6f), 1);
            if (Exclude.IsValid) q.Exclude = new Godot.Collections.Array<Rid> { Exclude };
            var h = space.IntersectRay(q); if (h.Count == 0) { lastN = Vector3.Up; return baseY; }
            lastN = ((Vector3)h["normal"]).Normalized(); return ((Vector3)h["position"]).Y;
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
        if (_ankle < 0) { var rest = sg * sk.GetBoneGlobalRest(_fl); _ankle = Mathf.Clamp(rest.Origin.Y - baseY, .03f, .2f); _ball = _bl >= 0 ? Mathf.Clamp((sg * sk.GetBoneGlobalRest(_bl)).Origin.Y - baseY, .0f, .12f) : -1; }
        // the ball of each foot (toes): a foot can stand on its toes with the heel raised; that foot touches the floor with its ball
        Vector3? BallW(int b) => b >= 0 && _ball >= 0 ? (sg * sk.GetBoneGlobalPose(b)).Origin + new Vector3(0, bob, 0) : null;
        var bl = BallW(_bl); var br = BallW(_br);
        // each foot: the real ground under it and its animated lift off the flat floor. A foot within Plant of the floor is pinned flat
        // on the ground (the clips wobble a few cm and their stance feet hover); higher up it keeps the lift, less Plant (blended in).
        // the ground under the heel and under the ball: the higher one (a foot on a slope or a step must not dip its toes in)
        float G(Transform3D f, Vector3? ball, out Vector3 n)
        {
            float a = Ground(f.Origin); var na = lastN; float b = ball is { } bp ? Ground(bp) : -999f; var nb = lastN;
            n = (b > -998f ? (na + nb) : na).Normalized(); if (n.Y < .5f) n = Vector3.Up;   // ignore walls and cliffs
            return Mathf.Clamp(Mathf.Max(a, b) - baseY, -MaxDrop, MaxLift);
        }
        float grL = G(fl, bl, out nL), grR = G(fr, br, out nR);
        // the lowest part of the foot decides how far it is off the floor (heel: ankle above its rest height; toes: ball above its rest height).
        // Within Plant of the floor that part is pinned to the ground and the ankle keeps its height above it (a raised heel stays raised).
        float Target(Transform3D f, Vector3? ball, float gr)
        {
            float l = f.Origin.Y - baseY - _ankle, lb = ball is { } b ? b.Y - baseY - _ball : l, c = Mathf.Min(l, lb);
            float keep = c <= Plant ? 0 : c - Plant * Mathf.Clamp(1.5f - c / (Plant * 4f), 0, 1);
            return baseY + gr + _ankle + (l - c) + keep;
        }
        float tyL = Target(fl, bl, grL), tyR = Target(fr, br, grR);
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

        // a planted foot lies flat on the ground: tilt it from flat to the slope under it (a lifted foot keeps the animation's angle)
        Basis Tilt(Transform3D f, Vector3? ball, Vector3 n)
        {
            float l = f.Origin.Y - baseY - _ankle, lb = ball is { } b ? b.Y - baseY - _ball : l, c = Mathf.Min(l, lb);
            float k = Mathf.Clamp(1f - c / (Plant * 3f), 0, 1); if (k <= 0 || n.IsEqualApprox(Vector3.Up)) return f.Basis;
            var q = new Quaternion(Vector3.Up, n); return new Basis(Quaternion.Identity.Slerp(q, k)) * f.Basis;
        }
        TargetL.GlobalTransform = new Transform3D(Tilt(fl, bl, nL), fl.Origin + new Vector3(0, gl, 0));
        TargetR.GlobalTransform = new Transform3D(Tilt(fr, br, nR), fr.Origin + new Vector3(0, gr, 0));
        // knees bend forward: poles ahead of each knee in the body's facing
        var fw = -Root.GlobalBasis.Z.Normalized();
        PoleL.GlobalPosition = (sg * sk.GetBoneGlobalPose(_cl)).Origin + fw * .6f;
        PoleR.GlobalPosition = (sg * sk.GetBoneGlobalPose(_cr)).Origin + fw * .6f;
        // bend the legs ourselves (Godot's TwoBoneIK3D stopped moving the bones in 4.7): each foot reaches its target
        Solve(sk, sg, _tl, _cl, _fl, TargetL.GlobalTransform, PoleL.GlobalPosition);
        Solve(sk, sg, _tr, _cr, _fr, TargetR.GlobalTransform, PoleR.GlobalPosition);
        HeelL = sg * sk.GetBoneGlobalPose(_fl).Origin; HeelR = sg * sk.GetBoneGlobalPose(_fr).Origin;
        if (_bl >= 0) ToeL = sg * sk.GetBoneGlobalPose(_bl).Origin; if (_br >= 0) ToeR = sg * sk.GetBoneGlobalPose(_br).Origin;
    }

    /// <summary>
    /// Two-bone leg IK in skeleton space: the knee goes where the thigh and shin lengths meet on the side of the pole, the thigh and the shin turn
    /// to reach it, and the foot takes the target's rotation. Blended in by the fade weight. Bones keep their lengths.
    /// </summary>
    void Solve(Skeleton3D sk, Transform3D sg, int thigh, int calf, int foot, Transform3D targetW, Vector3 poleW)
    {
        if (thigh < 0 || calf < 0 || foot < 0) return;
        var inv = sg.AffineInverse(); var T = inv * targetW.Origin; var P = inv * poleW;
        var gH = sk.GetBoneGlobalPose(thigh); var gK = sk.GetBoneGlobalPose(calf); var gA = sk.GetBoneGlobalPose(foot);
        Vector3 H = gH.Origin, K = gK.Origin, A = gA.Origin; float a = H.DistanceTo(K), b = K.DistanceTo(A); if (a < .01f || b < .01f) return;
        var toT = T - H; float d = Mathf.Clamp(toT.Length(), Mathf.Abs(a - b) + .001f, a + b - .001f); var dir = toT.Normalized();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1), sinA = Mathf.Sqrt(1 - cosA * cosA);
        var bend = (P - H) - dir * (P - H).Dot(dir); if (bend.LengthSquared() < 1e-6f) bend = (K - H) - dir * (K - H).Dot(dir); if (bend.LengthSquared() < 1e-6f) return; bend = bend.Normalized();
        Vector3 K2 = H + dir * a * cosA + bend * a * sinA, A2 = H + dir * d;
        // thigh: turn so the knee lands at K2
        var q1 = Rot(K - H, K2 - H); var thighG = new Basis(q1) * gH.Basis;
        // shin: carried by the thigh, then turned so the ankle lands at A2
        Vector3 kAfter = H + q1 * (K - H); Vector3 aAfter = kAfter + q1 * (A - K); var q2 = Rot(aAfter - kAfter, A2 - K2);
        var calfG = new Basis(q2 * q1) * gK.Basis;
        var footG = (inv.Basis * targetW.Basis).Orthonormalized().Scaled(gA.Basis.Scale);
        void Set(int bone, Basis global, Basis animGlobal)
        {
            int par = sk.GetBoneParent(bone); var pg = par >= 0 ? sk.GetBoneGlobalPose(par).Basis : Basis.Identity;
            var want = (pg.Inverse() * global).GetRotationQuaternion(); var cur = sk.GetBonePoseRotation(bone);
            sk.SetBonePoseRotation(bone, cur.Slerp(want, _w));
        }
        Set(thigh, thighG, gH.Basis); Set(calf, calfG, gK.Basis); Set(foot, footG, gA.Basis);
    }
    static Quaternion Rot(Vector3 from, Vector3 to)
    {
        from = from.Normalized(); to = to.Normalized(); float dt = Mathf.Clamp(from.Dot(to), -1, 1);
        if (dt > .99999f) return Quaternion.Identity;
        var axis = from.Cross(to); if (axis.LengthSquared() < 1e-8f) axis = from.Cross(Vector3.Right).LengthSquared() > 1e-6f ? from.Cross(Vector3.Right) : from.Cross(Vector3.Up);
        return new Quaternion(axis.Normalized(), Mathf.Acos(dt));
    }

    /// <summary>Sets up foot IK on an avatar's skeleton (call once after Avatar.Build). exclude = the character's own collider.</summary>
    public static FootPlant? Attach(Avatar av, Rid exclude)
    {
        var sk = av.Skel; if (sk == null || sk.FindBone("thigh_l") < 0 || sk.FindBone("calf_l") < 0) return null;
        Node3D Marker(string n) { var m = new Node3D { Name = n, TopLevel = true }; av.AddChild(m); return m; }
        var fp = new FootPlant { Name = "FootPlant", Root = av, Exclude = exclude, TargetL = Marker("IkFootL"), TargetR = Marker("IkFootR"), PoleL = Marker("IkPoleL"), PoleR = Marker("IkPoleR") };
        var ik = new TwoBoneIK3D { Name = "FootIK", Active = false };   // kept for its settings; the legs are bent by FootPlant.Solve
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
