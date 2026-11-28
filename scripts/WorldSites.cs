using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-36: the world filled with things to find, Elden Ring style, across all six lands (data/sites.json): ruined chapels with an offering on
/// the altar, graveyards with a grave to dig, abandoned carts with stores, fallen travellers holding a relic, broken monuments, standing
/// stones, fallen watchtowers with a chest, hidden cellars, war camps guarding a chest, pedlars, the remains of giants, glinting relics on
/// cliff edges, message stones and vistas. The F-33 roadside sites also get breakable stores (data/sites.json "wayside").
/// Placement follows only the terrain, the roads, the towns and distances (seeded by land, kind and try), so every machine and the online
/// server get the same places, props (Breakables ids) and monsters (Zones camp slots). Blocks are stamped where block structures are on;
/// the meshes of a site (gravestones, wagons, giant bones) are made only while a hero is near (SiteField). Dev: "/sites", "/sites goto".
/// </summary>
public static class WorldSites
{
    public sealed class Site
    {
        public string Id = "", Land = "", Kind = "", Name = ""; public float X, Z; public int Bx, By, Bz, Rot, Foes; public uint Seed;
        public Action<Node3D>? Dress; public Node3D? Node;
        /// <summary>World point of a local spot (local +Z = the front).</summary>
        public Vector2 W(float lx, float lz) { var (rx, rz) = (Rot & 3) switch { 0 => (lx, lz), 1 => (lz, -lx), 2 => (-lx, -lz), _ => (-lz, lx) }; return new Vector2(Bx + .5f + rx, Bz + .5f + rz); }
        public float Yaw => Rot * Mathf.Pi / 2f;
    }
    sealed class KindCfg { public string Name = ""; public float Radius = 5, Slope = 2, Gap = 60; public int FoeLo, FoeHi, FoeLevel; }

    public static readonly List<Site> Sites = new();
    static JsonDocument? _doc; static JsonElement _root;
    static bool _registered; static int _packSeq;
    const string File = "res://data/sites.json";

    static uint Fnv(string s) { uint h = 2166136261u; foreach (char c in s) { h ^= c; h *= 16777619u; } return h == 0 ? 1u : h; }
    static float H(uint seed, int a, int b = 0) => StructGen.H01(a, b, 7, seed);
    static JsonElement Obj(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
    static float F(JsonElement e, string k, float d) => e.ValueKind == JsonValueKind.Object ? JsonUtil.F(e, k, d) : d;
    static string[] Arr(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToArray() : Array.Empty<string>();
    static (int, int) Range(JsonElement e, string k, int lo, int hi)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() >= 2) { lo = a[0].GetInt32(); hi = Math.Max(lo, a[1].GetInt32()); }
        return (lo, hi);
    }
    static float DistSeg(Vector2 q, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp((q - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0, 1); return (a + ab * t - q).Length(); }
    static string Pick(string[] a, uint h) => a.Length == 0 ? "" : a[(int)(h % (uint)a.Length)];

    // ---------------------------------------------------------------- the build
    public static void Build(Node3D over)
    {
        Register(); Sites.Clear(); _packSeq = 0;
        if (!Godot.FileAccess.FileExists(File)) return;
        ulong t0 = Time.GetTicksMsec();
        _doc = JsonUtil.Load(File); _root = _doc.RootElement; var r = _root;
        var mg = Obj(r, "margins");
        float mTown = F(mg, "town", 34), mPoi = F(mg, "poi", 18), mPlace = F(mg, "place", 26), mRoad = F(mg, "road", 10), mBridge = F(mg, "bridge", 26), mWay = F(mg, "wayside", 34);
        float minGap = F(r, "minGap", 40), clear = F(r, "waterClear", .8f); int tries = (int)F(r, "tries", 60);
        var kinds = new List<(string key, KindCfg c)>();
        var kj = Obj(r, "kinds");
        if (kj.ValueKind == JsonValueKind.Object) foreach (var k in kj.EnumerateObject())
        {
            var (flo, fhi) = Range(k.Value, "foes", 0, 0);
            kinds.Add((k.Name, new KindCfg { Name = JsonUtil.S(k.Value, "name", k.Name), Radius = F(k.Value, "radius", 5), Slope = F(k.Value, "slope", 2), Gap = F(k.Value, "gap", 60), FoeLo = flo, FoeHi = fhi, FoeLevel = (int)F(k.Value, "foeLevel", 0) }));
        }
        var keep = new List<(float x, float z, float r)>();
        foreach (var t in TownZones.All) keep.Add((t.X, t.Z, t.R + mTown));
        foreach (var p in World.Pois) if (!p.Sky) keep.Add((p.X, p.Z, p.R + mPoi));
        foreach (var L in Lands.All) foreach (var p in L.Places) keep.Add((p.X, p.Z, p.R + mPlace));
        foreach (var s in Wayside.Sites) keep.Add((s.X, s.Z, mWay));

        var lands = new List<(string id, Rect2 box, List<Vector2[]> roads)> { ("home", new Rect2(-World.Half + 14, -World.Half + 14, (World.Half - 14) * 2, (World.Half - 14) * 2), World.Roads) };
        foreach (var L in Lands.All) lands.Add((L.Id, new Rect2(L.Cx - L.Half, L.Cz - L.Half, L.Half * 2, L.Half * 2), L.Roads.Select(l => l.P).ToList()));
        var counts = Obj(r, "counts");
        long checks = 0;

        foreach (var (land, box, roads) in lands)
        {
            var cnt = Obj(counts, land); if (cnt.ValueKind != JsonValueKind.Object) cnt = Obj(counts, "default");
            bool In(float x, float z) => land == "home" ? Mathf.Abs(x) < World.Half - 14 && Mathf.Abs(z) < World.Half - 14 : Lands.LandAt(x, z)?.Id == land;
            bool Ok(float x, float z, KindCfg kc, bool slopes)
            {
                checks++;
                if (!In(x, z)) return false;
                foreach (var c in keep) if ((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < (c.r + kc.Radius) * (c.r + kc.Radius)) return false;
                foreach (var s in Sites) { float g = Mathf.Max(minGap, s.Kind == kc.Name ? kc.Gap : minGap); if ((x - s.X) * (x - s.X) + (z - s.Z) * (z - s.Z) < g * g) return false; }
                float g0 = World.GroundAt(x, z); if (g0 < Lands.WaterAt(x, z) + clear) return false;
                for (int ring = 0; ring < 2; ring++)
                {
                    float rr = ring == 0 ? Mathf.Min(kc.Radius, 3f) : kc.Radius; if (ring == 1 && kc.Radius <= 3f) break;
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.Tau / 8f, sx = x + Mathf.Cos(a) * rr, sz = z + Mathf.Sin(a) * rr, gg = World.GroundAt(sx, sz);
                        if (gg < Lands.WaterAt(sx, sz) + clear) return false;
                        if (slopes && Mathf.Abs(gg - g0) > kc.Slope) return false;
                    }
                }
                var q = new Vector2(x, z);
                foreach (var P in roads) for (int i = 0; i < P.Length - 1; i++) if (DistSeg(q, P[i], P[i + 1]) < mRoad + kc.Radius) return false;
                foreach (var b in Lands.Bridges) if (DistSeg(q, b.A, b.B) < mBridge) return false;
                return true;
            }
            foreach (var (kind, kc) in kinds)
            {
                kc.Name = kind;   // (the gap check compares kinds by key)
                int want = (int)F(cnt, kind, 0); if (want <= 0) continue;
                int got = 0, n = kind is "glint" ? tries * 4 : tries;
                for (int t = 0; t < want * n && got < want; t++)
                {
                    uint h = Fnv($"{land}|{kind}|{t}");
                    float x = box.Position.X + H(h, 1) * box.Size.X, z = box.Position.Y + H(h, 2) * box.Size.Y;
                    if (kind == "vista")
                    {   // the highest of a handful of spots
                        float best = -1e9f; Vector2? at = null;
                        for (int i = 0; i < 16; i++) { float vx = box.Position.X + H(h, 10 + i) * box.Size.X, vz = box.Position.Y + H(h, 40 + i) * box.Size.Y; float g = World.GroundAt(vx, vz); if (g > best && In(vx, vz) && Ok(vx, vz, kc, true)) { best = g; at = new Vector2(vx, vz); } }
                        if (at is not { } v) continue; x = v.X; z = v.Y;
                    }
                    else if (kind == "glint")
                    {   // a cliff edge (flat where it lies, a drop close by); late tries take any odd spot
                        if (!Ok(x, z, kc, true)) continue;
                        if (t < want * n * 2 / 3 && !Cliff(x, z)) continue;
                    }
                    else if (!Ok(x, z, kc, true)) continue;
                    Add(land, kind, kc, x, z, got); got++;
                }
            }
        }
        // the F-33 roadside sites get breakable stores
        int way = 0; var wj = Obj(r, "wayside");
        foreach (var s in Wayside.Sites)
        {
            if (!wj.TryGetProperty(s.Kind, out var list) || list.ValueKind != JsonValueKind.Array) continue;
            foreach (var e in list.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3) continue;
                string type = e[0].GetString() ?? ""; float lx = e[1].GetSingle(), lz = e[2].GetSingle();
                var w = s.W(lx, lz); float y = !Structures.On ? float.NaN : s.Kind == "crypt" ? s.By - 6 : s.By;
                if (Breakables.Add(type, w.X, w.Z, StructGen.H01(s.Bx, s.Bz, way, 5u) * 6f, y, false) != null) way++;
            }
        }
        if (!Net.Dedicated) Over(over).AddChild(new SiteField { Name = "SiteField" });
        GD.Print($"[SITES] {Sites.Count} places ({string.Join(", ", Sites.GroupBy(s => s.Kind).Select(g => $"{g.Key} {g.Count()}"))}), {Sites.Sum(s => s.Foes)} monsters, {way} roadside props, {checks} spot checks, {Time.GetTicksMsec() - t0} ms");
    }
    static Node Over(Node3D over) => over.GetParent() ?? over;

    /// <summary>A cliff edge: level where the relic lies, with a drop of 3.5 m or more within 5 m.</summary>
    static bool Cliff(float x, float z)
    {
        float g0 = World.GroundAt(x, z);
        for (int i = 0; i < 6; i++) { float a = i * Mathf.Tau / 6f; if (Mathf.Abs(World.GroundAt(x + Mathf.Cos(a) * 1.2f, z + Mathf.Sin(a) * 1.2f) - g0) > .7f) return false; }
        for (int i = 0; i < 12; i++) { float a = i * Mathf.Tau / 12f; if (g0 - World.GroundAt(x + Mathf.Cos(a) * 5f, z + Mathf.Sin(a) * 5f) > 3.5f) return true; }
        return false;
    }

    // ---------------------------------------------------------------- one place
    static byte I(string k) => Blocks.Id(k);
    static void Add(string land, string kind, KindCfg kc, float x, float z, int n)
    {
        int bx = Mathf.FloorToInt(x), bz = Mathf.FloorToInt(z);
        var s = new Site { Id = $"site_{land}_{kind}_{n}", Land = land, Kind = kind, Name = JsonUtil.S(Obj(Obj(_root, "kinds"), kind), "name", kind), Bx = bx, Bz = bz, X = bx + .5f, Z = bz + .5f, By = Structures.Floor(World.GroundAt(bx + .5f, bz + .5f)) };
        s.Seed = Fnv(s.Id); s.Rot = (int)((s.Seed >> 5) & 3);
        Sites.Add(s);
        Blueprint? b = null; bool on = Structures.On;
        float FY(int ly = 0) => on ? s.By + ly : float.NaN;    // a floor height on the site's pad, or the ground
        BreakRec? Put(string type, float lx, float lz, float y = float.NaN, float up = 0f, string text = "", string label = "", bool? owned = false, float yaw = float.NaN, int stack = 0)
        {
            var w = s.W(lx, lz); return Breakables.Add(type, w.X, w.Y, float.IsNaN(yaw) ? s.Yaw + H(s.Seed, (int)(lx * 7 + lz * 13), 77) * 1.2f - .6f : yaw, y, owned, stack, 0, text, label, up);
        }
        string Lore(string k) => Pick(Arr(Obj(_root, "lore"), k), s.Seed >> 7);
        string LandLore() { var l = Arr(Obj(_root, "landLore"), land); return l.Length == 0 ? Lore("lorestone") : Pick(l, s.Seed >> 9); }
        var fo = Obj(Obj(_root, "foes"), land); if (fo.ValueKind != JsonValueKind.Object) fo = Obj(Obj(_root, "foes"), "home");
        switch (kind)
        {
            case "chapel":
                if (on) b = Chapel(land, s.Seed);
                Put("altar", 0, -5, on ? s.By + 1.5f : float.NaN, on ? .1f : 1.1f, label: Pick(Arr(_root, "relicNames"), s.Seed >> 3));
                Put("lore", 2.2f, 6.5f, FY(), text: Lore("chapel"), label: s.Name);
                Put("pot", -2.4f, -4.6f, FY()); Put("urn", 2.4f, -4.6f, FY());
                s.Dress = g => DressChapel(s, g, on);
                break;
            case "graveyard":
            {
                if (on) b = Graveyard(s.Seed);
                int lucky = (int)(s.Seed % 6u); int gi = 0;
                foreach (float gz in new[] { -3f, .5f }) foreach (float gx in new[] { -4.5f, -2.5f, 2.5f, 4.5f })
                    if (gi++ == lucky) Put("grave", gx, gz + .9f, FY(), yaw: s.Yaw);
                Put("urn", -5f, 4f, FY()); Put("pot", 5f, -4f, FY()); Put("urn", 5f, 3.5f, FY());
                Put("lore", 1.8f, 6.4f, FY(), text: Lore("graveyard"), label: s.Name);
                s.Dress = g => DressGraveyard(s, g, lucky);
                break;
            }
            case "cart":
                Put("crate", 2.2f, 1f); Put("crate_small", 2.7f, -.3f); Put("barrel", -2.3f, .9f); Put("sack", -2.1f, -1.2f);
                if (H(s.Seed, 3) < .4f) Put("barrel_apples", 1.2f, 2.4f);
                if (H(s.Seed, 4) < .5f) Put("corpse", 0f, 1.9f, yaw: s.Yaw);
                s.Dress = g => DressCart(s, g);
                break;
            case "corpse":
                Put("corpse", 0, 0, yaw: s.Yaw);
                if (H(s.Seed, 5) < .5f) Put("lore", 1.6f, 1.2f, text: Lore("lorestone"));
                s.Dress = g => DressCorpse(s, g);
                break;
            case "statue":
                if (on) b = Statue(land, s.Seed);
                Put("lore", 0, 4.2f, FY(), text: Lore("statue") + "\n" + LandLore(), label: s.Name);
                if (H(s.Seed, 6) < .5f) Put("relic", -2.6f, 2.6f, label: Pick(Arr(_root, "relicNames"), s.Seed >> 4));
                s.Dress = g => { if (!on) Fit(g, s, "kit/graveyard/pillar-obelisk", 0, 0, 5f, true); };
                break;
            case "stones":
                if (on) b = Stones(s.Seed);
                Put("lore", 0, 2.4f, FY(), text: Lore("stones") + "\n" + LandLore(), label: s.Name);
                Put("relic", 0, 0, on ? s.By + 1f : float.NaN, .2f, label: Pick(Arr(_root, "relicNames"), s.Seed >> 4));
                s.Dress = g => { if (!on) for (int i = 0; i < 7; i++) { float a = i / 7f * Mathf.Tau; Fit(g, s, "kit/nature/rock_tall", Mathf.Cos(a) * 6f, Mathf.Sin(a) * 6f, 3f + H(s.Seed, i) * 2f, true); } };
                break;
            case "watchruin":
                if (on) b = WatchRuin(land, s.Seed);
                Put("chest", 0, -.5f, FY(), yaw: s.Yaw);
                Put("crate", -3.5f, 3.5f); Put("barrel", 3.6f, 3.2f);
                break;
            case "cellar":
                if (on) b = Cellar(s.Seed);
                {
                    float cy = on ? s.By - 6 : float.NaN;
                    Put("chest_rare", 3f, -11f, cy, yaw: s.Yaw);
                    Put("barrel", -2.5f, -10.5f, cy); Put("barrel", -2.5f, -9.4f, cy); Put("crate", 4f, -6f, cy); Put("crate_small", -2.6f, -5.6f, cy); Put("sack", .5f, -11f, cy); Put("pot", 4f, -8f, cy);
                    Put("lore", 2.6f, 3.4f, FY(), text: LandLore(), label: s.Name);
                }
                break;
            case "warcamp":
                if (on) b = WarCamp(s.Seed);
                Put("chest_rare", 0, -2.6f, FY(), yaw: s.Yaw);
                Put("crate", -3f, -4f, FY()); Put("crate", 3f, -4.2f, FY()); Put("crate_small", 3.4f, -3.2f, FY()); Put("barrel", -6.5f, 3.5f, FY()); Put("barrel", 6.5f, 3.5f, FY()); Put("sack", -6.3f, 4.5f, FY());
                break;
            case "pedlar":
                if (on) b = Pedlar();
                Put("crate", 2.6f, -3f, owned: true); Put("barrel", -2.6f, -3f, owned: true); Put("sack", -2.9f, -1.9f, owned: true); Put("fruit_crate", 3.1f, -1.8f, owned: true);
                if (!Net.Dedicated) { var hm = s.W(1.5f, 1.8f); var wk = s.W(0, 2.2f); Wayside.AddPerson(s.Id, "pedlar", new Vector3(hm.X, 0, hm.Y), new Vector3(wk.X, 0, wk.Y), s.Seed); }
                s.Dress = g => Fit(g, s, Kit.Str("props", "stall", "props/Stall_Cart_Empty"), 0, -3.4f, 2.4f, true);
                break;
            case "giant":
                Put("relic", 3.2f, 3.2f, label: Pick(Arr(_root, "relicNames"), s.Seed >> 4));
                Put("lore", -3.5f, 4.5f, text: Lore("giant") + "\n" + LandLore(), label: s.Name);
                s.Dress = g => DressGiant(s, g);
                break;
            case "glint":
                Put("relic", 0, 0, label: Pick(Arr(_root, "relicNames"), s.Seed >> 4));
                break;
            case "lorestone":
                Put("lore", 0, 0, text: H(s.Seed, 8) < .35f ? LandLore() : Lore("lorestone"));
                break;
            case "vista":
            {
                var names = Arr(Obj(_root, "vistaNames"), land); if (names.Length == 0) names = Arr(Obj(_root, "vistaNames"), "default");
                Put("vista", 0, 0, text: Lore("vista") + "\n" + LandLore(), label: Pick(names, s.Seed >> 6));
                break;
            }
        }
        if (b != null) Structures.Place(b, s.Bx, s.By, s.Bz, s.Rot);
        // monsters: streamed camp slots like the packs (the same numbers on every machine)
        var types = Arr(fo, kind);
        if (kc.FoeHi > 0 && types.Length > 0)
        {
            int count = kc.FoeLo + (int)(s.Seed % (uint)(kc.FoeHi - kc.FoeLo + 1));
            if (count > 0)
            {
                Zones.Load(); var camp = Zones.NewCamp(s.X, s.Z); int pid = 60000 + _packSeq++;
                for (int i = 0; i < count; i++)
                {
                    float a = i / (float)count * Mathf.Tau + H(s.Seed, 20 + i) * .6f, rr = kind == "warcamp" ? 3.6f : kc.Radius * .5f + 2.5f;
                    var w = s.W(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr);
                    int lvl = Math.Max(1, Game.LevelAt(w.X, w.Y, 0) + kc.FoeLevel);
                    Zones.AddSlot(camp, new CampSlot { Type = types[i % types.Length], Level = lvl, PackId = pid, AnyRealm = true, Leader = kind == "warcamp" && i == 0, Pos = World.OnGround(w.X, w.Y, .3f) });
                }
                s.Foes = count;
            }
        }
    }

    // ---------------------------------------------------------------- blueprints (front +Z; local y 0 = the first layer over the floor)
    static byte Wall(string land) => I(land switch { "sun" => "sandstone", "ember" => "dark_stone", "mire" => "cobblestone", _ => "stone_bricks" });
    static Blueprint Chapel(string land, uint seed)
    {
        var b = new Blueprint(); byte sb = Wall(land), moss = I("mossy_bricks"), cob = I("cobblestone"), slab = I(land == "sun" ? "sandstone_slab" : "brick_slab"), pew = I("planks_slab");
        b.PadBox(-5, -8, 5, 8, 255, Blocks.Dirt, 10);
        b.PadBox(-3, -6, 3, 5, cob, Blocks.Dirt, 10);
        var w = new Blueprint();
        for (int y = 0; y <= 5; y++) for (int z = -6; z <= 5; z++) for (int x = -3; x <= 3; x++)
        {
            if (!(Math.Abs(x) == 3 || z == -6 || z == 5)) continue;
            if (z == 5 && Math.Abs(x) <= 1 && y <= 3) continue;                                         // the doorway
            if (Math.Abs(x) == 3 && y is 2 or 3 && z is -3 or 0 or 3) continue;                          // windows
            w.Set(x, y, z, StructGen.H01(x, y, z, seed) < .3f ? moss : sb);
            if (y == 0) w.Post(x, -1, z, sb);
        }
        for (int y = 6; y <= 8; y++) for (int x = -3 + (y - 5); x <= 3 - (y - 5); x++) { w.Set(x, y, -6, sb); w.Set(x, y, 5, sb); }   // the gables
        foreach (int sx in new[] { -4, 4 }) foreach (int sz in new[] { -6, 5 }) { w.Box(sx, 0, sz, sx, 2, sz, sb); w.Post(sx, -1, sz, sb); }   // buttresses
        StructGen.Ruin(w, seed, 8);
        b.Merge(w, 0, 0, 0);
        b.Box(-1, 0, -5, 1, 0, -5, sb); b.Box(-1, 1, -5, 1, 1, -5, slab);   // the altar (top 1.5 over the floor)
        foreach (int z in new[] { -2, 0, 2 }) foreach (int x in new[] { -2, -1, 1, 2 }) if (StructGen.H01(x, 9, z, seed) < .7f) b.Set(x, 0, z, pew, 0);
        for (int i = 0; i < 5; i++) { int rx = -2 + (int)(StructGen.H01(i, 11, 0, seed) * 5), rz = -4 + (int)(StructGen.H01(i, 12, 0, seed) * 8); if (!b.Has(rx, 0, rz)) b.Set(rx, 0, rz, StructGen.H01(i, 13, 0, seed) < .5f ? moss : cob); }
        return b;
    }
    static Blueprint Graveyard(uint seed)
    {
        var b = new Blueprint(); byte fence = I("fence_dark"), path = I("path");
        b.PadBox(-7, -6, 7, 7, 255, Blocks.Dirt, 4);
        for (int x = -6; x <= 6; x++) { b.Set(x, 0, -5, fence); if (Math.Abs(x) > 1) b.Set(x, 0, 5, fence); }
        for (int z = -5; z <= 5; z++) { b.Set(-6, 0, z, fence); b.Set(6, 0, z, fence); }
        for (int i = 0; i < 4; i++) { int fx = -6 + (int)(StructGen.H01(i, 1, 0, seed) * 13); b.Remove(fx, 0, StructGen.H01(i, 2, 0, seed) < .5f ? -5 : 5); }   // a few fence panels fallen
        b.PadBox(-1, -4, 1, 6, path, Blocks.Dirt, 4);
        return b;
    }
    static Blueprint Statue(string land, uint seed)
    {
        var b = new Blueprint(); byte st = I(land == "sun" ? "sandstone" : "stone"), sb = Wall(land), moss = I("mossy_bricks"), dark = I("dark_stone");
        b.PadBox(-4, -4, 4, 6, 255, Blocks.Dirt, 12);
        b.Box(-2, 0, -2, 2, 0, 2, sb); b.Box(-1, 1, -1, 1, 1, 1, sb); for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++) b.Post(x, -1, z, sb);
        byte S(int x, int y, int z) => y < 4 && StructGen.H01(x, y, z, seed) < .35f ? moss : st;
        foreach (int lx in new[] { -1, 1 }) for (int y = 2; y <= 4; y++) b.Set(lx, y, 0, S(lx, y, 0));      // legs
        b.Box(-1, 5, 0, 1, 7, 0, st); b.Set(-1, 5, 0, S(-1, 5, 0));                                       // body
        b.Box(-2, 7, 0, 2, 7, 0, st);                                                                     // shoulders
        bool broken = StructGen.H01(0, 0, 1, seed) < .5f;
        b.Set(-2, 6, 0, st); if (!broken) b.Set(2, 6, 0, st);                                             // arms
        if (!broken) b.Set(0, 8, 0, st);                                                                  // head
        for (int y = 2; y <= 6; y++) b.Set(0, y, 1, dark); b.Box(-1, 5, 1, 1, 5, 1, dark);               // the sword held point-down
        if (broken) { b.Set(3, 0, 2, st); b.Set(2, 6, 0, 0); b.Set(-3, 0, 3, st); }                       // the head and an arm on the ground
        return b;
    }
    static Blueprint Stones(uint seed)
    {
        var b = new Blueprint(); byte dk = I("dark_stone"), st = I("stone"), moss = I("mossy_bricks"), slab = I("cobble_slab");
        b.PadBox(-9, -9, 9, 9, 255, Blocks.Dirt, 7);
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Mathf.Tau; int x = Mathf.RoundToInt(Mathf.Cos(a) * 6.5f), z = Mathf.RoundToInt(Mathf.Sin(a) * 6.5f);
            int h = 3 + (int)(StructGen.H01(i, 1, 0, seed) * 3); bool wide = StructGen.H01(i, 2, 0, seed) < .35f;
            if (StructGen.H01(i, 3, 0, seed) < .15f) { b.Box(x, 0, z, x + 1, 0, z, st); continue; }   // one fallen
            for (int y = 0; y < h; y++) { b.Set(x, y, z, y < 2 && StructGen.H01(x, y, z, seed) < .4f ? moss : dk); if (wide) b.Set(x + (x > 0 ? -1 : 1), y, z, dk); }
            b.Post(x, -1, z, dk);
        }
        b.Box(-1, 0, -1, 1, 0, 1, slab); b.Set(0, 0, 0, st);   // the altar stone (the relic lies on it)
        return b;
    }
    static Blueprint WatchRuin(string land, uint seed)
    {
        var b = new Blueprint(); byte cob = I("cobblestone"), moss = I("mossy_bricks");
        b.PadBox(-6, -6, 6, 6, 255, Blocks.Dirt, 14);
        var t = StructGen.Tower(5, 9, Wall(land), true); StructGen.Ruin(t, seed, 9);
        for (int x = -1; x <= 1; x++) for (int y = 0; y <= 2; y++) t.Set(x, y, 2, 0);   // the doorway broken wide
        b.Merge(t, 0, 0, 0);
        for (int i = 0; i < 9; i++) { int rx = -5 + (int)(StructGen.H01(i, 4, 0, seed) * 11), rz = -5 + (int)(StructGen.H01(i, 5, 0, seed) * 11); if (Math.Abs(rx) > 2 || Math.Abs(rz) > 2) b.Set(rx, 0, rz, StructGen.H01(i, 6, 0, seed) < .5f ? moss : cob); }
        return b;
    }
    /// <summary>A ruined foundation hiding a 2-wide stair down to a cellar room (floor 6 under the surface; as Wayside's crypt, without the mausoleum).</summary>
    static Blueprint Cellar(uint seed)
    {
        var b = new Blueprint(); byte sb = I("stone_bricks"), moss = I("mossy_bricks"), cob = I("cobblestone"), stairs = I("cobble_stairs"), torch = I("torch"), plank = I("dark_planks");
        byte Deep(int x, int y, int z) => StructGen.H01(x, y, z, seed) < .6f ? moss : cob;
        b.PadBox(-6, -13, 7, 6, 255, Blocks.Dirt, 5);
        for (int z = -3; z <= 4; z++) for (int x = -3; x <= 4; x++)
        {
            if (!(x == -3 || x == 4 || z == -3 || z == 4)) continue; int h = StructGen.H01(x, 0, z, seed) < .45f ? 2 : StructGen.H01(x, 1, z, seed) < .7f ? 1 : 0;
            for (int y = 0; y < h; y++) b.Set(x, y, z, StructGen.H01(x, y, z, seed + 3) < .5f ? moss : cob);
        }
        b.Set(-1, 0, 2, plank); b.Set(2, 0, 2, plank); b.Set(-1, 0, -1, plank);   // rotten boards round the hatch
        for (int y = -7; y <= -2; y++) for (int z = -12; z <= -4; z++) for (int x = -4; x <= 5; x++) b.Set(x, y, z, Deep(x, y, z));
        b.Box(-3, -6, -11, 4, -3, -5, 0);
        for (int k = 0; k <= 5; k++)
        {
            int z = 1 - k;
            for (int x = -1; x <= 2; x++) for (int y = -2 - k; y <= -1; y++) b.Set(x, y, z, k == 0 || y == -1 && z >= -3 ? sb : Deep(x, y, z));
            for (int x = 0; x <= 1; x++) { b.Set(x, -1 - k, z, stairs, 2); for (int y = -k; y <= -k + 3; y++) b.Set(x, y, z, 0); }
        }
        b.Set(-3, -4, -8, torch, 1 + 1); b.Set(4, -4, -8, torch, 1 + 0); b.Set(1, -4, -11, torch, 1 + 3);
        return b;
    }
    static Blueprint WarCamp(uint seed)
    {
        var b = new Blueprint(); byte log = I("fence_log");
        b.PadBox(-9, -8, 9, 8, 255, Blocks.Dirt, 6);
        b.Merge(StructGen.Campfire(), 0, 0, 1);
        b.Merge(StructGen.Tent(I("cloth_red"), 4), -5, 0, 0, 1); b.Merge(StructGen.Tent(I("cloth_tan"), 4), 5, 0, 0, 3); b.Merge(StructGen.Tent(I((seed % 2) == 0 ? "cloth_blue" : "cloth_red"), 4), 0, 0, -6, 0);
        for (int i = 0; i < 40; i++)
        {   // a broken palisade round the back and the sides
            float a = i / 40f * Mathf.Tau; if (Mathf.Sin(a) > .75f) continue;   // the open front (+Z)
            if (StructGen.H01(i, 7, 0, seed) < .2f) continue;
            int x = Mathf.RoundToInt(Mathf.Cos(a) * 8.5f), z = Mathf.RoundToInt(Mathf.Sin(a) * 7.5f); b.Set(x, 0, z, log); if (StructGen.H01(i, 8, 0, seed) < .6f) b.Set(x, 1, z, log);
        }
        b.Set(-1, 0, 4, I("log"), 1); b.Set(1, 0, 4, I("log"), 1);
        return b;
    }
    static Blueprint Pedlar()
    {
        var b = new Blueprint(); b.PadBox(-4, -5, 4, 4, 255, Blocks.Dirt, 5);
        b.Merge(StructGen.Campfire(), 0, 0, 1); b.Set(2, 0, 2, I("log"), 1);
        return b;
    }

    // ---------------------------------------------------------------- meshes (made while a hero is near)
    /// <summary>A model fitted to a height, standing on the ground (or a floor height y) at a local spot of the site; solid = a box collider.</summary>
    static MeshInstance3D? Fit(Node3D g, Site s, string model, float lx, float lz, float height, bool solid, float y = float.NaN, float yaw = float.NaN, Vector3? tilt = null)
    {
        var p = Kit.Get(model); if (p == null || p.Box.Size.Y < .001f) return null;
        var w = s.W(lx, lz); var at = float.IsNaN(y) ? World.OnGround(w.X, w.Y, -.03f) : new Vector3(w.X, y, w.Y);
        float sc = height / p.Box.Size.Y, yw = float.IsNaN(yaw) ? s.Yaw : yaw;
        var holder = new Node3D { Position = at, Rotation = new Vector3(tilt?.X ?? 0, yw, tilt?.Z ?? 0) }; g.AddChild(holder);
        var c = p.Box.GetCenter();
        var mi = new MeshInstance3D { Mesh = p.Mesh, Transform = new Transform3D(Basis.Identity.Scaled(Vector3.One * sc), new Vector3(-c.X * sc, -p.Box.Position.Y * sc, -c.Z * sc)) * p.Xf, VisibilityRangeEnd = 170f };
        if (height < 1.6f) mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        holder.AddChild(mi);
        if (solid)
        {
            var body = new StaticBody3D { CollisionLayer = 1 }; body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = p.Box.Size * sc }, Position = new Vector3(0, p.Box.Size.Y * sc / 2, 0) }); holder.AddChild(body);
        }
        return mi;
    }
    static void DressChapel(Site s, Node3D g, bool on)
    {
        float ay = on ? s.By + 1.5f : float.NaN;
        if (!on) { Fit(g, s, "kit/graveyard/altar-stone", 0, -5, 1f, true); foreach (var (x, z) in new[] { (-3.5f, -6f), (3.5f, -6f), (-3.5f, 5f), (3.5f, 5f) }) Fit(g, s, "kit/graveyard/column-large", x, z, 3f + H(s.Seed, (int)x) * 2f, true); }
        Fit(g, s, "kit/graveyard/candle-multiple", -1.1f, -5.1f, .45f, false, on ? ay : float.NaN);
        Fit(g, s, "kit/graveyard/candle-multiple", 1.1f, -5.1f, .4f, false, on ? ay : float.NaN);
        var w = s.W(0, -4.6f); var lp = float.IsNaN(ay) ? World.OnGround(w.X, w.Y, 1.6f) : new Vector3(w.X, ay + .6f, w.Y);
        g.AddChild(new OmniLight3D { Position = lp, LightColor = new Color(1f, .72f, .42f), OmniRange = 6f, LightEnergy = 1.2f, ShadowEnabled = false, DistanceFadeEnabled = true, DistanceFadeBegin = 60f, DistanceFadeLength = 15f });
    }
    static readonly string[] Headstones = { "gravestone-cross", "gravestone-round", "gravestone-bevel", "gravestone-decorative", "gravestone-roof", "gravestone-wide", "gravestone-broken" };
    static void DressGraveyard(Site s, Node3D g, int lucky)
    {
        int gi = 0;
        foreach (float gz in new[] { -3f, .5f }) foreach (float gx in new[] { -4.5f, -2.5f, 2.5f, 4.5f })
        {
            int i = gi++; if (i != lucky && H(s.Seed, 30 + i) < .15f) continue;
            string m = Headstones[(int)(StructGen.Hash(i, 3, 0, s.Seed) % (uint)Headstones.Length)];
            Fit(g, s, "kit/graveyard/" + m, gx, gz, m == "gravestone-broken" ? .5f : .9f + H(s.Seed, 50 + i) * .3f, true, float.NaN, s.Yaw + (H(s.Seed, 60 + i) - .5f) * .25f, new Vector3((H(s.Seed, 70 + i) - .5f) * .15f, 0, (H(s.Seed, 80 + i) - .5f) * .2f));
            if (i == lucky) Fit(g, s, "kit/graveyard/grave-border", gx, gz + .9f, .25f, false);
        }
        Fit(g, s, "kit/graveyard/cross-column", 0, -4.2f, 2.6f, true);
        Fit(g, s, "kit/graveyard/debris", -3.5f, 3.5f, .4f, false); Fit(g, s, "kit/graveyard/coffin-old", 3.6f, 3.3f, .5f, false, float.NaN, s.Yaw + .4f);
    }
    static void DressCart(Site s, Node3D g)
    {
        Fit(g, s, Kit.Str("props", "wagon", "village/Prop_Wagon"), 0, 0, 2f, true, float.NaN, s.Yaw + H(s.Seed, 9) * .8f - .4f, new Vector3(0, 0, .22f));
        if (H(s.Seed, 10) < .5f) Fit(g, s, "kit/graveyard/debris", 1.4f, -2f, .35f, false);
    }
    static void DressCorpse(Site s, Node3D g) => Fit(g, s, "nature/Rock_Medium_" + (1 + s.Seed % 3), 0, -1.1f, 1.7f, true, float.NaN, s.Yaw + H(s.Seed, 11) * 3f);

    static StandardMaterial3D? _steel, _bone, _rust;
    /// <summary>A fallen giant's sword driven into the earth, or its bones (skull, spine and ribs).</summary>
    static void DressGiant(Site s, Node3D g)
    {
        var w = s.W(0, 0); var at = World.OnGround(w.X, w.Y, 0);
        var root = new Node3D { Position = at, Rotation = new Vector3(0, s.Yaw, 0) }; g.AddChild(root);
        var body = new StaticBody3D { CollisionLayer = 1 }; root.AddChild(body);
        void P(Node3D parent, Mesh m, Material mat, Vector3 pos, Vector3 rot, Vector3? col = null)
        {
            parent.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mat, Position = pos, Rotation = rot, VisibilityRangeEnd = 320f });
            if (col is { } c) body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = c }, Position = pos, Rotation = rot });
        }
        if (H(s.Seed, 12) < .5f)
        {
            _steel ??= Gfx.Tex(new StandardMaterial3D { AlbedoColor = new Color(.42f, .43f, .45f), Roughness = .5f, Metallic = .6f }, "noise", 2f);
            _rust ??= Gfx.Tex(new StandardMaterial3D { AlbedoColor = new Color(.35f, .22f, .14f), Roughness = .85f }, "grain", 1.5f);
            var sw = new Node3D { Position = new Vector3(0, -2.5f, 0), Rotation = new Vector3(.32f, 0, .2f) }; root.AddChild(sw);
            P(sw, new BoxMesh { Size = new Vector3(1.4f, 13f, .28f) }, _steel, new Vector3(0, 6.5f, 0), Vector3.Zero);
            P(sw, new PrismMesh { Size = new Vector3(1.4f, 1.2f, .28f) }, _steel, new Vector3(0, -.6f, 0), new Vector3(0, 0, Mathf.Pi));
            P(sw, new BoxMesh { Size = new Vector3(5f, .6f, .7f) }, _rust, new Vector3(0, 13.2f, 0), Vector3.Zero);
            P(sw, new CylinderMesh { TopRadius = .32f, BottomRadius = .32f, Height = 3.4f, RadialSegments = 10 }, _rust, new Vector3(0, 15.2f, 0), Vector3.Zero);
            P(sw, new SphereMesh { Radius = .6f, Height = 1.2f, RadialSegments = 10, Rings = 5 }, _steel, new Vector3(0, 17.2f, 0), Vector3.Zero);
            body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.4f, 13f, .5f) }, Transform = sw.Transform * new Transform3D(Basis.Identity, new Vector3(0, 6.5f, 0)) });
        }
        else
        {
            _bone ??= Gfx.Tex(new StandardMaterial3D { AlbedoColor = new Color(.78f, .74f, .64f), Roughness = .9f }, "noise", 1.5f);
            P(root, new SphereMesh { Radius = 2.2f, Height = 3.6f, RadialSegments = 14, Rings = 8 }, _bone, new Vector3(0, .9f, 7.5f), new Vector3(.3f, .4f, .2f));      // the skull, half sunk
            P(root, new BoxMesh { Size = new Vector3(2.6f, .6f, 1.6f) }, _bone, new Vector3(.4f, -.1f, 9f), new Vector3(.1f, .4f, .3f));                                 // the jaw
            body.AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = 2f }, Position = new Vector3(0, .9f, 7.5f) });
            for (int i = 0; i < 9; i++)
            {
                float z = 5f - i * 1.4f; P(root, new SphereMesh { Radius = .55f, Height = .9f, RadialSegments = 8, Rings = 4 }, _bone, new Vector3(0, .1f, z), Vector3.Zero);
                if (i is < 1 or > 7) continue;
                foreach (float sd in new[] { -1f, 1f })
                {   // a rib: two long capsules bent up into an arch
                    var cap = new CapsuleMesh { Radius = .22f, Height = 4.2f - Math.Abs(i - 4) * .35f, RadialSegments = 6, Rings = 1 };
                    float hh = cap.Height;
                    P(root, cap, _bone, new Vector3(sd * hh * .32f, hh * .38f, z), new Vector3(0, 0, -sd * .75f), new Vector3(.4f, hh, .4f));
                    P(root, new CapsuleMesh { Radius = .18f, Height = hh * .6f, RadialSegments = 6, Rings = 1 }, _bone, new Vector3(sd * hh * .55f, hh * .85f, z), new Vector3(0, 0, sd * .2f));
                }
            }
        }
    }

    /// <summary>Makes a site's meshes while a hero is within 170 m, frees them past 200 m.</summary>
    internal static void StreamTick(Vector3 hero)
    {
        var over = World.I?.Over; if (over == null) return;
        foreach (var s in Sites)
        {
            if (s.Dress == null) continue;
            float dx = s.X - hero.X, dz = s.Z - hero.Z, d2 = dx * dx + dz * dz;
            bool has = s.Node != null && GodotObject.IsInstanceValid(s.Node);
            if (!has && d2 < 170f * 170f && !TwinRealms.Dungeon.Active) { s.Node = new Node3D { Name = s.Id }; over.AddChild(s.Node); s.Dress(s.Node); }
            else if (has && d2 > 200f * 200f) { s.Node!.QueueFree(); s.Node = null; }
        }
    }

    // ---------------------------------------------------------------- dev
    static void Register()
    {
        if (_registered) return; _registered = true;
        Commands.Register("sites", "A-36: places to find off the roads: counts per land and kind; sites list [kind]; sites goto <n|kind> [k]", a =>
        {
            if (a.Length > 0 && a[0] == "list")
            {
                var l = Sites.Select((s, i) => (s, i)).Where(t => a.Length < 2 || t.s.Kind == a[1]);
                return string.Join("\n", l.Select(t => $"{t.i,3} {t.s.Id,-24} {t.s.Name,-20} {t.s.X,7:0},{t.s.Z,-7:0} y {t.s.By,3}{(t.s.Foes > 0 ? $"  foes {t.s.Foes}" : "")}"));
            }
            if (a.Length > 1 && a[0] == "goto")
            {
                Site? s = null;
                if (int.TryParse(a[1], out int n)) s = n >= 0 && n < Sites.Count ? Sites[n] : null;
                else { int k = a.Length > 2 && int.TryParse(a[2], out int kk) ? kk : 0; s = Sites.Where(x => x.Kind == a[1]).Skip(Math.Max(0, k)).FirstOrDefault(); }
                if (s == null) return "no such site (sites list)";
                if (Player.Instance is not { } p) return "no hero";
                var f = s.W(0, 12f); var from = World.OnGround(f.X, f.Y, .6f); p.Teleport(from, Mathf.Atan2(-(s.X - from.X), -(s.Z - from.Z)));
                return $"at {s.Id} ({s.Name}) {s.X:0},{s.Z:0}";
            }
            var sb = new StringBuilder();
            sb.AppendLine($"sites: {Sites.Count} places, {Sites.Sum(s => s.Foes)} monsters");
            sb.AppendLine("  all: " + string.Join(", ", Sites.GroupBy(s => s.Kind).Select(g => $"{g.Key} {g.Count()}")));
            foreach (var g in Sites.GroupBy(s => s.Land)) sb.AppendLine($"  {g.Key,-6} {g.Count(),3}: " + string.Join(", ", g.GroupBy(s => s.Kind).Select(x => $"{x.Key} {x.Count()}")));
            return sb.ToString().TrimEnd();
        });
    }
}

/// <summary>Streams the sites' meshes near the hero (WorldSites.StreamTick). Client only.</summary>
public partial class SiteField : Node
{
    double _t;
    public override void _Process(double delta)
    {
        _t -= delta; if (_t > 0) return; _t = .5;
        if (Player.Instance is { } p && GodotObject.IsInstanceValid(p)) WorldSites.StreamTick(p.GlobalPosition);
    }
}
