using System;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Weapon-type animation data (data/avatar.json "weaponAnims") and the procedural bow pose. The animation libraries have no archery
/// clips, so the bow's raise, draw/hold, release and lower are bone overrides (two-bone IK on both arms, a chest turn) blended over the
/// clip that is playing. Player.AnimateAvatar drives it from the attack timeline every frame with BowPose.
/// </summary>
public partial class Avatar
{
    /// <summary>The "weaponAnims" entry of a weapon type, or null.</summary>
    public static JsonElement? WeaponAnim(string wt) =>
        Cfg.TryGetProperty("weaponAnims", out var wa) && wa.TryGetProperty(wt, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;

    ArmPose? _arms;
    ArmPose? Arms
    {
        get
        {
            if (_arms != null || _sk == null || WeaponAnim("bow") is not { } cfg) return _arms;
            _arms = new ArmPose { Name = "ArmPose", Av = this, C = cfg }; _sk.AddChild(_arms);   // after HeadLook: the arms follow the turned chest
            return _arms;
        }
    }
    /// <summary>The current bow phase (raise, draw, hold, release, lower or "") for logs and tests.</summary>
    public string ArmPhase => _arms?.Phase ?? "";
    /// <summary>
    /// Holds the bow pose this frame (it fades in over "raise" seconds and fades out over "lower" once calls stop).
    /// draw 0..1 = the string hand from the bow to the cheek; snap 0..1 = the release flick; aim = world direction of the shot.
    /// </summary>
    public void BowPose(float draw, float snap, Vector3 aim, string phase)
    {
        var a = Arms; if (a == null) return;
        a.SetMs = Time.GetTicksMsec(); a.Draw = Mathf.Clamp(draw, 0, 1); a.SnapT = Mathf.Clamp(snap, 0, 1); a.Aim = aim; a.Phase = phase;
    }
    /// <summary>The held bow's holder (grip) in the left hand, or null.</summary>
    internal Node3D? BowHolder()
    {
        foreach (var n in SlotNodes("weapon"))
            if (n is Node3D h && IsInstanceValid(h) && h.HasMeta("bone") && h.GetMeta("bone").AsString() == "hand_l") return h;
        return null;
    }
    /// <summary>World position of the arrow rest on the held bow (just above the grip hand), or null without a bow in the left hand.</summary>
    public Vector3? BowNock()
    {
        var h = BowHolder(); if (h == null || !h.IsInsideTree()) return null;
        float up = WeaponAnim("bow") is { } c ? JsonUtil.F(c, "nockUp", .05f) : .05f;
        return h.GlobalTransform * new Vector3(0, up, 0);
    }
}

/// <summary>Bow pose overrides on the skeleton (see Avatar.BowPose). Restores nothing: the animation rewrites the bones every frame.</summary>
public partial class ArmPose : SkeletonModifier3D
{
    public Avatar Av = null!; public JsonElement C;
    public ulong SetMs; public float Draw, SnapT; public Vector3 Aim = Vector3.Forward; public string Phase = "";
    float _w, _snap; bool _init; MeshInstance3D? _arrow;
    int _chest, _neck, _head, _ul, _ll, _hl, _ur, _lr, _hr;

    float F(string k, float d) => JsonUtil.F(C, k, d);
    Vector3 V3(string k, Vector3 d) => C.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3 ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : d;

    public override void _ProcessModificationWithDelta(double delta)
    {
        var sk = GetSkeleton(); if (sk == null || Av == null || !GodotObject.IsInstanceValid(Av)) return;
        if (!_init)
        {
            _init = true; _chest = sk.FindBone("spine_03"); _neck = sk.FindBone("neck_01"); _head = sk.FindBone("Head");
            _ul = sk.FindBone("upperarm_l"); _ll = sk.FindBone("lowerarm_l"); _hl = sk.FindBone("hand_l");
            _ur = sk.FindBone("upperarm_r"); _lr = sk.FindBone("lowerarm_r"); _hr = sk.FindBone("hand_r");
        }
        float dt = Mathf.Clamp((float)delta, 0.0001f, .1f);
        bool on = Time.GetTicksMsec() - SetMs < 120;
        _w = Mathf.MoveToward(_w, on ? 1f : 0f, dt / Mathf.Max(.02f, on ? F("raise", .2f) : F("lower", .28f)));
        _snap = Mathf.MoveToward(_snap, on ? SnapT : 0f, dt / Mathf.Max(.01f, F("snap", .06f)));
        if (!on && _w <= 0) Phase = "";
        else if (!on) Phase = "lower";
        if (_w <= .001f || _ul < 0 || _ll < 0 || _hl < 0 || _ur < 0 || _lr < 0 || _hr < 0 || _head < 0) { if (_arrow != null) _arrow.Visible = false; return; }
        float w = _w * _w * (3 - 2 * _w);

        // the hero's frame, in skeleton space
        var toSk = sk.GlobalTransform.Basis.Inverse();
        var fw = -Av.GlobalBasis.Z; fw.Y = 0; fw = fw.LengthSquared() > 1e-4f ? fw.Normalized() : Vector3.Forward;
        var rt = fw.Cross(Vector3.Up).Normalized();
        Vector3 Fs = (toSk * fw).Normalized(), Us = (toSk * Vector3.Up).Normalized(), Rs = (toSk * rt).Normalized();
        var aimW = Aim.LengthSquared() > 1e-4f ? Aim.Normalized() : fw;
        var D = (toSk * aimW).Normalized();
        Vector3 Off(Vector3 v) => Rs * v.X + Us * v.Y + Fs * v.Z;

        // chest turns so the bow shoulder leads; neck turns back so the head stays on the target
        float tw = Mathf.DegToRad(F("twist", 38f)) * w;
        if (_chest >= 0 && tw != 0) RotGlobal(sk, _chest, new Quaternion(Us, -tw));
        if (_neck >= 0 && tw != 0) RotGlobal(sk, _neck, new Quaternion(Us, tw * .85f));

        // anchor at the cheek; the bow hand sits on the aim line through it, at arm's reach
        var A = sk.GetBoneGlobalPose(_head).Origin + Off(V3("cheek", new Vector3(.05f, .05f, .1f)));
        var S = sk.GetBoneGlobalPose(_ul).Origin;
        float armL = (sk.GetBoneGlobalPose(_ll).Origin - S).Length() + (sk.GetBoneGlobalPose(_hl).Origin - sk.GetBoneGlobalPose(_ll).Origin).Length();
        float R = F("reach", .97f) * armL; var m = A - S; float md = m.Dot(D), disc = md * md - (m.LengthSquared() - R * R);
        float t = disc > 0 ? -md + Mathf.Sqrt(disc) : Mathf.Max(.3f, -md);
        var TL = A + D * t;

        // bow hand: the bow upright (top leaning out by cant), its string side toward the archer
        Basis? bowHand = null;
        if (Av.BowHolder() is { } holder)
        {
            var y = (Us - D * Us.Dot(D)).Normalized(); var outward = (-Rs - D * (-Rs).Dot(D)).Normalized();
            float cant = Mathf.DegToRad(F("cant", 8f)); y = (y * Mathf.Cos(cant) + outward * Mathf.Sin(cant)).Normalized();
            var bh = new Basis(y.Cross(D).Normalized(), y, D);
            bowHand = bh * holder.Transform.Basis.Orthonormalized().Inverse();
        }
        Solve(sk, _ul, _ll, _hl, TL, Off(V3("poleLeft", new Vector3(-.5f, -.85f, 0))), w, bowHand);

        // string hand: from the bow (nock) to the anchor, then the release flick
        var nockP = TL - D * F("nock", .16f);
        var TR = nockP.Lerp(A, Draw) + Off(V3("snapOffset", new Vector3(.1f, .02f, -.14f))) * _snap;
        Solve(sk, _ur, _lr, _hr, TR, Off(V3("poleRight", new Vector3(.45f, .2f, -1f))), w, null);

        // the nocked arrow, from the string hand along the aim, while the string is drawn
        bool show = w > .6f && Draw > .08f && _snap < .05f && on;
        if (show && _arrow == null) _arrow = MakeArrow();
        if (_arrow != null)
        {
            _arrow.Visible = show;
            if (show)
            {
                var tail = sk.GlobalTransform * sk.GetBoneGlobalPose(_hr).Origin;
                var up = Mathf.Abs(aimW.Dot(Vector3.Up)) > .98f ? Vector3.Right : Vector3.Up;
                _arrow.GlobalTransform = new Transform3D(Basis.LookingAt(aimW, up), tail + aimW * .4f);
            }
        }
    }

    MeshInstance3D MakeArrow()
    {
        var mi = new MeshInstance3D
        {
            Name = "NockedArrow", TopLevel = true, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Mesh = new CylinderMesh { TopRadius = .011f, BottomRadius = .011f, Height = .8f, RadialSegments = 5 },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.55f, .4f, .25f) }, Rotation = Vector3.Zero,
        };
        mi.Mesh = BakeAlongZ((CylinderMesh)mi.Mesh);   // the cylinder runs along Y; baked to run along -Z (LookingAt's forward)
        Av.AddChild(mi);
        return mi;
    }
    static Mesh BakeAlongZ(CylinderMesh c)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        var arr = c.GetMeshArrays(); var verts = (Vector3[])arr[(int)Mesh.ArrayType.Vertex]; var nrm = (Vector3[])arr[(int)Mesh.ArrayType.Normal]; var idx = (int[])arr[(int)Mesh.ArrayType.Index];
        var rot = new Basis(Vector3.Right, -Mathf.Pi / 2);   // +Y -> -Z
        foreach (int i in idx) { st.SetNormal(rot * nrm[i]); st.AddVertex(rot * verts[i]); }
        return st.Commit();
    }

    /// <summary>Pre-multiplies a skeleton-space rotation onto a bone's current global pose.</summary>
    internal static void RotGlobal(Skeleton3D sk, int b, Quaternion q)
    {
        int par = sk.GetBoneParent(b); var pg = par >= 0 ? sk.GetBoneGlobalPose(par).Basis.Orthonormalized() : Basis.Identity;
        var g = sk.GetBoneGlobalPose(b).Basis.Orthonormalized();
        sk.SetBonePoseRotation(b, (pg.Inverse() * (new Basis(q) * g)).GetRotationQuaternion().Normalized());
    }
    static Quaternion FromTo(Vector3 a, Vector3 b)
    {
        a = a.Normalized(); b = b.Normalized(); float d = a.Dot(b);
        if (d < -.9999f) { var ax = a.Cross(Vector3.Up); if (ax.LengthSquared() < 1e-4f) ax = a.Cross(Vector3.Right); return new Quaternion(ax.Normalized(), Mathf.Pi); }
        var c = a.Cross(b); return new Quaternion(c.X, c.Y, c.Z, 1 + d).Normalized();
    }
    static void SetLocal(Skeleton3D sk, int b, Basis local, float w)
    {
        var cur = sk.GetBonePoseRotation(b); var tgt = local.Orthonormalized().GetRotationQuaternion().Normalized();
        sk.SetBonePoseRotation(b, cur.Slerp(tgt, w).Normalized());
    }
    /// <summary>Two-bone IK (upper arm, forearm, hand) toward target in skeleton space; the elbow bends toward pole; weight w blends with the clip.</summary>
    internal static void Solve(Skeleton3D sk, int iu, int il, int ih, Vector3 target, Vector3 pole, float w, Basis? handGlobal)   // (also the swim stroke, AvatarSwim.cs)
    {
        var gU = sk.GetBoneGlobalPose(iu); var gL = sk.GetBoneGlobalPose(il); var gH = sk.GetBoneGlobalPose(ih);
        Vector3 S = gU.Origin, E = gL.Origin, H = gH.Origin;
        float a = (E - S).Length(), b = (H - E).Length(); if (a < 1e-4f || b < 1e-4f) return;
        var toT = target - S; if (toT.LengthSquared() < 1e-6f) return;
        float d = Mathf.Clamp(toT.Length(), Mathf.Abs(a - b) + 1e-3f, (a + b) * .999f); var dir = toT.Normalized();
        float cosA = Mathf.Clamp((a * a + d * d - b * b) / (2 * a * d), -1, 1), sinA = Mathf.Sqrt(Mathf.Max(0, 1 - cosA * cosA));
        var perp = pole - dir * pole.Dot(dir);
        if (perp.LengthSquared() < 1e-6f) perp = (E - S) - dir * (E - S).Dot(dir);
        if (perp.LengthSquared() < 1e-8f) return;
        perp = perp.Normalized();
        var E2 = S + dir * (a * cosA) + perp * (a * sinA); var T2 = S + dir * d;
        var q1 = FromTo(E - S, E2 - S);
        var bU = new Basis(q1) * gU.Basis.Orthonormalized();
        var q2 = FromTo(new Basis(q1) * (H - E), T2 - E2);
        var bL = new Basis(q2 * q1) * gL.Basis.Orthonormalized();
        var bH = handGlobal ?? new Basis(q2 * q1) * gH.Basis.Orthonormalized();
        int pu = sk.GetBoneParent(iu); var gP = pu >= 0 ? sk.GetBoneGlobalPose(pu).Basis.Orthonormalized() : Basis.Identity;
        // weighted: the hand's chosen global turn is reached exactly only at full weight (fine for the fades)
        SetLocal(sk, iu, gP.Inverse() * bU, w);
        SetLocal(sk, il, bU.Inverse() * bL, w);
        SetLocal(sk, ih, bL.Inverse() * bH, w);
    }
}
