using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// Gear fit bot. Run:  godot --path . -- --fit            (check + screenshots, bot_out/fit/)
///                     godot --path . -- --fit --tune     (also search better hold values -> bot_out/fit/fit_suggest.json)
///                     add --only=weapon,shield,armor,head to limit the categories.
/// Builds mannequins (data/avatar.json bodies, data/races.json looks) in a plain studio, dresses them in every weapon (each kit model),
/// shield and armor piece (data/items.json itemNames), holds each pose from data/fit.json and measures:
///   held items (weapons, shields, helmets, cape) against the body, as capsules between bones whose radii come from the body mesh;
///   the grip touching the hand, weapon length, items under the floor, skinned parts bound to bones that exist and sitting on the body.
/// Each case is photographed from three sides with its verdict printed on the picture; contact sheets per category.
/// Exit code 0 = no FAIL lines. Thresholds and pose lists: data/fit.json.
/// </summary>
public partial class FitBot : Node
{
    public static bool Wanted() => OS.GetCmdlineUserArgs().Contains("--fit");

    // ------------------------------------------------------------------ setup
    JsonDocument _cfgDoc = null!; JsonElement Cfg => _cfgDoc.RootElement;
    JsonElement Lim => Cfg.GetProperty("limits");
    string _dir = ""; bool _tune; HashSet<string>? _only;
    readonly StringBuilder _log = new(); int _pass, _warn, _fail;
    readonly Dictionary<string, List<string>> _shots = new();
    Node3D _stage = null!; Label _label = null!; Camera3D _cam = null!;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        _cfgDoc = JsonUtil.Load("res://data/fit.json");
        var args = OS.GetCmdlineUserArgs(); _tune = args.Contains("--tune");
        var only = args.FirstOrDefault(a => a.StartsWith("--only=")); if (only != null) _only = new HashSet<string>(only[7..].Split(','));
        _dir = ProjectSettings.GlobalizePath("res://") + "bot_out/fit/"; DirAccess.MakeDirRecursiveAbsolute(_dir);
        foreach (var f in DirAccess.GetFilesAt(_dir)) if (f.EndsWith(".png")) DirAccess.RemoveAbsolute(_dir + f);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        Studio();
        _ = Run();
    }

    void Studio()
    {
        _stage = new Node3D { Name = "FitStage" }; AddChild(_stage);
        var floor = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(10, 10) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.55f, .57f, .6f) } };
        AddChild(floor);
        // a 10 cm grid on the floor helps judge sizes in the pictures
        var grid = new StandardMaterial3D { AlbedoColor = new Color(.42f, .44f, .48f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        for (int i = -10; i <= 10; i++) { AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.01f, .002f, 4) }, MaterialOverride = grid, Position = new Vector3(i * .25f, .001f, 0) }); }
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, 150, 0), ShadowEnabled = true, LightEnergy = 1.2f });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-20, -30, 0), LightEnergy = .35f });
        var env = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(.66f, .72f, .8f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = .55f, TonemapMode = Godot.Environment.ToneMapper.Filmic };
        AddChild(new WorldEnvironment { Environment = env });
        _cam = new Camera3D { Fov = 34, Current = true }; AddChild(_cam); _cam.Position = new Vector3(0, 1.0f, -4.9f); _cam.LookAt(new Vector3(0, .92f, 0), Vector3.Up);
        var layer = new CanvasLayer(); AddChild(layer);
        _label = new Label { Position = new Vector2(14, 10), Size = new Vector2(1250, 120) };
        _label.AddThemeFontSizeOverride("font_size", 22); _label.AddThemeConstantOverride("outline_size", 6); _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(_label);
    }

    void Say(string s) { GD.Print("[FIT] " + s); _log.AppendLine(s); }
    async Task Frames(int n) { for (int i = 0; i < n; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); }

    // ------------------------------------------------------------------ cases
    record Case(string Cat, string Name, string Body, string Race, Dictionary<string, Item?> Eq, string Wt, string Poses);

    List<string> Races_(string key)
    {
        var l = new List<string>(); if (Cfg.TryGetProperty(key, out var a)) foreach (var x in a.EnumerateArray()) l.Add(x.GetString() ?? "");
        return l.Count > 0 ? l : TwinRealms.Races.All.Select(r => r.Id).ToList();
    }
    List<string> Strs(JsonElement e, string k) { var l = new List<string>(); if (e.TryGetProperty(k, out var a)) foreach (var x in a.EnumerateArray()) l.Add(x.GetString() ?? ""); return l; }
    bool Want(string cat) => _only == null || _only.Contains(cat);

    List<Case> Cases()
    {
        var cs = new List<Case>(); var bodies = Strs(Cfg, "bodies");
        using var items = JsonUtil.Load("res://data/items.json"); var names = items.RootElement.GetProperty("itemNames");
        Dictionary<string, Item?> Eq(params (string slot, Item it)[] l) { var d = new Dictionary<string, Item?>(); foreach (var (s, it) in l) d[s] = it; return d; }
        if (Want("weapon"))
            foreach (var t in Weapons.Types)
            {
                var models = Weapons.KitModels(t).ToList(); if (models.Count == 0) models.Add("");
                foreach (var m in models) foreach (var b in bodies) foreach (var r in Races_("weaponRaces"))
                    cs.Add(new Case("weapon", $"{t}/{(m == "" ? "builtin" : m)}", b, r, Eq(("weapon", new Item { Name = m == "" ? t : m, Slot = "weapon", Wt = t, Model = m == "" ? null : m, Two = Weapons.Get(t).Hands > 1 })), t, "armed"));
            }
        if (Want("shield"))
        {
            string sword = Weapons.KitModels("sword").FirstOrDefault() ?? "";
            foreach (var n in Strs(names, "shield")) foreach (var b in bodies) foreach (var r in Races_("shieldRaces"))
                cs.Add(new Case("shield", n, b, r, Eq(("weapon", new Item { Name = "Sword", Slot = "weapon", Wt = "sword", Model = sword == "" ? null : sword }), ("shield", new Item { Name = n, Slot = "shield" })), "sword", "shield"));
        }
        if (Want("armor"))
            foreach (var slot in new[] { "armor", "hands", "feet" })
                foreach (var n in Strs(names, slot)) foreach (var b in bodies) foreach (var r in Races_("armorRaces"))
                    cs.Add(new Case("armor", $"{slot}/{n}", b, r, Eq((slot, new Item { Name = n, Slot = slot })), "none", "unarmed"));
        if (Want("cape"))
            foreach (var n in Strs(names, "cape")) foreach (var b in bodies) foreach (var r in Races_("armorRaces"))
                cs.Add(new Case("cape", n, b, r, Eq(("cape", new Item { Name = n, Slot = "cape" })), "none", "unarmed"));
        if (Want("head"))
            foreach (var n in Strs(names, "head")) foreach (var b in bodies) foreach (var r in Races_("headRaces"))
                cs.Add(new Case("head", n, b, r, Eq(("head", new Item { Name = n, Slot = "head" })), "none", "unarmed"));
        return cs;
    }

    // ------------------------------------------------------------------ the body as capsules
    /// <summary>A capsule from one bone point to another (each a bone index plus an offset in that bone's space), radius from the mesh.</summary>
    sealed class Seg { public string Name = ""; public int A, B; public Vector3 OffA, OffB; public float R = .05f; }
    sealed class BodyModel { public Skeleton3D Sk = null!; public List<Seg> Segs = new(); public string Missing = ""; }   // Sk is only valid while the mannequin exists (not during Tune)

    static readonly (string name, string a, string b)[] SegDefs =
    {
        ("hips", "pelvis", "spine_01"), ("torso", "spine_01", "spine_03"), ("chest", "spine_03", "neck_01"), ("neck", "neck_01", "Head"), ("head", "Head", "@top"),
        ("upperarm_l", "upperarm_l", "lowerarm_l"), ("forearm_l", "lowerarm_l", "hand_l"), ("upperarm_r", "upperarm_r", "lowerarm_r"), ("forearm_r", "lowerarm_r", "hand_r"),
        ("hand_l", "hand_l", "@ext"), ("hand_r", "hand_r", "@ext"),
        ("thigh_l", "thigh_l", "calf_l"), ("shin_l", "calf_l", "foot_l"), ("foot_l", "foot_l", "ball_l"),
        ("thigh_r", "thigh_r", "calf_r"), ("shin_r", "calf_r", "foot_r"), ("foot_r", "foot_r", "ball_r"),
    };

    BodyModel MeasureBody(Avatar av)
    {
        var bm = new BodyModel { Sk = av.Skel! }; var sk = bm.Sk; var miss = new List<string>();
        var verts = new List<Vector3>();
        if (av.BodyMesh is { Mesh: { } mesh } bmi)
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) foreach (var v in (Vector3[])mesh.SurfaceGetArrays(s)[(int)Mesh.ArrayType.Vertex]) verts.Add(bmi.Transform * v);
        float topY = verts.Count > 0 ? verts.Max(v => v.Y) : 1.8f;
        foreach (var (name, a, b) in SegDefs)
        {
            int ia = sk.FindBone(a), ib = b.StartsWith("@") ? ia : sk.FindBone(b);
            if (ia < 0 || ib < 0) { miss.Add(ia < 0 ? a : b); continue; }
            var seg = new Seg { Name = name, A = ia, B = ib };
            var hr = sk.GetBoneGlobalRest(ia);
            if (b == "@top") seg.OffB = hr.AffineInverse() * new Vector3(hr.Origin.X, hr.Origin.Y + (topY - hr.Origin.Y) * .7f, hr.Origin.Z);
            if (b == "@ext")
            {
                // the hand: from the wrist on along the forearm, to about the finger tips
                int pa = sk.GetBoneParent(ia); var dir = pa >= 0 ? (hr.Origin - sk.GetBoneGlobalRest(pa).Origin).Normalized() : Vector3.Down;
                seg.OffB = hr.AffineInverse() * (hr.Origin + dir * .15f);
            }
            bm.Segs.Add(seg);
        }
        bm.Missing = string.Join(",", miss.Distinct());
        // radius: each body vertex belongs to the nearest capsule axis; the radius is a percentile of those distances
        var rest = new Dictionary<Seg, (Vector3, Vector3)>(); foreach (var s in bm.Segs) rest[s] = Ends(sk, s, true);
        var dist = bm.Segs.ToDictionary(s => s, _ => new List<float>());
        foreach (var v in verts)
        {
            Seg? best = null; float bd = 1e9f;
            foreach (var s in bm.Segs) { var (p, q) = rest[s]; float d = SegDist(v, p, q); if (d < bd) { bd = d; best = s; } }
            if (best != null) dist[best].Add(bd);
        }
        float pc = JsonUtil.F(Cfg, "radiusPercentile", .7f);
        foreach (var s in bm.Segs) { var l = dist[s]; if (l.Count > 8) { l.Sort(); s.R = l[(int)(l.Count * pc)]; } }
        return bm;
    }
    static (Vector3, Vector3) Ends(Skeleton3D sk, Seg s, bool rest)
    {
        var ta = rest ? sk.GetBoneGlobalRest(s.A) : sk.GetBoneGlobalPose(s.A); var tb = rest ? sk.GetBoneGlobalRest(s.B) : sk.GetBoneGlobalPose(s.B);
        return (ta * s.OffA, tb * s.OffB);
    }
    static float SegDist(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a; float l2 = ab.LengthSquared(); float t = l2 < 1e-8f ? 0 : Mathf.Clamp((p - a).Dot(ab) / l2, 0, 1);
        return p.DistanceTo(a + ab * t);
    }

    // ------------------------------------------------------------------ held items
    /// <summary>A holder's mesh vertices in the holder's own space (so a new hold transform can be tried without touching the scene).</summary>
    sealed class Held { public string Slot = "", Bone = ""; public int BoneIdx; public Node3D Node = null!; public Transform3D Xf; public Basis Rest; public List<Vector3> Local = new(); public List<Vector3> Tris = new(); }

    static List<Held> HeldItems(Avatar av, string slot)
    {
        var res = new List<Held>(); var sk = av.Skel!;
        foreach (var n in av.SlotNodes(slot))
        {
            if (n is not Node3D h || !h.HasMeta("bone")) continue;
            var held = new Held { Slot = slot, Bone = (string)h.GetMeta("bone"), Node = h, Xf = h.Transform }; held.BoneIdx = sk.FindBone(held.Bone); if (held.BoneIdx >= 0) held.Rest = sk.GetBoneGlobalRest(held.BoneIdx).Basis.Orthonormalized();
            var inv = h.GlobalTransform.AffineInverse(); var all = new List<Vector3>();
            foreach (var c in h.FindChildren("*", "MeshInstance3D", true, false))
                if (c is MeshInstance3D mi && mi.Mesh != null && mi.Visible)
                {
                    var x = inv * mi.GlobalTransform;
                    for (int s = 0; s < mi.Mesh.GetSurfaceCount(); s++)
                    {
                        var arr = mi.Mesh.SurfaceGetArrays(s); var vs = (Vector3[])arr[(int)Mesh.ArrayType.Vertex];
                        foreach (var v in vs) all.Add(x * v);
                        if (slot != "head" && slot != "weapon") continue;   // triangles: the helmet test and the grip check
                        var idx = arr[(int)Mesh.ArrayType.Index].VariantType == Variant.Type.Nil ? null : (int[])arr[(int)Mesh.ArrayType.Index];
                        if (idx != null) foreach (int i in idx) held.Tris.Add(x * vs[i]); else foreach (var v in vs) held.Tris.Add(x * v);
                    }
                }
            int stride = Math.Max(1, all.Count / 600); for (int i = 0; i < all.Count; i += stride) held.Local.Add(all[i]);
            if (held.BoneIdx >= 0 && held.Local.Count > 0) res.Add(held);
        }
        return res;
    }

    /// <summary>One pose: every bone's global pose (skeleton space) and the skeleton's world transform.</summary>
    sealed class Snap { public string Name = ""; public Transform3D[] Bones = Array.Empty<Transform3D>(); public Transform3D World; public bool Warn; }
    static Snap Snapshot(Skeleton3D sk, string name, bool warn)
    {
        var s = new Snap { Name = name, World = sk.GlobalTransform, Warn = warn, Bones = new Transform3D[sk.GetBoneCount()] };
        for (int i = 0; i < s.Bones.Length; i++) s.Bones[i] = sk.GetBoneGlobalPose(i);
        return s;
    }

    /// <summary>Clipping of held vertices (holder transform xf, extra scale about the holder origin) into body capsules, in one pose.</summary>
    struct Clip { public float Frac, Depth, Ground, Gap; public string Where; }
    static Clip Measure(BodyModel bm, Snap sn, Held h, Transform3D xf, float scale, HashSet<string> skip, string gapSeg = "")
    {
        var caps = new List<(Seg s, Vector3 a, Vector3 b)>();
        foreach (var s in bm.Segs) caps.Add((s, sn.Bones[s.A] * s.OffA, sn.Bones[s.B] * s.OffB));
        var toSk = sn.Bones[h.BoneIdx] * xf; int inside = 0; float depth = 0, ground = 0, gap = 1e9f; string where = "";
        foreach (var lv in h.Local)
        {
            var p = toSk * (lv * scale);
            foreach (var (s, a, b) in caps)
            {
                float d = SegDist(p, a, b);
                if (s.Name == gapSeg) gap = Mathf.Min(gap, d - s.R);
                if (skip.Contains(s.Name)) continue;
                if (d < s.R * .92f) { inside++; float dp = s.R - d; if (dp > depth) { depth = dp; where = s.Name; } break; }
            }
            float wy = (sn.World * p).Y; if (wy < -ground) ground = -wy;
        }
        return new Clip { Frac = h.Local.Count > 0 ? inside / (float)h.Local.Count : 0, Depth = depth, Ground = ground, Gap = gap, Where = where };
    }

    /// <summary>
    /// Helmet against the head, the way it looks: rays go out from the head capsule's axis through its upper surface.
    /// A direction where the helmet has surface but all of it is under the scalp = the head shows through (Poke).
    /// Gap = how far above the scalp the helmet's first surface is, looking up. Hidden inner faces (a closed cap bottom) do not count.
    /// </summary>
    struct HelmFit { public float Poke, Gap; public int Covered; }
    static HelmFit Helm(BodyModel bm, Snap sn, Held h, Transform3D xf, float scale)
    {
        var head = bm.Segs.FirstOrDefault(s => s.Name == "head"); var r = new HelmFit { Gap = -1 }; if (head == null || h.Tris.Count < 3) return r;
        var a = sn.Bones[head.A] * head.OffA; var b = sn.Bones[head.B] * head.OffB; var up = (b - a).Normalized();
        var side = up.Cross(Vector3.Forward).LengthSquared() > .1f ? up.Cross(Vector3.Forward).Normalized() : up.Cross(Vector3.Right).Normalized(); var fwd = side.Cross(up);
        var toSk = sn.Bones[h.BoneIdx] * xf; var tri = new Vector3[h.Tris.Count]; for (int i = 0; i < tri.Length; i++) tri[i] = toSk * (h.Tris[i] * scale);
        int poke = 0; float gap = 1e9f;
        void Cast(Vector3 o, Vector3 dir, bool top)
        {
            float near = 1e9f, far = -1;
            for (int t = 0; t + 2 < tri.Length; t += 3) { float d = Ray(o, dir, tri[t], tri[t + 1], tri[t + 2]); if (d >= 0) { near = Mathf.Min(near, d); far = Mathf.Max(far, d); } }
            if (far < 0) return;
            r.Covered++; if (far < head.R * .97f) poke++;
            if (top) gap = Mathf.Min(gap, near - head.R);
        }
        // sides: level rays from several heights along the head axis (bands like circlets sit at one height)
        foreach (float ht in new[] { .2f, .4f, .6f, .8f, 1f })
            for (int k = 0; k < 16; k++) { float az = k * Mathf.Tau / 16; Cast(a.Lerp(b, ht), (side * Mathf.Cos(az) + fwd * Mathf.Sin(az)).Normalized(), false); }
        // the dome: rays from the top of the axis, upward
        for (int ring = 1; ring <= 4; ring++)
        {
            float el = ring / 4f * Mathf.Pi / 2; int n = ring == 4 ? 1 : 12;
            for (int k = 0; k < n; k++) { float az = k * Mathf.Tau / n; Cast(b, (up * Mathf.Sin(el) + (side * Mathf.Cos(az) + fwd * Mathf.Sin(az)) * Mathf.Cos(el)).Normalized(), el > .9f); }
        }
        r.Poke = r.Covered > 0 ? poke / (float)r.Covered : 0; r.Gap = gap < 1e8f ? gap : 0;
        return r;
    }
    /// <summary>Ray-triangle distance (Moller-Trumbore), -1 when missed.</summary>
    static float Ray(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var e1 = v1 - v0; var e2 = v2 - v0; var p = d.Cross(e2); float det = e1.Dot(p); if (Mathf.Abs(det) < 1e-9f) return -1;
        float inv = 1f / det; var tv = o - v0; float u = tv.Dot(p) * inv; if (u < 0 || u > 1) return -1;
        var q = tv.Cross(e1); float v = d.Dot(q) * inv; if (v < 0 || u + v > 1) return -1;
        float t = e2.Dot(q) * inv; return t > 0 ? t : -1;
    }

    /// <summary>How far the cape hangs away from straight down, in degrees (its holder's -Y in the world).</summary>
    static float CapeTilt(Snap sn, Held h, Transform3D xf, Basis capeRot)
    {
        var down = (sn.World.Basis * sn.Bones[h.BoneIdx].Basis * xf.Basis * capeRot * Vector3.Down).Normalized();
        return Mathf.RadToDeg(down.AngleTo(Vector3.Down));
    }

    // ------------------------------------------------------------------ mannequins and poses
    Avatar Mannequin(Case c, float x, float yaw)
    {
        var ps = new PlayerStats { Race = c.Race, Body = c.Body, Class = "warrior" }; var ap = TwinRealms.Races.Look(ps);
        var av = new Avatar { Name = "M", Position = new Vector3(x, 0, 0), Rotation = new Vector3(0, yaw, 0) }; _stage.AddChild(av);
        av.Build(ap, "fit");
        av.Dress(s => c.Eq.GetValueOrDefault(s), c.Wt, Classes.Get("warrior").Tunic);
        if (av.Cape != null) av.Cape.Rotation = new Vector3(CapeIdle, 0, 0);   // what Player sets when standing (-capeX * .8 with capeX about -.05)
        return av;
    }
    const float CapeIdle = .04f;
    List<(string key, float frac, int step, bool warn)> Poses(string set)
    {
        var l = new List<(string, float, int, bool)>(); var p = Cfg.GetProperty("poses");
        void Add(string k, bool warn) { if (p.TryGetProperty(k, out var a)) foreach (var e in a.EnumerateArray()) l.Add((e[0].GetString() ?? "idle", e[1].GetSingle(), e.GetArrayLength() > 2 ? e[2].GetInt32() : 0, warn)); }
        Add(set, false); if (set == "armed") Add("warn", true);
        return l;
    }

    // ------------------------------------------------------------------ run
    sealed class Result { public Case C = null!; public BodyModel Bm = null!; public List<Snap> Snaps = new(); public Dictionary<string, List<Held>> Held = new(); }
    readonly List<Result> _results = new(); readonly HashSet<string> _sane = new();

    async Task Run()
    {
        await Frames(3);
        if (!Avatar.Available) { Say("FAIL  character models not installed"); _fail++; Finish(); return; }
        var cases = Cases(); Say($"{cases.Count} cases");
        int n = 0;
        foreach (var c in cases)
        {
            n++;
            try { await One(c, n); }
            catch (Exception e) { Fail(c, "threw " + e.Message); }
        }
        if (_tune) try { Tune(); } catch (Exception e) { _fail++; Say("FAIL  tune threw: " + e); }
        try { Sheets(); } catch (Exception e) { Say("sheets threw: " + e.Message); }
        Finish();
    }

    void Fail(Case c, string why) { _fail++; Say($"FAIL  {c.Cat} {c.Name} [{c.Body} {c.Race}]: {why}"); }

    async Task One(Case c, int n)
    {
        bool left = c.Wt == "bow";
        var a = Mannequin(c, .95f, .55f); var b = Mannequin(c, 0, left ? -Mathf.Pi / 2 : Mathf.Pi / 2); var d = Mannequin(c, -.95f, Mathf.Pi - .55f);
        await Frames(2);
        // camera: whole bodies, or the heads close up for helmets
        if (c.Cat == "head" && b.Skel!.FindBone("Head") is int hb && hb >= 0)
        {
            var hp = b.Skel.GlobalTransform * b.Skel.GetBoneGlobalPose(hb).Origin; _cam.Position = new Vector3(0, hp.Y + .12f, -2.5f); _cam.LookAt(new Vector3(0, hp.Y + .05f, 0), Vector3.Up);
        }
        else { _cam.Position = new Vector3(0, 1.0f, -4.9f); _cam.LookAt(new Vector3(0, .92f, 0), Vector3.Up); }
        var r = new Result { C = c, Bm = MeasureBody(a) };
        if (r.Bm.Missing != "") Say($"note: bones not found for capsules: {r.Bm.Missing}");
        foreach (var slot in new[] { "weapon", "shield", "head", "cape" }) r.Held[slot] = HeldItems(a, slot);
        var poses = Poses(c.Poses);
        foreach (var (key, frac, step, warn) in poses) { a.PoseAt(key, frac, step); r.Snaps.Add(Snapshot(a.Skel!, $"{key}@{frac:0.##}" + (step > 0 ? $"#{step}" : ""), warn)); }
        if (!_sane.Contains(c.Body))
        {
            // once per body: the measured capsules, and proof that holding a pose really moves the skeleton we measure
            _sane.Add(c.Body); int hr = a.Skel!.FindBone("hand_r");
            var bn = new List<string>(); for (int i = 0; i < a.Skel!.GetBoneCount(); i++) bn.Add(a.Skel.GetBoneName(i)); Say($"body {c.Body}: bones " + string.Join(" ", bn));
            Say($"body {c.Body}: capsule radii " + string.Join(", ", r.Bm.Segs.Select(s => $"{s.Name} {s.R * 100:0.0}")) + " cm");
            if (hr >= 0) Say($"body {c.Body}: hand_r per pose " + string.Join(" | ", r.Snaps.Select(s => $"{s.Name} {s.Bones[hr].Origin.X:0.00},{s.Bones[hr].Origin.Y:0.00},{s.Bones[hr].Origin.Z:0.00}")));
        }

        var issues = new List<string>(); var warns = new List<string>(); var info = new List<string>(); string worstPose = "";
        var L = Lim; float clipFrac = JsonUtil.F(L, "clipFrac", .04f), clipDepth = JsonUtil.F(L, "clipDepth", .035f);
        float worstScore = 0;
        void CheckHeld(string slot, HashSet<string> skip, string label, float fracLim, string gapSeg = "")
        {
            foreach (var h in r.Held[slot])
                foreach (var sn in r.Snaps)
                {
                    var m = Measure(r.Bm, sn, h, h.Xf, 1, skip, gapSeg);
                    bool bad = m.Frac > fracLim || m.Depth > clipDepth;
                    string txt = $"{label} clips {m.Where} {m.Frac:P0} / {m.Depth * 100:0.0} cm in {sn.Name}";
                    if (bad) { (sn.Warn ? warns : issues).Add(txt); float sc = m.Frac + m.Depth * 10; if (!sn.Warn && sc > worstScore) { worstScore = sc; worstPose = sn.Name; } }
                    if (m.Ground > JsonUtil.F(L, "groundDepth", .03f)) warns.Add($"{label} {m.Ground * 100:0} cm under the floor in {sn.Name}");
                }
        }
        // weapon
        if (c.Eq.TryGetValue("weapon", out var w) && w != null)
        {
            var wh = r.Held["weapon"];
            if (wh.Count == 0) issues.Add("no weapon shown");
            foreach (var h in wh)
            {
                bool two = Weapons.Get(c.Wt).Hands > 1;
                var skip = WeaponSkip(h.Bone, two || c.Wt == "bow");
                float grip = GripGap(h); var bb = new Aabb(h.Local[0], Vector3.Zero); foreach (var v in h.Local) bb = bb.Expand(v);
                float len = bb.Size[(int)bb.GetLongestAxisIndex()] * a.Skel!.GlobalTransform.Basis.Scale.Y;
                info.Add($"grip {grip * 100:0.0} cm, length {len:0.00} m");
                if (grip > JsonUtil.F(L, "gripGap", .05f)) issues.Add($"grip is {grip * 100:0} cm from the hand (model origin not at the grip?)");
                if (Cfg.GetProperty("lengths").TryGetProperty(c.Wt, out var lr) && (len < lr[0].GetSingle() || len > lr[1].GetSingle())) issues.Add($"length {len:0.00} m outside {lr[0].GetSingle()}-{lr[1].GetSingle()}");
                CheckHeld("weapon", skip, "weapon", clipFrac);
            }
        }
        if (c.Eq.TryGetValue("shield", out var shi) && shi != null)
        {
            if (r.Held["shield"].Count == 0) issues.Add("no shield shown");
            CheckHeld("shield", ShieldSkip, "shield", clipFrac, "forearm_l");
            foreach (var h in r.Held["shield"]) { var m = Measure(r.Bm, r.Snaps[0], h, h.Xf, 1, new(), "forearm_l"); info.Add($"shield gap to forearm {m.Gap * 100:0.0} cm"); if (m.Gap > JsonUtil.F(L, "shieldGap", .08f)) issues.Add($"shield floats {m.Gap * 100:0} cm off the forearm"); }
        }
        if (c.Cat == "head")
        {
            var hh = r.Held["head"];
            foreach (var h in hh)
                foreach (var sn in r.Snaps)
                {
                    var hf = Helm(r.Bm, sn, h, h.Xf, 1);
                    var m = Measure(r.Bm, sn, h, h.Xf, 1, new HashSet<string> { "head", "neck" });
                    if (sn == r.Snaps[0]) info.Add($"helmet: head shows through {hf.Poke:P0} of {hf.Covered} covered directions, {hf.Gap * 100:0.0} cm above the scalp");
                    if (hf.Poke > JsonUtil.F(L, "helmClipFrac", .06f)) issues.Add($"head shows through the helmet ({hf.Poke:P0}) in {sn.Name}");
                    if (hf.Gap > JsonUtil.F(L, "helmGap", .04f)) issues.Add($"helmet floats {hf.Gap * 100:0} cm above the head in {sn.Name}");
                    if (m.Frac > clipFrac) issues.Add($"helmet clips {m.Where} ({m.Frac:P0}) in {sn.Name}");
                }
        }
        // a cape (an equipment slot) should hang, not stick out, and stay out of the body
        if (c.Eq.TryGetValue("cape", out var cpi) && cpi != null && r.Held["cape"].Count == 0) issues.Add("no cape shown");
        CheckHeld("cape", CapeSkip, "cape", clipFrac);
        foreach (var h in r.Held["cape"])
        {
            float tilt = CapeTilt(r.Snaps[0], h, h.Xf, new Basis(Vector3.Right, CapeIdle));
            info.Add($"cape {tilt:0} deg from hanging");
            if (tilt > JsonUtil.F(L, "capeTilt", 25f)) issues.Add($"cape sticks out {tilt:0} deg from hanging straight in {r.Snaps[0].Name}");
        }
        // skinned parts: bound to bones we have, and on the body
        var sk = a.Skel!; var body = a.BodyMesh?.Mesh?.GetAabb() ?? new Aabb(); float off = JsonUtil.F(L, "partOffBody", .15f);
        foreach (var slot in new[] { "armor", "hands", "feet", "head" })
        {
            bool wanted = c.Eq.TryGetValue(slot, out var it) && it != null; int shown = 0;
            foreach (var nn in a.SlotNodes(slot))
            {
                if (nn is Node3D h3 && h3.HasMeta("bone")) { shown++; continue; }
                if (nn is not MeshInstance3D mi) continue; shown++;
                if (mi.Skin != null) { int bad = 0; for (int i = 0; i < mi.Skin.GetBindCount(); i++) if (sk.FindBone(mi.Skin.GetBindName(i)) < 0) bad++; if (bad > 0) issues.Add($"{slot} part {mi.Name}: {bad} bones not on the body skeleton"); }
                var pb = mi.Mesh?.GetAabb() ?? new Aabb(); var grow = body.Grow(off);
                if (!grow.Encloses(pb)) issues.Add($"{slot} part {mi.Name} reaches {Outside(pb, body):0.00} m outside the body");
            }
            if (wanted && shown == 0) issues.Add($"{slot} '{it!.Name}' shows nothing");
        }

        string verdict = issues.Count > 0 ? "FAIL" : warns.Count > 0 ? "WARN" : "PASS";
        if (verdict == "FAIL") _fail++; else if (verdict == "WARN") _warn++; else _pass++;
        Say($"{verdict,-5} {c.Cat} {c.Name} [{c.Body} {c.Race}]  {string.Join("; ", info)}");
        foreach (var s in issues.Distinct().Take(6)) Say("        - " + s);
        foreach (var s in warns.Distinct().Take(4)) Say("        ~ " + s);

        // pictures: the first pose of the set, and the worst failing pose when it is another one
        var shotPoses = new List<int> { 0 }; int wi = r.Snaps.FindIndex(s => s.Name == worstPose); if (wi > 0) shotPoses.Add(wi);
        foreach (int pi in shotPoses)
        {
            var (key, frac, step, _) = poses[pi];
            foreach (var av in new[] { a, b, d }) av.PoseAt(key, frac, step);
            _label.Text = $"{verdict}  {c.Cat}: {c.Name}   [{c.Body} {c.Race}]   pose {r.Snaps[pi].Name}\n" + (issues.Count > 0 ? issues[0] : warns.Count > 0 ? "~ " + warns[0] : string.Join(", ", info));
            _label.AddThemeColorOverride("font_color", verdict == "FAIL" ? new Color(1, .45f, .4f) : verdict == "WARN" ? new Color(1, .85f, .4f) : new Color(.7f, 1, .7f));
            await Frames(3); await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            string file = $"{c.Cat}_{n:000}_{Safe(c.Name)}_{c.Body}_{c.Race}{(pi > 0 ? "_worst" : "")}.png";
            GetViewport().GetTexture().GetImage().SavePng(_dir + file);
            if (!_shots.TryGetValue(c.Cat, out var l)) _shots[c.Cat] = l = new(); l.Add(file);
        }
        if (_tune) _results.Add(r);
        foreach (var av in new[] { a, b, d }) av.QueueFree();
        await Frames(1);
    }
    /// <summary>How far the weapon's surface is from the hand (the holder origin): the nearest triangle edge, not the nearest vertex,
    /// because a long shaft has vertices only at its ends.</summary>
    static float GripGap(Held h)
    {
        if (h.Tris.Count < 3) return h.Local.Count > 0 ? h.Local.Min(v => v.Length()) : 1e9f;
        float best = 1e9f;
        for (int t = 0; t + 2 < h.Tris.Count; t += 3)
            best = Mathf.Min(best, Mathf.Min(SegDist(Vector3.Zero, h.Tris[t], h.Tris[t + 1]), Mathf.Min(SegDist(Vector3.Zero, h.Tris[t + 1], h.Tris[t + 2]), SegDist(Vector3.Zero, h.Tris[t + 2], h.Tris[t]))));
        return best;
    }
    /// <summary>Body parts a held item may touch: the holding hand and forearm (both for two-handed weapons and bows).</summary>
    static HashSet<string> WeaponSkip(string bone, bool both)
    {
        string s = bone.EndsWith("_l") ? "_l" : "_r"; var k = new HashSet<string> { "forearm" + s, "hand" + s };
        if (both) foreach (var x in new[] { "forearm_l", "forearm_r", "hand_l", "hand_r" }) k.Add(x);
        return k;
    }
    static readonly HashSet<string> ShieldSkip = new() { "forearm_l", "upperarm_l", "hand_l" }, CapeSkip = new() { "neck", "head", "upperarm_l", "upperarm_r" };
    static HashSet<string> AllBut(BodyModel bm, string keep) => bm.Segs.Where(s => s.Name != keep).Select(s => s.Name).ToHashSet();
    static float Outside(Aabb p, Aabb b) => Mathf.Max(0, Mathf.Max(Mathf.Max(b.Position.X - p.Position.X, p.End.X - b.End.X), Mathf.Max(Mathf.Max(b.Position.Y - p.Position.Y, p.End.Y - b.End.Y), Mathf.Max(b.Position.Z - p.Position.Z, p.End.Z - b.End.Z))));
    static string Safe(string s) => s.Replace('/', '-').Replace(' ', '_');

    // ------------------------------------------------------------------ tune
    /// <summary>
    /// Searches hold values that clip less: each weapon type's rotation (avatar.json weapons.&lt;type&gt;.rot), the shield hold, and each kit helmet's
    /// position and scale. Uses the poses already measured, so nothing in the scene moves. Writes fit_suggest.json; changes no data.
    /// </summary>
    void Tune()
    {
        var t = Cfg.GetProperty("tune"); var rotSteps = t.GetProperty("rotSteps").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        var posSteps = t.GetProperty("posSteps").EnumerateArray().Select(x => x.GetSingle()).ToArray(); var scaleSteps = t.GetProperty("scaleSteps").EnumerateArray().Select(x => x.GetSingle()).ToArray();
        float close = JsonUtil.F(t, "stayClose", .0004f);
        using var av = JsonUtil.Load("res://data/avatar.json"); var root = av.RootElement; var hold = root.GetProperty("hold");
        Vector3 V(JsonElement e, string k, Vector3 d) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : d;
        var outp = new Dictionary<string, object>();
        Say("--- tune");

        // weapons, per type: rotate the hold, keep the grip where it is
        var wout = new Dictionary<string, object>();
        foreach (var grp in _results.Where(r => r.C.Cat == "weapon").GroupBy(r => r.C.Wt))
        {
            Say($"tuning weapon {grp.Key} ({grp.Count()} cases)");
            var wc = root.GetProperty("weapons").GetProperty(grp.Key); bool left = Avatar.Str(wc, "hand") == "left"; var hs = hold.GetProperty(left ? "left" : "right");
            var pos = V(hs, "pos", Vector3.Zero); var rot0 = V(wc, "rot", V(hs, "rot", Vector3.Zero));
            float Score(Vector3 rot)
            {
                float sum = 0;
                foreach (var r in grp) foreach (var h in r.Held["weapon"])
                {
                    var skip = WeaponSkip(h.Bone, Weapons.Get(grp.Key).Hands > 1 || left);
                    var xf = Avatar.HoldXf(h.Rest, pos, rot);
                    foreach (var sn in r.Snaps.Where(s => !s.Warn)) { var m = Measure(r.Bm, sn, h, xf, 1, skip); sum += m.Frac + m.Depth * 8 + m.Ground * 4; }
                }
                return sum + close * (rot - rot0).Length();
            }
            var (best, s0, s1) = Search(rot0, Score, rotSteps, 3);
            wout[grp.Key] = new { rot = Arr(best), was = Arr(rot0), score_before = Mathf.Snapped(s0, .001f), score_after = Mathf.Snapped(s1, .001f) };
            Say($"weapon {grp.Key}: rot {rot0} -> {best}  score {s0:0.000} -> {s1:0.000}");
        }
        outp["weapons"] = wout;

        // shield hold
        var shields = _results.Where(r => r.C.Cat == "shield").ToList();
        if (shields.Count > 0)
        {
            var hs = hold.GetProperty("shield"); var pos0 = V(hs, "pos", Vector3.Zero); var rot0 = V(hs, "rot", Vector3.Zero);
            float Score(Vector3 pos, Vector3 rot)
            {
                float sum = 0;
                foreach (var r in shields) foreach (var h in r.Held["shield"])
                {
                    var xf = Avatar.HoldXf(h.Rest, pos, rot);
                    foreach (var sn in r.Snaps) { var m = Measure(r.Bm, sn, h, xf, 1, ShieldSkip, "forearm_l"); sum += m.Frac + m.Depth * 8 + Mathf.Max(0, m.Gap - .04f) * 4; }
                }
                return sum + close * (rot - rot0).Length() + close * 100 * (pos - pos0).Length();
            }
            var (rot, s0, s1) = Search(rot0, x => Score(pos0, x), rotSteps, 3);
            var (pos, _, s2) = Search(pos0, x => Score(x, rot), posSteps, 3);
            outp["shield"] = new { pos = Arr(pos), rot = Arr(rot), was_pos = Arr(pos0), was_rot = Arr(rot0), score_before = Mathf.Snapped(s0, .001f), score_after = Mathf.Snapped(s2, .001f) };
            Say($"shield: pos {pos0} -> {pos}, rot {rot0} -> {rot}  score {s0:0.000} -> {s2:0.000}");
        }

        // kit helmets: position (y, z) and scale, per head rule
        var hout = new Dictionary<string, object>();
        foreach (var grp in _results.Where(r => r.C.Cat == "head" && r.Held["head"].Count > 0).GroupBy(r => r.C.Name))
        {
            var rule = root.GetProperty("head").EnumerateArray().FirstOrDefault(x => x.GetProperty("match").EnumerateArray().Any(m => m.GetString() == "" || grp.Key.Contains(m.GetString()!)));
            if (rule.ValueKind != JsonValueKind.Object || !rule.TryGetProperty("kit", out var kl) || kl.GetArrayLength() == 0) continue;   // procedural shapes are not tuned
            var k = kl[0]; var pos0 = V(k, "pos", Vector3.Zero); var rot = V(k, "rot", Vector3.Zero); float sc0 = k.TryGetProperty("scale", out var sv) && sv.ValueKind == JsonValueKind.Number ? sv.GetSingle() : 1f;
            float Score(Vector3 pos, float sc)
            {
                float sum = 0;
                foreach (var r in grp) foreach (var h in r.Held["head"])
                {
                    var xf = Avatar.HoldXf(h.Rest, pos, rot); var sn = r.Snaps[0];
                    var hf = Helm(r.Bm, sn, h, xf, sc / sc0);
                    var other = Measure(r.Bm, sn, h, xf, sc / sc0, new HashSet<string> { "head", "neck" });
                    // the head must not show through; the helmet should sit on the scalp (1-2 cm above it), and keep off the shoulders
                    sum += hf.Poke * 3 + Mathf.Max(0, hf.Gap - .02f) * 10 + Mathf.Max(0, -hf.Gap) * 10 + other.Frac + other.Depth * 4;
                }
                return sum + close * 100 * (pos - pos0).Length() + close * 10 * Mathf.Abs(sc - sc0);
            }
            var (pos, s0, s1) = Search(pos0, x => Score(x, sc0), posSteps, 3, new[] { 1, 2 });
            var (scv, _, s2) = Search(new Vector3(sc0, 0, 0), x => Score(pos, x.X), scaleSteps, 1);
            hout[grp.Key] = new { pos = Arr(pos), scale = Mathf.Snapped(scv.X, .01f), was_pos = Arr(pos0), was_scale = sc0, score_before = Mathf.Snapped(s0, .001f), score_after = Mathf.Snapped(s2, .001f) };
            Say($"head {grp.Key}: pos {pos0} -> {pos}, scale {sc0} -> {scv.X:0.00}  score {s0:0.000} -> {s2:0.000}");
        }
        outp["head"] = hout;

        // cape: hang straight when standing without going into the back or legs (the kit cape values in avatar.json "cape", else hold.cape)
        var all = _results.Where(r => r.Held.TryGetValue("cape", out var l) && l.Count > 0).ToList();
        if (all.Count > 0)
        {
            var hs = hold.GetProperty("cape");
            if (root.TryGetProperty("cape", out var crules)) foreach (var cr in crules.EnumerateArray()) if (cr.TryGetProperty("kit", out var ck) && ck.GetArrayLength() > 0) { hs = ck[0]; break; }
            var pos0 = V(hs, "pos", Vector3.Zero); var rot0 = V(hs, "rot", Vector3.Zero); var capeRot = new Basis(Vector3.Right, CapeIdle);
            float Score(Vector3 pos, Vector3 rot)
            {
                float sum = 0;
                foreach (var r in all) foreach (var h in r.Held["cape"])
                {
                    var xf = Avatar.HoldXf(h.Rest, pos, rot);
                    sum += CapeTilt(r.Snaps[0], h, xf, capeRot) / 90f;
                    foreach (var sn in r.Snaps.Where(s => !s.Warn)) { var m = Measure(r.Bm, sn, h, xf, 1, CapeSkip); sum += m.Frac + m.Depth * 8; }
                }
                return sum / all.Count + close * (rot - rot0).Length() + close * 100 * (pos - pos0).Length();
            }
            var (rot, s0, _) = Search(rot0, x => Score(pos0, x), rotSteps, 1);
            var (pos, _, s2) = Search(pos0, x => Score(x, rot), posSteps, 3, new[] { 1, 2 });
            outp["cape"] = new { pos = Arr(pos), rot = Arr(rot), was_pos = Arr(pos0), was_rot = Arr(rot0), score_before = Mathf.Snapped(s0, .001f), score_after = Mathf.Snapped(s2, .001f) };
            Say($"cape: pos {pos0} -> {pos}, rot {rot0} -> {rot}  score {s0:0.000} -> {s2:0.000}");
        }
        System.IO.File.WriteAllText(_dir + "fit_suggest.json", JsonSerializer.Serialize(outp, new JsonSerializerOptions { WriteIndented = true }));
        Say("suggestions written to bot_out/fit/fit_suggest.json (copy the values you agree with into data/avatar.json)");
    }
    static float[] Arr(Vector3 v) => new[] { Mathf.Snapped(v.X, .001f), Mathf.Snapped(v.Y, .001f), Mathf.Snapped(v.Z, .001f) };

    /// <summary>Coordinate search: try +-step on each axis, keep improvements, then halve to the next step.</summary>
    static (Vector3 best, float before, float after) Search(Vector3 start, Func<Vector3, float> f, float[] steps, int dims, int[]? axes = null)
    {
        var cur = start; float s0 = f(cur), sc = s0; axes ??= Enumerable.Range(0, dims).ToArray();
        foreach (float st in steps)
            for (int pass = 0; pass < 4; pass++)
            {
                bool moved = false;
                foreach (int ax in axes)
                    foreach (float sg in new[] { -1f, 1f })
                    {
                        var c = cur; c[ax] += sg * st; float s = f(c);
                        if (s < sc - 1e-5f) { sc = s; cur = c; moved = true; }
                    }
                if (!moved) break;
            }
        return (cur, s0, sc);
    }

    // ------------------------------------------------------------------ output
    void Sheets()
    {
        const int cols = 3, rows = 3, cw = 640, ch = 360;
        foreach (var (cat, files) in _shots)
            for (int i = 0, sheet = 1; i < files.Count; i += cols * rows, sheet++)
            {
                var img = Image.CreateEmpty(cols * cw, rows * ch, false, Image.Format.Rgb8);
                for (int j = 0; j < cols * rows && i + j < files.Count; j++)
                {
                    var src = Image.LoadFromFile(_dir + files[i + j]); if (src == null) continue;
                    src.Convert(Image.Format.Rgb8); src.Resize(cw, ch, Image.Interpolation.Bilinear);
                    img.BlitRect(src, new Rect2I(0, 0, cw, ch), new Vector2I(j % cols * cw, j / cols * ch));
                }
                img.SavePng(_dir + $"sheet_{cat}_{sheet:00}.png");
            }
    }

    void Finish()
    {
        Say($"=== {_pass} pass, {_warn} warn, {_fail} fail");
        System.IO.File.WriteAllText(_dir + "report.txt", _log.ToString());
        GC.Collect(); GC.WaitForPendingFinalizers();
        GetTree().Quit(_fail > 0 ? 1 : 0);
    }
}
