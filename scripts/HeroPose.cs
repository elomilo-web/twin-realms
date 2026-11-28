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
    /// <summary>Set by Player each physics frame: forward acceleration (m/s2, + speeding up), ground speed, the share the body outruns the clip
    /// (Avatar.OverSpeed), the torso's counter-twist toward a locked foe (radians) and the bend toward a low melee target (radians, up +).</summary>
    public float Accel, Speed, Over, TorsoYaw, MeleePitch;
    float _acc, _ty, _mp; int _ua1 = -1, _ua2 = -1; Quaternion _m1 = Quaternion.Identity, _m2 = Quaternion.Identity; bool _mInit;
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
            _ua1 = sk.FindBone("upperarm_l"); _ua2 = sk.FindBone("upperarm_r");
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
        // melee bend (toward a low foe, so the blade reaches it) rides on the same spine pitch as the build-mode aim
        _mp = Mathf.Lerp(_mp, MeleePitch, 1f - Mathf.Exp(-10f * dt));
        float pitch = _p + _mp;
        if (Mathf.Abs(pitch) > .003f)
        {
            var q = new Quaternion(Vector3.Right, -pitch * .5f);
            Turn(sk, _sp1, q); Turn(sk, _sp2, q);
        }
        Life(sk, dt);
        AirArms(sk, dt);
        TwoHands(sk, dt);
        TuckRoll(sk);
    }

    /// <summary>Set by Player: the free hand's grip on the main weapon (Avatar.OffGrip) and how much to use it (0..1).</summary>
    public Node3D? OffGrip; public float TwoHand;
    float _two; int _hl = -1, _hr = -1; BoneAttachment3D? _gripAtt; Node3D? _gripFor;
    /// <summary>For AnimeTool: the two-hand blend now, the last reach weight and why it last skipped.</summary>
    public string TwoDebug = ""; public static bool Debug;
    /// <summary>
    /// Both hands on the weapon (data/tuning.json game.anim.twoHand*): the library's sword clips swing with the right arm only, the left
    /// doing its own thing. When the off hand is free, two-bone IK puts it on the handle behind the sword hand (on a shaft, a forearm
    /// apart), elbow down and out, so a one-handed sword is swung with both arms and a two-handed one is actually held with two.
    /// </summary>
    void TwoHands(Skeleton3D sk, float dt)
    {
        if (_hl < 0) { _hl = sk.FindBone("hand_l"); _hr = sk.FindBone("hand_r"); }
        _two = Mathf.MoveToward(_two, OffGrip != null && IsInstanceValid(OffGrip) ? Mathf.Clamp(TwoHand, 0, 1) : 0f, dt * Tune.F("anim.twoHandBlend", 7f));
        if (Debug) TwoDebug = $"want {TwoHand:0.0} now {_two:0.00} grip {(OffGrip != null)}";
        if (_two < .01f || OffGrip == null || !IsInstanceValid(OffGrip) || _ua1 < 0 || _la < 0 || _hl < 0 || _hr < 0) return;
        if (_gripFor != OffGrip) { _gripFor = OffGrip; _gripAtt = null; for (Node? n = OffGrip.GetParent(); n != null; n = n.GetParent()) if (n is BoneAttachment3D ba) { _gripAtt = ba; break; } }
        if (_gripAtt == null || !OffGrip.IsInsideTree()) return;
        // the grip in the sword hand's bone space is rigid; placed on this frame's hand pose, in skeleton space
        var rel = _gripAtt.GlobalTransform.AffineInverse() * OffGrip.GlobalTransform;
        int ur = _ua2, lr = _ra; if (ur < 0 || lr < 0) return;
        float e = _two * _two * (3 - 2 * _two);
        var gripNow = sk.GetBoneGlobalPose(_hr) * rel.Origin;
        Vector3 SL = G(sk, _ua1); float lenL = SL.DistanceTo(G(sk, _la)) + G(sk, _la).DistanceTo(G(sk, _hl));
        // both hands meet near the middle: the clips hold the sword out to the right, beyond the left arm's reach (0.6-0.9 m from the left
        // shoulder for a 0.52 m arm), so the sword hand comes in toward the left shoulder first (the blade keeps its angle), as in a real
        // two-handed cut, then the left hand closes on the handle
        float need = SL.DistanceTo(gripNow) - lenL * Tune.F("anim.twoHandReach", .9f);
        if (need > 0)
        {
            var shift = (SL - gripNow).Normalized() * Mathf.Min(need, Tune.F("anim.twoHandPullMax", .45f)) * e;
            var gR = sk.GetBoneGlobalPose(_hr);
            ArmIK(sk, ur, lr, _hr, gR.Origin + shift, new Vector3(-Tune.F("anim.twoHandPoleOut", .55f), -1f, -.25f));
            SetGlobal(sk, _hr, gR.Basis);   // the sword keeps its angle
        }
        var target = sk.GetBoneGlobalPose(_hr) * rel.Origin;
        Vector3 W = G(sk, _hl);
        float far = SL.DistanceTo(target), w = e * Mathf.Clamp((lenL * 1.1f - far) / (lenL * .15f), 0, 1);
        if (Debug) TwoDebug += $" far {far:0.00} len {lenL:0.00} w {w:0.00}";
        if (w < .01f) return;
        var gH = sk.GetBoneGlobalPose(_hl);
        ArmIK(sk, _ua1, _la, _hl, W.Lerp(target, w), new Vector3(Tune.F("anim.twoHandPoleOut", .55f), -1f, -.25f));
        // the hand wraps the handle the same way round as the sword hand (blended in), so the two fists stack on the grip
        var cur = sk.GetBoneGlobalPose(_hl).Basis.Orthonormalized();
        var want = (sk.GetBoneGlobalPose(_hr).Basis * new Basis(new Vector3(0, 0, 1), Mathf.Pi)).Orthonormalized();
        SetGlobal(sk, _hl, new Basis(new Quaternion(cur).Slerp(new Quaternion(want), w * Tune.F("anim.twoHandWrap", .6f))));
    }

    /// <summary>Two-bone arm IK in skeleton space: upper arm and forearm turn so the hand reaches T, the elbow toward pole (a direction).</summary>
    void ArmIK(Skeleton3D sk, int up, int low, int hand, Vector3 T, Vector3 pole)
    {
        var gU = sk.GetBoneGlobalPose(up); var gL = sk.GetBoneGlobalPose(low);
        Vector3 S = gU.Origin, E = gL.Origin, W = sk.GetBoneGlobalPose(hand).Origin; float a = S.DistanceTo(E), b = E.DistanceTo(W), len = a + b;
        if (a < 1e-4f || b < 1e-4f) return;
        var toT = T - S; if (toT.LengthSquared() < 1e-10f) return;
        float d = Mathf.Clamp(toT.Length(), Mathf.Abs(a - b) + len * .001f, len * .999f); var dir = toT.Normalized();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1), sinA = Mathf.Sqrt(1 - cosA * cosA);
        var bend = pole - dir * pole.Dot(dir); if (bend.LengthSquared() < 1e-8f) bend = (E - S) - dir * (E - S).Dot(dir); if (bend.LengthSquared() < 1e-8f) return; bend = bend.Normalized();
        Vector3 E2 = S + dir * a * cosA + bend * a * sinA, W2 = S + dir * d;
        var q1 = Rot(E - S, E2 - S); var q2 = Rot(q1 * (W - E), W2 - E2);
        SetGlobal(sk, up, new Basis(q1) * gU.Basis); SetGlobal(sk, low, new Basis(q2 * q1) * gL.Basis);
    }
    /// <summary>Set by Player: 1 while airborne from a jump or a fall (not flying, swimming, rolling or attacking).</summary>
    public float Air;
    /// <summary>Set by Player: a melee weapon is in the right hand (its arm swings less in the run).</summary>
    public bool Armed;
    /// <summary>Set by Player: the roll's progress 0..1, or below 0 when not rolling.</summary>
    public float Roll = -1f;
    float _air; int _la = -1, _ra = -1, _sp3 = -1, _lowL = -1, _lowR = -1; bool _init2;
    void Init2(Skeleton3D sk)
    {
        if (_init2) return; _init2 = true;
        _la = sk.FindBone("lowerarm_l"); _ra = sk.FindBone("lowerarm_r"); _sp3 = sk.FindBone("spine_03");
        _lowL = _la; _lowR = _ra;
    }
    Vector3 G(Skeleton3D sk, int b) => b >= 0 ? sk.GetBoneGlobalPose(b).Origin : Vector3.Zero;

    /// <summary>
    /// The library's jump / fall clip holds both arms straight out to the sides (about 85 deg from hanging: it read as a T-pose in the air).
    /// While airborne each upper arm is brought down to data/tuning.json game.anim.airArmMax degrees (default 45), eased in and out.
    /// </summary>
    void AirArms(Skeleton3D sk, float dt)
    {
        Init2(sk);
        _air = Mathf.MoveToward(_air, Mathf.Clamp(Air, 0, 1), dt * 6f);
        if (_air < .01f) return;
        float maxUp = Mathf.DegToRad(Tune.F("anim.airArmMax", 45f)), e = _air * _air * (3 - 2 * _air);
        foreach (var (up, low) in new[] { (_ua1, _la), (_ua2, _ra) })
        {
            if (up < 0 || low < 0) continue;
            var d = G(sk, low) - G(sk, up); if (d.LengthSquared() < 1e-8f) continue; d = d.Normalized();
            float elev = d.AngleTo(Vector3.Down); if (elev <= maxUp) continue;
            var axis = d.Cross(Vector3.Down); if (axis.LengthSquared() < 1e-8f) continue;
            Turn(sk, up, new Quaternion(axis.Normalized(), (elev - maxUp) * e));
        }
    }

    static float Smooth(float a, float b, float x) { float t = Mathf.Clamp((x - a) / (b - a), 0, 1); return t * t * (3 - 2 * t); }
    /// <summary>
    /// The roll (data/tuning.json game.anim.roll*): the library's only rolls (Roll, Roll_RM) are dive rolls (a flat launch, the hands,
    /// a flip over the head), so the roll is built here over a crouch: the back curls, knees come to the chest, arms tuck in, and the
    /// whole ball turns once forward about its own centre, resting on the ground, then opens back out. Skeleton space (faces +Z).
    /// </summary>
    void TuckRoll(Skeleton3D sk)
    {
        if (Roll < 0 || _pel < 0) return; Init2(sk);
        float u = Mathf.Clamp(Roll, 0, 1);
        float w = Smooth(0f, Tune.F("anim.rollTuckIn", .16f), u) * (1f - Smooth(Tune.F("anim.rollOpen", .74f), 1f, u));
        float th = Mathf.Tau * Smooth(Tune.F("anim.rollTurnFrom", .08f), Tune.F("anim.rollTurnTo", .8f), u);
        if (w < .001f) return;
        // the tuck
        float curl = Tune.F("anim.rollCurl", 1.05f) * w;
        Turn(sk, _sp1, new Quaternion(Vector3.Right, curl * .35f)); Turn(sk, _sp2, new Quaternion(Vector3.Right, curl * .35f)); Turn(sk, _sp3, new Quaternion(Vector3.Right, curl * .3f));
        Turn(sk, _head, new Quaternion(Vector3.Right, Tune.F("anim.rollHead", .5f) * w));
        float knee = Tune.F("anim.rollKnees", 1.0f) * w, shin = Tune.F("anim.rollShins", .9f) * w, arm = Tune.F("anim.rollArms", .7f) * w;
        foreach (var t in new[] { _tl, _tr }) Turn(sk, t, new Quaternion(Vector3.Right, -knee));
        foreach (var c in new[] { _cl, _cr }) Turn(sk, c, new Quaternion(Vector3.Right, shin));
        foreach (var a in new[] { _ua1, _ua2 }) Turn(sk, a, new Quaternion(Vector3.Right, -arm));
        foreach (var a in new[] { _lowL, _lowR }) Turn(sk, a, new Quaternion(Vector3.Right, -arm * .8f));
        // the turn: about the ball's centre, which rests a radius above the ground
        float hip = Mathf.Max(.01f, sk.GetBoneGlobalRest(_pel).Origin.Y);
        var P = G(sk, _pel); var C0 = (P + G(sk, _sp3 >= 0 ? _sp3 : _sp2) + G(sk, _cl) + G(sk, _cr)) / 4f;
        var q = new Quaternion(Vector3.Right, th);
        Turn(sk, _pel, q);
        var Cn = P + q * (C0 - P); var T = new Vector3(C0.X, hip * Tune.F("anim.rollRadius", .5f), C0.Z);
        int par = sk.GetBoneParent(_pel); var pb = par >= 0 ? sk.GetBoneGlobalPose(par).Basis : Basis.Identity;
        sk.SetBonePosePosition(_pel, sk.GetBonePosePosition(_pel) + pb.Inverse() * ((T - Cn) * w));
    }

    /// <summary>
    /// Life on top of the run (data/feel.json "life"): the back leans into acceleration (forward when speeding up, back when stopping) while
    /// the head counters most of it to stay level; the torso turns toward a locked foe while the legs follow the step (lock-on strafing);
    /// the arms swing wider when the body moves faster than the clip's top rate (the clip's own swing is amplified around its mean pose).
    /// </summary>
    void Life(Skeleton3D sk, float dt)
    {
        Feel.Load();
        float accK = Tune.F("life.accelLean", .028f), accMax = Tune.F("life.accelLeanMax", .16f);
        _acc = Mathf.Lerp(_acc, Mathf.Clamp(Accel * accK, -accMax, accMax), 1f - Mathf.Exp(-Tune.F("life.accelSmooth", 6f) * dt));
        if (Mathf.Abs(_acc) > .002f)
        {
            Turn(sk, _sp1, new Quaternion(Vector3.Right, _acc * .6f)); Turn(sk, _sp2, new Quaternion(Vector3.Right, _acc * .4f));
            Turn(sk, _head, new Quaternion(Vector3.Right, -_acc * Tune.F("life.headStab", .75f)));
        }
        _ty = Mathf.Lerp(_ty, TorsoYaw, 1f - Mathf.Exp(-10f * dt));
        if (Mathf.Abs(_ty) > .003f)
        {   // the hips follow the legs, the chest turns back toward the foe (spread over the spine), the head finishes the turn
            var q = new Quaternion(Vector3.Up, _ty * .35f); Turn(sk, _sp1, q); Turn(sk, _sp2, q); Turn(sk, _head, new Quaternion(Vector3.Up, _ty * .3f));
        }
        float amp = Mathf.Clamp(Over, 0, 1) * Tune.F("life.armSwingExtra", .35f);
        foreach (var (b, first) in new[] { (_ua1, true), (_ua2, false) })
        {
            if (b < 0) continue; var cur = sk.GetBonePoseRotation(b);
            if (!_mInit) { _m1 = first ? cur : _m1; _m2 = first ? _m2 : cur; }
            ref var mean = ref (first ? ref _m1 : ref _m2);
            mean = mean.Slerp(cur, 1f - Mathf.Exp(-2f * dt)).Normalized();
            // the sword arm swings less while armed: the clips pump an empty fist, and a blade held at a right angle to the forearm
            // pointed straight up, then behind, then at the ground on every stride (tuning life.armedSwingDamp)
            float k = (1 + amp) * (!first && Armed ? 1f - Tune.F("life.armedSwingDamp", .45f) : 1f);
            if (Mathf.Abs(k - 1f) < .01f || Speed < .5f) continue;
            var dev = (mean.Inverse() * cur).Normalized(); float ang = dev.GetAngle(); if (ang < .001f || ang > 1.2f) continue;
            sk.SetBonePoseRotation(b, (mean * new Quaternion(dev.GetAxis().Normalized(), ang * k)).Normalized());
        }
        _mInit = true;
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
