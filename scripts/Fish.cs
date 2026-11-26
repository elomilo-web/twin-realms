using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Fish species, fishing junk and treasure from data/fishing.json, the water questions both fish and fishing ask (is this point water,
/// what kind of water, which biome), and the fishing loot roll (F-23). The swimming fish themselves are FishField below (F-22).
/// </summary>
public static class FishDb
{
    public sealed class FishDef
    {
        public string Id = "", Name = "", Rarity = "common", Time = "any", Weather = "any"; public Color Col = Colors.Gray, Belly = Colors.White;
        public string[] Biomes = Array.Empty<string>(), Water = Array.Empty<string>(); public float D0 = .3f, D1 = 2f, Sz0 = 10, Sz1 = 20, Diff = .3f;
        public int S0 = 1, S1 = 1, Value = 1, Xp = 5, MinLevel = 1;
    }
    public sealed class JunkDef { public string Id = "", Name = ""; public Color Col = Colors.Gray; public int Value = 1; public float W = 1; public string[] Water = Array.Empty<string>(); }
    public sealed class TreasureDef { public string Kind = "gold", Mat = ""; public int G0, G1, N0 = 1, N1 = 1, R0 = 1, R1 = 2; public float W = 1; public string[] Water = Array.Empty<string>(); }
    public readonly record struct Ctx(string Biome, string Water, string Time, string Weather, int Level);
    public sealed class Pick { public string Kind = "fish"; public FishDef? Fish; public JunkDef? Junk; public TreasureDef? Treasure; public float Size; public float Diff = .2f; public string Gem = ""; }
    sealed class Cat { public float W, PerLevel, Min = .1f; }
    sealed class Rar { public float W = 1, PerLevel; public Color Col = Colors.White; }

    public static readonly Dictionary<string, FishDef> Fish = new();
    public static readonly Dictionary<string, JunkDef> Junk = new();
    public static readonly List<TreasureDef> Treasure = new();
    static readonly Dictionary<string, Cat> Cats = new();
    static readonly Dictionary<string, Rar> Rars = new();
    static readonly Dictionary<string, Dictionary<string, Dictionary<string, float>>> Mods = new();   // "water"/"weather"/"time" -> tag -> category -> mul
    public static JsonElement Field, Rod, Bite, Reel; static JsonDocument? _doc; static bool _loaded;
    public const string Skill = "fishing";

    static string[] Strs(JsonElement e, string k) => e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : Array.Empty<string>();
    static (float, float) Pair(JsonElement e, string k, float a, float b) => e.TryGetProperty(k, out var x) && x.ValueKind == JsonValueKind.Array && x.GetArrayLength() >= 2 ? (x[0].GetSingle(), x[1].GetSingle()) : (a, b);
    public static (float, float) Pair(JsonElement e, string k, (float, float) d) => Pair(e, k, d.Item1, d.Item2);

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        _doc = JsonUtil.Load("res://data/fishing.json"); var r = _doc.RootElement;
        Field = r.GetProperty("field"); Rod = r.GetProperty("rod"); Bite = r.GetProperty("bite"); Reel = r.GetProperty("reel");
        if (r.TryGetProperty("skill", out var sk)) LifeSkills.Define(Skill, new LifeSkills.Curve { Base = JsonUtil.F(sk, "base", 40), Growth = JsonUtil.F(sk, "growth", 1.35f), Max = (int)JsonUtil.F(sk, "max", 50) });
        foreach (var p in r.GetProperty("categories").EnumerateObject()) Cats[p.Name] = new Cat { W = JsonUtil.F(p.Value, "w", 1), PerLevel = JsonUtil.F(p.Value, "perLevel"), Min = JsonUtil.F(p.Value, "min", .1f) };
        foreach (var p in r.GetProperty("rarities").EnumerateObject()) Rars[p.Name] = new Rar { W = JsonUtil.F(p.Value, "w", 1), PerLevel = JsonUtil.F(p.Value, "perLevel"), Col = Color.FromHtml(JsonUtil.S(p.Value, "color", "#ffffff")) };
        if (r.TryGetProperty("modifiers", out var md))
            foreach (var g in md.EnumerateObject())
            {
                var gd = Mods[g.Name] = new();
                foreach (var t in g.Value.EnumerateObject()) { var td = gd[t.Name] = new(); foreach (var c in t.Value.EnumerateObject()) td[c.Name] = c.Value.GetSingle(); }
            }
        foreach (var p in r.GetProperty("fish").EnumerateObject())
        {
            var e = p.Value; var f = new FishDef
            {
                Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Col = Color.FromHtml(JsonUtil.S(e, "color", "#808080")), Belly = Color.FromHtml(JsonUtil.S(e, "belly", "#ffffff")),
                Biomes = Strs(e, "biomes"), Water = Strs(e, "water"), Rarity = JsonUtil.S(e, "rarity", "common"), Value = (int)JsonUtil.F(e, "value", 1), Xp = (int)JsonUtil.F(e, "xp", 5),
                Diff = JsonUtil.F(e, "diff", .3f), Time = JsonUtil.S(e, "time", "any"), Weather = JsonUtil.S(e, "weather", "any"), MinLevel = (int)JsonUtil.F(e, "minLevel", 1),
            };
            (f.D0, f.D1) = Pair(e, "depth", .3f, 2f); (f.Sz0, f.Sz1) = Pair(e, "size", 10, 20); var (s0, s1) = Pair(e, "school", 1, 1); f.S0 = (int)s0; f.S1 = Math.Max(f.S0, (int)s1);
            Fish[f.Id] = f;
        }
        foreach (var p in r.GetProperty("junk").EnumerateObject())
            Junk[p.Name] = new JunkDef { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Col = Color.FromHtml(JsonUtil.S(p.Value, "color", "#808080")), Value = (int)JsonUtil.F(p.Value, "value", 1), W = JsonUtil.F(p.Value, "w", 1), Water = Strs(p.Value, "water") };
        foreach (var e in r.GetProperty("treasure").EnumerateArray())
        {
            var t = new TreasureDef { Kind = JsonUtil.S(e, "kind", "gold"), Mat = JsonUtil.S(e, "mat"), W = JsonUtil.F(e, "w", 1), Water = Strs(e, "water") };
            var (g0, g1) = Pair(e, "gold", 0, 0); t.G0 = (int)g0; t.G1 = (int)g1; var (n0, n1) = Pair(e, "n", 1, 1); t.N0 = (int)n0; t.N1 = (int)n1; var (r0, r1) = Pair(e, "rar", 1, 2); t.R0 = (int)r0; t.R1 = (int)r1;
            Treasure.Add(t);
        }
    }

    /// <summary>Fish and junk as pack materials (GatherDb.Load calls this), so the pack, the shop and cooking (F-24) know them.</summary>
    public static void AddMats(Dictionary<string, MatDef> mats)
    {
        Load();
        foreach (var f in Fish.Values) mats.TryAdd("fish_" + f.Id, new MatDef { Id = "fish_" + f.Id, Name = f.Name, Value = f.Value, Col = f.Col.Lerp(f.Belly, .35f) });
        foreach (var j in Junk.Values) mats.TryAdd("junk_" + j.Id, new MatDef { Id = "junk_" + j.Id, Name = j.Name, Value = j.Value, Col = j.Col });
    }
    public static JsonElement RodData() { Load(); return Rod; }
    public static FishDef? FishByName(string name) { Load(); foreach (var f in Fish.Values) if (f.Name == name) return f; return null; }
    public static Color RarityColor(string r) { Load(); return Rars.TryGetValue(r, out var x) ? x.Col : Colors.White; }

    // ---------------------------------------------------------------- the water and the world around it
    /// <summary>True when a point is inside water: water blocks where the block world is loaded, else below the lake/river/sea surface and above the ground.</summary>
    public static bool InWater(Vector3 p)
    {
        if (BlockWorld.Drawn(p.X, p.Z))
        {
            byte b = BlockWorld.At(p);
            if (b != 255) return b != 0 && Blocks.Def(b).Fluid == "water";
        }
        return p.Y < Lands.WaterAt(p.X, p.Z) - .05f && p.Y > World.GroundAt(p.X, p.Z) + .15f;
    }
    /// <summary>The water surface over (x, z) near height y: the land water level, or the top of a column of water blocks (placed ponds).</summary>
    public static float Surface(float x, float z, float y)
    {
        float w = Lands.WaterAt(x, z);
        if (BlockWorld.Drawn(x, z) && BlockWorld.FluidAt(new Vector3(x, y, z)) == "water")
        {   // climb the water blocks to their top
            float yy = Mathf.Floor(y) + .5f; for (int i = 0; i < 24 && BlockWorld.FluidAt(new Vector3(x, yy + 1, z)) == "water"; i++) yy += 1;
            return Mathf.Floor(yy) + .9f;
        }
        return w;
    }
    /// <summary>The bottom under (x, z) below the surface s: the first non-water point going down (blocks) or the ground height.</summary>
    public static float Bottom(float x, float z, float s)
    {
        if (BlockWorld.Drawn(x, z) && BlockWorld.At(new Vector3(x, s - .3f, z)) != 255)
        {
            float y = s - .3f; for (int i = 0; i < 40 && InWater(new Vector3(x, y - .5f, z)); i++) y -= .5f;
            return y;
        }
        return World.GroundAt(x, z);
    }
    public static string Biome(float x, float z)
    {
        var L = Lands.LandAt(x, z); if (L == null) return "meadow";
        foreach (var (b, rx, rz, rr) in L.Regions) if (new Vector2(x - rx, z - rz).Length() < rr) return b;
        return L.Default;
    }
    /// <summary>lake, river, sea or pond (placed block water above the land water).</summary>
    public static string WaterKind(Vector3 p)
    {
        float w = Lands.WaterAt(p.X, p.Z);
        if (BlockWorld.FluidAt(p) == "water" && p.Y > w + .6f) return "pond";
        var L = Lands.LandAt(p.X, p.Z);
        if (L != null) { foreach (var lk in L.Lakes) if (new Vector2(p.X - lk.X, p.Z - lk.Z).Length() < lk.R * 1.35f && w >= lk.Level - .01f) return "lake"; if (w > .3f) return "river"; }
        return "sea";
    }
    public static string TimeTag(float tod) => tod is >= .2f and < .32f ? "dawn" : tod is >= .32f and < .68f ? "day" : tod is >= .68f and < .8f ? "dusk" : "night";
    public static string WeatherTag(float rain) => rain > .7f ? "storm" : rain > .2f ? "rain" : "clear";
    public static Ctx Here(Vector3 p) => new(Biome(p.X, p.Z), WaterKind(p), TimeTag(World.TimeOfDay), WeatherTag(Weather.Rain), LifeSkills.Level(Skill) + (int)StatPipeline.Key("fishLuck"));   // F-24 perk: rarer catches (extra levels in the loot tables)

    // ---------------------------------------------------------------- loot
    static bool Has(string[] a, string v) => a.Length == 0 || a.Contains("*") || a.Contains(v);
    static bool TimeOk(string want, string now) => want == "any" || want == now || (want == "day" && now is "dawn" or "dusk");
    static bool WeatherOk(string want, string now) => want == "any" || want == now || (want == "rain" && now == "storm");
    /// <summary>How likely a fish species bites here (0: never): rarity weight shifted by fishing level, and the biome, water, time, weather and level gates.</summary>
    public static float FishWeight(FishDef f, Ctx c, bool ignoreBiome = false)
    {
        if (!Has(f.Water, c.Water) || (!ignoreBiome && !Has(f.Biomes, c.Biome)) || !TimeOk(f.Time, c.Time) || !WeatherOk(f.Weather, c.Weather) || c.Level < f.MinLevel) return 0;
        var r = Rars.TryGetValue(f.Rarity, out var x) ? x : new Rar();
        return r.W * Mathf.Max(.05f, 1 + r.PerLevel * (c.Level - 1));
    }
    static float Mul(string group, string tag, string cat) => Mods.TryGetValue(group, out var g) && g.TryGetValue(tag, out var t) && t.TryGetValue(cat, out var m) ? m : 1f;
    public static float CatWeight(string cat, Ctx c)
    {
        if (!Cats.TryGetValue(cat, out var k)) return 0;
        float w = Mathf.Max(k.Min, k.W + k.PerLevel * (c.Level - 1));
        return w * Mul("water", c.Water, cat) * Mul("weather", c.Weather, cat) * Mul("time", c.Time, cat);
    }
    static T Choose<T>(List<(T v, float w)> l, Func<float> rnd) { float tot = l.Sum(x => x.w), x = rnd() * tot; foreach (var e in l) { x -= e.w; if (x <= 0) return e.v; } return l[^1].v; }

    /// <summary>One catch for this place, time, weather and fishing level (pure: nothing is given). rnd returns 0..1.</summary>
    public static Pick Roll(Ctx c, Func<float> rnd)
    {
        Load();
        var cats = new List<(string, float)>(); foreach (var k in Cats.Keys) { float w = CatWeight(k, c); if (w > 0) cats.Add((k, w)); }
        string cat = cats.Count > 0 ? Choose(cats, rnd) : "fish";
        switch (cat)
        {
            case "junk":
            {
                var l = Junk.Values.Where(j => Has(j.Water, c.Water)).Select(j => (j, j.W)).ToList();
                if (l.Count > 0) { var j = Choose(l, rnd); return new Pick { Kind = "junk", Junk = j, Diff = .1f }; }
                break;
            }
            case "treasure":
            {
                var l = Treasure.Where(t => Has(t.Water, c.Water)).Select(t => (t, t.W)).ToList();
                if (l.Count > 0) { var t = Choose(l, rnd); return new Pick { Kind = "treasure", Treasure = t, Diff = .5f }; }
                break;
            }
            case "gem":
                GemDb.Load(); return new Pick { Kind = "gem", Gem = GemDb.Roll(GemDb.RollTier(c.Level / 3)), Diff = .55f };
        }
        var fl = Fish.Values.Select(f => (f, FishWeight(f, c))).Where(x => x.Item2 > 0).ToList();
        if (fl.Count == 0) fl = Fish.Values.Select(f => (f, FishWeight(f, c, true))).Where(x => x.Item2 > 0).ToList();
        var fish = fl.Count > 0 ? Choose(fl, rnd) : Fish.Values.First();
        return new Pick { Kind = "fish", Fish = fish, Size = Mathf.Lerp(fish.Sz0, fish.Sz1, Mathf.Pow(rnd(), 1.6f)), Diff = fish.Diff };
    }
    /// <summary>A short label for a pick (sim logs, the catch card).</summary>
    public static string Label(Pick p) => p.Kind switch
    {
        "fish" => p.Fish!.Name, "junk" => p.Junk!.Name, "gem" => GemDb.Name(p.Gem),
        _ => p.Treasure!.Kind switch { "gold" => "Gold pouch", "chest" => "Sunken chest", _ => GatherDb.Mat(p.Treasure.Mat).Name },
    };
}

/// <summary>
/// Fish swimming around the camera (F-22): a capped number of schools and loners, one MultiMesh per species, seated in real water near
/// the hero (like Ambient's butterflies) and reseated when left behind. Species by biome, water kind and depth band (data/fishing.json).
/// Schools steer like boids (cohesion, separation, alignment toward a wandering goal); every fish bolts away from the hero inside
/// "flee" metres; a step that would leave the water (above the surface, into the bottom or the shore) is refused and the fish turns.
/// </summary>
public partial class FishField : Node3D
{
    public static FishField? I;
    sealed class F { public Vector3 P, V; public float Ph, Len; }
    sealed class Group { public FishDb.FishDef S = null!; public readonly List<F> Fish = new(); public Vector3 Goal; public float GoalT, Depth; public bool Live; }
    readonly List<Group> _groups = new();
    readonly Dictionary<string, MultiMeshInstance3D> _mm = new();
    readonly RandomNumberGenerator _rg = new();
    int _max = 64, _nGroups = 9, _per = 32; float _r = 30, _flee = 4, _cruise = .9f, _fleeSpeed = 3.6f, _seatT;
    public int LiveFish => _groups.Where(g => g.Live).Sum(g => g.Fish.Count);
    public int Fled { get; private set; }

    public override void _Ready()
    {
        I = this; FishDb.Load(); var f = FishDb.Field;
        _max = (int)JsonUtil.F(f, "maxFish", 64); _nGroups = (int)JsonUtil.F(f, "groups", 9); _r = JsonUtil.F(f, "radius", 30); _flee = JsonUtil.F(f, "flee", 4);
        _cruise = JsonUtil.F(f, "cruise", .9f); _fleeSpeed = JsonUtil.F(f, "fleeSpeed", 3.6f); _per = (int)JsonUtil.F(f, "perSpecies", 32);
        var mat = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .45f, Metallic = .15f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        foreach (var s in FishDb.Fish.Values)
        {
            var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = BuildMesh(s), InstanceCount = _per, VisibleInstanceCount = 0 };
            var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, Name = "Fish_" + s.Id };
            AddChild(mi); _mm[s.Id] = mi;
        }
        for (int i = 0; i < _nGroups; i++) _groups.Add(new Group());
    }
    public override void _ExitTree() { if (I == this) I = null; }

    /// <summary>A low-poly fish 1 m long, nose to -Z: a diamond-section body (dark back, light belly), a forked tail and a dorsal fin.</summary>
    static ArrayMesh BuildMesh(FishDb.FishDef s)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); st.SetSmoothGroup(uint.MaxValue);
        float h = .16f, w = .09f; var back = s.Col; var belly = s.Belly; var fin = s.Col.Darkened(.25f);
        var nose = new Vector3(0, 0, -.5f); var tail = new Vector3(0, .01f, .32f);
        var top = new Vector3(0, h, -.08f); var bot = new Vector3(0, -h * .8f, -.05f); var l = new Vector3(-w, 0, -.1f); var r = new Vector3(w, 0, -.1f);
        void T(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc) { st.SetColor(ca); st.AddVertex(a); st.SetColor(cb); st.AddVertex(b); st.SetColor(cc); st.AddVertex(c); }
        Color side = back.Lerp(belly, .45f);
        T(nose, top, r, back.Darkened(.1f), back, side); T(nose, r, bot, side, side, belly); T(nose, bot, l, belly, belly, side); T(nose, l, top, side, side, back);
        T(tail, r, top, back, side, back); T(tail, bot, r, belly, belly, side); T(tail, l, bot, side, side, belly); T(tail, top, l, back, back, side);
        // forked tail and dorsal fin (double sided: the material does not cull)
        T(tail, new Vector3(0, h * 1.3f, .55f), new Vector3(0, 0, .45f), fin, fin, fin); T(tail, new Vector3(0, 0, .45f), new Vector3(0, -h * 1.1f, .55f), fin, fin, fin);
        T(top, new Vector3(0, h * 1.6f, .02f), new Vector3(0, h * .7f, .12f), fin, fin, fin);
        // a pale eye dot on each side
        foreach (float sx in new[] { -1f, 1f }) T(new Vector3(sx * w * .62f, .045f, -.34f), new Vector3(sx * w * .62f, .015f, -.3f), new Vector3(sx * w * .62f, .015f, -.37f), Colors.White, Colors.Black, Colors.Black);
        st.GenerateNormals(); return st.Commit();
    }

    bool Valid(Vector3 p) => FishDb.InWater(p) && FishDb.InWater(p + Vector3.Down * .2f) && p.Y < FishDb.Surface(p.X, p.Z, p.Y) - .15f;

    /// <summary>A school or loner in water near a point: a random spot in water deep enough, a species that lives in this biome/water at that depth.</summary>
    bool Seat(Group g, Vector3 around, float rMin, float rMax)
    {
        var c = FishDb.Here(around);
        for (int tries = 0; tries < 24; tries++)
        {
            float a = _rg.Randf() * Mathf.Tau, d = _rg.RandfRange(rMin, rMax); float x = around.X + Mathf.Cos(a) * d, z = around.Z + Mathf.Sin(a) * d;
            float s = Lands.WaterAt(x, z); if (!FishDb.InWater(new Vector3(x, s - .3f, z))) continue;
            float bottom = FishDb.Bottom(x, z, s), depth = s - bottom; if (depth < .6f) continue;
            var here = new FishDb.Ctx(FishDb.Biome(x, z), FishDb.WaterKind(new Vector3(x, s - .3f, z)), c.Time, c.Weather, 99);
            // species that live here and fit this depth (their band starts above the bottom); shallow water gets the small fry
            var opts = FishDb.Fish.Values.Where(f => f.D0 < depth - .2f && FishDb.FishWeight(f, here with { Level = 99 }, false) > 0).ToList();
            if (opts.Count == 0) opts = FishDb.Fish.Values.Where(f => f.D0 < depth - .2f && FishDb.FishWeight(f, here with { Level = 99, Time = "any" }, true) > 0).ToList();
            if (opts.Count == 0) continue;
            int used = _groups.Where(o => o.Live && o != g).Sum(o => o.Fish.Count);
            var sp = opts[_rg.RandiRange(0, opts.Count - 1)];
            int already = _groups.Where(o => o.Live && o != g && o.S == sp).Sum(o => o.Fish.Count);
            int n = Math.Min(_rg.RandiRange(sp.S0, sp.S1), Math.Min(_max - used, _per - already)); if (n <= 0) return false;
            g.S = sp; g.Depth = Mathf.Min(_rg.RandfRange(sp.D0, sp.D1), depth - .35f); g.Fish.Clear();
            var centre = new Vector3(x, s - g.Depth, z);
            if (!Valid(centre)) continue;
            for (int i = 0; i < n; i++)
            {
                var p = centre + new Vector3(_rg.RandfRange(-.8f, .8f), _rg.RandfRange(-.2f, .2f), _rg.RandfRange(-.8f, .8f)); if (!Valid(p)) p = centre;
                float len = Mathf.Clamp(_rg.RandfRange(sp.Sz0, sp.Sz1) / 100f * 1.6f, .24f, 1.2f);   // a little larger than life so they read from the shore
                g.Fish.Add(new F { P = p, V = new Vector3(_rg.RandfRange(-1, 1), 0, _rg.RandfRange(-1, 1)).Normalized() * _cruise * .5f, Ph = _rg.Randf() * 10, Len = len });
            }
            g.Goal = centre; g.GoalT = 0; g.Live = true; return true;
        }
        g.Live = false; return false;
    }

    /// <summary>Reseats every group around a point now (dev captures).</summary>
    public void SeatAll(Vector3 around, float rMin = 2, float rMax = 14) { foreach (var g in _groups) Seat(g, around, rMin, rMax); Upload(0); }

    Vector3 NewGoal(Group g, Vector3 centre)
    {
        for (int i = 0; i < 8; i++)
        {
            var p = centre + new Vector3(_rg.RandfRange(-6, 6), 0, _rg.RandfRange(-6, 6));
            float s = FishDb.Surface(p.X, p.Z, centre.Y), b = FishDb.Bottom(p.X, p.Z, s);
            if (b + .3f > s - .3f) continue;   // too shallow here: try another goal (Clamp needs min <= max)
            p.Y = Mathf.Clamp(s - g.Depth, b + .3f, s - .3f);
            if (Valid(p)) return p;
        }
        return centre;
    }

    public override void _Process(double delta)
    {
        float dt = Mathf.Min((float)delta, .05f); var pl = Player.Instance;
        bool over = pl != null && !Dungeon.Active && World.I != null && World.I.Over.Visible; Visible = over; if (!over) return;
        var cam = GetViewport().GetCamera3D(); var around = pl!.GlobalPosition; var hero = pl.GlobalPosition + Vector3.Up * .6f;
        _seatT -= dt;
        if (_seatT <= 0)
        {   // one empty or left-behind group per tick: keeps the cost flat
            _seatT = .35f;
            var g = _groups.FirstOrDefault(x => !x.Live) ?? _groups.Where(x => x.Fish.Count > 0).OrderByDescending(x => x.Fish[0].P.DistanceSquaredTo(around)).FirstOrDefault();
            if (g != null && (!g.Live || g.Fish[0].P.DistanceTo(around) > _r * 1.5f)) Seat(g, around, 3, _r);
        }
        float t = (float)Time.GetTicksMsec() / 1000f;
        foreach (var g in _groups)
        {
            if (!g.Live || g.Fish.Count == 0) continue;
            var centre = Vector3.Zero; var avgV = Vector3.Zero; foreach (var f in g.Fish) { centre += f.P; avgV += f.V; } centre /= g.Fish.Count; avgV /= g.Fish.Count;
            g.GoalT -= dt; if (g.GoalT <= 0 || centre.DistanceTo(g.Goal) < 1f) { g.Goal = NewGoal(g, centre); g.GoalT = _rg.RandfRange(4, 9); }
            float cruise = _cruise * Mathf.Lerp(.8f, 1.3f, Mathf.Clamp(g.S.Sz1 / 80f, 0, 1));
            foreach (var f in g.Fish)
            {
                var steer = (g.Goal - f.P).Normalized() * .8f;
                if (g.Fish.Count > 1)
                {
                    steer += (centre - f.P) * .6f + (avgV - f.V) * .5f;
                    foreach (var o in g.Fish) { if (o == f) continue; var dd = f.P - o.P; float l2 = dd.LengthSquared(); if (l2 < .25f && l2 > 1e-5f) steer += dd / l2 * .08f; }
                }
                float speed = cruise; var away = f.P - hero; float dh = away.Length();
                if (dh < _flee) { steer += (away with { Y = away.Y * .3f }).Normalized() * (6f * (1 - dh / _flee) + 2f); speed = _fleeSpeed; g.GoalT = 0; if (dh < _flee * .6f) Fled++; }
                f.V += steer * dt * 2.2f; f.V.Y *= .9f;
                if (f.V.Length() > speed) f.V = f.V.Normalized() * speed;
                f.Ph += dt * (2 + f.V.Length() * 5);
                var np = f.P + f.V * dt;
                if (Valid(np)) f.P = np;
                else if (Valid(np with { Y = f.P.Y })) { f.P = np with { Y = f.P.Y }; f.V.Y = -f.V.Y * .5f; }
                else { f.V = new Vector3(-f.V.X + _rg.RandfRange(-.4f, .4f), 0, -f.V.Z + _rg.RandfRange(-.4f, .4f)); if (!Valid(f.P)) g.Live = false; }
            }
        }
        Upload(t);
    }

    void Upload(float t)
    {
        var count = new Dictionary<string, int>();
        foreach (var g in _groups)
        {
            if (!g.Live) continue; var mi = _mm[g.S.Id]; var mm = mi.Multimesh;
            foreach (var f in g.Fish)
            {
                int i = count.GetValueOrDefault(g.S.Id); if (i >= mm.InstanceCount) break; count[g.S.Id] = i + 1;
                var dir = f.V with { Y = f.V.Y * .5f }; if (dir.LengthSquared() < 1e-4f) dir = Vector3.Forward;
                var basis = Basis.LookingAt(dir.Normalized(), Vector3.Up).Rotated(Vector3.Up, 0) * new Basis(Vector3.Up, Mathf.Sin(f.Ph) * .28f);   // tail sway
                mm.SetInstanceTransform(i, new Transform3D(basis.Scaled(Vector3.One * f.Len), f.P));
            }
        }
        foreach (var (id, mi) in _mm) mi.Multimesh.VisibleInstanceCount = count.GetValueOrDefault(id);
    }

    /// <summary>Fish of a group near a point (fishing: the bobber's neighbours), for dev logs.</summary>
    public string Describe()
    {
        var by = _groups.Where(g => g.Live).GroupBy(g => g.S.Name).Select(x => $"{x.Key} x{x.Sum(g => g.Fish.Count)} ({x.Count()} group{(x.Count() > 1 ? "s" : "")})");
        return $"{LiveFish} fish: " + string.Join(", ", by);
    }
    /// <summary>The live fish positions (dev: water check).</summary>
    public IEnumerable<Vector3> Positions() => _groups.Where(g => g.Live).SelectMany(g => g.Fish.Select(f => f.P));
    public Vector3? SchoolCentre() { var g = _groups.Where(x => x.Live && x.Fish.Count > 1).OrderByDescending(x => x.Fish.Count).FirstOrDefault(); if (g == null) return null; var c = Vector3.Zero; foreach (var f in g.Fish) c += f.P; return c / g.Fish.Count; }
}
