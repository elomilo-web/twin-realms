using Godot;

namespace TwinRealms;

/// <summary>
/// Two procedural poses laid over the hero's clips (a skeleton modifier, added last so it runs after the look-at and the foot planting):
/// - Crouch (sneaking, after-focus A-10): the hips sink (data/life.json sneak.crouchDrop, a share of the hip height), both legs are bent
///   back to where the clip put the feet (two-bone IK, knees forward, so the walk still steps), the back leans forward and the head
///   lifts to keep looking ahead. The animation libraries have no crouch clips in the baked set, so this is the crouch.
/// - Aim pitch (build mode, after-focus A-01): while the pickaxe swings the back bends up or down towards the aimed block, so the
///   blow goes at the block under the reticle and not at a fixed spot in front of the chest.
/// Skeleton space: the model faces +Z, +Y is up (as HeadLook in Avatar.cs).
/// </summary>
public partial class HeroPose : SkeletonModifier3D
{
    /// <summary>Goals, set by Player every physics frame: crouch 0..1, and the swing's pitch in radians (up positive).</summary>
    public float Crouch, AimPitch;
    public float Drop = .26f, Lean = .3f;
    float _c, _p; bool _init;
    int _pel = -1, _sp1 = -1, _sp2 = -1, _head = -1, _tl = -1, _cl = -1, _fl = -1, _tr = -1, _cr = -1, _fr = -1;
    /// <summary>How far the crouch is in (0..1), for tests.</summary>
    public float CrouchNow => _c;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton(); if (sk == null) return;
        if (!_init)
        {
            _init = true; _pel = sk.FindBone("pelvis"); _sp1 = sk.FindBone("spine_01"); _sp2 = sk.FindBone("spine_02"); _head = sk.FindBone("Head");
            _tl = sk.FindBone("thigh_l"); _cl = sk.FindBone("calf_l"); _fl = sk.FindBone("foot_l"); _tr = sk.FindBone("thigh_r"); _cr = sk.FindBone("calf_r"); _fr = sk.FindBone("foot_r");
        }
        float dt = Mathf.Clamp((float)delta, .0001f, .1f);
        _c = Mathf.MoveToward(_c, Mathf.Clamp(Crouch, 0, 1), dt * 5f);
        _p = Mathf.Lerp(_p, AimPitch, 1f - Mathf.Exp(-14f * dt));
        if (_c > .001f && _pel >= 0)
        {
            float e = _c * _c * (3f - 2f * _c);
            bool legs = _tl >= 0 && _cl >= 0 && _fl >= 0 && _tr >= 0 && _cr >= 0 && _fr >= 0;
            Transform3D footL = legs ? sk.GetBoneGlobalPose(_fl) : Transform3D.Identity, footR = legs ? sk.GetBoneGlobalPose(_fr) : Transform3D.Identity;
            if (legs)
            {
                // hips down by a share of their standing height (so it fits any body size), a little back to stay over the feet
                float hip = Mathf.Max(.01f, sk.GetBoneGlobalRest(_pel).Origin.Y);
                int par = sk.GetBoneParent(_pel); var pb = par >= 0 ? sk.GetBoneGlobalPose(par).Basis : Basis.Identity;
                sk.SetBonePosePosition(_pel, sk.GetBonePosePosition(_pel) + pb.Inverse() * new Vector3(0, -Drop * hip * e, -.05f * hip * e));
                Leg(sk, _tl, _cl, _fl, footL); Leg(sk, _tr, _cr, _fr, footR);
            }
            Turn(sk, _sp1, new Quaternion(Vector3.Right, Lean * e));
            Turn(sk, _head, new Quaternion(Vector3.Right, -Lean * .75f * e));
        }
        if (Mathf.Abs(_p) > .003f)
        {
            var q = new Quaternion(Vector3.Right, -_p * .5f);
            Turn(sk, _sp1, q); Turn(sk, _sp2, q);
        }
    }

    /// <summary>Pre-multiplies a skeleton-space rotation onto a bone's current global pose (its children follow).</summary>
    static void Turn(Skeleton3D sk, int b, Quaternion q)
    {
        if (b < 0) return;
        int par = sk.GetBoneParent(b); var pg = par >= 0 ? sk.GetBoneGlobalPose(par).Basis.Orthonormalized() : Basis.Identity;
        var g = sk.GetBoneGlobalPose(b).Basis.Orthonormalized();
        sk.SetBonePoseRotation(b, (pg.Inverse() * (new Basis(q) * g)).GetRotationQuaternion().Normalized());
    }

    /// <summary>Two-bone leg IK in skeleton space: thigh and shin turn so the ankle is back where the clip had it, the knee forward; the foot keeps the clip's angle.</summary>
    static void Leg(Skeleton3D sk, int thigh, int calf, int foot, Transform3D footWas)
    {
        var gH = sk.GetBoneGlobalPose(thigh); var gK = sk.GetBoneGlobalPose(calf); var gA = sk.GetBoneGlobalPose(foot);
        Vector3 H = gH.Origin, K = gK.Origin, A = gA.Origin, T = footWas.Origin; float a = H.DistanceTo(K), b = K.DistanceTo(A), len = a + b;
        if (a < len * .05f || b < len * .05f) return;
        var toT = T - H; if (toT.LengthSquared() < 1e-10f) return;
        float d = Mathf.Clamp(toT.Length(), Mathf.Abs(a - b) + len * .001f, len * .999f); var dir = toT.Normalized();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1), sinA = Mathf.Sqrt(1 - cosA * cosA);
        var pole = K + Vector3.Back * len - H;   // ahead of the knee (the model faces +Z)
        var bend = pole - dir * pole.Dot(dir); if (bend.LengthSquared() < 1e-10f) bend = (K - H) - dir * (K - H).Dot(dir); if (bend.LengthSquared() < 1e-10f) return; bend = bend.Normalized();
        Vector3 K2 = H + dir * a * cosA + bend * a * sinA, A2 = H + dir * d;
        var q1 = Rot(K - H, K2 - H); var q2 = Rot(q1 * (A - K), A2 - K2);
        SetGlobal(sk, thigh, new Basis(q1) * gH.Basis); SetGlobal(sk, calf, new Basis(q2 * q1) * gK.Basis); SetGlobal(sk, foot, footWas.Basis);
    }
    static void SetGlobal(Skeleton3D sk, int bone, Basis global)
    {
        int par = sk.GetBoneParent(bone); var pg = par >= 0 ? sk.GetBoneGlobalPose(par).Basis : Basis.Identity;
        sk.SetBonePoseRotation(bone, (pg.Inverse() * global).Orthonormalized().GetRotationQuaternion().Normalized());
    }
    static Quaternion Rot(Vector3 from, Vector3 to)
    {
        from = from.Normalized(); to = to.Normalized(); float dt = Mathf.Clamp(from.Dot(to), -1, 1);
        if (dt > .99999f) return Quaternion.Identity;
        var axis = from.Cross(to); if (axis.LengthSquared() < 1e-8f) axis = from.Cross(Vector3.Right).LengthSquared() > 1e-6f ? from.Cross(Vector3.Right) : from.Cross(Vector3.Up);
        return new Quaternion(axis.Normalized(), Mathf.Acos(dt));
    }
}
