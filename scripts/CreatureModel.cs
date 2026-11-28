using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Procedural animals (A-21 / A-22): a smooth-skinned body lofted from elliptical cross-sections (torso, neck, wedge head, jointed
/// legs, tail chain, ears, horns / antlers, wings) with vertex-colour coats (dark back, pale belly, spots, patches), built from the
/// numbers in data/creatures.json. One blueprint (pivots + meshes) is built per species and shared by every instance; an instance
/// owns only its nodes and materials. The model animates itself from a few inputs (Speed, Attack, Breath, Airborne, Calm, Dead).
/// Anything may use it: <c>CreatureModel.Make("wolf", height)</c> (Enemy does, for types with "proc" in data/enemies.json).
/// Forward is -Z, the feet stand on y = 0, sizes in the data are metres before the model is scaled to the wanted height.
/// </summary>
public partial class CreatureModel : Node3D
{
    // ------------------------------------------------------------------ inputs (set by the owner whenever they change)
    /// <summary>Ground speed in world m/s (drives the gait).</summary>
    public float Speed;
    /// <summary>0..1 through a bite / blow wind-up, -1 = none.</summary>
    public float Attack = -1;
    public bool Breath, Airborne, Dead, Calm, Menace, Flash;
    /// <summary>The body materials (skin, wing membrane): tint, hit flash and fades are done on these.</summary>
    public readonly List<StandardMaterial3D> Mats = new();
    public string Id = "";
    /// <summary>World metres per design metre.</summary>
    public float K = 1;
    public float Height => _bp.Top * K;
    /// <summary>Where a rider sits, in this node's space (y up, z back from the shoulders).</summary>
    public Vector3 Saddle => new(0, _bp.SaddleY * K, _bp.SaddleZ * K);
    public bool HasWings => _bp.Wings.Count > 0;
    public bool Flies => _bp.Flies;
    /// <summary>The jaws' hitbox in world space (the head pivot follows the bite lunge), false for a body without a head.</summary>
    public bool BitePoint(out Vector3 at, out float r)
    {
        at = GlobalPosition; r = .3f; if (_bp.Head < 0 || _n.Length <= _bp.Head) return false;
        var g = _n[_bp.Head].GlobalTransform; at = g * _bp.Bite; r = _bp.BiteR * g.Basis.Scale.X; return true;
    }

    // ------------------------------------------------------------------ blueprint
    sealed class Buf { public readonly List<Vector3> V = new(); public readonly List<Color> C = new(); public readonly List<int> I = new(); }
    sealed class Piv { public string Name = ""; public int Parent = -1; public Vector3 Pos, Rot; }
    sealed class LegRef { public int Hip, Knee; public bool Hind; public float Side, Phase; }
    sealed class WingRef { public int Sh, El; public float Side; }
    sealed class Blueprint
    {
        public readonly List<Piv> Pivs = new(); public readonly Dictionary<(int piv, int mat), Buf> Bufs = new();
        public readonly List<(int piv, int mat, ArrayMesh mesh)> Meshes = new();
        public float Top = 1, SaddleY = 1, SaddleZ, Leg = .5f, Vis = 1;
        public Vector3 Bite; public float BiteR = .08f;   // the jaws' hitbox: a point in the head pivot's space and its radius (model units)
        public int Body = -1, Neck = -1, Head = -1, Jaw = -1;
        public readonly List<LegRef> Legs = new(); public readonly List<int> Tail = new(), Ears = new(); public readonly List<WingRef> Wings = new();
        public bool Sprawl, Grazer, Flies, Bat; public float GrazeAng = .9f, TailSway = .22f, FlapHz = 2.5f, WingRest = 1, Glide;
        public Color Eye = new(.08f, .06f, .05f), Glow = new(1f, .4f, .1f); public float EyeGlow;
    }
    const int MSkin = 0, MEye = 1, MSheet = 2, MGlow = 3, MHorn = 4;   // MHorn = skin, but left out of the height (antlers, horns)

    /// <summary>Layered parameters: the species, then what it is "like", then "defaults".</summary>
    sealed class Pm
    {
        readonly List<JsonElement> _l = new();
        public void Add(JsonElement e) { if (e.ValueKind == JsonValueKind.Object) _l.Add(e); }
        bool Try(string k, out JsonElement v) { foreach (var e in _l) if (e.TryGetProperty(k, out v)) return true; v = default; return false; }
        public bool Has(string k) => Try(k, out _);
        public float F(string k, float d) => Try(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetSingle() : d;
        public bool B(string k, bool d = false) => Try(k, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && d) : d;
        public string S(string k, string d = "") => Try(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? d : d;
        public Vector2 V(string k, float x, float y) => Try(k, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 2 ? new Vector2(v[0].GetSingle(), v[1].GetSingle()) : new Vector2(x, y);
        public Vector3 V3(string k, float x, float y, float z) => Try(k, out var v) && v.ValueKind == JsonValueKind.Array && v.GetArrayLength() >= 3 ? new Vector3(v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle()) : new Vector3(x, y, z);
        public Color C(string k, Color d) { if (Try(k, out var v) && v.ValueKind == JsonValueKind.String) { try { return Color.FromHtml(v.GetString() ?? ""); } catch { } } return d; }
    }

    static JsonElement _root; static bool _loaded;
    static readonly Dictionary<string, Blueprint?> _bps = new();
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        try { using var doc = JsonUtil.Load("res://data/creatures.json"); _root = doc.RootElement.Clone(); }
        catch (Exception e) { GD.PushError("creatures.json: " + e.Message); }
    }
    /// <summary>Is there a procedural body with this id in data/creatures.json?</summary>
    public static bool Has(string id)
    {
        Load(); return id != "" && _root.ValueKind == JsonValueKind.Object && _root.TryGetProperty("creatures", out var c) && c.TryGetProperty(id, out var e) && e.ValueKind == JsonValueKind.Object;
    }
    /// <summary>Every procedural body id.</summary>
    public static IEnumerable<string> Ids
    {
        get { Load(); if (_root.ValueKind == JsonValueKind.Object && _root.TryGetProperty("creatures", out var c)) foreach (var p in c.EnumerateObject()) if (!p.Name.StartsWith("_")) yield return p.Name; }
    }
    static Blueprint? Get(string id)
    {
        if (_bps.TryGetValue(id, out var b)) return b;
        b = null;
        if (Has(id))
        {
            try
            {
                var cs = _root.GetProperty("creatures"); var pm = new Pm(); string cur = id;
                for (int guard = 0; guard < 6 && cur != "" && cs.TryGetProperty(cur, out var e) && e.ValueKind == JsonValueKind.Object; guard++) { pm.Add(e); cur = JsonUtil.S(e, "like"); }
                if (_root.TryGetProperty("defaults", out var df)) pm.Add(df);
                b = Build(pm);
            }
            catch (Exception ex) { GD.PushError($"creature '{id}': {ex.Message}"); b = null; }
        }
        _bps[id] = b; return b;
    }

    /// <summary>
    /// A new model of this species, scaled so the top of its head (horns and antlers not counted) stands <paramref name="height"/> metres
    /// high (0 = the size in the data). tint multiplies the coat; emissive makes it glow (shadow beasts). Null if the id is unknown.
    /// </summary>
    public static CreatureModel? Make(string id, float height = 0, Color? tint = null, bool emissive = false)
    {
        var bp = Get(id); if (bp == null) return null;
        var m = new CreatureModel { Name = "Creature_" + id, Id = id, _bp = bp };
        m.Init(height, tint ?? Colors.White, emissive); return m;
    }

    // ------------------------------------------------------------------ instance
    Blueprint _bp = null!; Node3D[] _n = Array.Empty<Node3D>(); Vector3[] _rest = Array.Empty<Vector3>(); Vector3[] _restPos = Array.Empty<Vector3>();
    StandardMaterial3D? _glowMat, _eyeMat; bool _emissive; Color _tint = Colors.White;
    float _t, _ph, _amt, _flap, _air, _fold = 1, _graze, _grazeT, _earT, _jaw, _neck, _glow, _seed;

    void Init(float height, Color tint, bool emissive)
    {
        var bp = _bp; _emissive = emissive; _tint = tint; _seed = GD.Randf() * 50f; _grazeT = 2f + GD.Randf() * 6f; _earT = 1f + GD.Randf() * 5f; _fold = bp.WingRest;
        K = (height > 0 ? height / Mathf.Max(.01f, bp.Top) : 1f) * bp.Vis; Scale = Vector3.One * K;
        _n = new Node3D[bp.Pivs.Count]; _rest = new Vector3[bp.Pivs.Count]; _restPos = new Vector3[bp.Pivs.Count];
        for (int i = 0; i < bp.Pivs.Count; i++)
        {
            var pv = bp.Pivs[i]; var nd = new Node3D { Name = pv.Name, Position = pv.Pos, Rotation = pv.Rot };
            (pv.Parent >= 0 ? _n[pv.Parent] : this).AddChild(nd); _n[i] = nd; _rest[i] = pv.Rot; _restPos[i] = pv.Pos;
        }
        StandardMaterial3D? skin = null, sheet = null;
        foreach (var (piv, mat, mesh) in bp.Meshes)
        {
            Material mt;
            switch (mat)
            {
                case MEye:
                    _eyeMat ??= new StandardMaterial3D { AlbedoColor = bp.Eye, Roughness = .15f, EmissionEnabled = bp.EyeGlow > 0, Emission = bp.Eye, EmissionEnergyMultiplier = bp.EyeGlow };
                    mt = _eyeMat; break;
                case MSheet:
                    if (sheet == null) { sheet = new StandardMaterial3D { VertexColorUseAsAlbedo = true, AlbedoColor = tint, Roughness = .85f, CullMode = BaseMaterial3D.CullModeEnum.Disabled }; Mats.Add(sheet); }
                    mt = sheet; break;
                case MGlow:
                    _glowMat ??= new StandardMaterial3D { AlbedoColor = bp.Glow.Darkened(.55f), Roughness = .5f, EmissionEnabled = true, Emission = bp.Glow, EmissionEnergyMultiplier = .25f };
                    mt = _glowMat; break;
                default:
                    if (skin == null) { skin = new StandardMaterial3D { VertexColorUseAsAlbedo = true, AlbedoColor = tint, Roughness = .88f }; Mats.Add(skin); }
                    mt = skin; break;
            }
            _n[piv].AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = mt });
        }
        ApplyGlow(0);
    }
    void ApplyGlow(float flash)
    {
        foreach (var m in Mats)
        {
            bool on = _emissive || flash > 0; if (m.EmissionEnabled != on) m.EmissionEnabled = on;
            if (on) { m.Emission = flash > 0 ? Colors.White : new Color(_tint.R, _tint.G, _tint.B) * .6f; m.EmissionEnergyMultiplier = flash > 0 ? .9f : .6f; }
        }
    }

    public override void _Process(double delta)
    {
        if (!IsVisibleInTree()) return;
        Tick(Mathf.Min((float)delta, .1f));
    }

    /// <summary>One animation step (called by _Process; public so a paused preview can pose it).</summary>
    public void Tick(float dt)
    {
        var bp = _bp; if (_n.Length == 0) return;
        ApplyGlow(Flash ? 1 : 0);
        if (_glowMat != null) { _glow = Mathf.MoveToward(_glow, Breath ? 1 : 0, dt * (Breath ? 1.4f : 2.5f)); _glowMat.EmissionEnergyMultiplier = .25f + _glow * (4.2f + .8f * Mathf.Sin(_t * 23f)); }
        if (Dead) { foreach (var w in bp.Wings) { Rot(w.Sh, new Vector3(0, w.Side * -.9f, w.Side * -.25f), dt, 4); Rot(w.El, new Vector3(0, w.Side * -1.2f, 0), dt, 4); } return; }
        _t += dt;
        float sp = Speed / Mathf.Max(.05f, K), run = Mathf.Clamp(sp / Mathf.Max(.25f, bp.Leg * 2.4f), 0, 1);
        _amt = Mathf.Lerp(_amt, Airborne ? 0 : run, 1f - Mathf.Exp(-9f * dt));
        float hz = Mathf.Clamp(sp / Mathf.Max(.08f, bp.Leg * 2.7f), 0, 3.3f);
        _ph = Mathf.Wrap(_ph + hz * Mathf.Tau * dt, 0, Mathf.Tau);
        _air = Mathf.MoveToward(_air, Airborne ? 1 : 0, dt * 2.5f);

        // legs: diagonal pairs trot; the knee folds while the leg swings forward. Fliers tuck them.
        foreach (var l in bp.Legs)
        {
            float a = _ph + l.Phase, swing = Mathf.Sin(a) * (.42f + .3f * run) * _amt, flex = Mathf.Max(0, Mathf.Cos(a)) * (l.Hind ? .75f : 1f) * _amt;
            if (_air > 0) { swing = Mathf.Lerp(swing, -.9f, _air); flex = Mathf.Lerp(flex, 1.1f, _air); }
            _n[l.Hip].Rotation = _rest[l.Hip] + (bp.Sprawl ? new Vector3(0, -l.Side * swing * .9f, 0) : new Vector3(swing, 0, 0));
            if (l.Knee >= 0) _n[l.Knee].Rotation = _rest[l.Knee] + (bp.Sprawl ? new Vector3(0, 0, l.Side * flex * .5f) : new Vector3(-flex, 0, 0));
        }
        // trunk: two bounces a stride, a breath at rest
        if (bp.Body >= 0)
        {
            float bob = Mathf.Abs(Mathf.Sin(_ph)) * bp.Leg * .05f * _amt + Mathf.Sin(_t * 1.7f + _seed) * bp.Leg * .008f * (1 - _amt);
            _n[bp.Body].Position = _restPos[bp.Body] + new Vector3(0, bob, 0);
            _n[bp.Body].Rotation = _rest[bp.Body] + new Vector3(Mathf.Sin(_ph * 2) * .025f * _amt + (_air > 0 ? -.12f * _air : 0), 0, Mathf.Sin(_ph) * .03f * _amt);
        }
        // neck and head: grazing, the lunge of a bite, the reared neck of a breath
        float atk = Attack >= 0 ? Mathf.Sin(Mathf.Clamp(Attack, 0, 1) * Mathf.Pi) : 0;
        if (bp.Grazer && Calm && _amt < .05f) { _grazeT -= dt; if (_grazeT <= 0) { _grazeT = 3f + GD.Randf() * 7f; _graze = _graze > .5f ? 0 : 1; } } else { _graze = 0; _grazeT = Mathf.Max(_grazeT, 1.5f); }
        float wantNeck = _graze > .5f ? -bp.GrazeAng : -.14f * _amt;
        if (Breath) wantNeck = .22f; else if (atk > 0) wantNeck = -.3f * atk; else if (Menace) wantNeck -= .06f;
        _neck = Mathf.Lerp(_neck, wantNeck, 1f - Mathf.Exp(-(atk > 0 ? 14f : 3.5f) * dt));
        float look = Calm && _graze < .5f ? Mathf.Sin(_t * .45f + _seed) * .28f * Mathf.Clamp(Mathf.Sin(_t * .13f + _seed * 2) * 3f, 0, 1) : 0;
        if (bp.Neck >= 0) _n[bp.Neck].Rotation = _rest[bp.Neck] + new Vector3(_neck, look * .5f, 0);
        if (bp.Head >= 0) _n[bp.Head].Rotation = _rest[bp.Head] + new Vector3(Mathf.Sin(_ph * 2 + .6f) * .04f * _amt - _neck * (_graze > .5f ? .35f : .5f) + (Breath ? -.2f : 0), look * .5f, 0);
        float wantJaw = Breath ? .62f : atk > 0 ? .7f * atk : Menace ? .12f + .05f * Mathf.Sin(_t * 5f) : 0;
        _jaw = Mathf.Lerp(_jaw, wantJaw, 1f - Mathf.Exp(-16f * dt));
        if (bp.Jaw >= 0) _n[bp.Jaw].Rotation = _rest[bp.Jaw] + new Vector3(-_jaw, 0, 0);
        // tail: a travelling wave; it lifts and streams when running
        for (int i = 0; i < bp.Tail.Count; i++)
        {
            int ti = bp.Tail[i]; float w = Mathf.Sin(_t * (1.6f + 3.2f * _amt + (Menace ? 2f : 0)) - i * .7f + _seed) * bp.TailSway * (.55f + .45f * i / Mathf.Max(1, bp.Tail.Count));
            _n[ti].Rotation = new Vector3(Mathf.Lerp(_rest[ti].X, _rest[ti].X * .45f, _amt * .7f), w, 0);
        }
        // ears: a flick now and then
        _earT -= dt; float flick = _earT < .18f ? Mathf.Sin((.18f - Mathf.Max(0, _earT)) / .18f * Mathf.Pi) : 0; if (_earT <= 0) _earT = 1.5f + GD.Randf() * 6f;
        for (int i = 0; i < bp.Ears.Count; i++) { int e = bp.Ears[i]; _n[e].Rotation = _rest[e] + new Vector3(-.35f * flick * (i == 0 ? 1 : .4f) - (Menace ? .25f : 0), 0, 0); }
        // wings: folded on the ground, beating in the air; a dragon half-spreads them when it hunts and for its breath
        if (bp.Wings.Count > 0)
        {
            float wantFold = _air > .5f ? 0 : Breath ? .12f : Menace && bp.Bat ? .35f : bp.WingRest;
            _fold = Mathf.MoveToward(_fold, wantFold, dt * 1.8f);
            float open = 1 - _fold, glide = bp.Glide > 0 ? Mathf.Clamp(.5f + 1.6f * Mathf.Sin(_t * .35f + _seed), 1 - bp.Glide, 1) : 1;
            _flap = Mathf.Wrap(_flap + bp.FlapHz * (_air > .5f ? 1f : .35f) * Mathf.Tau * dt, 0, Mathf.Tau);
            float amp = (_air > .5f ? (bp.Bat ? .5f : .8f) * glide : .1f) * open, s1 = Mathf.Sin(_flap), s2 = Mathf.Sin(_flap - .9f);
            foreach (var w in bp.Wings)
            {
                float sx = w.Side;
                if (bp.Bat)
                {
                    _n[w.Sh].Rotation = new Vector3(0, sx * -.9f * _fold, sx * (.55f * _fold + open * (.12f + amp * s1)));
                    _n[w.El].Rotation = new Vector3(0, sx * -1.9f * _fold, sx * open * amp * .7f * s2);
                }
                else
                {
                    _n[w.Sh].Rotation = new Vector3(0, sx * -1.4f * _fold, sx * (-.28f * _fold + open * (.1f + amp * s1)));
                    _n[w.El].Rotation = new Vector3(0, sx * -.15f * _fold, sx * open * amp * .6f * s2);
                    _n[w.El].Scale = new Vector3(1 - .6f * _fold, 1, 1);
                }
            }
        }
    }
    void Rot(int i, Vector3 to, float dt, float k) { _n[i].Rotation = _n[i].Rotation.Lerp(to, 1f - Mathf.Exp(-k * dt)); }

    // ------------------------------------------------------------------ mesh helpers
    static float Hash(int n) { unchecked { uint x = (uint)n * 747796405u + 2891336453u; x = ((x >> ((int)(x >> 28) + 4)) ^ x) * 277803737u; x ^= x >> 22; return (x & 0xffffff) / 16777215f; } }
    static float Hash3(int x, int y, int z) { unchecked { return Hash(x * 73856093 ^ y * 19349663 ^ z * 83492791); } }
    /// <summary>Smooth value noise 0..1.</summary>
    static float Noise(Vector3 p)
    {
        int x = Mathf.FloorToInt(p.X), y = Mathf.FloorToInt(p.Y), z = Mathf.FloorToInt(p.Z); float fx = p.X - x, fy = p.Y - y, fz = p.Z - z;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float L(float a, float b, float t) => a + (b - a) * t;
        return L(L(L(Hash3(x, y, z), Hash3(x + 1, y, z), fx), L(Hash3(x, y + 1, z), Hash3(x + 1, y + 1, z), fx), fy),
                 L(L(Hash3(x, y, z + 1), Hash3(x + 1, y, z + 1), fx), L(Hash3(x, y + 1, z + 1), Hash3(x + 1, y + 1, z + 1), fx), fy), fz);
    }
    /// <summary>A triangle wound so its front (Godot: clockwise) faces <paramref name="outward"/>.</summary>
    static void Tri(Buf b, int a, int c1, int c2, Vector3 outward)
    {
        var ng = (b.V[c2] - b.V[a]).Cross(b.V[c1] - b.V[a]);
        b.I.Add(a); if (ng.Dot(outward) < 0) { b.I.Add(c2); b.I.Add(c1); } else { b.I.Add(c1); b.I.Add(c2); }
    }
    /// <summary>
    /// A smooth tube through the control points with elliptical sections (rad.X sideways, rad.Y along the section's "up": world up for a
    /// lying tube, forward for a standing one), Catmull-Rom smoothed, closed at both ends. col(position, upness -1..1) colours each vertex.
    /// </summary>
    static void Tube(Buf b, IReadOnlyList<Vector3> cp, IReadOnlyList<Vector2> cr, Func<Vector3, float, Color> col, int seg = 12, int sub = 3, float lump = 0)
    {
        int n = cp.Count; if (n < 2) return;
        var pts = new List<Vector3>(); var rad = new List<Vector2>();
        if (sub <= 1 || n < 3) { pts.AddRange(cp); rad.AddRange(cr); }
        else
        {
            for (int i = 0; i < n - 1; i++)
            {
                Vector3 p0 = cp[Math.Max(i - 1, 0)], p1 = cp[i], p2 = cp[i + 1], p3 = cp[Math.Min(i + 2, n - 1)];
                Vector2 r0 = cr[Math.Max(i - 1, 0)], r1 = cr[i], r2 = cr[i + 1], r3 = cr[Math.Min(i + 2, n - 1)];
                for (int s = 0; s < sub; s++) { float t = s / (float)sub; pts.Add(p1.CubicInterpolate(p2, p0, p3, t)); rad.Add(r1.CubicInterpolate(r2, r0, r3, t)); }
            }
            pts.Add(cp[n - 1]); rad.Add(cr[n - 1]);
        }
        int m = pts.Count, v0 = b.V.Count; Vector3 up = Vector3.Up, prevT = Vector3.Forward, t0 = Vector3.Forward, t1 = Vector3.Forward;
        for (int i = 0; i < m; i++)
        {
            var t = pts[Math.Min(i + 1, m - 1)] - pts[Math.Max(i - 1, 0)]; t = t.LengthSquared() < 1e-10f ? prevT : t.Normalized(); prevT = t;
            if (i == 0) { var rf = Mathf.Abs(t.Y) < .9f ? Vector3.Up : Vector3.Forward; up = (rf - t * rf.Dot(t)).Normalized(); t0 = t; }
            else { var u2 = up - t * up.Dot(t); if (u2.LengthSquared() > 1e-8f) up = u2.Normalized(); }
            t1 = t; var side = up.Cross(t).Normalized();
            float rx = Mathf.Max(rad[i].X, .0004f), ry = Mathf.Max(rad[i].Y, .0004f);
            for (int j = 0; j < seg; j++)
            {
                float a = j / (float)seg * Mathf.Tau, ca = Mathf.Cos(a), sa = Mathf.Sin(a); var dir = side * ca + up * sa;
                var off = side * (ca * rx) + up * (sa * ry);
                if (lump > 0) off *= 1 + lump * (Hash(i * 131 + j * 17 + v0) - .5f) * 2f;
                var p = pts[i] + off; b.V.Add(p); b.C.Add(col(p, dir.Y));
            }
        }
        for (int i = 0; i < m - 1; i++)
            for (int j = 0; j < seg; j++)
            {
                int a0 = v0 + i * seg + j, a1 = v0 + i * seg + (j + 1) % seg, b0 = a0 + seg, b1 = a1 + seg;
                var outw = b.V[a0] + b.V[a1] + b.V[b0] + b.V[b1] - (pts[i] + pts[i + 1]) * 2f;
                Tri(b, a0, a1, b1, outw); Tri(b, a0, b1, b0, outw);
            }
        int c0 = b.V.Count; b.V.Add(pts[0]); b.C.Add(col(pts[0], -t0.Y));
        int c1 = b.V.Count; b.V.Add(pts[m - 1]); b.C.Add(col(pts[m - 1], t1.Y));
        for (int j = 0; j < seg; j++)
        {
            Tri(b, c0, v0 + j, v0 + (j + 1) % seg, -t0);
            Tri(b, c1, v0 + (m - 1) * seg + j, v0 + (m - 1) * seg + (j + 1) % seg, t1);
        }
    }
    /// <summary>An ellipsoid (radii along x, y, z).</summary>
    static void Ball(Buf b, Vector3 c, Vector3 r, Func<Vector3, float, Color> col, int seg = 10, float lump = 0)
    {
        var pts = new List<Vector3>(); var rad = new List<Vector2>(); const int N = 6;
        for (int k = 0; k <= N; k++) { float a = Mathf.Pi * k / N, s = Mathf.Sin(a); pts.Add(c + new Vector3(0, 0, -Mathf.Cos(a) * r.Z)); rad.Add(new Vector2(r.X * s, r.Y * s)); }
        Tube(b, pts, rad, col, seg, 1, lump);
    }
    /// <summary>A limb: a tube from a to b that tapers from ra to rb with rounded ends.</summary>
    static void Limb(Buf b, Vector3 a, Vector3 e, float ra, float rb, Func<Vector3, float, Color> col, int seg = 8)
    {
        var d = e - a; if (d.LengthSquared() < 1e-10f) return; d = d.Normalized();
        Tube(b, new[] { a - d * ra * .5f, a, e, e + d * rb * .5f }, new[] { new Vector2(ra * .5f, ra * .5f), new Vector2(ra, ra), new Vector2(rb, rb), new Vector2(rb * .45f, rb * .45f) }, col, seg, 1);
    }
    /// <summary>A flat fan (double-sided material): centre then the rim points in order.</summary>
    static void Fan(Buf b, Vector3 c, Color cc, IReadOnlyList<Vector3> rim, IReadOnlyList<Color> rc, bool closed)
    {
        int i0 = b.V.Count; b.V.Add(c); b.C.Add(cc);
        for (int i = 0; i < rim.Count; i++) { b.V.Add(rim[i]); b.C.Add(rc[i]); }
        int cnt = closed ? rim.Count : rim.Count - 1;
        for (int i = 0; i < cnt; i++) Tri(b, i0, i0 + 1 + i, i0 + 1 + (i + 1) % rim.Count, Vector3.Up);
    }
    static ArrayMesh Commit(Buf b)
    {
        var v = b.V.ToArray(); var nr = new Vector3[v.Length]; var idx = b.I.ToArray();
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            var n = (v[idx[i + 2]] - v[idx[i]]).Cross(v[idx[i + 1]] - v[idx[i]]);
            nr[idx[i]] += n; nr[idx[i + 1]] += n; nr[idx[i + 2]] += n;
        }
        for (int i = 0; i < nr.Length; i++) nr[i] = nr[i].LengthSquared() < 1e-14f ? Vector3.Up : nr[i].Normalized();
        var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = v; arr[(int)Mesh.ArrayType.Normal] = nr; arr[(int)Mesh.ArrayType.Color] = b.C.ToArray(); arr[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr); return mesh;
    }

    // ------------------------------------------------------------------ the builder
    static Blueprint Build(Pm p)
    {
        var bp = new Blueprint();
        int Pv(string name, int parent, Vector3 pos, Vector3 rot = default) { bp.Pivs.Add(new Piv { Name = name, Parent = parent, Pos = pos, Rot = rot }); return bp.Pivs.Count - 1; }
        Buf B(int piv, int mat) { if (!bp.Bufs.TryGetValue((piv, mat), out var b)) bp.Bufs[(piv, mat)] = b = new Buf(); return b; }
        float D(float deg) => Mathf.DegToRad(deg);

        // ---- numbers
        float L = p.F("len", .98f), leg = p.F("leg", .5f), wither = p.F("wither", .05f), lump = p.F("lump", 0);
        var ch = p.V("chest", .15f, .215f); var wa = p.V("waist", .115f, .135f); var ru = p.V("rump", .135f, .165f);
        int seg = Mathf.Clamp((int)p.F("seg", 14), 6, 24), nLegs = (int)p.F("legs", 4);
        float legR = p.F("legR", .05f), hindK = p.F("hindK", 1.3f), stance = p.F("stance", .7f), sprawl = p.F("sprawl", 0), footSize = p.F("footSize", 1);
        string foot = p.S("foot", "paw");
        float top = leg + ru.Y * 1.25f;
        Color main = p.C("main", new Color(.5f, .47f, .42f)), back = p.C("back", main.Darkened(.35f)), belly = p.C("belly", main.Lightened(.4f));
        Color legCol = p.C("legCol", main.Darkened(.12f)), footCol = p.C("footCol", legCol.Darkened(.45f)), headCol = p.C("headCol", main), muzzle = p.C("muzzle", belly.Lerp(main, .35f));
        Color snoutCol = p.C("snoutCol", muzzle), nose = p.C("nose", p.Has("snoutCol") ? snoutCol.Darkened(.2f) : new Color(.08f, .07f, .07f));
        Color neckCol = p.C("neckCol", main), hornCol = p.C("hornCol", new Color(.82f, .76f, .62f)), patCol = p.C("patCol", back.Darkened(.3f));
        string pattern = p.S("pattern"); float patFreq = p.F("patFreq", 6f);
        bp.Eye = p.C("eye", new Color(.08f, .06f, .05f)); bp.EyeGlow = p.F("eyeGlow", 0); bp.Glow = p.C("glow", new Color(1f, .4f, .1f));
        bp.Leg = leg; bp.Vis = p.F("vis", 1); bp.Sprawl = sprawl > 0; bp.Grazer = p.B("grazer"); bp.GrazeAng = D(p.F("grazeAng", 55)); bp.TailSway = p.F("sway", .22f);

        Color Pattern(Vector3 q, float d, Color c)
        {
            switch (pattern)
            {
                case "spots": { float n = Noise(q * patFreq); if (n > .64f && d > -.45f) c = c.Lerp(patCol, Mathf.Clamp((n - .64f) * 9f, 0, 1)); break; }
                case "fawn": { float n = Noise(q * patFreq); if (n > .68f && d > -.1f) c = c.Lerp(patCol, Mathf.Clamp((n - .68f) * 10f, 0, 1)); break; }
                case "patch": { float n = Noise(q * patFreq + new Vector3(3.1f, 7.7f, 1.3f)); if (n > .5f) c = c.Lerp(patCol, Mathf.Clamp((n - .5f) * 14f, 0, 1)); break; }
                case "stripe": { float n = Mathf.Sin(q.Z * patFreq * 3f + Noise(q * 2f) * 3f); if (n > .45f && d > -.3f) c = c.Lerp(patCol, Mathf.Clamp((n - .45f) * 5f, 0, 1) * Mathf.Clamp(d + .6f, 0, 1)); break; }
                case "scale": { float n = Noise(q * patFreq); c = c.Lerp(n > .5f ? c.Lightened(.18f) : c.Darkened(.22f), Mathf.Abs(n - .5f) * 1.6f); break; }
            }
            return c;
        }
        Color Coat3(Vector3 q, float d, Color mid)
        {
            Color c = d > .25f ? mid.Lerp(back, Mathf.SmoothStep(0, 1, (d - .25f) / .6f)) : d < -.2f ? mid.Lerp(belly, Mathf.SmoothStep(0, 1, (-d - .2f) / .6f)) : mid;
            return Pattern(q, d, c);
        }
        Color Coat(Vector3 q, float d) => Coat3(q, d, main);
        Func<Vector3, float, Color> Flat(Color c) => (_, d) => d > 0 ? c : c.Darkened(.12f);

        // ---- trunk
        int root = Pv("root", -1, Vector3.Zero);
        var pb = new Vector3(0, leg, L * .30f);
        int body = Pv("body", root, pb, new Vector3(D(p.F("bodyPitch", 0)), 0, 0)); bp.Body = body;
        Vector3 T(float x, float y, float z) => new Vector3(x, y, z) - pb;
        float[] zs = { -.52f, -.44f, -.30f, -.10f, .10f, .30f, .44f, .52f };
        Vector2[] rr = { new(ch.X * .35f, ch.Y * .40f), new(ch.X * .80f, ch.Y * .85f), ch, new(Mathf.Lerp(ch.X, wa.X, .5f), Mathf.Lerp(ch.Y, wa.Y, .45f)), wa, ru, ru * .85f, new(ru.X * .35f, ru.Y * .40f) };
        float[] tops = { -ch.Y * .50f, wither * .6f - ch.Y * .05f, wither, wither * .35f, -.012f * L, ru.Y * .06f, -ru.Y * .10f, -ru.Y * .45f };
        float TopAt(float zf) { for (int i = 0; i < zs.Length - 1; i++) if (zf <= zs[i + 1]) { float t = Mathf.Clamp((zf - zs[i]) / (zs[i + 1] - zs[i]), 0, 1); return top + Mathf.Lerp(tops[i], tops[i + 1], t); } return top + tops[^1]; }
        {
            var pts = new Vector3[zs.Length]; for (int i = 0; i < zs.Length; i++) pts[i] = T(0, top + tops[i] - rr[i].Y, zs[i] * L);
            Tube(B(body, MSkin), pts, rr, Coat, seg, 3, lump);
        }
        if (p.B("shell"))
        {
            var sc = p.C("shellCol", new Color(.35f, .31f, .19f));
            Ball(B(body, MSkin), T(0, top - wa.Y * .85f, 0), new Vector3(wa.X * 1.42f, wa.Y * 1.35f, L * .56f), (q, d) => { float n = Noise(q * 14f); return (d > -.2f ? sc : sc.Lightened(.3f)).Lerp(sc.Darkened(.4f), n > .6f ? .7f : 0); }, 14);
        }
        float hump = p.F("hump", 0);
        if (hump > 0) Ball(B(body, MSkin), T(0, top + hump * .3f, -.02f * L), new Vector3(wa.X * .78f, hump, L * .2f), Coat, 12);
        float spines = p.F("spines", 0); Color spineCol = p.C("spineCol", back.Darkened(.3f));
        void Spine(Buf b, Vector3 at, Vector3 dir, float h) => Tube(b, new[] { at - dir * h * .2f, at + dir * h * .45f, at + dir * h }, new[] { new Vector2(h * .1f, h * .3f), new Vector2(h * .07f, h * .2f), new Vector2(.001f, .001f) }, Flat(spineCol), 6, 1);
        if (spines > 0) for (float zf = -.36f; zf <= .5f; zf += .095f) Spine(B(body, MSkin), T(0, TopAt(zf) - .02f * ch.Y, zf * L), new Vector3(0, .93f, .37f), ch.Y * .34f * spines * (1 - Mathf.Abs(zf - .05f) * .5f));

        // ---- legs
        float footH = foot == "hoof" ? legR * 1.1f : foot == "bird" || foot == "web" ? legR * .6f : legR * .9f;
        Color LegC(Vector3 q, float lenDown) => main.Lerp(legCol, Mathf.Clamp(-q.Y / Mathf.Max(.01f, lenDown) * 1.6f - .2f, 0, 1));
        void Foot(Buf b, Buf sheet, Vector3 fp, float sx)
        {
            float fr = legR * footSize;
            switch (foot)
            {
                case "hoof": Tube(b, new[] { fp + new Vector3(0, footH * 1.15f, 0), fp + new Vector3(0, footH * .5f, -fr * .12f), fp + new Vector3(0, 0, -fr * .2f) }, new[] { new Vector2(fr * .6f, fr * .62f), new Vector2(fr * .74f, fr * .82f), new Vector2(fr * .8f, fr * .92f) }, Flat(footCol), 8, 1); break;
                case "bird": case "web":
                {
                    var tips = new List<Vector3>(); float tl = fr * 4.2f;
                    foreach (float a in new[] { -.5f, 0f, .5f }) { var tip = fp + new Vector3(Mathf.Sin(a) * tl, 0, -Mathf.Cos(a) * tl); tips.Add(tip); Limb(b, fp + new Vector3(0, footH, 0), tip + new Vector3(0, fr * .25f, 0), fr * .42f, fr * .2f, Flat(footCol), 5); }
                    Limb(b, fp + new Vector3(0, footH, 0), fp + new Vector3(0, fr * .25f, tl * .45f), fr * .4f, fr * .18f, Flat(footCol), 5);
                    if (foot == "web") Fan(sheet, fp + new Vector3(0, fr * .2f, 0), footCol, tips.ConvertAll(t => t + new Vector3(0, fr * .2f, 0)), new[] { footCol, footCol.Lightened(.1f), footCol }, false);
                    break;
                }
                default:
                    Ball(b, fp + new Vector3(0, footH * .55f, -fr * .4f), new Vector3(fr * .95f, footH * .6f, fr * 1.45f), Flat(foot == "claw" ? legCol : footCol), 8);
                    if (foot == "claw") foreach (float dx in new[] { -.6f, 0f, .6f }) Limb(b, fp + new Vector3(dx * fr, footH * .5f, -fr * 1.5f), fp + new Vector3(dx * fr * 1.25f, 0, -fr * 2.5f), fr * .24f, fr * .04f, Flat(hornCol.Darkened(.5f)), 5);
                    break;
            }
        }
        void Leg(bool hind, float sx, float zf, float halfW, float phase)
        {
            float k = hind ? hindK : 1f; var hipPos = new Vector3(sx * (halfW * stance + sprawl * halfW * .25f), leg, zf * L);
            int hip = Pv((hind ? "hipH" : "hipF") + (sx > 0 ? "R" : "L"), root, hipPos); var bu = B(hip, MSkin);
            Vector3 knee, end; int kn;
            if (sprawl > 0)
            {
                knee = new Vector3(sx * sprawl * leg, -.30f * leg, 0); end = new Vector3(sx * sprawl * leg * .25f, -.70f * leg + footH, 0);
                Tube(bu, new[] { new Vector3(-sx * legR, 0, 0), new Vector3(0, 0, 0), knee }, new[] { new Vector2(legR * 1.2f * k, legR * 1.2f * k), new Vector2(legR * 1.25f * k, legR * 1.25f * k), new Vector2(legR * .85f, legR * .85f) }, (q, d) => LegC(q, leg * 2), 8, 1);
                kn = Pv("knee", hip, knee); Limb(B(kn, MSkin), Vector3.Zero, end, legR * .82f, legR * .6f, (q, d) => legCol, 8);
            }
            else if (hind)
            {
                knee = new Vector3(0, -.40f * leg, -.11f * leg); end = new Vector3(0, -.60f * leg + footH, .11f * leg);
                Tube(bu, new[] { new Vector3(0, legR * 1.6f, .02f * leg), new Vector3(0, -.12f * leg, -.03f * leg), knee }, new[] { new Vector2(legR * 1.45f * k, legR * 2.1f * k), new Vector2(legR * 1.3f * k, legR * 1.8f * k), new Vector2(legR * .86f, legR * .98f) }, (q, d) => Coat3(q, d * .4f, LegC(q, leg)), 10, 3);
                kn = Pv("stifle", hip, knee);
                Tube(B(kn, MSkin), new[] { new Vector3(0, .03f * leg, 0), new Vector3(0, -.26f * leg, .17f * leg), new Vector3(0, -.50f * leg, .125f * leg), end }, new[] { new Vector2(legR * .82f, legR * .92f), new Vector2(legR * .56f, legR * .62f), new Vector2(legR * .5f, legR * .5f), new Vector2(legR * .52f, legR * .52f) }, (q, d) => main.Lerp(legCol, Mathf.Clamp(-q.Y / (leg * .5f) + .35f, 0, 1)), 8, 3);
            }
            else
            {
                knee = new Vector3(0, -.48f * leg, .03f * leg); end = new Vector3(0, -.52f * leg + footH, -.03f * leg);
                Tube(bu, new[] { new Vector3(0, legR * 1.8f, .01f * leg), new Vector3(0, -.14f * leg, .0f), knee }, new[] { new Vector2(legR * 1.25f, legR * 1.7f), new Vector2(legR * 1.1f, legR * 1.35f), new Vector2(legR * .8f, legR * .86f) }, (q, d) => Coat3(q, d * .4f, LegC(q, leg)), 10, 3);
                kn = Pv("elbow", hip, knee);
                Tube(B(kn, MSkin), new[] { new Vector3(0, .03f * leg, 0), new Vector3(0, -.24f * leg, -.01f * leg), end }, new[] { new Vector2(legR * .76f, legR * .82f), new Vector2(legR * .56f, legR * .58f), new Vector2(legR * .52f, legR * .52f) }, (q, d) => main.Lerp(legCol, Mathf.Clamp(-q.Y / (leg * .5f) + .35f, 0, 1)), 8, 3);
            }
            Foot(B(kn, MSkin), B(kn, MSheet), end - new Vector3(0, footH, 0), sx);
            bp.Legs.Add(new LegRef { Hip = hip, Knee = kn, Hind = hind, Side = sx, Phase = phase });
        }
        if (nLegs >= 4) { Leg(false, -1, -.33f, ch.X, 0); Leg(false, 1, -.33f, ch.X, Mathf.Pi); Leg(true, -1, .32f, ru.X, Mathf.Pi); Leg(true, 1, .32f, ru.X, 0); }
        else if (nLegs == 2) { float zf = p.F("legZ", .12f); Leg(true, -1, zf, wa.X, 0); Leg(true, 1, zf, wa.X, Mathf.Pi); }

        // ---- neck
        float neckLen = p.F("neckLen", .3f), neckAng = D(p.F("neckAng", 32)), neckCurve = D(p.F("neckCurve", 0)); var nR = p.V("neckR", .115f, .088f);
        var nb = T(0, top + wither - ch.Y * .75f, -.44f * L); int neck = Pv("neck", body, nb); bp.Neck = neck;
        var npts = new List<Vector3>(); var nrad = new List<Vector2>(); var nups = new List<Vector3>(); var hq = Vector3.Zero; const int NP = 5;
        for (int i = 0; i < NP; i++)
        {
            float ang = neckAng + neckCurve * (1 - 2 * i / (NP - 1f)); var dir = new Vector3(0, Mathf.Sin(ang), -Mathf.Cos(ang)); float r = Mathf.Lerp(nR.X, nR.Y, i / (float)NP);
            if (i == 0) { npts.Add(hq - dir * Mathf.Max(neckLen * .2f, nR.X * .8f)); nrad.Add(new Vector2(r * .95f, r * 1.12f)); nups.Add(new Vector3(0, Mathf.Cos(ang), Mathf.Sin(ang))); }
            npts.Add(hq); nrad.Add(new Vector2(r * .84f, r)); nups.Add(new Vector3(0, Mathf.Cos(ang), Mathf.Sin(ang)));
            hq += dir * neckLen / NP;
        }
        npts.Add(hq); nrad.Add(new Vector2(nR.Y * .84f, nR.Y)); nups.Add(nups[^1]);
        Tube(B(neck, MSkin), npts, nrad, (q, d) => Coat3(q, d, neckCol), Mathf.Max(8, seg - 2), 3, lump * .7f);
        float mane = p.F("mane", 0);
        if (mane > 0)
        {
            var mc = p.C("maneCol", back.Darkened(.4f)); var mp = new List<Vector3>(); var mr = new List<Vector2>();
            for (int i = 0; i < npts.Count; i++) { mp.Add(npts[i] + nups[i] * (nrad[i].Y * .85f + mane * .35f)); mr.Add(new Vector2(mane * .2f, mane * .6f)); }
            Tube(B(neck, MSkin), mp, mr, Flat(mc), 6, 2);
        }
        if (spines > 0) for (int i = 1; i < npts.Count - 1; i++) Spine(B(neck, MSkin), npts[i] + nups[i] * nrad[i].Y * .85f, (nups[i] + new Vector3(0, .1f, .35f)).Normalized(), nR.X * .55f * spines);
        if (p.B("throat")) for (int i = 1; i < npts.Count - 1; i++) Ball(B(neck, MGlow), npts[i] - nups[i] * nrad[i].Y * .72f, new Vector3(nrad[i].X * .62f, nrad[i].Y * .34f, neckLen / NP * .42f), Flat(Colors.White), 8);

        // ---- head
        var hd = p.V3("head", .21f, .088f, .082f); var sn = p.V3("snout", .17f, .036f, .034f); float ha = D(p.F("headAng", -14)), drop = p.F("snoutDrop", .018f);
        float hl = hd.X, hw = hd.Y, hh = hd.Z, sl = sn.X, sw = sn.Y, sh = sn.Z;
        int head = Pv("head", neck, hq); bp.Head = head;
        Vector3 f = new(0, Mathf.Sin(ha), -Mathf.Cos(ha)), u = new(0, Mathf.Cos(ha), Mathf.Sin(ha)), X = Vector3.Right;
        bp.Bite = f * (hl * .5f + sl * .7f) - u * drop; bp.BiteR = Mathf.Max(hw, hh) * 1.3f;   // the bite lands at the muzzle
        Color HeadC(Vector3 q, float d)
        {
            float along = q.Dot(f); Color c = Coat3(q, d, headCol);
            if (along > hl * .42f) { float t = Mathf.Clamp((along - hl * .42f) / Mathf.Max(.005f, sl * .35f), 0, 1); c = c.Lerp(p.Has("snoutCol") ? snoutCol : (q.Dot(u) < hh * .15f ? muzzle : c.Lerp(muzzle, .35f)), t); }
            if (along > hl * .5f + sl * .86f) c = nose;
            return c;
        }
        Tube(B(head, MSkin), new[] { -f * hl * .45f, -f * hl * .15f, f * hl * .25f, f * hl * .5f - u * drop * .5f, f * (hl * .5f + sl * .6f) - u * drop, f * (hl * .5f + sl) - u * drop },
            new[] { new Vector2(hw * .55f, hh * .6f), new Vector2(hw, hh), new Vector2(hw * .92f, hh * .88f), new Vector2(Mathf.Lerp(hw, sw, .6f), Mathf.Lerp(hh, sh, .55f)), new Vector2(sw * 1.05f, sh * 1.05f), new Vector2(sw * .8f, sh * .8f) }, HeadC, Mathf.Max(8, seg - 2), 3);
        if (p.B("jaw", true))
        {
            int jaw = Pv("jaw", head, f * hl * .12f - u * hh * .55f); bp.Jaw = jaw;
            Tube(B(jaw, MSkin), new[] { -f * hl * .1f, Vector3.Zero, f * (hl * .38f + sl * .55f), f * (hl * .38f + sl * .95f) + u * hh * .08f }, new[] { new Vector2(hw * .45f, hh * .25f), new Vector2(hw * .62f, hh * .32f), new Vector2(sw * .9f, sh * .42f), new Vector2(sw * .6f, sh * .3f) }, (q, d) => muzzle.Lerp(belly, .4f), 8, 2);
        }
        float eyeR = p.F("eyeR", .016f), eyeUp = p.F("eyeUp", .3f);
        foreach (float sx in new[] { -1f, 1f })
        {
            Ball(B(head, MEye), f * hl * .2f + X * sx * hw * .78f + u * hh * eyeUp, Vector3.One * eyeR, Flat(Colors.White), 8);
            if (p.B("brow")) Limb(B(head, MSkin), f * hl * .0f + X * sx * hw * .88f + u * hh * .62f, f * hl * .46f + X * sx * hw * .55f + u * hh * .5f, hh * .2f, hh * .12f, Flat(back), 6);
        }
        // ears
        string ear = p.S("ear", "point"); float es = p.F("earSize", .1f); Color earCol = p.C("earCol", headCol.Lerp(back, .5f));
        if (ear != "" && ear != "none") foreach (float sx in new[] { -1f, 1f })
        {
            int e = Pv("ear" + (sx > 0 ? "R" : "L"), head, -f * hl * .2f + X * sx * hw * .68f + u * hh * .72f); bp.Ears.Add(e); var eb = B(e, MSkin);
            Vector3 tip; float[] prof; Vector2 r;
            switch (ear)
            {
                case "round": tip = new Vector3(sx * es * .3f, es, .05f * es); prof = new[] { .9f, 1f, .85f, .45f }; r = new Vector2(es * .55f, es * .18f); break;
                case "long": tip = new Vector3(sx * es * .12f, es, .22f * es); prof = new[] { .6f, 1f, .9f, .35f }; r = new Vector2(es * .2f, es * .07f); break;
                case "leaf": tip = new Vector3(sx * es * .85f, es * .45f, .1f * es); prof = new[] { .5f, 1f, .8f, .12f }; r = new Vector2(es * .09f, es * .3f); break;
                case "droop": tip = new Vector3(sx * es * .8f, -es * .35f, -.1f * es); prof = new[] { .6f, 1f, .85f, .3f }; r = new Vector2(es * .1f, es * .34f); break;
                default: tip = new Vector3(sx * es * .28f, es, .1f * es); prof = new[] { 1f, .85f, .5f, .04f }; r = new Vector2(es * .42f, es * .14f); break;
            }
            Tube(eb, new[] { Vector3.Zero, tip * .35f, tip * .7f, tip }, new[] { r * prof[0], r * prof[1], r * prof[2], r * prof[3] }, (q, d) => earCol, 8, 2);
        }
        // horns and antlers (not counted in the height)
        string horn = p.S("horn"); float hs = p.F("hornSize", .3f); int tines = (int)p.F("tines", 3);
        if (horn != "") foreach (float sx in new[] { -1f, 1f })
        {
            var hb = B(head, MHorn); var hc = Flat(hornCol); var b0 = -f * hl * .08f + X * sx * hw * .5f + u * hh * .82f;
            Vector3 P(float x, float y, float z) => b0 + X * (sx * x * hs) + u * (y * hs) - f * (z * hs);   // x out, y up, z back
            void Beam(float r0, float r1, params Vector3[] cps) { var rs = new Vector2[cps.Length]; for (int i = 0; i < cps.Length; i++) { float r = Mathf.Lerp(r0, r1, i / (cps.Length - 1f)) * hs; rs[i] = new Vector2(r, r); } Tube(hb, cps, rs, hc, 6, 3); }
            switch (horn)
            {
                case "antler":
                {
                    var beam = new[] { P(0, -.05f, 0), P(.18f, .35f, .12f), P(.32f, .70f, .10f), P(.38f, 1.0f, -.08f) }; Beam(.04f, .012f, beam);
                    Beam(.024f, .006f, P(.04f, .1f, .02f), P(.07f, .2f, -.14f), P(.08f, .32f, -.24f));   // brow tine
                    for (int t = 0; t < tines; t++) { float k = .34f + .5f * t / Mathf.Max(1, tines); var at = beam[1].Lerp(beam[3], Mathf.Clamp((k - .35f) / .65f, 0, 1)); Beam(.02f, .005f, at, at + X * (sx * .03f * hs) + u * (.16f * hs) + f * (.08f * hs), at + X * (sx * .02f * hs) + u * (.3f * hs) + f * (.14f * hs)); }
                    break;
                }
                case "palm":
                {
                    Beam(.05f, .04f, P(0, -.05f, 0), P(.2f, .1f, .02f), P(.34f, .2f, .04f));
                    var c = P(.52f, .34f, .06f); Tube(hb, new[] { c - X * (sx * .2f * hs), c - X * (sx * .05f * hs) + u * (.02f * hs), c + X * (sx * .14f * hs) + u * (.08f * hs), c + X * (sx * .26f * hs) + u * (.16f * hs) }, new[] { new Vector2(.04f, .02f) * hs, new Vector2(.2f, .03f) * hs, new Vector2(.24f, .028f) * hs, new Vector2(.1f, .02f) * hs }, hc, 8, 2);
                    for (int t = 0; t < 5; t++) { float a = -1.1f + t * .55f; var at = c + X * (sx * (.1f + .12f * Mathf.Cos(a)) * hs) - f * (-.2f * Mathf.Sin(a) * hs) + u * (.08f * hs); Beam(.022f, .005f, at, at + X * (sx * .1f * Mathf.Cos(a) * hs) + u * (.14f * hs) + f * (.16f * Mathf.Sin(a) * hs)); }
                    break;
                }
                case "back": Beam(.07f, .014f, P(0, -.05f, 0), P(.05f, .5f, .15f), P(.1f, .85f, .5f), P(.14f, .95f, .85f)); break;
                case "curl": { var cps = new Vector3[8]; for (int i = 0; i < 8; i++) { float th = i / 7f * 5.2f; cps[i] = P(.04f + .2f * th / 5.5f, .3f * Mathf.Sin(th), .3f * (1 - Mathf.Cos(th))); } Beam(.09f, .02f, cps); break; }
                case "bull": Beam(.08f, .015f, P(0, -.02f, 0), P(.5f, .1f, 0), P(.75f, .45f, -.15f), P(.7f, .8f, -.25f)); break;
                case "dragon":
                    Beam(.1f, .012f, P(0, -.05f, 0), P(.1f, .35f, .45f), P(.16f, .5f, 1.0f));
                    { var c0 = -f * hl * .2f + X * sx * hw * .9f - u * hh * .1f; Tube(hb, new[] { c0, c0 + X * (sx * .14f * hs) - f * (.3f * hs), c0 + X * (sx * .2f * hs) - f * (.6f * hs) + u * (.06f * hs) }, new[] { new Vector2(.06f, .06f) * hs, new Vector2(.04f, .04f) * hs, new Vector2(.006f, .006f) * hs }, hc, 6, 2); }
                    break;
            }
        }
        if (p.B("beard")) Tube(B(head, MSkin), new[] { f * hl * .4f - u * hh * .7f, f * hl * .38f - u * (hh * .7f + hl * .35f), f * hl * .32f - u * (hh * .7f + hl * .6f) }, new[] { new Vector2(hw * .3f, hw * .3f), new Vector2(hw * .22f, hw * .22f), new Vector2(.002f, .002f) }, Flat(p.C("beardCol", belly)), 6, 1);
        if (p.B("comb"))
        {
            var red = Flat(new Color(.8f, .12f, .1f));
            for (int i = 0; i < 3; i++) Ball(B(head, MSkin), f * hl * (.25f - i * .22f) + u * (hh * 1.05f + hw * (i == 1 ? .25f : .1f)), new Vector3(hw * .18f, hw * .42f, hw * .32f), red, 6);
            Ball(B(head, MSkin), f * hl * .45f - u * hh * 1.0f, new Vector3(hw * .2f, hw * .4f, hw * .22f), red, 6);
        }
        float tusk = p.F("tusks", 0);
        if (tusk > 0) foreach (float sx in new[] { -1f, 1f }) Limb(B(head, MHorn), f * (hl * .5f + sl * .55f) + X * sx * sw * .9f - u * sh * .4f, f * (hl * .5f + sl * .8f) + X * sx * sw * 1.5f + u * tusk, sw * .3f, sw * .05f, Flat(hornCol), 5);

        // ---- tail
        string tail = p.S("tail", "bushy"); float tl = p.F("tailLen", .52f), tr = p.F("tailR", .055f), bulge = p.F("tailBulge", .65f), tipK = p.F("tailTip", .2f);
        Color tailCol = p.C("tailCol", main), tailTip = p.C("tailTipCol", tailCol), tailUnder = p.C("tailUnder", tailCol);
        var tb = T(0, top - ru.Y * .45f, .5f * L);
        if (tail == "fan" && tl > 0)
        {
            int tp = Pv("tail0", body, tb, new Vector3(-D(p.F("tailAng", 20)), 0, 0)); bp.Tail.Add(tp);
            var rim = new List<Vector3>(); var rc = new List<Color>(); float spread = D(p.F("tailSpread", 32));
            for (int i = 0; i <= 6; i++) { float a = -spread + 2 * spread * i / 6f; rim.Add(new Vector3(Mathf.Sin(a) * tl, 0, Mathf.Cos(a) * tl * (i % 2 == 0 ? 1f : .93f))); rc.Add(tailTip); }
            Fan(B(tp, MSheet), new Vector3(0, 0, -tl * .15f), tailCol, rim, rc, false);
            Ball(B(tp, MSkin), new Vector3(0, 0, tl * .12f), new Vector3(ru.X * .45f, ru.Y * .3f, tl * .3f), Coat, 8);
        }
        else if (tl > 0)
        {
            int ns = Mathf.Clamp((int)p.F("tailSegs", 4), 1, 10); float sl2 = tl / ns, ang = D(p.F("tailAng", -38)), curve = D(p.F("tailCurve", 10));
            float R(float s) { float r = tr * (1 - bulge * .3f + bulge * Mathf.Sin(Mathf.Pi * Mathf.Pow(Mathf.Clamp(s, 0, 1), .8f))); if (tail == "tuft") r = tr * (s > .78f ? tipK : 1f - .35f * s); else if (tail == "taper") r = tr * Mathf.Lerp(1f, tipK, Mathf.Pow(s, .85f)); else if (s > .75f) r = Mathf.Lerp(r, tr * tipK, (s - .75f) / .25f); return r; }
            int par = body;
            for (int i = 0; i < ns; i++)
            {
                int tp = Pv("tail" + i, par, i == 0 ? tb : new Vector3(0, 0, sl2), new Vector3(-(i == 0 ? ang : curve), 0, 0)); bp.Tail.Add(tp); par = tp;
                float s0 = i / (float)ns, s1 = (i + 1f) / ns, sm = (s0 + s1) / 2; int ii = i;
                Tube(B(tp, MSkin), new[] { new Vector3(0, 0, -sl2 * .12f), new Vector3(0, 0, sl2 * .5f), new Vector3(0, 0, sl2 * (i == ns - 1 ? 1f : 1.08f)) }, new[] { new Vector2(R(s0), R(s0)), new Vector2(R(sm), R(sm)), new Vector2(R(s1) * (i == ns - 1 ? .6f : 1f), R(s1) * (i == ns - 1 ? .6f : 1f)) },
                    (q, d) => { float s = (ii + Mathf.Clamp(q.Z / sl2, 0, 1)) / ns; Color c = d < -.25f ? tailUnder : Coat3(q, d, tailCol); return s > .78f ? c.Lerp(tailTip, Mathf.Clamp((s - .78f) / .1f, 0, 1)) : c; }, Mathf.Max(6, seg - 4), 2, tail == "bushy" ? lump + .06f : 0);
                if (spines > 0 && i < ns - 1) Spine(B(tp, MSkin), new Vector3(0, R(sm) * .8f, sl2 * .5f), new Vector3(0, .93f, .37f), ch.Y * .26f * spines * (1 - sm * .7f));
                if (i == ns - 1 && p.B("tailSpade")) Tube(B(tp, MSkin), new[] { new Vector3(0, 0, sl2 * .85f), new Vector3(0, 0, sl2 * 1.2f), new Vector3(0, 0, sl2 * 1.75f) }, new[] { new Vector2(tr * .12f, tr * .1f), new Vector2(tr * .75f, tr * .12f), new Vector2(.002f, .002f) }, Flat(spineCol), 6, 2);
            }
        }

        // ---- wings
        string wing = p.S("wing"); float W = p.F("wingSpan", 1f), Cw = p.F("wingChord", .35f);
        Color wingCol = p.C("wingCol", back), wingTip = p.C("wingTipCol", wingCol.Darkened(.35f));
        bp.Flies = wing != "" && p.B("flies", true); bp.Bat = wing == "bat"; bp.FlapHz = p.F("flapHz", 2.5f); bp.WingRest = p.F("wingRest", 1f); bp.Glide = p.F("glide", 0);
        if (wing != "") foreach (float sx in new[] { -1f, 1f })
        {
            int sh2 = Pv("wing" + (sx > 0 ? "R" : "L"), body, T(sx * ch.X * .78f, top + wither - ch.Y * .3f, -.30f * L));
            Vector3 V(float x, float y, float z) => new(sx * x * W, y * W, z * W);
            int el;
            if (wing == "bat")
            {
                var E = V(.30f, .04f, .06f); var bone = Flat(wingCol.Darkened(.3f)); Color mIn = wingCol, mOut = wingCol.Lightened(.16f), mB = wingCol.Darkened(.25f);
                Limb(B(sh2, MSkin), Vector3.Zero, E, W * .03f, W * .022f, bone, 6);
                Fan(B(sh2, MSheet), Vector3.Zero, mB, new[] { E, V(.22f, 0, .42f), V(.03f, -.02f, .5f) }, new[] { mB, mOut, mIn }, false);
                el = Pv("wingOut", sh2, E);
                var Wr = V(.28f, .03f, -.12f); Vector3[] F = { Wr + V(.44f, -.02f, -.02f), Wr + V(.36f, -.03f, .30f), Wr + V(.20f, -.03f, .48f), V(-.08f, -.04f, .36f) };
                Limb(B(el, MSkin), Vector3.Zero, Wr, W * .022f, W * .015f, bone, 6);
                for (int i = 0; i < 3; i++) Limb(B(el, MSkin), Wr, F[i], W * .013f, W * .004f, bone, 5);
                Limb(B(el, MSkin), Wr, Wr + V(.02f, .03f, -.1f), W * .012f, W * .002f, Flat(hornCol.Darkened(.4f)), 5);
                var rim = new List<Vector3>(); var rc = new List<Color>();
                for (int i = 0; i < F.Length; i++) { rim.Add(F[i]); rc.Add(mB); if (i < F.Length - 1) { rim.Add(((F[i] + F[i + 1]) / 2).Lerp(Wr, .16f)); rc.Add(mOut); } }
                Fan(B(el, MSheet), Wr, mIn, rim, rc, false);
                Fan(B(el, MSheet), Vector3.Zero, mB, new[] { Wr, F[3] }, new[] { mIn, mB }, false);
            }
            else
            {
                var E = V(.34f, .02f, .02f);
                Limb(B(sh2, MSkin), Vector3.Zero, E, W * .035f, W * .026f, Flat(wingCol), 6);
                Fan(B(sh2, MSheet), V(.17f, 0, Cw * .5f / W), wingCol, new[] { Vector3.Zero, E, E + new Vector3(0, -.02f * W, Cw * .95f), new Vector3(0, -.01f * W, Cw) }, new[] { wingCol, wingCol, wingTip, wingTip }, true);
                el = Pv("wingOut", sh2, E);
                var Wr = V(.26f, 0, -.02f); var tip = V(.66f, -.01f, Cw * .25f / W);
                Limb(B(el, MSkin), Vector3.Zero, Wr, W * .026f, W * .014f, Flat(wingCol), 6);
                Fan(B(el, MSheet), V(.25f, 0, Cw * .4f / W), wingCol, new[] { Vector3.Zero, Wr, V(.48f, 0, 0), tip, V(.45f, 0, Cw * .7f / W), V(.22f, 0, Cw * .9f / W), new Vector3(0, 0, Cw * .95f) }, new[] { wingCol, wingCol, wingCol, wingTip, wingTip, wingTip, wingTip }, true);
            }
            bp.Wings.Add(new WingRef { Sh = sh2, El = el, Side = sx });
        }

        // ---- commit: meshes, the height (rest pose, horns left out) and the saddle point
        var xf = new Transform3D[bp.Pivs.Count];
        for (int i = 0; i < bp.Pivs.Count; i++) { var pv = bp.Pivs[i]; var loc = new Transform3D(Basis.FromEuler(pv.Rot), pv.Pos); xf[i] = pv.Parent >= 0 ? xf[pv.Parent] * loc : loc; }
        float maxY = .05f;
        foreach (var kv in bp.Bufs)
        {
            var b = kv.Value; if (b.I.Count == 0) continue;
            bp.Meshes.Add((kv.Key.piv, kv.Key.mat, Commit(b)));
            bool wingPart = false; foreach (var w in bp.Wings) if (kv.Key.piv == w.Sh || kv.Key.piv == w.El) wingPart = true;
            if (kv.Key.mat == MHorn || wingPart || bp.Ears.Contains(kv.Key.piv)) continue;
            foreach (var v in b.V) maxY = Mathf.Max(maxY, (xf[kv.Key.piv] * v).Y);
        }
        bp.Top = maxY; bp.Bufs.Clear();
        float szf = p.F("saddleZ", -.14f);
        bp.SaddleY = TopAt(szf) + p.F("saddleUp", 0); bp.SaddleZ = szf * L;
        return bp;
    }
}
