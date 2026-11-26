using Godot;

namespace TwinRealms;

/// <summary>
/// The swimming pose (F-20). The animation libraries have no swim clip, so, like the bow (AvatarBow.cs), it is a procedural override
/// on the skeleton over the idle clip: a breaststroke while swimming along (the body lies forward, Player.SwimTilt), arms sculling and
/// legs slowly kicking while treading water upright. Player.AnimateAvatar calls SwimPose every frame the hero swims; it fades out after.
/// </summary>
public partial class Avatar
{
    SwimArms? _swim;
    /// <summary>Holds the swim pose this frame: phase advances with the stroke, tilt 0 = treading water upright .. 1 = swimming along.</summary>
    public void SwimPose(float phase, float tilt)
    {
        if (_sk == null) return;
        if (_swim == null) { _swim = new SwimArms { Name = "SwimArms" }; _sk.AddChild(_swim); }
        _swim.SetMs = Time.GetTicksMsec(); _swim.Phase = phase; _swim.Tilt = Mathf.Clamp(tilt, 0, 1);
    }
}

public partial class SwimArms : SkeletonModifier3D
{
    public ulong SetMs; public float Phase, Tilt;
    float _w; bool _init;
    int _spine, _head, _ul, _ll, _hl, _ur, _lr, _hr, _tl, _tr;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton(); if (sk == null) return;
        if (!_init)
        {
            _init = true; _spine = sk.FindBone("spine_01"); if (_spine < 0) _spine = sk.FindBone("pelvis"); _head = sk.FindBone("Head");
            _ul = sk.FindBone("upperarm_l"); _ll = sk.FindBone("lowerarm_l"); _hl = sk.FindBone("hand_l");
            _ur = sk.FindBone("upperarm_r"); _lr = sk.FindBone("lowerarm_r"); _hr = sk.FindBone("hand_r");
            _tl = sk.FindBone("thigh_l"); _tr = sk.FindBone("thigh_r");
        }
        float dt = Mathf.Clamp((float)delta, .0001f, .1f);
        bool on = Time.GetTicksMsec() - SetMs < 150;
        _w = Mathf.MoveToward(_w, on ? 1f : 0f, dt / .25f);
        if (_w <= .001f || _spine < 0 || _head < 0 || _ul < 0 || _ll < 0 || _hl < 0 || _ur < 0 || _lr < 0 || _hr < 0) return;
        float w = _w * _w * (3 - 2 * _w);
        // the body's own frame from its bones (skeleton space): up the spine, right across the shoulders, forward = up x right
        var Sl = sk.GetBoneGlobalPose(_ul).Origin; var Sr = sk.GetBoneGlobalPose(_ur).Origin;
        var R = (Sr - Sl).Normalized(); var U = sk.GetBoneGlobalPose(_head).Origin - sk.GetBoneGlobalPose(_spine).Origin;
        U = (U - R * U.Dot(R)).Normalized(); var F = U.Cross(R).Normalized();
        float L = (sk.GetBoneGlobalPose(_ll).Origin - Sl).Length() + (sk.GetBoneGlobalPose(_hl).Origin - sk.GetBoneGlobalPose(_ll).Origin).Length();
        float c = Mathf.PosMod(Phase, 1f), t = Tilt;
        Vector3 Stroke(float sg)
        {
            Vector3 glide = U * .95f + R * sg * .12f, wide = U * .55f + R * sg * .78f + F * .1f, chest = F * .45f + U * .05f + R * sg * .15f;
            float e(float x) => x * x * (3 - 2 * x);
            if (c < .35f) return glide.Lerp(wide, e(c / .35f));
            if (c < .6f) return wide.Lerp(chest, e((c - .35f) / .25f));
            return chest.Lerp(glide, e((c - .6f) / .4f));
        }
        Vector3 Tread(float sg) => F * .6f - U * .3f + R * sg * (.38f + .18f * Mathf.Sin(c * Mathf.Tau + (sg > 0 ? 0 : Mathf.Pi)));
        foreach (float sg in new[] { 1f, -1f })
        {
            var S = sg > 0 ? Sr : Sl; var off = Tread(sg).Lerp(Stroke(sg), t);
            var pole = (R * sg * .9f - U * .6f + F * .2f * (1 - t)).Normalized();
            if (sg > 0) ArmPose.Solve(sk, _ur, _lr, _hr, S + off * L, pole, w, null);
            else ArmPose.Solve(sk, _ul, _ll, _hl, S + off * L, pole, w, null);
        }
        // legs: a flutter kick along, a slow pedal while treading
        float kick = Mathf.Sin(Phase * Mathf.Tau * (t > .5f ? 2f : 1f)) * Mathf.Lerp(.25f, .35f, t) * w;
        if (_tl >= 0) ArmPose.RotGlobal(sk, _tl, new Quaternion(R, kick));
        if (_tr >= 0) ArmPose.RotGlobal(sk, _tr, new Quaternion(R, -kick));
    }
}
