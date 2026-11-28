using System;
using System.Collections.Generic;
using System.Linq;
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
    /// <summary>Active window (fractions of the swing) in which the weapon's hitbox can strike; below 0 = around Hit (data/tuning.json game.hitbox.activeBefore / activeAfter).
    /// Step = metres the body is carried forward through the swing (below 0 = by attack type, game.attack.step*).</summary>
    public float Act0 = -1, Act1 = -1, Step = -1;
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
    /// <summary>Sounds per event ("swing" at attack start, "shoot" at a ranged release, "hit", "miss"), from data/weapons.json "sfx" (Sfx.Weapon plays them).</summary>
    public Dictionary<string, List<SfxPick>> Sfx = new();
    // A-11 (all optional in data/weapons.json): light = quick small weapon (dagger rules for stamina, swing weight and recovery), heavy = the
    // heavy impact feel, icon = inventory icon id, drop = weight when loot rolls a weapon type, affinity = class id -> +% damage with this
    // type, shot = a thrown or shot missile for ranged types that are not bow / wand / staff.
    public bool Light, Heavy; public string Icon = ""; public float Drop = 1;
    public Dictionary<string, float> Affinity = new();
    public ShotDef? Shot;
}

/// <summary>A-11: the missile of a thrown or mechanical ranged weapon (data/weapons.json type "shot").</summary>
public class ShotDef
{
    public string Kind = "arrow"; public float Speed = 50, PowerSpeed = 60, Life = 2, Lift = .5f, Aoe;
    public bool Pierce, PowerPierce; public int PowerShots = 1; public float PowerSpread;
}

/// <summary>One weapon sound choice: an Sfx id, its loudness and a random pitch range.</summary>
public readonly record struct SfxPick(string Id, float Vol, float P0, float P1);

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
            Finisher = e.TryGetProperty("finisher", out var f) && f.GetBoolean(), Pierce = e.TryGetProperty("pierce", out var pi) && pi.GetBoolean(), Step = JsonUtil.F(e, "step", -1),
        };
        if (e.TryGetProperty("active", out var av) && av.ValueKind == JsonValueKind.Array && av.GetArrayLength() >= 2) { a.Act0 = av[0].GetSingle(); a.Act1 = av[1].GetSingle(); }
        if (e.TryGetProperty("keys", out var k)) { var l = new List<HandPose>(); foreach (var x in k.EnumerateArray()) l.Add(P(x, new HandPose())); a.Keys = l.ToArray(); }
        return a;
    }

    // kit weapon models (assets/models/kit/weapons, data/weapons.json "kitModels"):
    // type -> base item name (data/items.json weaponNames) -> [model, scale] choices; the element word in the item name picks among them
    public readonly record struct KitPick(string Model, float Scale);
    static readonly Dictionary<string, List<(string name, List<KitPick> picks)>> _kitByName = new();
    static readonly Dictionary<string, Dictionary<string, string[]>> _elemHints = new();
    static readonly Dictionary<string, string[]> _hint = new();
    static bool _useKit; static float _jitter = .06f, _elemTint = .3f;
    static readonly Dictionary<string, PackedScene?> _kitScenes = new();

    // ---------------------------------------------------------------- A-11: item sizes and drop weights (data/weapons.json "size")
    static float _szMin = .85f, _szMax = 1.2f, _szReach = 1f, _szArc = .35f, _szSpeed = .5f;
    static readonly List<(float upTo, string name)> _szNames = new();
    /// <summary>A new weapon's size factor: two rolls averaged, so most are near the middle and few are Short or Great.</summary>
    public static float RollSize() { Load(); float t = (GD.Randf() + GD.Randf()) * .5f; return Mathf.Round(Mathf.Lerp(_szMin, _szMax, t) * 100f) / 100f; }
    /// <summary>"Short", "Long", "Great"... for a size factor ("" for the plain middle sizes).</summary>
    public static string SizeName(float size) { Load(); foreach (var (upTo, n) in _szNames) if (size <= upTo) return n; return ""; }
    static float SizeOf(Item? it) => it == null || it.Size <= 0 ? 1f : Mathf.Clamp(it.Size, .5f, 2f);
    /// <summary>Reach, arc and speed factors of an item's size (1 for a plain item): longer reaches farther, sweeps a little wider, swings a little slower.</summary>
    public static float SizeReach(Item? it) { Load(); return 1f + (SizeOf(it) - 1f) * _szReach; }
    public static float SizeArc(Item? it) { Load(); return 1f + (SizeOf(it) - 1f) * _szArc; }
    public static float SizeSpeed(Item? it) { Load(); return Mathf.Clamp(1f - (SizeOf(it) - 1f) * _szSpeed, .5f, 1.5f); }
    // every weapon drawn bigger (data/weapons.json size.grandScale, bows / wands / thrown size.grandScaleRanged) so weapons read grand and
    // heavy; melee reach grows with it (size.grandReach) so the swing still lands where the bigger blade is
    static float _grand = 1f, _grandRanged = 1f, _grandReach = 1f;
    public static float Grand(string wt) { Load(); return Get(wt) is { Ranged: true } || wt is "bow" or "wand" or "crossbow" or "javelin" or "kunai" ? _grandRanged : _grand; }
    /// <summary>The swing with the item's size applied to its reach and arc (a copy; the weapon data is not touched).</summary>
    public static AttackDef Sized(AttackDef a, Item? it)
    {
        float s = SizeOf(it), g = it != null && !Get(it.Wt ?? "sword").Ranged ? _grandReach : 1f; if (Mathf.Abs(s - 1f) < .005f && Mathf.Abs(g - 1f) < .005f) return a;
        return new AttackDef { Dur = a.Dur, Wind = a.Wind, Hit = a.Hit, Dmg = a.Dmg, Arc = Mathf.Min(180, a.Arc * SizeArc(it)), Range = a.Range * SizeReach(it) * g, Knock = a.Knock, Stun = a.Stun, Up = a.Up,
            Lunge = a.Lunge, Spin = a.Spin, CritBonus = a.CritBonus, Finisher = a.Finisher, Pierce = a.Pierce, Keys = a.Keys, Act0 = a.Act0, Act1 = a.Act1, Step = a.Step };
    }
    /// <summary>A random weapon type from the list, by each type's "drop" weight.</summary>
    public static string RollType(List<string> types)
    {
        Load(); float tot = 0; foreach (var t in types) tot += Mathf.Max(0, Get(t).Drop);
        if (tot <= 0) return types[(int)(GD.Randf() * types.Count) % types.Count];
        float r = GD.Randf() * tot; foreach (var t in types) { r -= Mathf.Max(0, Get(t).Drop); if (r <= 0) return t; }
        return types[^1];
    }
    /// <summary>Inventory icon of a weapon type: its own "icon" in data, a drawn one, else the sword.</summary>
    public static string IconOf(string wt)
    {
        if (wt is "bow" or "wand" or "staff" or "greatsword" or "axe" or "mace" or "dagger" or "spear") return wt;
        var ic = wt == "" ? "" : Get(wt).Icon; return ic != "" ? ic : "sword";
    }

    // ---------------------------------------------------------------- A-12: dual wield numbers (data/weapons.json "dual")
    static readonly Dictionary<string, float> _dual = new(); static readonly List<string> _dualAnims = new();
    public static float Dual(string key, float def) { Load(); return _dual.TryGetValue(key, out var v) ? v : def; }
    /// <summary>Attack animations an off-hand swing plays instead of the step's own (empty = the step's own).</summary>
    public static IReadOnlyList<string> DualOffAnims { get { Load(); return _dualAnims; } }

    /// <summary>Every kit model a weapon type can show (for tests and previews).</summary>
    public static IReadOnlyList<string> KitModels(string type)
    {
        Load(); if (!_kitByName.TryGetValue(type, out var l)) return new List<string>();
        return l.SelectMany(x => x.picks).Select(p => p.Model).Distinct().ToList();
    }
    /// <summary>The base item names a weapon type knows, with their kit models (for the audit in tests).</summary>
    public static IEnumerable<(string name, IReadOnlyList<KitPick> picks)> KitNames(string type)
    {
        Load(); if (_kitByName.TryGetValue(type, out var l)) foreach (var (n, p) in l) yield return (n, p);
    }

    static uint Hash(string s) { uint h = 2166136261; foreach (char ch in s) h = (h ^ ch) * 16777619; return h; }

    /// <summary>
    /// Which kit model an item shows: its own Model if set; else the models listed for the base name in its name ("Bearded Axe" before "Axe"),
    /// preferring ones that suit its element (an Ember sword picks sword_ember when the name allows it), stable per item name. Model "" = built-in mesh.
    /// </summary>
    public static KitPick KitModelFor(string wt, Item? it)
    {
        Load();
        if (!string.IsNullOrEmpty(it?.Model) && ResourceLoader.Exists(KitPath(it!.Model!))) return new KitPick(it.Model!, ScaleOf(wt, it.Model!));
        if (!_useKit || !_kitByName.TryGetValue(wt, out var l) || l.Count == 0) return new KitPick("", 1);
        string name = it?.Name ?? wt;
        var picks = l.FirstOrDefault(x => x.name != "" && name.Contains(x.name, StringComparison.OrdinalIgnoreCase)).picks
                    ?? l.FirstOrDefault(x => x.name == "").picks ?? l.SelectMany(x => x.picks).ToList();
        if (picks.Count == 0) return new KitPick("", 1);
        var pool = picks;
        if (it?.Elem != null && _hint.TryGetValue(it.Elem, out var words))
        {
            var suited = picks.Where(p => words.Any(w => p.Model.Contains(w))).ToList(); if (suited.Count > 0) pool = suited;
        }
        return pool[(int)(Hash(name) % (uint)pool.Count)];
    }
    static float ScaleOf(string wt, string model)
    {
        if (_kitByName.TryGetValue(wt, out var l)) foreach (var (_, ps) in l) foreach (var p in ps) if (p.Model == model) return p.Scale;
        return 1f;
    }
    static string KitPath(string name) => $"res://assets/models/kit/weapons/{name}.glb";

    public static void Load()
    {
        if (_d.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/weapons.json");
        if (doc.RootElement.TryGetProperty("kitModels", out var km))
        {
            _useKit = !km.TryGetProperty("use", out var u) || u.ValueKind != JsonValueKind.False;
            _jitter = JsonUtil.F(km, "sizeJitter", .06f); _elemTint = JsonUtil.F(km, "elementTint", .3f);
            if (km.TryGetProperty("byName", out var bn))
                foreach (var t in bn.EnumerateObject())
                {
                    var l = new List<(string, List<KitPick>)>();
                    foreach (var n in t.Value.EnumerateObject())
                    {
                        var ps = new List<KitPick>();
                        foreach (var x in n.Value.EnumerateArray())
                            ps.Add(x.ValueKind == JsonValueKind.Array ? new KitPick(x[0].GetString() ?? "", x.GetArrayLength() > 1 ? x[1].GetSingle() : 1f) : new KitPick(x.GetString() ?? "", 1f));
                        l.Add((n.Name, ps));
                    }
                    l.Sort((a, b) => b.Item1.Length.CompareTo(a.Item1.Length));   // longest name first: "Bearded Axe" before "Axe"
                    _kitByName[t.Name] = l;
                }
            if (km.TryGetProperty("elementModels", out var em))
                foreach (var e in em.EnumerateObject()) _hint[e.Name] = e.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
        }
        if (doc.RootElement.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Object)
        {
            _szMin = JsonUtil.F(sz, "min", .85f); _szMax = Mathf.Max(_szMin, JsonUtil.F(sz, "max", 1.2f));
            _szReach = JsonUtil.F(sz, "reachPer", 1f); _szArc = JsonUtil.F(sz, "arcPer", .35f); _szSpeed = JsonUtil.F(sz, "speedPer", .5f);
            _grand = JsonUtil.F(sz, "grandScale", 1f); _grandRanged = JsonUtil.F(sz, "grandScaleRanged", 1f); _grandReach = JsonUtil.F(sz, "grandReach", 1f);
            if (sz.TryGetProperty("names", out var sn) && sn.ValueKind == JsonValueKind.Array)
                foreach (var x in sn.EnumerateArray()) if (x.ValueKind == JsonValueKind.Array && x.GetArrayLength() >= 2) _szNames.Add((x[0].GetSingle(), x[1].GetString() ?? ""));
        }
        if (doc.RootElement.TryGetProperty("dual", out var du) && du.ValueKind == JsonValueKind.Object)
            foreach (var x in du.EnumerateObject())
            {
                if (x.Value.ValueKind == JsonValueKind.Number) _dual[x.Name] = x.Value.GetSingle();
                else if (x.Name == "offAnims" && x.Value.ValueKind == JsonValueKind.Array) foreach (var a in x.Value.EnumerateArray()) _dualAnims.Add(a.GetString() ?? "");
            }
        var keysFrom = new List<(WeaponDef w, string from)>();
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
            if (e.TryGetProperty("sfx", out var sx) && sx.ValueKind == JsonValueKind.Object)
                foreach (var ev in sx.EnumerateObject())
                {
                    if (ev.Name.StartsWith("_") || ev.Value.ValueKind != JsonValueKind.Array) continue;
                    var l = new List<SfxPick>();
                    foreach (var x in ev.Value.EnumerateArray())
                    {
                        if (x.ValueKind == JsonValueKind.String) { l.Add(new SfxPick(x.GetString() ?? "", 1f, 1f, 1f)); continue; }
                        float p0 = 1f, p1 = 1f;
                        if (x.TryGetProperty("pitch", out var pv)) { if (pv.ValueKind == JsonValueKind.Array) { p0 = pv[0].GetSingle(); p1 = pv.GetArrayLength() > 1 ? pv[1].GetSingle() : p0; } else p0 = p1 = pv.GetSingle(); }
                        l.Add(new SfxPick(JsonUtil.S(x, "id"), JsonUtil.F(x, "vol", 1f), p0, p1));
                    }
                    w.Sfx[ev.Name] = l;
                }
            // A-11 extras
            bool B(string k) => e.TryGetProperty(k, out var bv) && bv.ValueKind == JsonValueKind.True;
            w.Light = B("light") || t.Name is "dagger" or "unarmed"; w.Heavy = B("heavy") || t.Name is "greatsword" or "mace" or "axe";
            w.Icon = JsonUtil.S(e, "icon"); w.Drop = JsonUtil.F(e, "drop", 1f);
            if (e.TryGetProperty("affinity", out var af) && af.ValueKind == JsonValueKind.Object) foreach (var x in af.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) w.Affinity[x.Name] = x.Value.GetSingle();
            if (e.TryGetProperty("shot", out var sh) && sh.ValueKind == JsonValueKind.Object)
                w.Shot = new ShotDef
                {
                    Kind = JsonUtil.S(sh, "kind", "arrow"), Speed = JsonUtil.F(sh, "speed", 50), PowerSpeed = JsonUtil.F(sh, "powerSpeed", JsonUtil.F(sh, "speed", 50) * 1.2f), Life = JsonUtil.F(sh, "life", 2f),
                    Lift = JsonUtil.F(sh, "lift", .5f), Aoe = JsonUtil.F(sh, "aoe"), Pierce = sh.TryGetProperty("pierce", out var sp1) && sp1.ValueKind == JsonValueKind.True,
                    PowerPierce = sh.TryGetProperty("powerPierce", out var sp2) && sp2.ValueKind == JsonValueKind.True, PowerShots = Math.Max(1, (int)JsonUtil.F(sh, "powerShots", 1)), PowerSpread = JsonUtil.F(sh, "powerSpread"),
                };
            if (JsonUtil.S(e, "keysFrom") != "") keysFrom.Add((w, JsonUtil.S(e, "keysFrom")));
            _d[t.Name] = w;
        }
        // "keysFrom": a type without hand poses of its own borrows another type's (swing i from its swing i, the power attack's from its power attack)
        foreach (var (w, from) in keysFrom)
        {
            if (!_d.TryGetValue(from, out var src) || src.Combo.Count == 0) continue;
            for (int i = 0; i < w.Combo.Count; i++) if (w.Combo[i].Keys.Length == 0) w.Combo[i].Keys = src.Combo[Math.Min(i, src.Combo.Count - 1)].Keys;
            if (w.Power.Keys.Length == 0) w.Power.Keys = src.Power.Keys;
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
        if (wt == "unarmed") return (null, null);   // fists: nothing in the hand
        var km = KitModelFor(wt, it);
        if (km.Model != "" && BuildKit(parent, wt, km, it) is { } kr) return kr;
        int rar = it?.Rar ?? 0; var rc = Items.RarityColor(rar);
        var steel = M(new Color(.78f, .8f, .85f), .85f, .3f); var dark = M(new Color(.32f, .33f, .36f), .8f, .4f);
        var wood = Gfx.Tex(M(new Color(.42f, .28f, .16f)), "grain", .4f, true, .6f); var wrap = Gfx.Tex(M(new Color(.28f, .17f, .1f)), "cloth", .12f);
        var gold = M(new Color(.88f, .7f, .32f), .9f, .3f); var gem = M(rc, .2f, .2f, rar >= 2 ? 2.2f : .8f);
        var accent = rar >= 3 ? gold : rar >= 1 ? M(rc.Darkened(.25f), .7f, .35f) : dark;
        if (rar >= 4) { steel.EmissionEnabled = true; steel.Emission = rc; steel.EmissionEnergyMultiplier = .08f; steel.AlbedoColor = steel.AlbedoColor.Lerp(rc, .12f); }
        Node3D? b = null, t = null;
        float size = SizeOf(it) * Grand(wt);   // A-11: the built-in meshes grow with the item's size too (the marks with them); x the overall weapon scale
        Node3D Mark(float z) { var n = new Node3D { Position = new Vector3(0, 0, z * size) }; parent.AddChild(n); return n; }
        int before = parent.GetChildCount();
        switch (wt)
        {
            case "whip":
                // A-11: a short handle and a lash of tapering segments that droops toward the tip
                Add(parent, new CylinderMesh { TopRadius = .024f, BottomRadius = .028f, Height = .26f, RadialSegments = 8 }, wrap, new Vector3(0, 0, .02f), new Vector3(H, 0, 0));
                Add(parent, new SphereMesh { Radius = .034f, Height = .068f, RadialSegments = 8, Rings = 4 }, accent, new Vector3(0, 0, .16f));
                {
                    const int seg = 12; Vector3 Pt(float u) => new(0, -.55f * u * u, -.1f - 2.3f * u);
                    for (int k = 0; k < seg; k++)
                    {
                        var p0 = Pt(k / (float)seg); var p1 = Pt((k + 1) / (float)seg); var y = (p1 - p0).Normalized(); var x = Vector3.Right; var z = x.Cross(y).Normalized();
                        float r = .02f - .015f * (k / (float)seg);
                        parent.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = (p1 - p0).Length() + .01f, RadialSegments = 5 }, MaterialOverride = wrap, Transform = new Transform3D(new Basis(x, y, z), (p0 + p1) / 2) });
                    }
                }
                Add(parent, new SphereMesh { Radius = .028f, Height = .056f, RadialSegments = 6, Rings = 3 }, gem, new Vector3(0, -.55f, -2.4f));
                b = Mark(-.6f); t = Mark(-2.4f); break;
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
        if (Mathf.Abs(size - 1f) > .005f)
            for (int i = before; i < parent.GetChildCount(); i++)
                if (parent.GetChild(i) is MeshInstance3D sm) { sm.Position *= size; sm.Scale *= size; }   // the marks were placed at their scaled spot already
        Gfx.MergeChildren(parent);
        return (b, t);
    }

    /// <summary>
    /// A kit weapon model under parent. Kit weapons hold the grip at the origin and point +Y, so they are turned to point -Z like the built-in meshes;
    /// bows stay upright (limbs along Y, as the built-in bow). Melee weapons get trail markers from the blade's length. Null if the file is missing.
    /// </summary>
    /// <summary>A kit tool model (pickaxe) under parent, pointing -Z like the weapons; false if the file is missing.</summary>
    public static bool BuildTool(Node3D parent, string model, float scale = 1f) => BuildKit(parent, "tool", new KitPick(model, scale), null) != null;
    static (Node3D?, Node3D?)? BuildKit(Node3D parent, string wt, KitPick pick, Item? it)
    {
        string name = pick.Model;
        if (!_kitScenes.TryGetValue(name, out var ps)) _kitScenes[name] = ps = ResourceLoader.Exists(KitPath(name)) ? GD.Load<PackedScene>(KitPath(name)) : null;
        if (ps?.Instantiate() is not Node3D model) { GD.PushWarning("[WEAPONS] missing kit model " + name); return null; }
        // size: the listed scale for this name, and a small stable variation per item so two swords of a kind differ
        // (A-11: an item with a rolled size uses that instead of the name jitter, so the model matches its reach)
        float jit = it != null && Mathf.Abs(it.Size - 1f) > .005f ? SizeOf(it) : 1f + (((Hash((it?.Name ?? "") + "#size") % 1000) / 1000f) * 2f - 1f) * _jitter, sc = pick.Scale * jit * (wt == "tool" ? 1f : Grand(wt));   // x the overall weapon scale (not the pickaxe)
        model.Scale = Vector3.One * sc;
        if (wt != "bow") model.Rotation = new Vector3(-H, 0, 0);
        parent.AddChild(model);
        // colour: the element tints the metal and gems (vertex colours stay, multiplied); GearFx adds glow, light and sparks on top
        var ec = GearFx.Get(it?.Elem)?.Col;
        foreach (var n in model.FindChildren("*", "MeshInstance3D", true, false))
            if (n is MeshInstance3D mi && mi.Mesh != null)
                for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    if (mi.GetActiveMaterial(s) is BaseMaterial3D bm)
                    {
                        var src = GothicMats.For(bm.ResourceName) ?? bm;   // realistic shared material for G_* names
                        var d = (BaseMaterial3D)src.Duplicate(); d.VertexColorUseAsAlbedo = true;
                        string mn = bm.ResourceName ?? "";
                        if (ec != null && (mn.Contains("Metal") || mn.Contains("Glow"))) d.AlbedoColor = src.AlbedoColor * Colors.White.Lerp(ec.Value.Lightened(.2f), _elemTint);
                        mi.SetSurfaceOverrideMaterial(s, d);
                    }
        float len = 0;
        foreach (var mi in model.FindChildren("*", "MeshInstance3D", true, false)) if (mi is MeshInstance3D m && m.Mesh != null) len = Mathf.Max(len, (m.Transform * m.Mesh.GetAabb()).End.Y);
        len *= sc;
        Node3D? b = null, t = null;
        if (wt != "tool" && _d.TryGetValue(wt, out var kd) && !kd.Ranged && wt != "unarmed" && len > .1f)
        {
            b = new Node3D { Position = new Vector3(0, 0, -len * .3f) }; parent.AddChild(b);
            t = new Node3D { Position = new Vector3(0, 0, -len) }; parent.AddChild(t);
        }
        return (b, t);
    }
}
