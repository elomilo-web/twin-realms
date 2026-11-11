using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Look of a body: skin, hair and race features (data/races.json feeds this).</summary>
public class Appearance
{
    public Color Skin = new(.88f, .71f, .56f), Hair = new(.32f, .2f, .1f), Tunic = new(.58f, .16f, .17f), Pants = new(.27f, .22f, .2f), Cape = new(.18f, .22f, .4f), Eyes = new(.08f, .1f, .14f);
    public float Height = 1f, Bulk = 1f; public bool PointyEars, Tusks, Beard, Horns, GlowEyes, Female;
}

/// <summary>
/// The hero's skeleton and visible gear. Joints: pelvis, spine (lean and twist), head, clavicles, shoulders, elbows and wrists
/// (two-bone IK per arm with an elbow pole), hips, knees and ankles. Weapons are held in the hand (grip at the hand), never part of the arm.
/// Armor, helmet, gloves, boots, trinket, shield and weapon are rebuilt from the equipped items.
/// </summary>
public partial class HeroRig : Node3D
{
    public Node3D Pelvis = null!, Spine = null!, Head = null!, Cape = null!, HandR = null!, HandL = null!, WeaponR = null!, WeaponL = null!, ShieldN = null!;
    public Node3D? BladeBase, BladeTip;
    Node3D _thighR = null!, _thighL = null!, _kneeR = null!, _kneeL = null!, _upR = null!, _upL = null!, _foreR = null!, _foreL = null!, _hair = null!;
    readonly List<Node3D> _gear = new();
    public const float UpperLen = .3f, ForeLen = .29f, SpineY = 1.0f;
    static readonly Vector3 ShoulderLocalR = new(.33f, .47f, 0), ShoulderLocalL = new(-.33f, .47f, 0);
    public Vector3 ShoulderR { get; private set; } public Vector3 ShoulderL { get; private set; }
    Appearance _ap = new();
    Vector3 _upHintR = Vector3.Up, _upHintL = Vector3.Up, _lastDirR = Vector3.Forward, _lastDirL = Vector3.Forward;

    static StandardMaterial3D M(Color c, float metal = 0, float rough = .8f) => new() { AlbedoColor = c, Metallic = metal, Roughness = rough };
    static MeshInstance3D P(Node3D par, Mesh m, Material mt, Vector3 pos, Vector3? rot = null, Vector3? scl = null)
    { var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mt, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scl ?? Vector3.One }; par.AddChild(mi); return mi; }
    static CylinderMesh Cyl(float top, float bot, float h, int seg = 10) => new() { TopRadius = top, BottomRadius = bot, Height = h, RadialSegments = seg };
    static SphereMesh Sph(float r, float h = -1, int seg = 10, bool hemi = false) => new() { Radius = r, Height = h < 0 ? r * 2 : h, RadialSegments = seg, Rings = Math.Max(3, seg / 2), IsHemisphere = hemi };
    Node3D N(Node3D par, Vector3 pos) { var n = new Node3D { Position = pos }; par.AddChild(n); return n; }

    // ================================================================ build
    public void Build(Appearance ap)
    {
        _ap = ap; Scale = new Vector3(ap.Bulk, ap.Height, ap.Bulk);
        var skin = M(ap.Skin, 0, .7f); var tunic = Gfx.Tex(M(ap.Tunic), "cloth", .5f); var pants = Gfx.Tex(M(ap.Pants), "cloth", .5f);
        var leather = Gfx.Tex(M(new Color(.36f, .24f, .14f), 0, .75f), "grain", .5f, true, .6f); var hair = M(ap.Hair, 0, .9f);
        var shoe = M(new Color(.25f, .17f, .1f));
        Pelvis = N(this, new Vector3(0, .95f, 0));
        P(Pelvis, Cyl(.24f, .26f, .26f, 14), pants, new Vector3(0, .02f, 0));
        P(Pelvis, Cyl(.255f, .262f, .07f, 14), leather, new Vector3(0, .12f, 0));
        // legs: hip -> knee -> ankle
        foreach (var (s, isR) in new[] { (1f, true), (-1f, false) })
        {
            var th = N(Pelvis, new Vector3(s * .13f, -.03f, 0)); var kn = N(th, new Vector3(0, -.44f, 0));
            P(th, Cyl(.12f, .095f, .46f), pants, new Vector3(0, -.22f, 0));
            P(kn, Sph(.095f), pants, Vector3.Zero);
            P(kn, Cyl(.092f, .075f, .4f), pants, new Vector3(0, -.2f, 0));
            P(kn, new BoxMesh { Size = new Vector3(.14f, .09f, .25f) }, shoe, new Vector3(0, -.43f, -.05f));
            if (isR) { _thighR = th; _kneeR = kn; } else { _thighL = th; _kneeL = kn; }
        }
        // spine: chest, shoulders, neck, head
        Spine = N(this, new Vector3(0, SpineY, 0));
        P(Spine, Cyl(.3f, .24f, .52f, 14), tunic, new Vector3(0, .26f, 0));
        P(Spine, Sph(.29f, .25f, 14), tunic, new Vector3(0, .52f, 0));
        P(Spine, Cyl(.07f, .085f, .14f), skin, new Vector3(0, .64f, 0));
        Head = N(Spine, new Vector3(0, .8f, 0));
        P(Head, Sph(.165f, .36f, 14), skin, Vector3.Zero);
        var eye = ap.GlowEyes ? new StandardMaterial3D { AlbedoColor = ap.Eyes, EmissionEnabled = true, Emission = ap.Eyes, EmissionEnergyMultiplier = 2f } : M(ap.Eyes);
        foreach (float sx in new[] { -1f, 1f })
        {
            P(Head, Sph(.022f, -1, 6), eye, new Vector3(sx * .06f, .02f, -.148f));
            P(Head, new BoxMesh { Size = new Vector3(.06f, .014f, .02f) }, hair, new Vector3(sx * .062f, .058f, -.15f), new Vector3(0, 0, sx * -.15f));
            P(Head, Sph(.035f, .07f, 6), skin, new Vector3(sx * .165f, 0, 0));   // plain ears for every race (no horn, tusk or pointy-ear cones)
        }
        P(Head, new PrismMesh { Size = new Vector3(.045f, .07f, .055f) }, skin, new Vector3(0, -.025f, -.165f), new Vector3(-Mathf.Pi / 2, 0, 0));
        _hair = N(Head, Vector3.Zero);
        P(_hair, Sph(.178f, .24f, 14, true), hair, new Vector3(0, .03f, .025f), new Vector3(-.3f, 0, 0));
        P(_hair, new BoxMesh { Size = new Vector3(.3f, .16f, .1f) }, hair, new Vector3(0, -.01f, .12f));
        if (ap.Beard) P(Head, Cyl(.13f, .06f, .2f, 10), hair, new Vector3(0, -.13f, -.07f), new Vector3(.25f, 0, 0));
        Cape = N(Spine, new Vector3(0, .58f, .2f));
        P(Cape, new BoxMesh { Size = new Vector3(.58f, 1.15f, .03f) }, Gfx.Tex(new StandardMaterial3D { AlbedoColor = ap.Cape, CullMode = BaseMaterial3D.CullModeEnum.Disabled }, "cloth", .4f), new Vector3(0, -.58f, .06f), new Vector3(-.1f, 0, 0));
        // arms: upper arm and forearm pivots point along -Z (aimed by IK every frame); hands carry the grip
        Node3D Seg(float len, float r0, float r1, Material mt, bool joint)
        {
            var n = new Node3D(); AddChild(n);
            P(n, new CapsuleMesh { Radius = (r0 + r1) / 2, Height = len + r0 + r1, RadialSegments = 10, Rings = 2 }, mt, new Vector3(0, 0, -len / 2), new Vector3(Mathf.Pi / 2, 0, 0));
            if (joint) P(n, Sph(r0 * 1.05f), mt, Vector3.Zero);
            return n;
        }
        _upR = Seg(UpperLen, .085f, .07f, tunic, true); _upL = Seg(UpperLen, .085f, .07f, tunic, true);
        _foreR = Seg(ForeLen, .068f, .055f, skin, true); _foreL = Seg(ForeLen, .068f, .055f, skin, true);
        HandR = new Node3D(); AddChild(HandR); HandL = new Node3D(); AddChild(HandL);
        P(HandR, Sph(.065f, .12f, 8), skin, new Vector3(0, 0, .02f)); P(HandL, Sph(.065f, .12f, 8), skin, new Vector3(0, 0, .02f));
        WeaponR = N(HandR, Vector3.Zero); WeaponL = N(HandL, Vector3.Zero); ShieldN = N(HandL, Vector3.Zero);
        foreach (var n in new[] { Pelvis, Spine, Head, _hair, _thighR, _thighL, _kneeR, _kneeL, _upR, _upL, _foreR, _foreL, HandR, HandL }) Gfx.MergeChildren(n);
        Pose(0, new LegPose(), 0, 0, new HandPose { H = new Vector3(.42f, .95f, -.1f), Pitch = -60 }, new HandPose { H = new Vector3(-.42f, .95f, -.1f), Pitch = -60 });
    }

    // ================================================================ gear
    Node3D G(Node3D on) { var g = new Node3D(); on.AddChild(g); _gear.Add(g); return g; }
    static string Has(Item? it, params string[] keys) { if (it == null) return ""; foreach (var k in keys) if (it.Name.Contains(k)) return k; return keys[^1]; }

    /// <summary>Rebuilds every visible piece of equipment from the save data.</summary>
    public void ApplyGear(PlayerStats d)
    {
        Cape.Visible = d.Eq.GetValueOrDefault("cape") != null;   // capes are an equipment slot
        foreach (var g in _gear) { g.GetParent()?.RemoveChild(g); g.QueueFree(); } _gear.Clear();
        foreach (var c in WeaponR.GetChildren()) { WeaponR.RemoveChild(c); c.QueueFree(); }
        foreach (var c in WeaponL.GetChildren()) { WeaponL.RemoveChild(c); c.QueueFree(); }
        foreach (var c in ShieldN.GetChildren()) { ShieldN.RemoveChild(c); c.QueueFree(); }
        var steel = M(new Color(.7f, .72f, .77f), .85f, .32f); var gold = M(new Color(.88f, .7f, .32f), .9f, .3f);
        var leather = Gfx.Tex(M(new Color(.4f, .27f, .15f), 0, .7f), "grain", .4f, true, .7f); var darkLeather = Gfx.Tex(M(new Color(.22f, .15f, .1f), 0, .75f), "grain", .4f, true, .7f);
        Material Trim(Item it) => it.Rar >= 3 ? gold : it.Rar >= 1 ? M(Items.RarityColor(it.Rar).Darkened(.2f), .7f, .35f) : M(new Color(.4f, .4f, .42f), .7f, .4f);

        // ---- body armor
        var ar = d.Eq.GetValueOrDefault("armor");
        if (ar != null)
        {
            string kind = Has(ar, "Robe", "Plate", "Cuirass", "Hauberk", "Brigandine", "Leather");
            var trim = Trim(ar); var gS = G(Spine); var gP = G(Pelvis);
            if (kind == "Robe")
            {
                var robe = Gfx.Tex(M(Items.RarityColor(ar.Rar).Lerp(new Color(.2f, .2f, .35f), .55f)), "cloth", .5f);
                P(gS, Cyl(.31f, .26f, .54f, 14), robe, new Vector3(0, .26f, 0)); P(gP, Cyl(.27f, .4f, .8f, 14), robe, new Vector3(0, -.3f, 0));
                P(gS, new BoxMesh { Size = new Vector3(.06f, .5f, .02f) }, trim, new Vector3(0, .26f, -.31f)); P(gP, Cyl(.405f, .41f, .05f, 14), trim, new Vector3(0, -.68f, 0));
                var hood = G(Head); P(hood, Sph(.215f, .3f, 12, true), robe, new Vector3(0, -.02f, .07f), new Vector3(.75f, 0, 0));
            }
            else
            {
                bool plate = kind == "Plate" || kind == "Cuirass"; bool chain = kind == "Hauberk";
                var body = plate ? steel : chain ? Gfx.Tex(M(new Color(.55f, .57f, .6f), .7f, .45f), "cloth", .08f, true, 1.5f) : leather;
                P(gS, Cyl(.32f, .26f, .5f, 14), body, new Vector3(0, .27f, 0)); P(gS, Sph(.31f, .24f, 14), body, new Vector3(0, .52f, 0));
                P(gS, new BoxMesh { Size = new Vector3(.5f, .04f, .05f) }, trim, new Vector3(0, .5f, -.27f));
                if (plate) P(gS, new BoxMesh { Size = new Vector3(.05f, .4f, .05f) }, trim, new Vector3(0, .28f, -.3f));
                if (kind == "Brigandine") for (int i = 0; i < 6; i++) P(gS, Sph(.018f, -1, 6), gold, new Vector3((i % 3 - 1) * .13f, .18f + (i / 3) * .16f, -.29f));
                // tassets / skirt
                for (int i = -1; i <= 1; i++) P(gP, new BoxMesh { Size = new Vector3(.17f, .26f, .03f) }, chain ? body : plate ? steel : leather, new Vector3(i * .17f, -.14f, -.25f), new Vector3(-.12f, -i * .25f, 0));
                // pauldrons on the spine at the shoulders
                foreach (float sx in new[] { -1f, 1f })
                {
                    P(gS, Sph(plate ? .17f : .14f, .22f, 12, true), plate || chain ? steel : leather, new Vector3(sx * .35f, .5f, 0), new Vector3(0, 0, -sx * .4f));
                    P(gS, new TorusMesh { InnerRadius = .14f, OuterRadius = .165f, Rings = 12, RingSegments = 4 }, trim, new Vector3(sx * .35f, .5f, 0), new Vector3(0, 0, -sx * .4f));
                }
                // sleeves over the upper arms
                foreach (var up in new[] { _upR, _upL }) { var g = G(up); P(g, Cyl(.092f, .082f, UpperLen * .7f), chain || plate ? body : leather, new Vector3(0, 0, -UpperLen * .45f), new Vector3(Mathf.Pi / 2, 0, 0)); }
            }
        }
        // ---- helmet
        var hd = d.Eq.GetValueOrDefault("head"); _hair.Visible = true;
        if (hd != null)
        {
            string kind = Has(hd, "Hood", "Cap", "Circlet", "Greathelm", "Helm"); var g = G(Head); var trim = Trim(hd);
            switch (kind)
            {
                case "Hood": P(g, Sph(.21f, .3f, 12, true), Gfx.Tex(M(new Color(.25f, .3f, .22f)), "cloth", .4f), new Vector3(0, .02f, .05f), new Vector3(.55f, 0, 0)); P(g, Cyl(0, .09f, .22f, 8), Gfx.Tex(M(new Color(.25f, .3f, .22f)), "cloth", .4f), new Vector3(0, -.05f, .22f), new Vector3(1.9f, 0, 0)); _hair.Visible = false; break;
                case "Cap": P(g, Sph(.185f, .2f, 12, true), leather, new Vector3(0, .04f, .01f)); P(g, Cyl(.19f, .19f, .03f, 12), trim, new Vector3(0, .04f, 0)); _hair.Visible = false; break;
                case "Circlet": P(g, new TorusMesh { InnerRadius = .165f, OuterRadius = .18f, Rings = 16, RingSegments = 4 }, gold, new Vector3(0, .06f, 0), new Vector3(.15f, 0, 0)); P(g, Sph(.025f, -1, 8), M(Items.RarityColor(hd.Rar), .1f, .2f), new Vector3(0, .07f, -.175f)); break;
                case "Greathelm":
                    P(g, Cyl(.2f, .2f, .32f, 12), steel, new Vector3(0, .02f, 0)); P(g, Sph(.2f, .14f, 12, true), steel, new Vector3(0, .18f, 0));
                    P(g, new BoxMesh { Size = new Vector3(.24f, .025f, .02f) }, M(new Color(.02f, .02f, .03f)), new Vector3(0, .03f, -.2f)); P(g, new BoxMesh { Size = new Vector3(.03f, .3f, .02f) }, trim, new Vector3(0, .02f, -.205f)); _hair.Visible = false; break;
                default:
                    P(g, Sph(.19f, .26f, 12, true), steel, new Vector3(0, .02f, 0)); P(g, Cyl(.195f, .195f, .04f, 12), trim, new Vector3(0, .02f, 0));
                    P(g, new BoxMesh { Size = new Vector3(.03f, .14f, .02f) }, steel, new Vector3(0, -.03f, -.19f)); _hair.Visible = false; break;
            }
        }
        // ---- gloves (on the forearm ends and hands)
        var hn = d.Eq.GetValueOrDefault("hands");
        if (hn != null)
        {
            string kind = Has(hn, "Gauntlets", "Bracers", "Wraps", "Gloves"); var mat = kind == "Gauntlets" ? steel : kind == "Wraps" ? Gfx.Tex(M(new Color(.75f, .7f, .6f)), "cloth", .1f) : leather;
            foreach (var (fore, hand) in new[] { (_foreR, HandR), (_foreL, HandL) })
            {
                var g = G(fore); P(g, Cyl(.078f, .064f, ForeLen * .55f), mat, new Vector3(0, 0, -ForeLen * .68f), new Vector3(Mathf.Pi / 2, 0, 0));
                if (kind != "Wraps") P(g, Cyl(.08f, .08f, .03f), Trim(hn), new Vector3(0, 0, -ForeLen * .42f), new Vector3(Mathf.Pi / 2, 0, 0));
                if (kind != "Bracers") { var gh = G(hand); P(gh, Sph(.071f, .13f, 8), mat, new Vector3(0, 0, .02f)); }
            }
        }
        // ---- boots (on the shins)
        var ft = d.Eq.GetValueOrDefault("feet");
        foreach (var kn in new[] { _kneeR, _kneeL })
        {
            var g = G(kn);
            if (ft == null) continue;
            string kind = Has(ft, "Greaves", "Sabatons", "Treads", "Boots"); var mat = kind == "Greaves" || kind == "Sabatons" ? steel : kind == "Treads" ? darkLeather : leather;
            P(g, Cyl(.1f, .085f, kind == "Treads" ? .12f : .3f), mat, new Vector3(0, kind == "Treads" ? -.36f : -.27f, 0));
            P(g, new BoxMesh { Size = new Vector3(.155f, .1f, .28f) }, mat, new Vector3(0, -.43f, -.055f));
            if (kind == "Greaves") P(g, Sph(.1f, .12f, 8, true), steel, new Vector3(0, 0, -.04f), new Vector3(-Mathf.Pi / 2, 0, 0));
            P(g, Cyl(.105f, .105f, .03f), Trim(ft), new Vector3(0, kind == "Treads" ? -.3f : -.12f, 0));
        }
        // ---- trinket: pendant with a glowing gem
        var tr = d.Eq.GetValueOrDefault("trinket");
        if (tr != null)
        {
            var g = G(Spine); var rc = Items.RarityColor(tr.Rar);
            P(g, new TorusMesh { InnerRadius = .14f, OuterRadius = .15f, Rings = 16, RingSegments = 3 }, gold, new Vector3(0, .55f, -.06f), new Vector3(1.1f, 0, 0));
            P(g, Sph(.035f, -1, 8), new StandardMaterial3D { AlbedoColor = rc, EmissionEnabled = true, Emission = rc, EmissionEnergyMultiplier = 2f }, new Vector3(0, .43f, -.3f));
        }
        // ---- weapon in the hand that holds it, shield on the left forearm
        var w = d.Weapon; string wt = d.WType(); var wd = Weapons.Get(wt);
        var (bb, bt) = Weapons.Build(wd.LeftHand ? WeaponL : WeaponR, wt, w); BladeBase = bb; BladeTip = bt;
        var sh = d.Eq.GetValueOrDefault("shield");
        if (sh != null && wd.Hands < 2) BuildShield(sh, steel, Trim(sh));
        foreach (var g in _gear) Gfx.MergeChildren(g);
    }

    void BuildShield(Item sh, Material steel, Material trim)
    {
        string kind = Has(sh, "Buckler", "Kite", "Tower", "Aegis", "Heater");
        var paint = Gfx.Tex(M(Items.RarityColor(sh.Rar).Lerp(new Color(.2f, .32f, .6f), .6f)), "planks", .5f);
        Vector2[] o = kind switch
        {
            "Kite" => new[] { new Vector2(-.22f, .38f), new Vector2(.22f, .38f), new Vector2(.24f, .1f), new Vector2(0, -.5f), new Vector2(-.24f, .1f) },
            "Tower" => new[] { new Vector2(-.26f, .5f), new Vector2(.26f, .5f), new Vector2(.26f, -.5f), new Vector2(-.26f, -.5f) },
            "Heater" => new[] { new Vector2(-.27f, .34f), new Vector2(.27f, .34f), new Vector2(.26f, -.05f), new Vector2(.15f, -.27f), new Vector2(0, -.37f), new Vector2(-.15f, -.27f), new Vector2(-.26f, -.05f) },
            _ => Circle(kind == "Aegis" ? .36f : .24f),
        };
        // shield lies in the hand's YZ plane, face toward -X (outward from the left arm); a strap offset keeps it off the forearm
        var g = new Node3D { Position = new Vector3(-.07f, 0, .12f) }; ShieldN.AddChild(g);
        P(g, Gfx.ExtrudeX(o, .045f), steel, Vector3.Zero);
        P(g, Gfx.ExtrudeX(Array.ConvertAll(o, v => v * .86f), .075f), paint, Vector3.Zero);
        P(g, Sph(.075f, .1f, 10, true), kind == "Aegis" ? trim : steel, new Vector3(-.045f, 0, 0), new Vector3(0, 0, Mathf.Pi / 2));
        if (kind != "Buckler") { P(g, new BoxMesh { Size = new Vector3(.08f, .05f, .9f * Mathf.Abs(o[0].X) * 2) }, trim, new Vector3(-.01f, .08f, 0)); P(g, new BoxMesh { Size = new Vector3(.08f, Mathf.Abs(o[0].Y) * 1.6f, .05f) }, trim, new Vector3(-.01f, 0, 0)); }
        Gfx.MergeChildren(g);
    }
    static Vector2[] Circle(float r) { var a = new Vector2[16]; for (int i = 0; i < 16; i++) { float t = i / 16f * Mathf.Tau; a[i] = new Vector2(Mathf.Cos(t), Mathf.Sin(t)) * r; } return a; }

    // ================================================================ pose
    public struct LegPose { public float SwingR, SwingL, KneeR, KneeL, Splay; }

    /// <summary>
    /// Poses the whole body. Hand poses are in model space; arms are solved with two-bone IK from the clavicle (which shrugs toward a far reach),
    /// with the elbow pointing down, back and out. The weapon orientation follows the motion so a blade's edge leads the swing.
    /// </summary>
    public void Pose(float dt, LegPose legs, float twist, float lean, HandPose r, HandPose l, Vector3? lookAt = null)
    {
        _thighR.Rotation = new Vector3(legs.SwingR, 0, legs.Splay); _thighL.Rotation = new Vector3(legs.SwingL, 0, -legs.Splay);
        _kneeR.Rotation = new Vector3(-legs.KneeR, 0, 0); _kneeL.Rotation = new Vector3(-legs.KneeL, 0, 0);
        // lean > 0 bends forward (negative X tips the top toward -Z)
        Spine.Rotation = new Vector3(-lean, twist, 0);
        float hy = -twist * .5f, hp = lean * .6f;
        if (lookAt is Vector3 la) { var d = la - (Spine.Position + new Vector3(0, .8f, 0)); float want = Mathf.Atan2(-d.X, -d.Z) - twist; hy = Mathf.Clamp(want, -1f, 1f); hp += Mathf.Clamp(Mathf.Atan2(d.Y, new Vector2(d.X, d.Z).Length()) * .5f, -.4f, .3f); }
        _headYaw = Mathf.Lerp(_headYaw, hy, .15f);
        Head.Rotation = new Vector3(hp, _headYaw, 0);
        var sR = Spine.Transform * ShoulderLocalR; var sL = Spine.Transform * ShoulderLocalL;
        // clavicles: shoulders shrug toward the hand when it reaches far or high
        Vector3 Shrug(Vector3 s, Vector3 h) { var d = h - s; float reach = Mathf.Clamp((d.Length() - .35f) * .35f, 0, .08f); return s + d.Normalized() * reach + new Vector3(0, Mathf.Clamp((h.Y - s.Y) * .1f, -.02f, .05f), 0); }
        ShoulderR = Shrug(sR, r.H); ShoulderL = Shrug(sL, l.H);
        var hR = SolveArm(_upR, _foreR, ShoulderR, r.H, new Vector3(.6f, -1f, .55f));
        var hL = SolveArm(_upL, _foreL, ShoulderL, l.H, new Vector3(-.6f, -1f, .55f));
        PlaceHand(HandR, hR, r.Dir, ref _upHintR, ref _lastDirR);
        if (SteadyLeft) { _upHintL = Vector3.Up; _lastDirL = l.Dir.Normalized(); } // a shield arm keeps the shield upright
        PlaceHand(HandL, hL, l.Dir, ref _upHintL, ref _lastDirL);
    }
    public bool SteadyLeft; float _headYaw;

    static Vector3 SolveArm(Node3D up, Node3D fore, Vector3 s, Vector3 h, Vector3 pole)
    {
        float a = UpperLen, b = ForeLen; var d = h - s; float L = d.Length();
        if (L < .05f) { d = Vector3.Down * .05f; L = .05f; }
        var n = d / L; float Lc = Mathf.Clamp(L, Mathf.Abs(a - b) + .02f, a + b - .005f); var hc = s + n * Lc;
        float x = (a * a - b * b + Lc * Lc) / (2 * Lc), hh = Mathf.Sqrt(Mathf.Max(0, a * a - x * x));
        var p = pole - n * pole.Dot(n); if (p.LengthSquared() < 1e-5f) p = Vector3.Back; p = p.Normalized();
        var e = s + n * x + p * hh;
        Aim(up, s, e, p); Aim(fore, e, hc, p);
        return hc;
    }
    static void Aim(Node3D n, Vector3 a, Vector3 b, Vector3 up)
    {
        var d = (b - a).Normalized(); var u = Mathf.Abs(d.Dot(up)) > .98f ? Vector3.Right : up;
        n.Transform = new Transform3D(Basis.LookingAt(d, u), a);
    }
    void PlaceHand(Node3D hand, Vector3 pos, Vector3 dir, ref Vector3 upHint, ref Vector3 lastDir)
    {
        dir = dir.Normalized();
        // edge leads the motion: the hand's up axis is perpendicular to the blade and to the direction of travel
        var motion = dir - lastDir; lastDir = dir;
        if (motion.LengthSquared() > 1e-4f) { var u = dir.Cross(motion).Normalized(); if (u.Dot(upHint) < 0) u = -u; upHint = upHint.Lerp(u, .5f).Normalized(); }
        else upHint = upHint.Lerp(Vector3.Up, .08f).Normalized();
        var hint = Mathf.Abs(dir.Dot(upHint)) > .97f ? (Mathf.Abs(dir.Y) > .9f ? Vector3.Back : Vector3.Up) : upHint;
        hand.Transform = new Transform3D(Basis.LookingAt(dir, hint), pos);
    }
}
