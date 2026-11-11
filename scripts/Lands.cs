using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// The wider world from data/lands.json: heightfield islands across the sea from the home island, each with biomes, named
/// mountains, lakes, rivers, roads (with bridges over the rivers) and placeholder settlements (villages, stilt villages, walled
/// cities, castles, ruins, tombs, camps, cave mouths); great bridges between the islands; floating sky islands reached by updrafts.
/// Heights are worked out once in Load (static, the same on every machine); Build makes the meshes, colliders, trees and buildings.
/// Ground cover (grass) is streamed by Flora and asks CoverAt here how dense it should be.
/// </summary>
public static class Lands
{
    public sealed class Biome { public string Id = ""; public float Sand, Forest, Snow, Rock, Ash, Hills = 20, Grass = 1, TreeD = .5f, PropD; public string Trees = ""; public Color GrassTint = Colors.White; public List<Kit.Model> Props = new(); public List<Color> TreeTints = new(); }
    public sealed class Place { public string Id = "", Name = "", Kind = ""; public float X, Z, R, Yaw, H; public bool Capital; }
    public sealed class Line { public string Name = ""; public Vector2[] P = Array.Empty<Vector2>(); public float W = 10; public float[] Bed = Array.Empty<float>(); public Rect2 Box; }
    public sealed class Lake { public string Name = ""; public float X, Z, R, Level = float.NaN; }
    public sealed class Mountain { public string Name = ""; public float X, Z, R, H; public bool Crater, Mesa; }
    public sealed class Land
    {
        public string Id = "", Name = "", Default = "meadow"; public float Cx, Cz, Size, Cell, Half, SnowLine = 80, HillMul = 1, Shape = .25f; public int N; public float[] H = null!; public float Seed;
        public readonly List<(string b, float x, float z, float r)> Regions = new(); public readonly List<Mountain> Mts = new(); public readonly List<Lake> Lakes = new();
        public readonly List<Line> Rivers = new(), Roads = new(); public readonly List<Place> Places = new(); public readonly Dictionary<string, Vector2> Docks = new();
        public JsonElement Look; public bool HasLook;
        public float X0 => Cx - Half; public float Z0 => Cz - Half;
        public bool Inside(float x, float z) => x >= X0 && z >= Z0 && x < Cx + Half && z < Cz + Half;
    }
    public sealed class SkyIsle { public string Id = "", Name = "", Biome = "meadow"; public float X, Y, Z, R, Seed; public bool Temple; public string? Link; }
    public sealed class Bridge { public string Name = ""; public Vector2 A, B; }

    public static readonly List<Land> All = new(); public static readonly List<SkyIsle> Sky = new(); public static readonly List<Bridge> Bridges = new();
    static readonly Dictionary<string, Biome> Biomes = new(); static readonly Dictionary<string, List<Kit.Model>> TreeSets = new();
    public static float Extent = 1700f; public static string HomeName = "The Verdant Isle", SkyName = "The Skyreach", SeaName = "The Open Sea";
    static bool _loaded;

    static float Sm(float a, float b, float x) { float t = Mathf.Clamp((x - a) / (b - a), 0f, 1f); return t * t * (3 - 2 * t); }
    static float F(JsonElement e, string k, float d = 0f) => JsonUtil.F(e, k, d);
    static string S(JsonElement e, string k, string d = "") => JsonUtil.S(e, k, d);

    // =================================================================== data + heights
    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        if (!FileAccess.FileExists("res://data/lands.json")) return;
        using var doc = JsonUtil.Load("res://data/lands.json"); var r = doc.RootElement;
        Extent = F(r, "extent", Extent); HomeName = S(r, "home", HomeName); SkyName = S(r, "skyName", SkyName); SeaName = S(r, "sea", SeaName);
        foreach (var b in r.GetProperty("biomes").EnumerateObject())
        {
            var e = b.Value; var bi = new Biome { Id = b.Name, Sand = F(e, "sand"), Forest = F(e, "forest"), Snow = F(e, "snow"), Rock = F(e, "rock"), Ash = F(e, "ash"), Hills = F(e, "hills", 20), Grass = F(e, "grass", 1), TreeD = F(e, "treeDensity", .5f), Trees = S(e, "trees"), PropD = F(e, "propDensity") };
            if (S(e, "grassTint") != "") bi.GrassTint = Color.FromHtml(S(e, "grassTint"));
            if (e.TryGetProperty("props", out var pr)) bi.Props = Kit.Models(pr);
            // leaf colour multipliers (above 1 allowed): autumn reds and golds on the green leaf textures
            if (e.TryGetProperty("treeTints", out var tt)) foreach (var c in tt.EnumerateArray()) bi.TreeTints.Add(new Color(c[0].GetSingle(), c[1].GetSingle(), c[2].GetSingle()));
            Biomes[b.Name] = bi;
        }
        foreach (var t in r.GetProperty("treeSets").EnumerateObject()) TreeSets[t.Name] = Kit.Models(t.Value);
        foreach (var e in r.GetProperty("lands").EnumerateArray())
        {
            var L = new Land { Id = S(e, "id"), Name = S(e, "name"), Cx = F(e, "x"), Cz = F(e, "z"), Size = F(e, "size", 1024), Cell = F(e, "cell", 1.6f), Default = S(e, "biome", "meadow"), SnowLine = F(e, "snowLine", 80), HillMul = F(e, "hills", 1), Shape = F(e, "shape", .25f), Seed = F(e, "seed", 1) };
            L.Half = L.Size / 2; L.N = Mathf.RoundToInt(L.Size / L.Cell); L.Cell = L.Size / L.N;
            if (e.TryGetProperty("look", out var lk)) { L.Look = lk.Clone(); L.HasLook = true; }
            Vector2 W(float x, float z) => new(x + L.Cx, z + L.Cz);
            if (e.TryGetProperty("biomes", out var bs)) foreach (var b in bs.EnumerateArray()) L.Regions.Add((S(b, "type"), F(b, "x") + L.Cx, F(b, "z") + L.Cz, F(b, "r", 100)));
            if (e.TryGetProperty("mountains", out var ms)) foreach (var m in ms.EnumerateArray())
                L.Mts.Add(new Mountain { Name = S(m, "name"), X = F(m, "x") + L.Cx, Z = F(m, "z") + L.Cz, R = F(m, "r", 100), H = F(m, "h", 40), Crater = m.TryGetProperty("crater", out var c) && c.ValueKind == JsonValueKind.True, Mesa = m.TryGetProperty("mesa", out var me) && me.ValueKind == JsonValueKind.True });
            if (e.TryGetProperty("lakes", out var ls)) foreach (var l in ls.EnumerateArray()) L.Lakes.Add(new Lake { Name = S(l, "name"), X = F(l, "x") + L.Cx, Z = F(l, "z") + L.Cz, R = F(l, "r", 30), Level = l.TryGetProperty("level", out var lv) ? lv.GetSingle() : float.NaN });
            if (e.TryGetProperty("rivers", out var rs)) foreach (var rv in rs.EnumerateArray())
            {
                var pts = new List<Vector2>(); foreach (var p in rv.GetProperty("pts").EnumerateArray()) pts.Add(W(p[0].GetSingle(), p[1].GetSingle()));
                L.Rivers.Add(new Line { Name = S(rv, "name"), W = F(rv, "width", 10), P = Densify(pts.ToArray(), 6f) });
            }
            if (e.TryGetProperty("places", out var ps)) foreach (var p in ps.EnumerateArray())
                L.Places.Add(new Place { Id = S(p, "id"), Name = S(p, "name"), Kind = S(p, "kind", "village"), X = F(p, "x") + L.Cx, Z = F(p, "z") + L.Cz, R = F(p, "r", 25), Yaw = F(p, "yaw"), Capital = p.TryGetProperty("capital", out var cp) && cp.ValueKind == JsonValueKind.True });
            if (e.TryGetProperty("docks", out var ds)) foreach (var d in ds.EnumerateObject()) L.Docks[d.Name] = W(d.Value[0].GetSingle(), d.Value[1].GetSingle());
            if (e.TryGetProperty("roads", out var rds)) foreach (var rd in rds.EnumerateArray())
            {
                var anchors = new List<Vector2>();
                foreach (var a in rd.EnumerateArray())
                {
                    string id = a.GetString() ?? "";
                    if (id.StartsWith("dock:") && L.Docks.TryGetValue(id[5..], out var dk)) anchors.Add(dk);
                    else { var pl = L.Places.Find(q => q.Id == id); if (pl != null) anchors.Add(new Vector2(pl.X, pl.Z)); }
                }
                if (anchors.Count >= 2) L.Roads.Add(new Line { W = 3.4f, P = Wiggle(anchors, L.Seed) });
            }
            All.Add(L);
        }
        if (r.TryGetProperty("bridges", out var brs)) foreach (var b in brs.EnumerateArray())
            Bridges.Add(new Bridge { Name = S(b, "name"), A = new Vector2(b.GetProperty("a")[0].GetSingle(), b.GetProperty("a")[1].GetSingle()), B = new Vector2(b.GetProperty("b")[0].GetSingle(), b.GetProperty("b")[1].GetSingle()) });
        if (r.TryGetProperty("sky", out var sk)) foreach (var s in sk.EnumerateArray())
            Sky.Add(new SkyIsle { Id = S(s, "id"), Name = S(s, "name"), X = F(s, "x"), Y = F(s, "y", 180), Z = F(s, "z"), R = F(s, "r", 60), Biome = S(s, "biome", "meadow"), Temple = s.TryGetProperty("temple", out var tp) && tp.ValueKind == JsonValueKind.True, Link = S(s, "link") is { Length: > 0 } lk2 ? lk2 : null, Seed = F(s, "seed", 1) });
        foreach (var L in All) Heights(L);
    }

    static Vector2[] Densify(Vector2[] p, float step)
    {
        var o = new List<Vector2>();
        for (int i = 0; i < p.Length - 1; i++) { float len = (p[i + 1] - p[i]).Length(); int n = Math.Max(1, (int)(len / step)); for (int k = 0; k < n; k++) o.Add(p[i].Lerp(p[i + 1], k / (float)n)); }
        o.Add(p[^1]); return o.ToArray();
    }
    /// <summary>A road through the anchors that bends a little between them (ends stay put).</summary>
    static Vector2[] Wiggle(List<Vector2> a, float seed)
    {
        var o = new List<Vector2>();
        for (int i = 0; i < a.Count - 1; i++)
        {
            var d = a[i + 1] - a[i]; float len = d.Length(); var nrm = new Vector2(-d.Y, d.X) / Mathf.Max(len, .01f); int n = Math.Max(2, (int)(len / 8f));
            for (int k = 0; k < n; k++)
            {
                float t = k / (float)n, bend = (World.Fbm(a[i].X * .01f + t * 3f + seed, a[i].Y * .01f - seed, 3) - .5f) * len * .35f * Mathf.Sin(t * Mathf.Pi);
                o.Add(a[i] + d * t + nrm * bend);
            }
        }
        o.Add(a[^1]); return o.ToArray();
    }
    static Rect2 BoxOf(Vector2[] p, float pad) { var r = new Rect2(p[0], Vector2.Zero); foreach (var q in p) r = r.Expand(q); return r.Grow(pad); }

    static void Heights(Land L)
    {
        foreach (var p in L.Places) p.H = Mathf.Max(Raw(L, p.X, p.Z), 2.5f);
        foreach (var lk in L.Lakes) if (float.IsNaN(lk.Level)) lk.Level = Mathf.Max(Raw(L, lk.X, lk.Z) - 1.4f, .55f);
        // river beds only ever run downhill, and always sit under the banks
        foreach (var rv in L.Rivers)
        {
            rv.Bed = new float[rv.P.Length]; float prev = 1e9f;
            for (int i = 0; i < rv.P.Length; i++) { prev = Mathf.Min(prev, Raw(L, rv.P[i].X, rv.P[i].Y) - 1.7f); rv.Bed[i] = Mathf.Max(prev, -1.6f); }
            rv.Box = BoxOf(rv.P, rv.W * 2.2f);
        }
        foreach (var rd in L.Roads) rd.Box = BoxOf(rd.P, 8f);
        int n1 = L.N + 1; L.H = new float[n1 * n1];
        Parallel.For(0, n1, iz => { for (int ix = 0; ix < n1; ix++) L.H[iz * n1 + ix] = Final(L, L.X0 + ix * L.Cell, L.Z0 + iz * L.Cell); });
    }

    /// <summary>Weighted biome mix at a point: the land's own biome, with the regions blended in over wobbly borders.</summary>
    struct Mix { public float Sand, Forest, Snow, Rock, Ash, Hills, Grass, TreeD; public Biome Top; public Color GrassTint; }
    static Mix MixAt(Land L, float x, float z)
    {
        var m = new Mix(); float tot = 0, top = -1, rest = 1;
        float jit = (World.Fbm(x * .011f + 7 + L.Seed, z * .011f - 3, 3) - .5f) * 110f;
        void Add(Biome b, float w)
        {
            m.Sand += b.Sand * w; m.Forest += b.Forest * w; m.Snow += b.Snow * w; m.Rock += b.Rock * w; m.Ash += b.Ash * w; m.Hills += b.Hills * w; m.Grass += b.Grass * w; m.TreeD += b.TreeD * w;
            m.GrassTint += b.GrassTint * w; tot += w; if (w > top) { top = w; m.Top = b; }
        }
        foreach (var rg in L.Regions)
        {
            float d = new Vector2(x - rg.x, z - rg.z).Length() + jit, w = 1 - Sm(rg.r * .7f, rg.r * 1.05f, d);
            if (w <= 0 || !Biomes.TryGetValue(rg.b, out var b)) continue;
            Add(b, w); rest = Mathf.Min(rest, 1 - w);
        }
        Add(Biomes.TryGetValue(L.Default, out var db) ? db : new Biome(), Mathf.Max(rest, .0001f));
        float k = 1f / tot; m.Sand *= k; m.Forest *= k; m.Snow *= k; m.Rock *= k; m.Ash *= k; m.Hills *= k; m.Grass *= k; m.TreeD *= k; m.GrassTint *= k;
        return m;
    }

    /// <summary>Terrain before lakes, rivers and building plots: island mask, biome hills, small bumps, mountains (mesas, a crater).</summary>
    static float Raw(Land L, float x, float z)
    {
        float lx = x - L.Cx, lz = z - L.Cz, s = L.Seed * 13.7f;
        float d = Mathf.Sqrt(lx * lx + lz * lz) / L.Half + (World.Fbm(lx * .004f + s, lz * .004f - s, 3) - .5f) * L.Shape * 2f;
        float mask = 1 - Sm(.6f, .9f, d);
        float hills = Mathf.Pow(World.Fbm(lx * .0065f + s, lz * .0065f + 11 + s, 5), 1.35f) * MixAt(L, x, z).Hills * L.HillMul;
        hills += (World.Fbm(x * .09f + 51, z * .09f - 17, 3) - .5f) * .7f;
        float m = 0;
        foreach (var mt in L.Mts)
        {
            float dd = new Vector2(x - mt.X, z - mt.Z).Length() / mt.R; if (dd >= 1) continue;
            float u = 1 - dd, rough = .8f + World.Fbm(x * .03f, z * .03f, 3) * .5f;
            if (mt.Mesa) m += mt.H * Sm(.18f, .34f, u + (World.Fbm(x * .05f, z * .05f, 3) - .5f) * .12f) * (.94f + .06f * rough);
            else m += mt.H * Mathf.Pow(u, 1.7f) * rough;
            if (mt.Crater && u > .84f) m -= mt.H * .4f * Sm(.84f, .97f, u);
        }
        return mask * (hills + m) - (1 - mask) * 12f + 1.5f;
    }

    static float Final(Land L, float x, float z)
    {
        float h = Raw(L, x, z);
        foreach (var lk in L.Lakes) { float d = new Vector2(x - lk.X, z - lk.Z).Length(); if (d < lk.R * 1.5f) h = Mathf.Min(h, Mathf.Lerp(lk.Level - 2.4f, h, Sm(lk.R * .55f, lk.R * 1.5f, d))); }
        foreach (var rv in L.Rivers) { if (!rv.Box.HasPoint(new Vector2(x, z))) continue; var (d, bed) = Near(rv, x, z); if (d < rv.W * 2f) h = Mathf.Min(h, Mathf.Lerp(bed, h, Sm(rv.W * .35f, rv.W * 1.7f, d))); }
        foreach (var p in L.Places) { float d = new Vector2(x - p.X, z - p.Z).Length(), b = 24f; if (d < p.R + b) h = Mathf.Lerp(h, p.H, 1 - Sm(p.R, p.R + b, d)); }
        return h;
    }

    /// <summary>Distance to a line and the bed height there (rivers).</summary>
    static (float d, float bed) Near(Line l, float x, float z)
    {
        float best = 1e9f, bed = 0; var q = new Vector2(x, z);
        for (int i = 0; i < l.P.Length - 1; i++)
        {
            var a = l.P[i]; var ab = l.P[i + 1] - a; float t = Mathf.Clamp((q - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0, 1);
            float d = (a + ab * t - q).Length(); if (d < best) { best = d; bed = l.Bed.Length > i + 1 ? Mathf.Lerp(l.Bed[i], l.Bed[i + 1], t) : 0; }
        }
        return (best, bed);
    }
    static float RoadDist(Land L, float x, float z)
    {
        float best = 1e9f; var q = new Vector2(x, z);
        foreach (var rd in L.Roads) if (rd.Box.HasPoint(q)) best = Mathf.Min(best, Near(rd, x, z).d);
        return best;
    }

    // =================================================================== queries
    public static Land? LandAt(float x, float z) { foreach (var L in All) if (L.Inside(x, z)) return L; return null; }

    /// <summary>Ground height on the lands (World.GroundAt asks here outside the home island). -9 over open sea.</summary>
    public static float GroundAt(float x, float z)
    {
        foreach (var L in All)
        {
            if (!L.Inside(x, z)) continue;
            float fx = (x - L.X0) / L.Cell, fz = (z - L.Z0) / L.Cell; int ix = Math.Min((int)fx, L.N - 1), iz = Math.Min((int)fz, L.N - 1);
            float u = fx - ix, v = fz - iz; int n1 = L.N + 1, i0 = iz * n1 + ix;
            float h0 = L.H[i0], h1 = L.H[i0 + 1], h2 = L.H[i0 + n1], h3 = L.H[i0 + n1 + 1];
            return u + v <= 1 ? h0 + u * (h1 - h0) + v * (h2 - h0) : h3 + (1 - u) * (h2 - h3) + (1 - v) * (h1 - h3);
        }
        return -9f;
    }

    /// <summary>Surface layers for the terrain shader: road, sand, forest floor, snow (vertex colour) and extra rock, ash (UV2).</summary>
    static (float road, float sand, float forest, float snow, float rock, float ash) Surface(Land L, float x, float z, float h, float ny)
    {
        var m = MixAt(L, x, z);
        float road = 1 - Sm(1.6f, 3.6f, RoadDist(L, x, z));
        float sand = Mathf.Max(m.Sand * (.8f + .2f * World.Fbm(x * .05f, z * .05f, 2)), 1 - Sm(.6f, 2.2f, h));
        float forest = m.Forest * (.45f + .55f * Sm(.38f, .62f, World.Fbm(x * .02f + 3, z * .02f + 9, 3)));
        float snow = Mathf.Max(m.Snow * Sm(.3f, .55f, World.Fbm(x * .03f + 5, z * .03f, 3) + .15f), Sm(L.SnowLine, L.SnowLine + 16, h) * Sm(.55f, .8f, ny));
        return (road, sand, forest, snow, m.Rock, m.Ash);
    }

    /// <summary>How much ground cover grows here (0..1) and its tint; Flora calls this outside the home island.</summary>
    public static float CoverAt(float x, float z, float h, bool grass, out Color tint)
    {
        tint = Colors.White;
        var L = LandAt(x, z); if (L == null) return 0;
        if (RoadDist(L, x, z) < 2.8f) return 0;
        foreach (var p in L.Places) if (new Vector2(x - p.X, z - p.Z).Length() < p.R * (p.Kind == "city" || p.Kind == "castle" ? 1.05f : .5f)) return 0;
        foreach (var lk in L.Lakes) if (new Vector2(x - lk.X, z - lk.Z).Length() < lk.R * .9f) return 0;
        var m = MixAt(L, x, z); tint = m.GrassTint;
        if (!grass) return .6f;
        var sf = Surface(L, x, z, h, 1f);
        return m.Grass * (1 - sf.sand) * (1 - sf.snow * .9f) * (1 - sf.ash);
    }

    /// <summary>Open ground under a footstep on the lands and sky islands: snow, sand, dirt (roads, ash), leaves (forest floor), stone (towns, rock) or grass.</summary>
    public static string StepSurface(float x, float z, float h)
    {
        foreach (var s in Sky) if (h > s.Y - 30 && new Vector2(x - s.X, z - s.Z).Length() < s.R + 4) return new Vector2(x - s.X, z - s.Z).Length() < 10 && s.Temple ? "stone" : "grass";
        var L = LandAt(x, z); if (L == null) return "stone";   // bridges and piers out at sea
        foreach (var p in L.Places) if ((p.Kind is "city" or "castle" or "ruins" or "tombs") && new Vector2(x - p.X, z - p.Z).Length() < p.R * .9f) return "stone";
        var sf = Surface(L, x, z, h, 1f);
        if (sf.snow > .5f) return "snow"; if (sf.road > .5f || sf.ash > .5f) return "dirt"; if (sf.sand > .5f) return "sand";
        if (sf.rock > .5f || h > L.SnowLine) return "stone"; if (sf.forest > .45f) return "leaves";
        return "grass";
    }

    /// <summary>The region name at a point: a land, the sky islands, the home island or the sea.</summary>
    public static string RegionAt(Vector3 p)
    {
        foreach (var s in Sky) if (p.Y > s.Y - 40 && new Vector2(p.X - s.X, p.Z - s.Z).Length() < s.R + 30) return SkyName;
        if (Mathf.Abs(p.X) < World.Half && Mathf.Abs(p.Z) < World.Half) return HomeName;
        var L = LandAt(p.X, p.Z); if (L != null && GroundAt(p.X, p.Z) > -1f) return L.Name;
        return SeaName;
    }

    // =================================================================== sky islands
    static float EdgeR(SkyIsle s, float a) => s.R * Mathf.Clamp(.8f + .32f * World.Fbm(Mathf.Cos(a) * 1.7f + s.Seed * 3.1f, Mathf.Sin(a) * 1.7f - s.Seed, 3), .7f, 1f);
    /// <summary>Top surface height of a sky island at (x, z); NaN off its edge.</summary>
    public static float SkyTop(SkyIsle s, float x, float z)
    {
        float lx = x - s.X, lz = z - s.Z, d = Mathf.Sqrt(lx * lx + lz * lz), u = d / EdgeR(s, Mathf.Atan2(lz, lx));
        if (u > 1) return float.NaN;
        return s.Y + (World.Fbm(x * .025f + s.Seed * 7, z * .025f - s.Seed, 4) - .38f) * 10f * (1 - u * u * u) - Sm(.78f, 1f, u) * 2.2f;
    }
    public static SkyIsle? SkyGet(string id) => Sky.Find(s => s.Id == id);

    // =================================================================== build
    static StandardMaterial3D _stone = null!, _sandstone = null!, _wood = null!, _dark = null!, _cloth = null!, _white = null!, _void = null!, _rope = null!;
    static Func<float> _rg = World.Rng(1u);
    static float R(float a, float b) => a + _rg() * (b - a);

    public static void Build(Node3D over)
    {
        Load(); if (All.Count == 0 && Sky.Count == 0) return;
        _stone = World.Mat(new Color(.62f, .6f, .57f), null, 1f); Gfx.Tex(_stone, "stone", 2.6f);
        _sandstone = World.Mat(new Color(.82f, .68f, .5f), null, 1f); Gfx.Tex(_sandstone, "stone", 3f);
        _wood = World.Mat(new Color(.46f, .32f, .2f)); Gfx.Tex(_wood, "planks", 1.6f);
        _dark = World.Mat(new Color(.25f, .18f, .12f)); Gfx.Tex(_dark, "grain", 1.2f);
        _cloth = World.Mat(new Color(.72f, .62f, .46f)); Gfx.Tex(_cloth, "cloth", .8f);
        _white = World.Mat(new Color(.9f, .88f, .84f), null, .8f); Gfx.Tex(_white, "stone", 3f, true, .5f);
        _rope = World.Mat(new Color(.5f, .4f, .26f));
        _void = new StandardMaterial3D { AlbedoColor = new Color(.01f, .01f, .015f), ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        var root = new Node3D { Name = "Lands" }; over.AddChild(root);
        foreach (var L in All)
        {
            _rg = World.Rng((uint)(L.Seed * 7919 + 13));
            var lroot = new Node3D { Name = L.Id }; root.AddChild(lroot);
            Terrain(L, lroot); Water(L, lroot); Vegetation(L, lroot);
            foreach (var p in L.Places) BuildPlace(L, p, lroot);
            RiverBridges(L, lroot);
        }
        foreach (var b in Bridges) { _rg = World.Rng((uint)(b.A.X * 31 + b.B.Y * 7 + 5)); SeaBridge(b, root); }
        foreach (var s in Sky) { _rg = World.Rng((uint)(s.Seed * 4099 + 3)); SkyIsland(s, root); }
        foreach (var s in Sky) if (s.Link != null && SkyGet(s.Link) is { } o) RopeBridge(s, o, root);
        RegisterPois();
    }

    // ---------------------------------------------------------------- terrain
    static void Terrain(Land L, Node3D parent)
    {
        int n1 = L.N + 1, V = n1 * n1;
        var verts = new Vector3[V]; var norms = new Vector3[V]; var cols = new Color[V]; var uv2 = new Vector2[V];
        Parallel.For(0, n1, iz =>
        {
            for (int ix = 0; ix < n1; ix++)
            {
                int i = iz * n1 + ix; float x = L.X0 + ix * L.Cell, z = L.Z0 + iz * L.Cell, h = L.H[i];
                verts[i] = new Vector3(x, h, z);
                float hl = L.H[iz * n1 + Math.Max(ix - 1, 0)], hr = L.H[iz * n1 + Math.Min(ix + 1, L.N)], hd = L.H[Math.Max(iz - 1, 0) * n1 + ix], hu = L.H[Math.Min(iz + 1, L.N) * n1 + ix];
                var nrm = new Vector3(hl - hr, 2 * L.Cell, hd - hu).Normalized(); norms[i] = nrm;
                var s = Surface(L, x, z, h, nrm.Y); cols[i] = new Color(s.road, s.sand, s.forest, s.snow); uv2[i] = new Vector2(s.rock, s.ash);
            }
        });
        var idx = new int[L.N * L.N * 6]; int k = 0;
        for (int iz = 0; iz < L.N; iz++) for (int ix = 0; ix < L.N; ix++)
        {
            int i0 = iz * n1 + ix, i1 = i0 + 1, i2 = i0 + n1, i3 = i2 + 1;
            idx[k++] = i0; idx[k++] = i1; idx[k++] = i2; idx[k++] = i1; idx[k++] = i3; idx[k++] = i2;
        }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts; arrays[(int)Mesh.ArrayType.Normal] = norms; arrays[(int)Mesh.ArrayType.Color] = cols;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2; arrays[(int)Mesh.ArrayType.Index] = idx;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var body = new StaticBody3D { Name = "Terrain", CollisionLayer = 1 };
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = TerrainMat(L.HasLook ? L.Look : null) });
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });
        parent.AddChild(body);
    }

    /// <summary>A copy of the home terrain material with this land's look (data/lands.json "look": any terrain shader setting).</summary>
    static ShaderMaterial TerrainMat(JsonElement? look)
    {
        var m = (ShaderMaterial)World.TerrainMaterial!.Duplicate();
        if (look is { } lk)
            foreach (var p in lk.EnumerateObject())
            {
                if (p.Value.ValueKind == JsonValueKind.Number) m.SetShaderParameter(p.Name, p.Value.GetSingle());
                else if (p.Value.ValueKind == JsonValueKind.String) m.SetShaderParameter(p.Name, Color.FromHtml(p.Value.GetString()!));
            }
        World.RealmShaders.Add(m); return m;
    }

    // ---------------------------------------------------------------- lakes and rivers
    static void Water(Land L, Node3D parent)
    {
        var mat = World.WaterMaterial; if (mat == null) return;
        foreach (var lk in L.Lakes)
        {
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); const int Seg = 48; float rr = lk.R * 1.35f;
            for (int i = 0; i < Seg; i++)
            {
                float a0 = i / (float)Seg * Mathf.Tau, a1 = (i + 1) / (float)Seg * Mathf.Tau;
                st.AddVertex(new Vector3(lk.X, lk.Level, lk.Z)); st.AddVertex(new Vector3(lk.X + Mathf.Cos(a1) * rr, lk.Level, lk.Z + Mathf.Sin(a1) * rr)); st.AddVertex(new Vector3(lk.X + Mathf.Cos(a0) * rr, lk.Level, lk.Z + Mathf.Sin(a0) * rr));
            }
            st.GenerateNormals();
            parent.AddChild(new MeshInstance3D { Name = "Lake", Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        foreach (var rv in L.Rivers)
        {
            var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); var P = rv.P; float hw = rv.W * .95f;
            Vector3 Side(int i, float s)
            {
                var d = (P[Math.Min(i + 1, P.Length - 1)] - P[Math.Max(i - 1, 0)]).Normalized(); var n = new Vector2(-d.Y, d.X);
                var q = P[i] + n * hw * s; return new Vector3(q.X, Mathf.Max(rv.Bed[i] + .95f, .3f), q.Y);
            }
            for (int i = 0; i < P.Length - 1; i++)
            {
                Vector3 a = Side(i, -1), b = Side(i, 1), c = Side(i + 1, -1), d = Side(i + 1, 1);
                st.AddVertex(a); st.AddVertex(c); st.AddVertex(b); st.AddVertex(b); st.AddVertex(c); st.AddVertex(d);
            }
            st.GenerateNormals();
            parent.AddChild(new MeshInstance3D { Name = "River", Mesh = st.Commit(), MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
    }

    // ---------------------------------------------------------------- trees and props
    static Color Tint(int i) { float k = i * 0.618034f % 1f, j = i * 0.414213f % 1f; return new Color(.72f + .2f * k, .86f + .16f * j, .76f + .16f * (1 - k)); }
    static bool Clear(Land L, float x, float z, float pad)
    {
        if (RoadDist(L, x, z) < 4f + pad) return false;
        foreach (var p in L.Places) if (new Vector2(x - p.X, z - p.Z).Length() < p.R + 6 + pad) return false;
        foreach (var lk in L.Lakes) if (new Vector2(x - lk.X, z - lk.Z).Length() < lk.R * 1.15f + pad) return false;
        foreach (var rv in L.Rivers) if (rv.Box.HasPoint(new Vector2(x, z)) && Near(rv, x, z).d < rv.W * 1.4f + pad) return false;
        foreach (var b in Bridges) if (DistSeg(new Vector2(x, z), b.A, b.B) < 9f) return false;
        return true;
    }
    static float DistSeg(Vector2 q, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp((q - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0, 1); return (a + ab * t - q).Length(); }

    sealed class Batch { public Kit.Model M; public Color? Leaf; public List<Transform3D> Xf = new(); public List<Color> Col = new(); }
    static readonly Dictionary<string, Mesh> _tinted = new();
    /// <summary>A copy of a tree mesh whose leaf material is recoloured (autumn reds and golds: instance colours cannot go above 1, so they can only darken green leaves).</summary>
    static Mesh LeafTinted(string name, Mesh src, Color leaf)
    {
        string key = name + leaf.ToHtml(); if (_tinted.TryGetValue(key, out var m)) return m;
        if (src is not ArrayMesh am) return src;
        var copy = (ArrayMesh)am.Duplicate();
        for (int i = 0; i < copy.GetSurfaceCount(); i++)
            if (copy.SurfaceGetMaterial(i) is StandardMaterial3D sm && (sm.ResourceName.Contains("Lea") || sm.ResourceName.Contains("leaf")))
            {
                var d = (StandardMaterial3D)sm.Duplicate(); var c = sm.AlbedoColor; d.AlbedoColor = new Color(c.R * leaf.R, c.G * leaf.G, c.B * leaf.B, c.A); copy.SurfaceSetMaterial(i, d);
            }
        return _tinted[key] = copy;
    }
    static void Vegetation(Land L, Node3D parent)
    {
        var batches = new Dictionary<string, Batch>();
        var col = new StaticBody3D { Name = "TreeColliders", CollisionLayer = 1 }; parent.AddChild(col);
        void Put(Kit.Model km, Transform3D t, Color c, Color? leaf = null) { var key = km.Name + "|" + km.Scale + "|" + (leaf?.ToHtml() ?? ""); if (!batches.TryGetValue(key, out var b)) batches[key] = b = new Batch { M = km, Leaf = leaf }; b.Xf.Add(t); b.Col.Add(c); }
        int tries = (int)(L.Size * L.Size / 150f), n = 0;
        for (int i = 0; i < tries; i++)
        {
            float x = L.X0 + _rg() * L.Size, z = L.Z0 + _rg() * L.Size, keep = _rg(), pick = _rg(), w = R(.8f, 1.3f), hgt = R(.75f, 1.45f), yaw = R(0, Mathf.Tau), giant = _rg();
            float h = GroundAt(x, z); if (h < 1.6f || h > L.SnowLine + 30) continue;
            if (Mathf.Abs(GroundAt(x + 1.5f, z) - GroundAt(x - 1.5f, z)) > 1.8f || Mathf.Abs(GroundAt(x, z + 1.5f) - GroundAt(x, z - 1.5f)) > 1.8f) continue;
            var m = MixAt(L, x, z);
            float dens = m.TreeD * (.3f + 1.2f * Sm(.36f, .62f, World.Fbm(x * .02f + 3 + L.Seed, z * .02f + 9, 3)));
            if (keep > dens || !Clear(L, x, z, 0)) continue;
            if (!TreeSets.TryGetValue(m.Top.Trees, out var set) || set.Count == 0) continue;
            var km = set[(int)(pick * set.Count) % set.Count];
            if (giant < .07f) { float g = R(1.6f, 2.3f); w *= g; hgt *= g; }
            var t = new Transform3D(new Basis(Vector3.Up, yaw) * Basis.FromScale(new Vector3(w, hgt, w)), new Vector3(x, h - .15f * w, z));
            Color? leaf = m.Top.TreeTints.Count > 0 ? m.Top.TreeTints[(int)(Mathf.Abs(x * 7.3f + z * 3.1f)) % m.Top.TreeTints.Count] : null;
            Put(km, t, Tint(n++), leaf);
            col.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .4f * w * km.Scale, Height = 4f }, Position = new Vector3(x, h + 2f, z) });
        }
        // small props by biome (rocks, logs, reeds, crystals...)
        int ptries = (int)(L.Size * L.Size / 500f);
        for (int i = 0; i < ptries; i++)
        {
            float x = L.X0 + _rg() * L.Size, z = L.Z0 + _rg() * L.Size, keep = _rg(), pick = _rg(), s = R(.7f, 1.4f), yaw = R(0, Mathf.Tau);
            float h = GroundAt(x, z); if (h < 1.2f) continue;
            var m = MixAt(L, x, z); var props = m.Top.Props; if (props.Count == 0 || keep > m.Top.PropD || !Clear(L, x, z, -2f)) continue;
            var km = props[(int)(pick * props.Count) % props.Count];
            Put(km, new Transform3D(new Basis(Vector3.Up, yaw) * Basis.FromScale(Vector3.One * s), new Vector3(x, h - .1f, z)), Colors.White);
            if (km.Name.Contains("oulder") || km.Name.Contains("cliff") || km.Name.Contains("Rock"))
                col.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .9f * s * km.Scale, Height = 3f * s * km.Scale }, Position = new Vector3(x, h + .5f, z) });
        }
        foreach (var b in batches.Values)
        {
            var p = Kit.Get(b.M.Name); if (p == null) continue;
            var local = new Transform3D(Basis.Identity.Scaled(Vector3.One * b.M.Scale), Vector3.Zero) * p.Xf;
            var list = new List<Transform3D>(b.Xf.Count); foreach (var t in b.Xf) list.Add(t * local);
            World.I!.Chunked(b.Leaf is { } lf && lf != Colors.White ? LeafTinted(b.M.Name, p.Mesh, lf) : p.Mesh, list, b.Col, 1800f);
        }
    }

    // ---------------------------------------------------------------- settlements (placeholders)
    static void K(Node p, string model, Vector3 pos, float yaw, float scale = 1.3f, bool solid = true) => Kit.Add(p, "kit/" + model, pos, yaw, scale, solid);
    static void Pillar(Node p, float r, float h, Vector3 pos, Material m)
    {
        var b = new StaticBody3D { Position = pos + Vector3.Up * h / 2, CollisionLayer = 1 };
        b.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r * .9f, BottomRadius = r, Height = h, RadialSegments = 12 }, MaterialOverride = m });
        b.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = h } }); p.AddChild(b);
    }
    static void Label(Node p, string text, Vector3 pos, float range = 70f) =>
        p.AddChild(new Label3D { Text = text, Position = pos, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, FontSize = 72, PixelSize = .012f, OutlineSize = 14, Modulate = new Color(1f, .9f, .62f), OutlineModulate = new Color(0, 0, 0, .85f), VisibilityRangeEnd = range, NoDepthTest = false });
    static float AngleTo(Vector2 from, Vector2 to) => Mathf.Atan2(to.Y - from.Y, to.X - from.X);
    /// <summary>Angles (around a place) where its roads cross the given radius.</summary>
    static List<float> RoadAngles(Land L, Place p, float rad)
    {
        var o = new List<float>(); var c = new Vector2(p.X, p.Z);
        foreach (var rd in L.Roads)
            for (int i = 0; i < rd.P.Length - 1; i++)
            {
                float d0 = (rd.P[i] - c).Length(), d1 = (rd.P[i + 1] - c).Length();
                if ((d0 - rad) * (d1 - rad) <= 0) { var q = rd.P[i].Lerp(rd.P[i + 1], Mathf.Clamp((rad - d0) / (d1 - d0 + 1e-4f), 0, 1)); float a = AngleTo(c, q); if (!o.Exists(x => Mathf.Abs(Mathf.AngleDifference(x, a)) < .4f)) o.Add(a); }
            }
        return o;
    }

    static void BuildPlace(Land L, Place p, Node3D parent)
    {
        var g = new Node3D { Name = (p.Kind == "stilts" ? "Stilt_" : "") + p.Id, Position = new Vector3(p.X, p.H, p.Z) }; parent.AddChild(g);
        switch (p.Kind)
        {
            case "city": City(L, p, g); break;
            case "castle": Castle(L, p, g, false); break;
            case "ruins": Castle(L, p, g, true); break;
            case "stilts": Village(p, g, true); break;
            case "cave": Cave(L, p, g); break;
            case "camp": Camp(p, g); break;
            case "tombs": Tombs(p, g); break;
            default: Village(p, g, false); break;
        }
        Label(g, p.Name, new Vector3(0, p.Kind == "city" ? 30 : 12, 0), p.Kind == "city" ? 160 : 80);
    }

    static readonly string[] Cottages = { "village/house_cottage", "village/house_hut", "village/house_town_teal", "village/house_cottage", "village/house_shop", "village/house_inn_lit" };
    static readonly string[] Townhouses = { "village/house_town_blue", "village/house_town_teal", "village/house_shop", "village/house_inn_lit", "village/house_cottage" };

    static void Village(Place p, Node3D g, bool stilts)
    {
        int n = Math.Max(5, (int)(p.R / 3.4f));
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.Tau + R(-.15f, .15f), rad = p.R * R(.6f, .85f);
            var pos = new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad); float yaw = Mathf.Atan2(-pos.X, -pos.Z);
            string model = stilts ? (i % 2 == 0 ? "village/house_hut" : "village/house_cottage") : Cottages[(int)(_rg() * Cottages.Length) % Cottages.Length];
            if (stilts)
            {
                var b = new Basis(Vector3.Up, yaw); pos.Y = 2.6f;
                foreach (var o in new[] { new Vector3(-2.4f, 0, -2.4f), new Vector3(2.4f, 0, -2.4f), new Vector3(-2.4f, 0, 2.4f), new Vector3(2.4f, 0, 2.4f) }) Pillar(g, .22f, 3.6f, pos + b * o - Vector3.Up * 3.4f, _dark);
                Builder.Solid(g, new Vector3(6.4f, .3f, 6.4f), pos - Vector3.Up * .15f, _wood, yaw);
                var dir = -new Vector3(pos.X, 0, pos.Z).Normalized(); var top = pos + dir * 3.4f; var bot = new Vector3(pos.X, 0, pos.Z) + dir * 9f;
                var mid = (top + bot) / 2; var along = bot - top;
                var ramp = new StaticBody3D { Transform = new Transform3D(Basis.LookingAt(along.Normalized(), Vector3.Up), mid), CollisionLayer = 1 };
                ramp.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.6f, .2f, along.Length()) }, MaterialOverride = _wood }); ramp.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.6f, .2f, along.Length()) } });
                g.AddChild(ramp);
            }
            K(g, model, pos, yaw);
        }
        if (stilts) { Builder.Solid(g, new Vector3(9, .3f, 9), new Vector3(0, .9f, 0), _wood); K(g, "village/street_lamp", new Vector3(3.5f, 1.05f, 3.5f), 0, 1.3f, false); }
        else
        {
            K(g, "village/well", Vector3.Zero, 0);
            K(g, "village/market_stall_red", new Vector3(6, 0, 2), -1.2f); K(g, "village/market_stall_blue", new Vector3(-5, 0, -4), 2f);
            for (int i = 0; i < 4; i++) { float a = i * Mathf.Pi / 2 + .6f; K(g, "village/street_lamp", new Vector3(Mathf.Cos(a) * p.R * .4f, 0, Mathf.Sin(a) * p.R * .4f), -a, 1.3f, false); }
            int nf = (int)(Mathf.Tau * (p.R + 3) / 2.8f); for (int i = 0; i < nf; i++) { float a = i / (float)nf * Mathf.Tau; if (i % 9 < 2) continue; var fp = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (p.R + 3); K(g, "village/fence", fp, -(a + Mathf.Pi / 2), 1.3f, false); }
        }
        K(g, "village/signpost", new Vector3(p.R + 5, 0, 0), Mathf.Pi / 2, 1.3f, false);
    }

    static void Gate(Node3D g, float a, float rad, float scale)
    {
        var pos = new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad); float yaw = -(a + Mathf.Pi / 2);
        K(g, "castle/castle_gatehouse", pos, yaw, scale, false);
        var tan = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
        foreach (float s in new[] { -1f, 1f }) Builder.Solid(g, new Vector3(2.4f * scale, 11f * scale, 3f * scale), pos + tan * s * 3.6f * scale + Vector3.Up * 5.5f * scale, _void, yaw).GetChild<MeshInstance3D>(0).Visible = false;
    }

    static void Walls(Node3D g, float rad, List<float> gates, float scale, bool ruined)
    {
        float seg = 4f * scale; int n = Math.Max(12, (int)(Mathf.Tau * rad / seg)); int towerEvery = Math.Max(4, n / 12);
        for (int k = 0; k < n; k++)
        {
            float a = k / (float)n * Mathf.Tau; if (gates.Exists(ga => Mathf.Abs(Mathf.AngleDifference(ga, a)) < (5.5f * scale) / rad)) continue;
            var pos = new Vector3(Mathf.Cos(a) * rad, 0, Mathf.Sin(a) * rad); float yaw = -(a + Mathf.Pi / 2);
            if (ruined && _rg() < .38f) continue;
            if (k % towerEvery == 0) K(g, ruined ? "castle/castle_tower_open" : "castle/castle_tower", pos, yaw, scale * .95f);
            else K(g, ruined && _rg() < .5f ? "castle/castle_wall_low" : "castle/castle_wall", pos, yaw, scale);
        }
        foreach (var ga in gates) if (!ruined) Gate(g, ga, rad, scale);
    }

    static void City(Land L, Place p, Node3D g)
    {
        var gates = RoadAngles(L, p, p.R); if (gates.Count == 0) gates.AddRange(new[] { 0f, Mathf.Pi / 2, Mathf.Pi, -Mathf.Pi / 2 });
        Walls(g, p.R, gates, 1.45f, false);
        // streets: one from every gate to the plaza; houses fill the blocks between them
        float plaza = p.Capital ? 22f : 14f;
        for (float gx = -p.R; gx <= p.R; gx += 13f)
            for (float gz = -p.R; gz <= p.R; gz += 13f)
            {
                float x = gx + R(-1.5f, 1.5f), z = gz + R(-1.5f, 1.5f), d = new Vector2(x, z).Length();
                if (d > p.R - 9 || d < plaza + 6) continue;
                float a = Mathf.Atan2(z, x); if (gates.Exists(ga => Mathf.Abs(Mathf.AngleDifference(ga, a)) * d < 7.5f)) continue;   // keep the gate streets open
                float yaw = Mathf.Atan2(-x, -z);   // fronts face the plaza
                K(g, Townhouses[(int)(_rg() * Townhouses.Length) % Townhouses.Length], new Vector3(x, 0, z), yaw, R(1.3f, 1.55f));
            }
        foreach (var ga in gates)
            for (float d = plaza + 4; d < p.R - 4; d += 12f)
                foreach (float s in new[] { -1f, 1f })
                {
                    var q = new Vector2(Mathf.Cos(ga), Mathf.Sin(ga)) * d + new Vector2(-Mathf.Sin(ga), Mathf.Cos(ga)) * s * 5f;
                    K(g, "village/street_lamp", new Vector3(q.X, 0, q.Y), -ga, 1.4f, false);
                }
        if (p.Capital)
        {
            K(g, "castle/castle_keep", Vector3.Zero, 0, 1.45f);
            for (int i = 0; i < 4; i++) { float a = i * Mathf.Pi / 2 + Mathf.Pi / 4; K(g, "castle/castle_tower_tall", new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 15f, -a, 1.1f); }
            for (int i = 0; i < 6; i++) { float a = i / 6f * Mathf.Tau + .3f; K(g, i % 2 == 0 ? "castle/banner_red" : "castle/banner_blue", new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 19f, -a, 1.4f, false); }
        }
        else
        {
            K(g, "village/well", Vector3.Zero, 0, 1.6f);
            for (int i = 0; i < 6; i++) { float a = i / 6f * Mathf.Tau; K(g, i % 2 == 0 ? "village/market_stall_red" : "village/market_stall_blue", new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 9f, -a + Mathf.Pi / 2, 1.4f); }
        }
    }

    static void Castle(Land L, Place p, Node3D g, bool ruined)
    {
        var ra = RoadAngles(L, p, p.R); float front = ra.Count > 0 ? ra[0] : Mathf.Pi / 2;
        // a square curtain wall, turned so its gate faces the road
        g.Rotation = new Vector3(0, -(front - Mathf.Pi / 2), 0);
        float s = p.R * .62f, sc = 1.35f, seg = 4f * sc; int n = Math.Max(3, (int)(2 * s / seg));
        for (int side = 0; side < 4; side++)
        {
            var b = new Basis(Vector3.Up, side * Mathf.Pi / 2);
            for (int i = 0; i < n; i++)
            {
                float t = -s + seg * (i + .5f);
                if (side == 0 && Mathf.Abs(t) < seg) { if (i == n / 2 && !ruined) { K(g, "castle/castle_gatehouse", b * new Vector3(0, 0, s), side * Mathf.Pi / 2, sc, false); foreach (float k in new[] { -1f, 1f }) Builder.Solid(g, new Vector3(3, 14, 4), b * new Vector3(k * 4.9f, 7, s), _void).GetChild<MeshInstance3D>(0).Visible = false; } continue; }
                if (ruined && _rg() < .4f) continue;
                K(g, ruined && _rg() < .5f ? "castle/castle_wall_low" : "castle/castle_wall", b * new Vector3(t, 0, s), side * Mathf.Pi / 2, sc);
            }
            K(g, ruined ? "castle/castle_tower_open" : "castle/castle_tower", b * new Vector3(s, 0, s), 0, sc);
        }
        if (!ruined)
        {
            K(g, "castle/castle_keep", new Vector3(0, 0, -s * .25f), 0, 1.2f);
            K(g, "castle/banner_red", new Vector3(-4, 0, s - 2), 0, 1.3f, false); K(g, "castle/banner_red", new Vector3(4, 0, s - 2), 0, 1.3f, false);
        }
        else
        {
            for (int i = 0; i < 10; i++) Pillar(g, R(.5f, .8f), R(1.5f, 6f), new Vector3(R(-s + 3, s - 3), 0, R(-s + 3, s - 3)), _stone);
            K(g, "castle/castle_tower_open", new Vector3(0, -1.5f, -2), .4f, 1.6f);
        }
    }

    static void Cave(Land L, Place p, Node3D g)
    {
        // the mouth faces downhill (away from the higher ground)
        float best = p.Yaw, low = 1e9f;
        for (int i = 0; i < 12; i++) { float a = i / 12f * Mathf.Tau; float h = GroundAt(p.X + Mathf.Cos(a) * 30, p.Z + Mathf.Sin(a) * 30); if (h < low) { low = h; best = a; } }
        g.Rotation = new Vector3(0, Mathf.Atan2(Mathf.Cos(best), Mathf.Sin(best)), 0);
        void Rock(string m, Vector3 at, float yaw, float sc) => Kit.Add(g, "nature/" + m, at, yaw, sc, true);
        Rock("Rock_Medium_1", new Vector3(-4.4f, -.6f, 0), .3f, 3.4f); Rock("Rock_Medium_2", new Vector3(4.4f, -.6f, .4f), -.2f, 3.3f);
        Rock("Rock_Medium_3", new Vector3(0, 3.4f, -.8f), 1.4f, 3.8f); Rock("Rock_Medium_1", new Vector3(-2.5f, .6f, -5.5f), 2f, 4.4f); Rock("Rock_Medium_2", new Vector3(2.8f, .8f, -6f), .6f, 4.2f);
        g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(5.5f, 4.5f, 6) }, MaterialOverride = _void, Position = new Vector3(0, 2, -3.5f) });
        K(g, "dungeon/brazier", new Vector3(-3, 0, 3), 0, 1.2f); K(g, "dungeon/brazier", new Vector3(3, 0, 3), 0, 1.2f);
        for (int i = 0; i < 6; i++) Kit.Add(g, "nature/Rock_Medium_" + (1 + i % 3), new Vector3(R(-6, 6), -.2f, R(2.5f, 7)), R(0, 6), R(.5f, 1f), false);
    }

    static void Camp(Place p, Node3D g)
    {
        for (int i = 0; i < 5; i++)
        {
            float a = i / 5f * Mathf.Tau, rad = p.R * .55f; var pos = new Vector3(Mathf.Cos(a) * rad, 1.2f, Mathf.Sin(a) * rad);
            var tent = new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(3.4f, 2.4f, 4.2f) }, MaterialOverride = _cloth, Position = pos, Rotation = new Vector3(0, -a, 0) }; g.AddChild(tent);
        }
        Pillar(g, .9f, .3f, Vector3.Zero, _stone); World.Lamp(g, new Vector3(0, 1.2f, 0), new Color(1f, .6f, .25f), 14f, 1.6f, -1, false);
        for (int i = 0; i < 6; i++) K(g, i % 2 == 0 ? "village/crate" : "village/barrel", new Vector3(R(-6, 6), 0, R(-6, 6)), R(0, 6), 1.3f, false);
    }

    static void Tombs(Place p, Node3D g)
    {
        // a stepped pyramid with a dark door, and rows of broken columns leading to it
        for (int i = 0; i < 4; i++) { float w = 22 - i * 5; Builder.Solid(g, new Vector3(w, 3.2f, w), new Vector3(0, 1.6f + i * 3.2f, -6), _sandstone); }
        g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.2f, 4, .4f) }, MaterialOverride = _void, Position = new Vector3(0, 2, 5.05f) });
        for (int i = 0; i < 6; i++) foreach (float s in new[] { -1f, 1f }) Pillar(g, .8f, _rg() < .4f ? R(1.5f, 3.5f) : 7f, new Vector3(s * 6f, 0, 10 + i * 5f), _sandstone);
        K(g, "dungeon/rune_obelisk", new Vector3(0, 0, 12), 0, 1.3f);
    }

    // ---------------------------------------------------------------- bridges
    static void RiverBridges(Land L, Node3D parent)
    {
        var done = new List<Vector2>();
        foreach (var rd in L.Roads) foreach (var rv in L.Rivers)
            for (int i = 0; i < rd.P.Length - 1; i++) for (int j = 0; j < rv.P.Length - 1; j++)
            {
                if (!Cross(rd.P[i], rd.P[i + 1], rv.P[j], rv.P[j + 1], out var q) || done.Exists(d => d.DistanceTo(q) < 25f)) continue;
                done.Add(q);
                var dir = (rd.P[i + 1] - rd.P[i]).Normalized(); float span = rv.W * 3.6f + 6f;
                var a = q - dir * span / 2; var b = q + dir * span / 2; float h = Mathf.Max(GroundAt(a.X, a.Y), GroundAt(b.X, b.Y)) + .25f;
                var g = new Node3D { Name = "RiverBridge", Position = new Vector3(q.X, h, q.Y), Rotation = new Vector3(0, Mathf.Atan2(dir.X, dir.Y), 0) }; parent.AddChild(g);
                Builder.Solid(g, new Vector3(4.6f, .5f, span), new Vector3(0, -.25f, 0), _wood);
                foreach (float s in new[] { -1f, 1f }) { Builder.Solid(g, new Vector3(.25f, 1f, span), new Vector3(s * 2.25f, .5f, 0), _dark); for (float z = -span / 2 + 2; z < span / 2; z += 5f) Pillar(g, .3f, h + 3f, new Vector3(s * 2f, -h - 2.5f, z), _dark); }
            }
    }
    static bool Cross(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 q)
    {
        q = default; var r = b - a; var s = d - c; float den = r.X * s.Y - r.Y * s.X; if (Mathf.Abs(den) < 1e-5f) return false;
        float t = ((c - a).X * s.Y - (c - a).Y * s.X) / den, u = ((c - a).X * r.Y - (c - a).Y * r.X) / den;
        if (t < 0 || t > 1 || u < 0 || u > 1) return false; q = a + r * t; return true;
    }

    /// <summary>A long arched bridge from coast to coast along the line between two docks, on stone piers, with lamps.</summary>
    static void SeaBridge(Bridge br, Node3D parent)
    {
        var dir = (br.B - br.A).Normalized(); float full = (br.B - br.A).Length();
        Vector2 sA = br.A, sB = br.B;
        for (float t = 0; t < full; t += 2) { var q = br.A + dir * t; if (World.GroundAt(q.X, q.Y) < .9f) { sA = q - dir * 8; break; } }
        for (float t = 0; t < full; t += 2) { var q = br.B - dir * t; if (World.GroundAt(q.X, q.Y) < .9f) { sB = q + dir * 8; break; } }
        float hA = World.GroundAt(sA.X, sA.Y), hB = World.GroundAt(sB.X, sB.Y), len = (sB - sA).Length(), arch = Mathf.Clamp(len * .035f, 5f, 16f);
        Vector3 P(float t) { var q = sA + (sB - sA) * t; return new Vector3(q.X, Mathf.Lerp(hA, hB, t) + Mathf.Sin(t * Mathf.Pi) * arch, q.Y); }
        var g = new Node3D { Name = "Bridge_" + br.Name }; parent.AddChild(g);
        int segs = Mathf.CeilToInt(len / 5f); var side = new Vector3(-dir.Y, 0, dir.X);
        for (int i = 0; i < segs; i++)
        {
            Vector3 p0 = P(i / (float)segs), p1 = P((i + 1) / (float)segs), fwd = p1 - p0, mid = (p0 + p1) / 2; float l = fwd.Length() + .2f;
            var basis = Basis.LookingAt(fwd.Normalized(), Vector3.Up);
            var body = new StaticBody3D { Transform = new Transform3D(basis, mid), CollisionLayer = 1 }; g.AddChild(body);
            void Part(Vector3 size, Vector3 at, Material m) { body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = at }); body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = at }); }
            Part(new Vector3(6f, .8f, l), new Vector3(0, -.4f, 0), _stone);
            Part(new Vector3(.5f, 1.2f, l), new Vector3(-2.8f, .6f, 0), _stone); Part(new Vector3(.5f, 1.2f, l), new Vector3(2.8f, .6f, 0), _stone);
            float ground = World.GroundAt(mid.X, mid.Z);
            if (i % 6 == 3 && ground < mid.Y - 3) { float bot = Mathf.Max(ground, -13f) - 1; Builder.Solid(g, new Vector3(4.2f, mid.Y - bot, 4.2f), new Vector3(mid.X, (mid.Y + bot) / 2 - .6f, mid.Z), _stone, Mathf.Atan2(dir.X, dir.Y)); }
            if (i % 8 == 1) { float s = (i / 8) % 2 == 0 ? 1 : -1; K(g, "village/street_lamp", mid + side * s * 2.6f + Vector3.Up * .0f, Mathf.Atan2(dir.X, dir.Y) + (s > 0 ? -Mathf.Pi / 2 : Mathf.Pi / 2), 1.4f, false); }
        }
        var c = P(.5f); Label(g, br.Name, c + Vector3.Up * 6, 120f);
        var a0 = P(0); Label(g, br.Name, a0 + Vector3.Up * 5, 50f); var b0 = P(1); Label(g, br.Name, b0 + Vector3.Up * 5, 50f);
    }

    // ---------------------------------------------------------------- sky islands
    static void SkyIsland(SkyIsle s, Node3D parent)
    {
        const int Kt = 30, Ku = 22, M = 96; int cols = M + 1;
        var verts = new List<Vector3>(); var colsL = new List<Color>(); var uv2 = new List<Vector2>();
        float depth = s.R * .95f;
        // top: rings from the centre to the edge
        for (int k = 0; k <= Kt; k++) for (int m = 0; m <= M; m++)
        {
            float a = m / (float)M * Mathf.Tau, e = EdgeR(s, a), u = k / (float)Kt * .999f;
            float x = s.X + Mathf.Cos(a) * e * u, z = s.Z + Mathf.Sin(a) * e * u; float y = SkyTop(s, x, z); if (float.IsNaN(y)) y = s.Y - 2;
            verts.Add(new Vector3(x, y, z)); colsL.Add(new Color(0, 0, Biomes.TryGetValue(s.Biome, out var bb) ? bb.Forest * .6f : 0, 0)); uv2.Add(Vector2.Zero);
        }
        // underside: from the edge down to a point, rough and narrowing
        for (int k = 1; k <= Ku; k++) for (int m = 0; m <= M; m++)
        {
            float a = m / (float)M * Mathf.Tau, e = EdgeR(s, a), v = k / (float)Ku;
            float n = World.Fbm(Mathf.Cos(a) * 2.2f + s.Seed + v * 3f, Mathf.Sin(a) * 2.2f - v * 2f, 3);
            float rr = e * Mathf.Pow(1 - v, .7f) * (.86f + .28f * n) * (k == Ku ? 0 : 1);
            float y = s.Y - 2.6f - depth * Mathf.Pow(v, 1.15f) * (.85f + .3f * n);
            verts.Add(new Vector3(s.X + Mathf.Cos(a) * rr, y, s.Z + Mathf.Sin(a) * rr)); colsL.Add(new Color(0, 0, 0, 0)); uv2.Add(new Vector2(1, 0));
        }
        int rings = Kt + 1 + Ku; var idx = new List<int>();
        for (int k = 0; k < rings - 1; k++) for (int m = 0; m < M; m++)
        {
            int a = k * cols + m, b = a + 1, c = a + cols, d = c + 1;
            idx.Add(a); idx.Add(c); idx.Add(b); idx.Add(b); idx.Add(c); idx.Add(d);
        }
        var norms = new Vector3[verts.Count];
        for (int i = 0; i < idx.Count; i += 3) { var p0 = verts[idx[i]]; var n = (verts[idx[i + 2]] - p0).Cross(verts[idx[i + 1]] - p0); norms[idx[i]] += n; norms[idx[i + 1]] += n; norms[idx[i + 2]] += n; }
        for (int i = 0; i < norms.Length; i++) norms[i] = norms[i].LengthSquared() > 1e-12f ? norms[i].Normalized() : Vector3.Up;
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray(); arrays[(int)Mesh.ArrayType.Normal] = norms; arrays[(int)Mesh.ArrayType.Color] = colsL.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2.ToArray(); arrays[(int)Mesh.ArrayType.Index] = idx.ToArray();
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var g = new Node3D { Name = "Sky_" + s.Id }; parent.AddChild(g);
        var body = new StaticBody3D { CollisionLayer = 1 }; g.AddChild(body);
        body.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = TerrainMat(null) });
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() });

        // trees, rocks and grass on top
        Biomes.TryGetValue(s.Biome, out var bio); var batches = new Dictionary<string, Batch>();
        var tc = new StaticBody3D { CollisionLayer = 1 }; g.AddChild(tc);
        Vector2 Spot(float maxU) { float a = R(0, Mathf.Tau), u = Mathf.Sqrt(_rg()) * maxU; float e = EdgeR(s, a); return new Vector2(s.X + Mathf.Cos(a) * e * u, s.Z + Mathf.Sin(a) * e * u); }
        if (bio != null && TreeSets.TryGetValue(bio.Trees, out var set) && set.Count > 0)
        {
            int nTrees = (int)(s.R * s.R * .012f * (.4f + bio.TreeD));
            for (int i = 0; i < nTrees; i++)
            {
                var q = Spot(.82f); if (s.Temple && new Vector2(q.X - s.X, q.Y - s.Z).Length() < 16) continue;
                float y = SkyTop(s, q.X, q.Y); if (float.IsNaN(y)) continue;
                var km = set[(int)(_rg() * set.Count) % set.Count]; float w = R(.8f, 1.3f), hh = R(.8f, 1.4f);
                var key = km.Name + "|" + km.Scale; if (!batches.TryGetValue(key, out var bt)) batches[key] = bt = new Batch { M = km };
                bt.Xf.Add(new Transform3D(new Basis(Vector3.Up, R(0, Mathf.Tau)) * Basis.FromScale(new Vector3(w, hh, w)), new Vector3(q.X, y - .2f, q.Y))); bt.Col.Add(Tint(i));
                tc.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .4f * w * km.Scale, Height = 4f }, Position = new Vector3(q.X, y + 2, q.Y) });
            }
        }
        foreach (var bt in batches.Values)
        {
            var p = Kit.Get(bt.M.Name); if (p == null) continue;
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = p.Mesh, InstanceCount = bt.Xf.Count };
            var local = new Transform3D(Basis.Identity.Scaled(Vector3.One * bt.M.Scale), Vector3.Zero) * p.Xf;
            for (int i = 0; i < bt.Xf.Count; i++) { mm.SetInstanceTransform(i, bt.Xf[i] * local); mm.SetInstanceColor(i, bt.Col[i]); }
            g.AddChild(new MultiMeshInstance3D { Multimesh = mm });
        }
        if (Flora.GrassMesh != null && Flora.GrassMat != null && bio != null && bio.Grass > .05f)
        {
            int nG = (int)(Mathf.Pi * s.R * s.R * .75f * bio.Grass); var xs = new List<Transform3D>();
            for (int i = 0; i < nG; i++) { var q = Spot(.9f); float y = SkyTop(s, q.X, q.Y); if (float.IsNaN(y)) continue; xs.Add(new Transform3D(new Basis(Vector3.Up, R(0, Mathf.Tau)) * Basis.FromScale(new Vector3(1, R(.85f, 1.15f), 1)), new Vector3(q.X, y - .03f, q.Y))); }
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = Flora.GrassMesh, InstanceCount = xs.Count };
            var gc = new[] { new Color("6c9a28"), new Color("7aaa2c"), new Color("88b232"), new Color("62922a") };
            for (int i = 0; i < xs.Count; i++) { mm.SetInstanceTransform(i, xs[i]); var c = gc[i % gc.Length] * bio.GrassTint; c.A = 1; mm.SetInstanceColor(i, c); }
            g.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = Flora.GrassMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        if (s.Temple)
        {
            float cy = SkyTop(s, s.X, s.Z); var t = new Node3D { Position = new Vector3(s.X, cy, s.Z) }; g.AddChild(t);
            Builder.Solid(t, new Vector3(18, 1, 18), new Vector3(0, -.3f, 0), _white);
            for (int i = 0; i < 10; i++) { float a = i / 10f * Mathf.Tau; Pillar(t, .65f, i % 3 == 2 ? 4f : 7.5f, new Vector3(Mathf.Cos(a) * 8, .2f, Mathf.Sin(a) * 8), _white); }
            K(t, "dungeon/portal_ring", new Vector3(0, .2f, 0), 0, 1.5f, false); K(t, "dungeon/altar", new Vector3(0, .2f, -4), 0, 1.3f);
        }
        Label(g, s.Name, new Vector3(s.X, (float.IsNaN(SkyTop(s, s.X, s.Z)) ? s.Y : SkyTop(s, s.X, s.Z)) + 14, s.Z), 200f);
        Updraft(s, g);
    }

    /// <summary>The way up: a column of rising air beside the island, from the ground below up to its rim.</summary>
    static void Updraft(SkyIsle s, Node3D g)
    {
        if (s.Link != null) return;   // reached over the rope bridge
        // the base goes where the ground under the rim is dry and not too steep or high (highest such spot: the shortest ride)
        float bestA = 0, bestH = -1e9f;
        for (int i = 0; i < 24; i++) { float a = i / 24f * Mathf.Tau, e = EdgeR(s, a) + 3f; float x = s.X + Mathf.Cos(a) * e, z = s.Z + Mathf.Sin(a) * e, h = World.GroundAt(x, z); float sl = Mathf.Abs(World.GroundAt(x + 2, z) - World.GroundAt(x - 2, z)) + Mathf.Abs(World.GroundAt(x, z + 2) - World.GroundAt(x, z - 2)); if (h > 2f && h < 45f && sl < 2.5f && h > bestH) { bestH = h; bestA = a; } }
        float ed = EdgeR(s, bestA) + 2.6f; var basePos = new Vector3(s.X + Mathf.Cos(bestA) * ed, World.GroundAt(s.X + Mathf.Cos(bestA) * ed, s.Z + Mathf.Sin(bestA) * ed), s.Z + Mathf.Sin(bestA) * ed);
        float rim = SkyTop(s, s.X + Mathf.Cos(bestA) * (ed - 5), s.Z + Mathf.Sin(bestA) * (ed - 5)); if (float.IsNaN(rim)) rim = s.Y;
        var up = new Updraft { Name = "Updraft_" + s.Id, Position = basePos, Top = rim + 3.5f, Radius = 3.8f, Target = s.Name }; g.AddChild(up);
        var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(.55f, .8f, 1f, .07f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        float hgt = up.Top - basePos.Y + 6;
        up.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 3.8f, BottomRadius = 3.8f, Height = hgt, RadialSegments = 24, CapTop = false, CapBottom = false }, MaterialOverride = mat, Position = Vector3.Up * hgt / 2, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var pm = new ParticleProcessMaterial { EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Ring, EmissionRingRadius = 3f, EmissionRingInnerRadius = .5f, EmissionRingHeight = .5f, EmissionRingAxis = Vector3.Up, Direction = Vector3.Up, Spread = 4, InitialVelocityMin = 14, InitialVelocityMax = 18, Gravity = Vector3.Zero, ScaleMin = .4f, ScaleMax = 1f };
        var quad = new QuadMesh { Size = new Vector2(.25f, 1.6f), Material = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Add, AlbedoColor = new Color(.7f, .9f, 1f, .5f), BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY } };
        up.AddChild(new GpuParticles3D { Amount = 160, Lifetime = hgt / 16f, ProcessMaterial = pm, DrawPass1 = quad, VisibilityAabb = new Aabb(new Vector3(-6, 0, -6), new Vector3(12, hgt + 6, 12)) });
        for (int i = 0; i < 3; i++) { float a = i / 3f * Mathf.Tau; K(up, "dungeon/rune_obelisk", new Vector3(Mathf.Cos(a) * 6.5f, 0, Mathf.Sin(a) * 6.5f), -a, 1.2f); }
        Label(up, "Updraft to " + s.Name, new Vector3(0, 5, 0), 60f);
    }

    static void RopeBridge(SkyIsle a, SkyIsle b, Node3D parent)
    {
        var d2 = new Vector2(b.X - a.X, b.Z - a.Z); var dir = d2.Normalized(); float ang = Mathf.Atan2(dir.Y, dir.X);
        float ea = EdgeR(a, ang) * .9f, eb = EdgeR(b, ang + Mathf.Pi) * .9f;
        var pa = new Vector2(a.X, a.Z) + dir * ea; var pb = new Vector2(b.X, b.Z) - dir * eb;
        float ha = SkyTop(a, pa.X, pa.Y), hb = SkyTop(b, pb.X, pb.Y); if (float.IsNaN(ha)) ha = a.Y; if (float.IsNaN(hb)) hb = b.Y;
        float len = (pb - pa).Length(), sag = Mathf.Min(6f, len * .06f); int segs = Mathf.CeilToInt(len / 2.2f);
        Vector3 P(float t) { var q = pa.Lerp(pb, t); return new Vector3(q.X, Mathf.Lerp(ha, hb, t) - Mathf.Sin(t * Mathf.Pi) * sag + .1f, q.Y); }
        var g = new Node3D { Name = "RopeBridge" }; parent.AddChild(g);
        for (int i = 0; i < segs; i++)
        {
            Vector3 p0 = P(i / (float)segs), p1 = P((i + 1) / (float)segs), f = p1 - p0, mid = (p0 + p1) / 2;
            var body = new StaticBody3D { Transform = new Transform3D(Basis.LookingAt(f.Normalized(), Vector3.Up), mid), CollisionLayer = 1 }; g.AddChild(body);
            void Part(Vector3 size, Vector3 at, Material m) { body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = m, Position = at }); body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Position = at }); }
            Part(new Vector3(2.6f, .18f, f.Length() * .92f), new Vector3(0, -.09f, 0), _wood);
            Part(new Vector3(.08f, .08f, f.Length() + .05f), new Vector3(-1.35f, 1f, 0), _rope); Part(new Vector3(.08f, .08f, f.Length() + .05f), new Vector3(1.35f, 1f, 0), _rope);
            if (i % 3 == 0) { body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.1f, 1f, .1f) }, MaterialOverride = _rope, Position = new Vector3(-1.35f, .5f, 0) }); body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.1f, 1f, .1f) }, MaterialOverride = _rope, Position = new Vector3(1.35f, .5f, 0) }); }
        }
    }

    // ---------------------------------------------------------------- map and points of interest
    static void RegisterPois()
    {
        foreach (var L in All)
        {
            foreach (var p in L.Places)
                World.Pois.Add(new Poi { Id = L.Id + "_" + p.Id, Name = p.Name, Icon = p.Kind, X = p.X, Z = p.Z, R = p.R + 14, Lvl = Game.LevelAt(p.X, p.Z, 0) });
            foreach (var m in L.Mts) if (m.Name != "") World.Pois.Add(new Poi { Id = L.Id + "_mt_" + m.Name.GetHashCode(), Name = m.Name, Icon = "peak", X = m.X, Z = m.Z, R = m.R * .35f, Lvl = Game.LevelAt(m.X, m.Z, 0) });
            foreach (var lk in L.Lakes) if (lk.Name != "") World.Pois.Add(new Poi { Id = L.Id + "_lake_" + lk.Name.GetHashCode(), Name = lk.Name, Icon = "lake", X = lk.X, Z = lk.Z, R = lk.R + 12, Lvl = 1 });
        }
        foreach (var s in Sky)
        {
            float y = SkyTop(s, s.X, s.Z); if (float.IsNaN(y)) y = s.Y;
            World.Pois.Add(new Poi { Id = "sky_" + s.Id, Name = s.Name, Icon = "sky", X = s.X, Z = s.Z, R = s.R * .8f, Lvl = Game.LevelAt(s.X, s.Z, 0), Sky = true, Y = y });
        }
    }

    /// <summary>The world map image: -Extent..Extent on both axes, S pixels square. Colours by height and surface, sky islands drawn pale.</summary>
    public static Image MapImage(int S)
    {
        var data = new byte[S * S * 3]; float E = Extent;
        Parallel.For(0, S, py =>
        {
            for (int px = 0; px < S; px++)
            {
                float x = -E + (px + .5f) * 2 * E / S, z = -E + (py + .5f) * 2 * E / S, h = World.GroundAt(x, z); Color c;
                if (h < .3f) c = new Color(.1f, .24f, .42f).Lerp(new Color(.2f, .42f, .58f), Mathf.Clamp((h + 9) / 9.3f, 0, 1));
                else
                {
                    var L = LandAt(x, z);
                    if (L == null) c = h < 2f ? new Color(.75f, .68f, .5f) : h < 18f ? new Color(.3f, .5f, .22f).Lerp(new Color(.4f, .46f, .24f), h / 18f) : h < 45f ? new Color(.5f, .48f, .45f) : new Color(.92f, .94f, .97f);
                    else
                    {
                        var sf = Surface(L, x, z, h, 1f);
                        c = new Color(.32f, .52f, .24f).Lerp(new Color(.22f, .36f, .18f), sf.forest).Lerp(new Color(.82f, .72f, .5f), sf.sand).Lerp(new Color(.52f, .5f, .47f), Mathf.Max(sf.rock, Sm(30, 60, h) * .7f)).Lerp(new Color(.3f, .27f, .26f), sf.ash).Lerp(new Color(.93f, .95f, .98f), sf.snow).Lerp(new Color(.72f, .6f, .42f), sf.road * .9f);
                        foreach (var lk in L.Lakes) if (new Vector2(x - lk.X, z - lk.Z).Length() < lk.R) c = new Color(.2f, .42f, .58f);
                        foreach (var rv in L.Rivers) if (rv.Box.HasPoint(new Vector2(x, z)) && Near(rv, x, z).d < rv.W * .9f) c = new Color(.2f, .42f, .58f);
                        foreach (var p in L.Places) if (new Vector2(x - p.X, z - p.Z).Length() < p.R * .8f && (p.Kind == "city" || p.Kind == "castle")) c = c.Lerp(new Color(.62f, .58f, .54f), .7f);
                    }
                    c = c * (.85f + .15f * Mathf.Clamp(h / 60f, 0, 1));
                }
                foreach (var s in Sky) { float d = new Vector2(x - s.X, z - s.Z).Length(); if (d < s.R) c = c.Lerp(new Color(.95f, .97f, 1f), d > s.R - 6 ? .85f : .45f); }
                foreach (var b in Bridges) if (DistSeg(new Vector2(x, z), b.A, b.B) < 3.2f && h < 1f) c = new Color(.66f, .62f, .56f);
                int o = (py * S + px) * 3; data[o] = (byte)(Mathf.Clamp(c.R, 0, 1) * 255); data[o + 1] = (byte)(Mathf.Clamp(c.G, 0, 1) * 255); data[o + 2] = (byte)(Mathf.Clamp(c.B, 0, 1) * 255);
            }
        });
        return Image.CreateFromData(S, S, false, Image.Format.Rgb8, data);
    }
}

/// <summary>A column of rising air: while the player stands in it they are carried up to Top and held there, to step off onto the sky island.</summary>
public partial class Updraft : Node3D
{
    public float Top, Radius = 3.8f; public string Target = "";
    public override void _PhysicsProcess(double delta)
    {
        var p = Player.Instance; if (p == null || p.Dead) return;
        var pp = p.GlobalPosition; var c = GlobalPosition;
        if (new Vector2(pp.X - c.X, pp.Z - c.Z).Length() < Radius && pp.Y > c.Y - 2f && pp.Y < Top + 8f) p.Lift(Top);
    }
}
