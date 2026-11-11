using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A hand pose in the hero's model space: hand position and the direction the weapon points.</summary>
public struct HandPose
{
    public Vector3 H; public float Yaw, Pitch;
    public Vector3 Dir => DirOf(Yaw, Pitch);
    public static Vector3 DirOf(float yawDeg, float pitchDeg)
    {
        float y = Mathf.DegToRad(yawDeg), p = Mathf.DegToRad(pitchDeg);
        return new Vector3(-Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Cos(y) * Mathf.Cos(p));
    }
    public static HandPose Lerp(HandPose a, HandPose b, float t) => new() { H = a.H.Lerp(b.H, t), Yaw = Mathf.Lerp(a.Yaw, b.Yaw, t), Pitch = Mathf.Lerp(a.Pitch, b.Pitch, t) };
}

public class AttackDef
{
    public float Dur = .5f, Wind = .35f, Hit = .5f, Dmg = 1, Arc = 60, Range = 3, Knock = 2.5f, Stun, Up, Lunge, Spin, CritBonus;
    public bool Finisher, Pierce;
    public HandPose[] Keys = new HandPose[0];
}

public class WeaponDef
{
    public string Id = "", Name = ""; public int Hands = 1; public bool Ranged, LeftHand;
    public float Dmg = 1, Str, Dex, Mag, Grip, Speed = 1, Crit;
    public Dictionary<string, float> OnHit = new();   // status id -> chance per hit (finishers and power attacks x1.5)
    public Color Trail = new(.45f, .85f, 1f);
    public HandPose Rest, Aim, Draw;
    public List<AttackDef> Combo = new(); public AttackDef Power = new();
}

/// <summary>Weapon types from data/weapons.json, and the meshes for each type (grip at the origin, weapon along -Z).</summary>
public static class Weapons
{
    static readonly Dictionary<string, WeaponDef> _d = new();
    public static IEnumerable<string> Types { get { Load(); return _d.Keys; } }

    public static WeaponDef Get(string id) { Load(); return _d.TryGetValue(id, out var w) ? w : _d["sword"]; }

    static HandPose P(JsonElement e, HandPose def)
    {
        if (e.ValueKind != JsonValueKind.Object) return def;
        var p = def;
        if (e.TryGetProperty("h", out var h)) p.H = new Vector3(h[0].GetSingle(), h[1].GetSingle(), h[2].GetSingle());
        if (e.TryGetProperty("d", out var d)) { p.Yaw = d[0].GetSingle(); p.Pitch = d[1].GetSingle(); }
        return p;
    }
    static AttackDef A(JsonElement e)
    {
        var a = new AttackDef
        {
            Dur = JsonUtil.F(e, "dur", .5f), Wind = JsonUtil.F(e, "wind", .35f), Hit = JsonUtil.F(e, "hit", .5f), Dmg = JsonUtil.F(e, "dmg", 1), Arc = JsonUtil.F(e, "arc", 60), Range = JsonUtil.F(e, "range", 3),
            Knock = JsonUtil.F(e, "knock", 2.5f), Stun = JsonUtil.F(e, "stun"), Up = JsonUtil.F(e, "up"), Lunge = JsonUtil.F(e, "lunge"), Spin = JsonUtil.F(e, "spin"), CritBonus = JsonUtil.F(e, "critBonus"),
            Finisher = e.TryGetProperty("finisher", out var f) && f.GetBoolean(), Pierce = e.TryGetProperty("pierce", out var pi) && pi.GetBoolean(),
        };
        if (e.TryGetProperty("keys", out var k)) { var l = new List<HandPose>(); foreach (var x in k.EnumerateArray()) l.Add(P(x, new HandPose())); a.Keys = l.ToArray(); }
        return a;
    }

    // kit weapon models (assets/models/kit/weapons, data/weapons.json "kitModels"): type -> model names
    static readonly Dictionary<string, List<string>> _kit = new();
    static bool _useKit;
    static readonly Dictionary<string, PackedScene?> _kitScenes = new();
    static readonly HashSet<string> Melee = new() { "sword", "greatsword", "axe", "mace", "dagger", "spear" };

    /// <summary>The kit model names listed for a weapon type (empty if none).</summary>
    public static IReadOnlyList<string> KitModels(string type) { Load(); return _kit.TryGetValue(type, out var l) ? l : new List<string>(); }

    /// <summary>Which kit model an item shows: its own Model if set and present, else one from its type's list (stable per item name). "" = the built-in mesh.</summary>
    public static string KitModelFor(string wt, Item? it)
    {
        Load();
        if (!string.IsNullOrEmpty(it?.Model) && ResourceLoader.Exists(KitPath(it!.Model!))) return it.Model!;
        if (!_useKit || !_kit.TryGetValue(wt, out var l) || l.Count == 0) return "";
        uint h = 2166136261; foreach (char ch in it?.Name ?? wt) h = (h ^ ch) * 16777619;
        return l[(int)(h % (uint)l.Count)];
    }
    static string KitPath(string name) => $"res://assets/models/kit/weapons/{name}.glb";

    public static void Load()
    {
        if (_d.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/weapons.json");
        if (doc.RootElement.TryGetProperty("kitModels", out var km))
        {
            _useKit = !km.TryGetProperty("use", out var u) || u.ValueKind != JsonValueKind.False;
            if (km.TryGetProperty("models", out var mm))
                foreach (var t in mm.EnumerateObject()) { var l = new List<string>(); foreach (var x in t.Value.EnumerateArray()) l.Add(x.GetString() ?? ""); _kit[t.Name] = l; }
        }
        foreach (var t in doc.RootElement.GetProperty("types").EnumerateObject())
        {
            var e = t.Value; var w = new WeaponDef
            {
                Id = t.Name, Name = JsonUtil.S(e, "name", t.Name), Hands = (int)JsonUtil.F(e, "hands", 1), Ranged = e.TryGetProperty("ranged", out var r) && r.GetBoolean(),
                LeftHand = JsonUtil.S(e, "hand") == "left", Dmg = JsonUtil.F(e, "dmg", 1), Str = JsonUtil.F(e, "str"), Dex = JsonUtil.F(e, "dex"), Mag = JsonUtil.F(e, "mag"),
                Grip = JsonUtil.F(e, "grip", .1f), Speed = JsonUtil.F(e, "speed", 1), Crit = JsonUtil.F(e, "crit"), Trail = Color.FromHtml(JsonUtil.S(e, "trail", "73d9ff")),
            };
            w.Rest = P(e.TryGetProperty("rest", out var re) ? re : default, new HandPose { H = new Vector3(.42f, .95f, -.14f), Yaw = -6, Pitch = -50 });
            w.Aim = P(e.TryGetProperty("aim", out var ai) ? ai : default, w.Rest); w.Draw = P(e.TryGetProperty("draw", out var dr) ? dr : default, w.Aim);
            if (e.TryGetProperty("onHit", out var oh)) foreach (var x in oh.EnumerateObject()) w.OnHit[x.Name] = x.Value.GetSingle();
            foreach (var c in e.GetProperty("combo").EnumerateArray()) w.Combo.Add(A(c));
            w.Power = A(e.GetProperty("power"));
            _d[t.Name] = w;
        }
    }

    // ---------------------------------------------------------------- meshes
    static StandardMaterial3D M(Color c, float metal = 0, float rough = .8f, float emit = 0)
    {
        var m = new StandardMaterial3D { AlbedoColor = c, Metallic = metal, Roughness = rough };
        if (emit > 0) { m.EmissionEnabled = true; m.Emission = c; m.EmissionEnergyMultiplier = emit; }
        return m;
    }
    static void Add(Node3D p, Mesh m, Material mat, Vector3 pos, Vector3? rot = null, Vector3? scl = null)
    {
        var mi = new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, Scale = scl ?? Vector3.One }; p.AddChild(mi);
    }
    const float H = Mathf.Pi / 2;

    /// <summary>Builds the weapon under parent (grip at origin, pointing -Z). Returns the trail markers (blade base and tip) for melee weapons.</summary>
    public static (Node3D? baseN, Node3D? tipN) Build(Node3D parent, string wt, Item? it)
    {
        string km = KitModelFor(wt, it);
        if (km != "" && BuildKit(parent, wt, km) is { } kr) return kr;
        int rar = it?.Rar ?? 0; var rc = Items.RarityColor(rar);
        var steel = M(new Color(.78f, .8f, .85f), .85f, .3f); var dark = M(new Color(.32f, .33f, .36f), .8f, .4f);
        var wood = Gfx.Tex(M(new Color(.42f, .28f, .16f)), "grain", .4f, true, .6f); var wrap = Gfx.Tex(M(new Color(.28f, .17f, .1f)), "cloth", .12f);
        var gold = M(new Color(.88f, .7f, .32f), .9f, .3f); var gem = M(rc, .2f, .2f, rar >= 2 ? 2.2f : .8f);
        var accent = rar >= 3 ? gold : rar >= 1 ? M(rc.Darkened(.25f), .7f, .35f) : dark;
        if (rar >= 4) { steel.EmissionEnabled = true; steel.Emission = rc; steel.EmissionEnergyMultiplier = .08f; steel.AlbedoColor = steel.AlbedoColor.Lerp(rc, .12f); }
        Node3D? b = null, t = null;
        Node3D Mark(float z) { var n = new Node3D { Position = new Vector3(0, 0, z) }; parent.AddChild(n); return n; }
        switch (wt)
        {
            case "greatsword":
                Add(parent, Gfx.Blade(1.25f, .13f, .03f), steel, new Vector3(0, 0, -.2f));
                Add(parent, new BoxMesh { Size = new Vector3(.5f, .05f, .06f) }, accent, new Vector3(0, 0, -.17f));
                Add(parent, new CylinderMesh { TopRadius = .03f, BottomRadius = .034f, Height = .42f, RadialSegments = 8 }, wrap, new Vector3(0, 0, .05f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .05f, Height = .1f, RadialSegments = 8, Rings = 4 }, accent, new Vector3(0, 0, .28f));
                Add(parent, new SphereMesh { Radius = .03f, Height = .06f, RadialSegments = 8, Rings = 4 }, gem, new Vector3(0, .03f, -.17f));
                b = Mark(-.35f); t = Mark(-1.55f); break;
            case "axe":
                Add(parent, new CylinderMesh { TopRadius = .028f, BottomRadius = .032f, Height = .78f, RadialSegments = 8 }, wood, new Vector3(0, 0, -.22f), new Vector3(H, 0, 0));
                Add(parent, Gfx.ExtrudeX(new[] { new Vector2(-.1f, .02f), new Vector2(.12f, .03f), new Vector2(.2f, .22f), new Vector2(-.18f, .22f), new Vector2(-.12f, .03f) }, .04f), steel, new Vector3(0, 0, -.52f), new Vector3(0, 0, H));
                Add(parent, new BoxMesh { Size = new Vector3(.07f, .07f, .12f) }, accent, new Vector3(0, 0, -.55f));
                Add(parent, new CylinderMesh { TopRadius = .036f, BottomRadius = .036f, Height = .14f, RadialSegments = 8 }, wrap, new Vector3(0, 0, .08f), new Vector3(H, 0, 0));
                b = Mark(-.45f); t = Mark(-.68f); break;
            case "mace":
                Add(parent, new CylinderMesh { TopRadius = .026f, BottomRadius = .03f, Height = .62f, RadialSegments = 8 }, dark, new Vector3(0, 0, -.18f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .1f, Height = .2f, RadialSegments = 10, Rings = 5 }, steel, new Vector3(0, 0, -.56f));
                for (int i = 0; i < 6; i++) { float a = i * Mathf.Tau / 6; Add(parent, Gfx.ExtrudeX(new[] { new Vector2(-.06f, 0), new Vector2(.06f, 0), new Vector2(0, .09f) }, .02f), steel, new Vector3(Mathf.Cos(a) * .08f, Mathf.Sin(a) * .08f, -.56f), new Vector3(0, 0, a - H)); }
                Add(parent, new CylinderMesh { TopRadius = .04f, BottomRadius = .04f, Height = .14f, RadialSegments = 8 }, wrap, new Vector3(0, 0, .06f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .035f, Height = .07f, RadialSegments = 8, Rings = 4 }, gem, new Vector3(0, 0, -.68f));
                b = Mark(-.45f); t = Mark(-.68f); break;
            case "dagger":
                Add(parent, Gfx.Blade(.32f, .06f, .018f), steel, new Vector3(0, 0, -.08f));
                Add(parent, new BoxMesh { Size = new Vector3(.16f, .03f, .03f) }, accent, new Vector3(0, 0, -.07f));
                Add(parent, new CylinderMesh { TopRadius = .022f, BottomRadius = .025f, Height = .12f, RadialSegments = 8 }, wrap, new Vector3(0, 0, .0f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .03f, Height = .06f, RadialSegments = 8, Rings = 4 }, gem, new Vector3(0, 0, .07f));
                b = Mark(-.12f); t = Mark(-.45f); break;
            case "spear":
                Add(parent, new CylinderMesh { TopRadius = .024f, BottomRadius = .028f, Height = 2.1f, RadialSegments = 8 }, wood, new Vector3(0, 0, -.45f), new Vector3(H, 0, 0));
                Add(parent, Gfx.Blade(.28f, .09f, .03f), steel, new Vector3(0, 0, -1.48f));
                Add(parent, new CylinderMesh { TopRadius = .035f, BottomRadius = .03f, Height = .08f, RadialSegments = 8 }, accent, new Vector3(0, 0, -1.46f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .025f, Height = .05f, RadialSegments = 6, Rings = 3 }, gem, new Vector3(0, .03f, -1.44f));
                Add(parent, new CylinderMesh { TopRadius = .035f, BottomRadius = .035f, Height = .1f, RadialSegments = 8 }, dark, new Vector3(0, 0, .58f), new Vector3(H, 0, 0));
                b = Mark(-1.2f); t = Mark(-1.85f); break;
            case "bow":
                // limbs curve back (+Z) from the grip along local Y; the string runs between the tips
                {
                    // continuous arc: points along y in [-.68, .68], bending back toward the archer (+Z) at the tips, tapering
                    Vector3 Pt(float t) => new(0, t * .68f, .2f * t * t - .02f);
                    const int seg = 10;
                    for (int k = 0; k < seg; k++)
                    {
                        float t0 = -1 + 2f * k / seg, t1 = -1 + 2f * (k + 1) / seg; var p0 = Pt(t0); var p1 = Pt(t1); var y = (p1 - p0).Normalized(); var x = Vector3.Right; var z = x.Cross(y).Normalized();
                        float r = .034f - Mathf.Abs((t0 + t1) / 2) * .018f;
                        parent.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = (p1 - p0).Length() + .01f, RadialSegments = 6 }, MaterialOverride = wood, Transform = new Transform3D(new Basis(x, y, z), (p0 + p1) / 2) });
                    }
                    Add(parent, new CylinderMesh { TopRadius = .004f, BottomRadius = .004f, Height = 1.36f, RadialSegments = 4 }, M(new Color(.92f, .9f, .82f)), new Vector3(0, 0, .18f));
                }
                Add(parent, new CylinderMesh { TopRadius = .03f, BottomRadius = .03f, Height = .14f, RadialSegments = 8 }, wrap, Vector3.Zero);
                Add(parent, new SphereMesh { Radius = .025f, Height = .05f, RadialSegments = 6, Rings = 3 }, gem, new Vector3(0, .1f, -.02f));
                break;
            case "wand":
                Add(parent, new CylinderMesh { TopRadius = .014f, BottomRadius = .022f, Height = .42f, RadialSegments = 8 }, wood, new Vector3(0, 0, -.14f), new Vector3(H, 0, 0));
                Add(parent, new TorusMesh { InnerRadius = .02f, OuterRadius = .035f, Rings = 10, RingSegments = 4 }, gold, new Vector3(0, 0, -.33f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .045f, Height = .09f, RadialSegments = 10, Rings = 5 }, M(new Color(.75f, .45f, 1f).Lerp(rc, .4f), 0, .2f, 2.2f), new Vector3(0, 0, -.38f));
                break;
            case "staff":
                Add(parent, new CylinderMesh { TopRadius = .026f, BottomRadius = .032f, Height = 1.9f, RadialSegments = 8 }, wood, new Vector3(0, 0, -.35f), new Vector3(H, 0, 0));
                for (int i = 0; i < 3; i++) { float a = i * Mathf.Tau / 3; Add(parent, new CylinderMesh { TopRadius = .006f, BottomRadius = .016f, Height = .26f, RadialSegments = 5 }, wood, new Vector3(Mathf.Cos(a) * .06f, Mathf.Sin(a) * .06f, -1.33f), new Vector3(H - .35f * Mathf.Sin(a), 0, .35f * Mathf.Cos(a))); }
                Add(parent, new SphereMesh { Radius = .09f, Height = .18f, RadialSegments = 12, Rings = 6 }, M(new Color(.45f, .9f, 1f).Lerp(rc, .35f), 0, .15f, 2.4f), new Vector3(0, 0, -1.4f));
                Add(parent, new CylinderMesh { TopRadius = .04f, BottomRadius = .04f, Height = .1f, RadialSegments = 8 }, accent, new Vector3(0, 0, -1.25f), new Vector3(H, 0, 0));
                break;
            default: // sword
                Add(parent, Gfx.Blade(.92f, .085f, .025f), steel, new Vector3(0, 0, -.15f));
                Add(parent, new CylinderMesh { TopRadius = .022f, BottomRadius = .022f, Height = .32f, RadialSegments = 6 }, accent, new Vector3(0, 0, -.12f), new Vector3(0, 0, H));
                foreach (float sx in new[] { -1f, 1f }) Add(parent, new SphereMesh { Radius = .032f, Height = .064f, RadialSegments = 6, Rings = 3 }, accent, new Vector3(sx * .16f, 0, -.12f));
                Add(parent, new CylinderMesh { TopRadius = .026f, BottomRadius = .03f, Height = .19f, RadialSegments = 8 }, wrap, new Vector3(0, 0, -.01f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .04f, Height = .08f, RadialSegments = 8, Rings = 4 }, rar >= 1 ? gem : accent, new Vector3(0, 0, .11f));
                b = Mark(-.28f); t = Mark(-1.15f); break;
        }
        Gfx.MergeChildren(parent);
        return (b, t);
    }

    /// <summary>
    /// A kit weapon model under parent. Kit weapons hold the grip at the origin and point +Y, so they are turned to point -Z like the built-in meshes;
    /// bows stay upright (limbs along Y, as the built-in bow). Melee weapons get trail markers from the blade's length. Null if the file is missing.
    /// </summary>
    static (Node3D?, Node3D?)? BuildKit(Node3D parent, string wt, string name)
    {
        if (!_kitScenes.TryGetValue(name, out var ps)) _kitScenes[name] = ps = ResourceLoader.Exists(KitPath(name)) ? GD.Load<PackedScene>(KitPath(name)) : null;
        if (ps?.Instantiate() is not Node3D model) { GD.PushWarning("[WEAPONS] missing kit model " + name); return null; }
        if (wt != "bow") model.Rotation = new Vector3(-H, 0, 0);
        parent.AddChild(model);
        float len = 0;
        foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false)) if (mi is MeshInstance3D m && m.Mesh != null) len = Mathf.Max(len, (m.Transform * m.Mesh.GetAabb()).End.Y);
        Node3D? b = null, t = null;
        if (Melee.Contains(wt) && len > .1f)
        {
            b = new Node3D { Position = new Vector3(0, 0, -len * .3f) }; parent.AddChild(b);
            t = new Node3D { Position = new Vector3(0, 0, -len) }; parent.AddChild(t);
        }
        return (b, t);
    }
}
