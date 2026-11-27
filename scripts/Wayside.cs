using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// F-33: block places to find along the roads between towns: camps, farms, watchtowers, ruins, shrines, inns and crypts (the small
/// dungeon: a mausoleum with a stair shaft down to a chamber). Numbers, kind cycles per land and the NPC pools live in data/wayside.json.
/// Build runs once per world build, right before Structures.Finish. Where the sites are follows only from the roads, the terrain and
/// distances (never from Structures.On / Structures.Free), so the online server, which has no block structures, still gets the same
/// sites and the same monsters. Blocks are stamped only where block structures are on; the people are client-local records added to
/// NpcDb (NpcDb.AddGenerated); the monsters are camp slots (Zones), numbered the same on every machine.
/// Loot: a site's loot chest is filled the first time a hero opens it (BlockUse.OpenChest -> FillChest), then it is an ordinary chest.
/// Dev command: "wayside", "wayside list", "wayside goto &lt;n|kind&gt; [k]".
/// </summary>
public static class Wayside
{
    public sealed class Site
    {
        public string Id = "", Land = "", Kind = "", Name = "", ChestKey = "";
        public int N, Bx, By, Bz, Rot, Foes; public float X, Z, GotoAdd; public Vector2 Road;
        public readonly List<string> Npcs = new();
        /// <summary>World point of a local spot (local +Z = the front, towards the road; local 0, 0 = the middle of the origin block). Y = NaN.</summary>
        public Vector3 W(float lx, float lz)
        {
            var (rx, rz) = (Rot & 3) switch { 0 => (lx, lz), 1 => (lz, -lx), 2 => (-lx, -lz), _ => (-lz, lx) };
            return new Vector3(Bx + .5f + rx, float.NaN, Bz + .5f + rz);
        }
        /// <summary>World cell of a local blueprint cell.</summary>
        public Vector3I Cell(int lx, int ly, int lz) { var (rx, rz) = Structures.Rot(lx, lz, Rot); return new Vector3I(Bx + rx, By + ly, Bz + rz); }
    }
    sealed class KindCfg { public string Name = ""; public float Radius = 7, GotoAdd; public int ChestTier = -1, FoeLo, FoeHi, FoeLevel; public string[] Npcs = Array.Empty<string>(); }

    public static readonly List<Site> Sites = new();
    static readonly Dictionary<string, int> _loot = new();   // chest key (BlockUse.Key) -> loot tier
    static int _itemsLo = 2, _itemsHi = 4, _goldLo = 15, _goldHi = 40, _goldTier = 30; static float _rarTier = .2f;
    static bool _registered;
    const string File = "res://data/wayside.json";
    const string AreaKey = "overworld";   // BlockArea.Key of the overworld (chest keys are "<area>|x,y,z")

    static byte I(string k) => Blocks.Id(k);
    static uint Fnv(string s) { uint h = 2166136261u; foreach (char c in s) { h ^= c; h *= 16777619u; } return h == 0 ? 1u : h; }
    static string[] Arr(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() ?? "" : "").ToArray() : Array.Empty<string>();
    static (int lo, int hi) Range(JsonElement e, string k, int lo, int hi)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() >= 2) { lo = a[0].GetInt32(); hi = Math.Max(lo, a[1].GetInt32()); }
        return (lo, hi);
    }
    static JsonElement Obj(JsonElement e, string k) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(k, out var o) && o.ValueKind == JsonValueKind.Object ? o : default;
    static float DistSeg(Vector2 q, Vector2 a, Vector2 b) { var ab = b - a; float t = Mathf.Clamp((q - a).Dot(ab) / Mathf.Max(ab.LengthSquared(), 1e-4f), 0, 1); return (a + ab * t - q).Length(); }

    // ---------------------------------------------------------------- the build
    public static void Build(Node3D over)
    {
        Register();
        Sites.Clear(); _loot.Clear(); NpcDb.ClearGenerated();
        if (!FileAccess.FileExists(File)) return;
        ulong t0 = Time.GetTicksMsec();
        using var doc = JsonUtil.Load(File); var r = doc.RootElement;
        float spacing = JsonUtil.F(r, "spacing", 170f), off = JsonUtil.F(r, "offset", 16f), gap = JsonUtil.F(r, "minGap", 60f);
        float clear = JsonUtil.F(r, "waterClear", .8f), slope = JsonUtil.F(r, "slope", 2.5f), slopeR = JsonUtil.F(r, "slopeR", 6f);
        var mg = Obj(r, "margins");
        float mTown = JsonUtil.F(mg, "town", 30f), mPoi = JsonUtil.F(mg, "poi", 14f), mPlace = JsonUtil.F(mg, "place", 22f), mRoad = JsonUtil.F(mg, "road", 9f), mBridge = JsonUtil.F(mg, "bridge", 26f);
        var lt = Obj(r, "loot");
        (_itemsLo, _itemsHi) = Range(lt, "items", 2, 4); (_goldLo, _goldHi) = Range(lt, "gold", 15, 40);
        _goldTier = (int)JsonUtil.F(lt, "goldPerTier", 30f); _rarTier = JsonUtil.F(lt, "rarityPerTier", .2f);
        var kinds = new Dictionary<string, KindCfg>();
        var kj = Obj(r, "kinds");
        if (kj.ValueKind == JsonValueKind.Object) foreach (var k in kj.EnumerateObject())
        {
            var (flo, fhi) = Range(k.Value, "foes", 0, 0);
            kinds[k.Name] = new KindCfg { Name = JsonUtil.S(k.Value, "name", k.Name), Radius = JsonUtil.F(k.Value, "radius", 7f), GotoAdd = JsonUtil.F(k.Value, "gotoAdd"), ChestTier = (int)JsonUtil.F(k.Value, "chestTier", -1f),
                FoeLo = flo, FoeHi = fhi, FoeLevel = (int)JsonUtil.F(k.Value, "foeLevel"), Npcs = Arr(k.Value, "npcs") };
        }
        var lands = Obj(r, "lands");

        // what a site keeps clear of: town zones, points of interest, the lands' places, sea bridges
        var keep = new List<(float x, float z, float r)>();
        foreach (var t in TownZones.All) keep.Add((t.X, t.Z, t.R + mTown));
        foreach (var p in World.Pois) if (!p.Sky) keep.Add((p.X, p.Z, p.R + mPoi));
        foreach (var L in Lands.All) foreach (var p in L.Places) keep.Add((p.X, p.Z, p.R + mPlace));

        var sets = new List<(string land, List<Vector2[]> roads)> { ("home", World.Roads) };
        foreach (var L in Lands.All) sets.Add((L.Id, L.Roads.Select(l => l.P).ToList()));

        foreach (var (land, roads) in sets)
        {
            var lc = Obj(lands, land); if (lc.ValueKind != JsonValueKind.Object) continue;
            var cycle = Arr(lc, "cycle").Where(kinds.ContainsKey).ToArray(); if (cycle.Length == 0) continue;
            float sp = Mathf.Max(20f, JsonUtil.F(lc, "spacing", spacing)); int n = 0, side = 1;

            bool Ok(float x, float z, float radius)
            {
                if (land == "home") { if (Mathf.Abs(x) > World.Half - 12 || Mathf.Abs(z) > World.Half - 12) return false; }
                else if (Lands.LandAt(x, z)?.Id != land) return false;
                foreach (var c in keep) if ((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < c.r * c.r) return false;
                foreach (var s in Sites) if ((x - s.X) * (x - s.X) + (z - s.Z) * (z - s.Z) < gap * gap) return false;
                float g0 = World.GroundAt(x, z); if (g0 < Lands.WaterAt(x, z) + clear) return false;
                for (int ring = 0; ring < 2; ring++)
                {
                    float rr = ring == 0 ? slopeR : radius, tol = ring == 0 ? slope : slope * 1.6f;
                    if (ring == 1 && radius <= slopeR) break;
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.Tau / 8f, sx = x + Mathf.Cos(a) * rr, sz = z + Mathf.Sin(a) * rr, g = World.GroundAt(sx, sz);
                        if (g < Lands.WaterAt(sx, sz) + clear || Mathf.Abs(g - g0) > tol) return false;
                    }
                }
                var q = new Vector2(x, z);
                foreach (var P in roads) for (int i = 0; i < P.Length - 1; i++) if (DistSeg(q, P[i], P[i + 1]) < mRoad) return false;
                foreach (var b in Lands.Bridges) if (DistSeg(q, b.A, b.B) < mBridge) return false;
                return true;
            }

            foreach (var P in roads)
            {
                if (P.Length < 2) continue;
                float next = sp * .5f, run = 0;
                for (int i = 0; i < P.Length - 1; i++)
                {
                    var ab = P[i + 1] - P[i]; float len = ab.Length(); if (len < .01f) continue;
                    var d = ab / len; var nrm = new Vector2(-d.Y, d.X);
                    while (next <= run + len)
                    {
                        var q = P[i] + d * (next - run); next += sp;
                        string kind = cycle[n % cycle.Length]; var kc = kinds[kind];
                        for (int tr = 0; tr < 2; tr++)
                        {
                            var c = q + nrm * off * (tr == 0 ? side : -side);
                            int bx = Mathf.FloorToInt(c.X), bz = Mathf.FloorToInt(c.Y); float cx = bx + .5f, cz = bz + .5f;
                            if (!Ok(cx, cz, kc.Radius)) continue;
                            Sites.Add(new Site { Id = $"way_{land}_{n}", Land = land, Kind = kind, Name = kc.Name, N = n, Bx = bx, Bz = bz, By = Structures.Floor(World.GroundAt(cx, cz)),
                                X = cx, Z = cz, Road = q, Rot = Structures.RotFacing(q.X - cx, q.Y - cz), GotoAdd = kc.GotoAdd });
                            n++; break;
                        }
                        side = -side;
                    }
                    run += len;
                }
            }
        }

        var roles = Obj(r, "roles"); var names = Obj(r, "names"); var inns = Arr(r, "innNames");
        for (int si = 0; si < Sites.Count; si++)
        {
            var s = Sites[si]; var kc = kinds[s.Kind]; uint seed = Fnv(s.Id);
            if (s.Kind == "inn" && inns.Length > 0) s.Name = inns[(int)(seed % (uint)inns.Length)];
            if (Structures.On) Stamp(s, kc, seed);
            if (!Net.Dedicated) People(s, kc, seed, roles, names);   // townsfolk are local to each player's game (Builder.Npcs)
            Foes(s, kc, seed, Arr(Obj(Obj(lands, s.Land), "foes"), s.Kind), 50000 + si);
        }
        GD.Print($"[WAYSIDE] {Sites.Count} sites ({string.Join(", ", Sites.GroupBy(s => s.Kind).Select(g => $"{g.Key} {g.Count()}"))}), {_loot.Count} loot chests, {Sites.Sum(s => s.Npcs.Count)} people, {Sites.Sum(s => s.Foes)} monsters, {Time.GetTicksMsec() - t0} ms");
    }

    // ---------------------------------------------------------------- blocks
    static void Stamp(Site s, KindCfg kc, uint seed)
    {
        (int x, int y, int z)? chest = null; Blueprint b;
        switch (s.Kind)
        {
            case "camp": b = Camp(seed); chest = (3, 0, -3); break;
            case "farm": b = Farm(s.Land, seed); break;
            case "watchtower": b = Watchtower(s.Land); chest = (1, 0, -1); break;
            case "ruin": b = RuinHouse(s.Land, seed); chest = (3, 0, -2); break;
            case "shrine": b = Shrine(s.Land); break;
            case "inn": b = Inn(s.Land, seed); break;
            case "crypt": b = Crypt(seed); chest = (3, -6, -11); break;
            default: return;
        }
        if (chest is { } c)
        {
            b.Set(c.x, c.y, c.z, I("chest"), 2); b.Set(c.x, c.y + 1, c.z, 0);   // faces the front; nothing on its lid
            if (kc.ChestTier >= 0) { var w = s.Cell(c.x, c.y, c.z); s.ChestKey = $"{AreaKey}|{w.X},{w.Y},{w.Z}"; _loot[s.ChestKey] = kc.ChestTier; }
        }
        Structures.Place(b, s.Bx, s.By, s.Bz, s.Rot);
        // F-27: the inn's and the farm's house, so their people sleep indoors (house centre in the site frame, half depth)
        if (s.Kind == "inn") Home(s, 0, -1, 4);
        else if (s.Kind == "farm") Home(s, -5, -1, 2);
    }
    static void Home(Site s, int cx, int cz, int hd)
    {
        var (px, pz) = Structures.Rot(cx, cz, s.Rot);
        NpcHomes.Add(s.Id, s.Bx, s.By, s.Bz, px, pz, s.Rot, hd);
    }

    /// <summary>A camp: a fire ring, two tents facing it, log seats behind the fire and a chest.</summary>
    static Blueprint Camp(uint seed)
    {
        var b = new Blueprint(); byte log = I("log");
        b.PadBox(-7, -5, 7, 5, 255, Blocks.Dirt, 5);
        b.Merge(StructGen.Campfire(), 0, 0, 0);
        b.Merge(StructGen.Tent(I((seed % 3) switch { 0 => "cloth_red", 1 => "cloth_blue", _ => "cloth_tan" }), 3), -4, 0, 0, 1);   // open side towards +X (the fire)
        b.Merge(StructGen.Tent(I("cloth_tan"), 3), 4, 0, 0, 3);
        for (int x = -1; x <= 1; x++) b.Set(x, 0, -3, log, 1);
        b.Set(-3, 0, -3, log);   // a stump
        return b;
    }

    /// <summary>A farm: a small house on the left, a fenced field of furrows on the right (gate at the front), hay bales.</summary>
    static Blueprint Farm(string land, uint seed)
    {
        var b = new Blueprint(); byte fence = I("fence"), rut = I("path_rut"), hay = I("thatch");
        b.PadBox(-11, -6, 10, 8, 255, Blocks.Dirt, 10);
        b.Merge(StructGen.House(StructGen.StyleFor(land, seed), 7, 5, 1, seed), -5, 0, -1);
        for (int z = -5; z <= 4; z++) for (int x = 1; x <= 9; x++)
        {
            bool edge = x == 1 || x == 9 || z == -5 || z == 4;
            if (!edge) b.Set(x, -1, z, x % 2 == 0 ? Blocks.Dirt : rut);
            else if (!(x == 5 && z == 4)) b.Set(x, 0, z, fence);
        }
        b.Set(2, 0, -4, hay); b.Set(3, 0, -4, hay); b.Set(2, 1, -4, hay);
        b.Set(8, 0, -4, hay);
        b.Set(0, 0, -3, hay); b.Set(0, 0, -2, hay); b.Set(0, 1, -3, hay);
        return b;
    }

    static byte TowerWall(string land) => I(land switch { "sun" => "sandstone", "ember" => "dark_stone", "mire" => "cobblestone", _ => "stone_bricks" });
    /// <summary>A watchtower (StructGen.Tower: door at the front, ladder to the roof deck) with a watch fire and a log seat beside it.</summary>
    static Blueprint Watchtower(string land)
    {
        var b = new Blueprint();
        b.PadBox(-4, -4, 8, 6, 255, Blocks.Dirt, 14);
        b.Merge(StructGen.Tower(5, 10, TowerWall(land)), 0, 0, 0);
        b.Merge(StructGen.Campfire(), 5, 0, 2);
        b.Set(5, 0, 5, I("log"), 1); b.Set(6, 0, 5, I("log"), 1);
        return b;
    }

    /// <summary>A stone house worn down by StructGen.Ruin, rubble round it (the loot chest is set by Stamp afterwards).</summary>
    static Blueprint RuinHouse(string land, uint seed)
    {
        var b = new Blueprint(); byte sb = I(land == "sun" ? "sandstone" : "stone_bricks"), cob = I("cobblestone"), moss = I("mossy_bricks");
        b.PadBox(-7, -6, 7, 7, 255, Blocks.Dirt, 9);
        var st = new StructGen.Style { Wall = sb, Upper = sb, Base = sb, Frame = sb, Roof = I("slate_stairs"), Ridge = I("slate_slab"), Floor = cob, Door = I("door_dark") };
        var h = StructGen.House(st, 9, 7, 1, seed); StructGen.Ruin(h, seed, 5);
        b.Merge(h, 0, 0, 0);
        var rubble = new[] { (-6, 2), (6, -1), (-5, 5), (5, 5), (2, 6), (-6, -4), (6, 3), (-2, -5) };
        for (int i = 0; i < rubble.Length; i++)
        {
            float q = StructGen.H01(i, 3, 0, seed); if (q > .62f) continue;
            b.Set(rubble[i].Item1, 0, rubble[i].Item2, q < .3f ? moss : cob);
        }
        return b;
    }

    /// <summary>A roadside shrine: a paved square, four pillars (torches on the front pair, a lintel across the back pair), a plinth with a rune stone.</summary>
    static Blueprint Shrine(string land)
    {
        var b = new Blueprint();
        byte stone = I(land switch { "sun" => "sandstone", "ember" => "dark_stone", _ => "stone_bricks" }), slab = I(land == "sun" ? "sandstone_slab" : "brick_slab");
        b.PadBox(-4, -4, 4, 5, 255, Blocks.Dirt, 7);
        b.PadBox(-3, -3, 3, 3, I("cobblestone"), Blocks.Dirt, 7);
        b.PadBox(-1, 4, 1, 5, I("path"), Blocks.Dirt, 7);
        foreach (int sx in new[] { -2, 2 }) foreach (int sz in new[] { -2, 2 })
        {
            b.Box(sx, 0, sz, sx, 2, sz, stone);
            if (sz > 0) b.Set(sx, 3, sz, I("torch"));
        }
        for (int x = -2; x <= 2; x++) b.Set(x, 3, -2, slab);
        b.Set(0, 0, 0, stone); b.Set(0, 1, 0, I("rune_stone")); b.Set(0, 2, 0, slab);
        b.Set(1, 0, 0, slab); b.Set(-1, 0, 0, slab); b.Set(0, 0, 1, slab); b.Set(0, 0, -1, slab);
        return b;
    }

    /// <summary>An inn: a two-floor house in the land's style, lamp posts by the path, a well on the left and a stall on the right.</summary>
    static Blueprint Inn(string land, uint seed)
    {
        var b = new Blueprint();
        b.PadBox(-14, -8, 14, 8, 255, Blocks.Dirt, 14);
        b.Merge(StructGen.House(StructGen.StyleFor(land, seed), 11, 9, 2, seed), 0, 0, -1);
        b.Merge(StructGen.LampPost(), -3, 0, 6); b.Merge(StructGen.LampPost(), 3, 0, 6);
        b.Merge(StructGen.Well(I(land == "sun" ? "sandstone" : "stone_bricks"), I("roof_slab")), -10, 0, 1);
        b.Merge(StructGen.Stall(I((seed % 3) switch { 0 => "awning_red", 1 => "awning_blue", _ => "awning_tan" }), true, seed), 10, 0, 1);
        return b;
    }

    /// <summary>
    /// The small dungeon. On the surface a mausoleum (inside x -1..2, z -3..2, a 2-wide doorway at the front). From the landing behind the
    /// doorway a 2-wide flight of six cobble stairs (x 0..1, z 1..-4) goes down towards the back, each step half a block, with four cells of
    /// air over every step, into a chamber (x -3..4, z -11..-5, floor 6 below the surface, 4 high) lined with stone and mossy bricks:
    /// wall torches, a sarcophagus, glowing rune stones in the back wall and the loot chest in the back right corner.
    /// </summary>
    static Blueprint Crypt(uint seed)
    {
        var b = new Blueprint(); byte sb = I("stone_bricks"), moss = I("mossy_bricks"), dark = I("dark_stone"), stairs = I("cobble_stairs"), torch = I("torch"), slab = I("cobble_slab");
        byte Wall(int x, int y, int z) => StructGen.H01(x, y, z, seed) < .4f ? moss : sb;
        byte Deep(int x, int y, int z) => StructGen.H01(x, y, z, seed) < .7f ? moss : sb;
        b.PadBox(-6, -14, 7, 7, 255, Blocks.Dirt, 7);
        b.PadBox(0, 4, 1, 6, I("cobblestone"), Blocks.Dirt, 7);
        // the mausoleum
        for (int y = 0; y <= 3; y++) for (int z = -4; z <= 3; z++) for (int x = -2; x <= 3; x++)
        {
            bool ex = x == -2 || x == 3, ez = z == -4 || z == 3;
            b.Set(x, y, z, ex && ez ? dark : ex || ez ? Wall(x, y, z) : (byte)0);
        }
        b.Box(-2, 4, -4, 3, 4, 3, sb); b.Box(-1, 5, -3, 2, 5, 2, slab);
        b.Box(-1, -1, -3, 2, -1, 2, sb);
        for (int x = 0; x <= 1; x++) { for (int y = 0; y <= 2; y++) b.Set(x, y, 3, 0); b.Set(x, 3, 3, dark); }
        b.Set(-1, 2, 4, torch, 1 + 3); b.Set(2, 2, 4, torch, 1 + 3);   // lanterns on the front wall
        // the chamber: a solid lined box, then its air
        for (int y = -7; y <= -2; y++) for (int z = -12; z <= -4; z++) for (int x = -4; x <= 5; x++) b.Set(x, y, z, Deep(x, y, z));
        b.Box(-3, -6, -11, 4, -3, -5, 0);
        // the stair shaft: lined sides and a bed under every step, the step, four cells of air above it
        for (int k = 0; k <= 5; k++)
        {
            int z = 1 - k;
            for (int x = -1; x <= 2; x++) for (int y = -2 - k; y <= -1; y++) b.Set(x, y, z, k == 0 || y == -1 && z >= -3 ? sb : Deep(x, y, z));
            for (int x = 0; x <= 1; x++)
            {
                b.Set(x, -1 - k, z, stairs, 2);   // rises towards the front (+Z)
                for (int y = -k; y <= -k + 3; y++) b.Set(x, y, z, 0);
            }
        }
        b.Set(0, -2, -2, torch, 1 + 1);   // half way down, on the left wall
        b.Set(-3, -4, -8, torch, 1 + 1); b.Set(4, -4, -8, torch, 1 + 0); b.Set(-2, -4, -11, torch, 1 + 3); b.Set(3, -4, -11, torch, 1 + 3);
        b.Set(0, -4, -12, I("rune_stone")); b.Set(1, -4, -12, I("rune_stone"));
        b.Set(0, -6, -8, dark); b.Set(1, -6, -8, dark); b.Set(0, -5, -8, slab); b.Set(1, -5, -8, slab);   // the sarcophagus
        return b;
    }

    // ---------------------------------------------------------------- people
    static NpcSpot At(Site s, float lx, float lz) { var w = s.W(lx, lz); return new NpcSpot { Abs = true, X = w.X, Z = w.Z }; }
    static void People(Site s, KindCfg kc, uint seed, JsonElement roles, JsonElement names)
    {
        bool house = Structures.On && NpcHomes.Of(s.Id) is { Count: > 0 };
        for (int slot = 0; slot < kc.Npcs.Length; slot++)
        {
            var alts = kc.Npcs[slot].Split('|', StringSplitOptions.RemoveEmptyEntries); if (alts.Length == 0) continue;
            string key = alts[(int)(StructGen.Hash(slot, 1, 0, seed) % (uint)alts.Length)];
            var rc = Obj(roles, key); if (rc.ValueKind != JsonValueKind.Object) continue;
            NpcSpot home, work;
            switch (s.Kind)
            {
                case "inn":
                    home = house ? new NpcSpot { Poi = s.Id, House = 0 } : At(s, slot * 1.5f, 5f);
                    work = slot == 0 ? (house ? new NpcSpot { Poi = s.Id, House = 0 } : At(s, 0, 5f)) : At(s, 5f, 5.5f);
                    break;
                case "farm": home = house ? new NpcSpot { Poi = s.Id, House = 0 } : At(s, -5f, 4f); work = At(s, 5f, 6f); break;
                case "watchtower": home = At(s, 0f, 3.5f); work = At(s, 1.5f, 4.5f); break;
                case "shrine": home = At(s, -1f, 4f); work = At(s, 1f, 4f); break;
                default: home = At(s, -2f, 1.5f); work = At(s, 1.5f, 2.5f); break;
            }
            Person(s, rc, names, StructGen.Hash(slot, 2, 0, seed), home, work);
        }
    }
    static string[] Some(JsonElement rc, string k, int n, uint h)
    {
        var pool = Arr(rc, k); if (pool.Length == 0) return new[] { "the road" };
        n = Math.Min(n, pool.Length); var o = new string[n]; int start = (int)(h % (uint)pool.Length);
        for (int i = 0; i < n; i++) o[i] = pool[(start + i) % pool.Length];
        return o;
    }
    static void Person(Site s, JsonElement rc, JsonElement names, uint h, NpcSpot home, NpcSpot work)
    {
        bool female = (h & 1) == 1;
        var first = Arr(names, female ? "f" : "m"); var sur = Arr(names, "surnames");
        string role = JsonUtil.S(rc, "role", "traveller");
        string name = (first.Length > 0 ? first[(int)((h >> 3) % (uint)first.Length)] : "Wanderer") + (sur.Length > 0 ? " " + sur[(int)((h >> 11) % (uint)sur.Length)] : "");
        if (role.Contains("warden")) name = "Warden " + name;
        var rec = new NpcRec
        {
            Generated = true, Id = $"{s.Id}_{JsonUtil.S(rc, "id", "npc")}", Name = name, Role = role, Talk = "talk", Area = "road", Town = "", Sex = female ? "f" : "m",
            Home = home, Work = work, Wander = JsonUtil.F(rc, "wander"), Outfit = JsonUtil.S(rc, "outfit", "traveller"), Color = JsonUtil.S(rc, "color"),
            Speech = Some(rc, "speech", 1, h >> 5)[0], Traits = Some(rc, "traits", 3, h >> 7), Likes = Some(rc, "likes", 3, h >> 9), Dislikes = Some(rc, "dislikes", 2, h >> 13), Greet = Some(rc, "greetings", 2, h >> 17),
        };
        if (rec.Speech == "the road") rec.Speech = "plain";
        if (NpcDb.Templates.TryGetValue(JsonUtil.S(rc, "schedule", "wild"), out var tpl) && tpl.Count > 0)
            foreach (var a in tpl) rec.Schedule.Add(new NpcAct { From = a.From, To = a.To, Act = a.Act, Place = a.Place });
        else
        {   // no such template: asleep at night, at the spot by day
            rec.Schedule.Add(new NpcAct { From = 22, To = 6, Act = "sleep", Place = "home" });
            rec.Schedule.Add(new NpcAct { From = 6, To = 22, Act = "work", Place = "work" });
        }
        if (NpcDb.AddGenerated(rec)) s.Npcs.Add(rec.Id);
    }

    // ---------------------------------------------------------------- monsters (ruins and crypts)
    static void Foes(Site s, KindCfg kc, uint seed, string[] types, int packId)
    {
        if (kc.FoeHi <= 0 || types.Length == 0) return;
        int count = kc.FoeLo + (int)(StructGen.Hash(5, 0, 0, seed) % (uint)(kc.FoeHi - kc.FoeLo + 1));
        if (count <= 0) return;
        Zones.Load();
        var camp = Zones.NewCamp(s.X, s.Z);   // streamed like the packs: nodes only while a hero is near (the same numbers on every machine)
        for (int i = 0; i < count; i++)
        {
            var w = s.W((i - (count - 1) / 2f) * 4f, kc.Radius * .5f + 3f + (i % 2) * 1.5f);   // in front, between the place and the road
            int lvl = Math.Max(1, Game.LevelAt(w.X, w.Z, 0) + kc.FoeLevel);
            Zones.AddSlot(camp, new CampSlot { Type = types[i % types.Length], Level = lvl, PackId = packId, AnyRealm = s.Land != "home", Pos = World.OnGround(w.X, w.Z, .3f) });
        }
        s.Foes = count;
    }

    // ---------------------------------------------------------------- loot
    /// <summary>BlockUse.OpenChest: the first time a hero opens a site's loot chest it is filled (rolled items, some gold). Once per hero and chest.</summary>
    public static void FillChest(string key)
    {
        if (!_loot.TryGetValue(key, out int tier)) return;
        var d = Game.D; d.Chests ??= new(); string mark = "wayside:" + key;   // an empty marker list: taken stays taken, even if the chest is dug out and set again
        if (d.Chests.ContainsKey(mark)) return;
        d.Chests[mark] = new List<Item>();
        var box = BlockUse.ChestItems(key);
        int n = _itemsLo + (int)(GD.Randi() % (uint)(_itemsHi - _itemsLo + 1));
        for (int i = 0; i < n; i++) box.Add(Items.Roll(d.Level + tier, tier * _rarTier));
        int gold = _goldLo + (int)(GD.Randi() % (uint)(_goldHi - _goldLo + 1)) + tier * _goldTier;
        d.Gold += gold; Sfx.Play("coin");
        Ui.Note($"The chest holds {n} items and {gold} gold", new Color(1f, .85f, .3f));
    }

    // ---------------------------------------------------------------- dev command
    static void Register()
    {
        if (_registered) return; _registered = true;
        Commands.Register("wayside", "F-33: places between towns: counts per land and kind (and the bridges); wayside list; wayside goto <n|kind> [k]: stand in front of site n (or the k-th of a kind)", a =>
        {
            if (a.Length == 0) return Report();
            if (a[0] == "list") return Sites.Count == 0 ? "no sites" : string.Join("\n", Sites.Select((s, i) =>
                $"{i,3} {s.Id,-14} {s.Kind,-10} {s.Name,-22} {s.X,7:0},{s.Z,-7:0} y {s.By,3} rot {s.Rot}{(s.Npcs.Count > 0 ? "  npcs " + string.Join(",", s.Npcs.Select(id => NpcDb.Get(id)?.Name ?? id)) : "")}{(s.ChestKey != "" ? "  loot chest" : "")}{(s.Foes > 0 ? $"  foes {s.Foes}" : "")}"));
            if (a[0] == "goto" && a.Length > 1)
            {
                Site? s = null;
                if (int.TryParse(a[1], out int n)) s = n >= 0 && n < Sites.Count ? Sites[n] : null;
                else { int k = a.Length > 2 && int.TryParse(a[2], out int kk) ? kk : 0; s = Sites.Where(x => x.Kind == a[1]).Skip(Math.Max(0, k)).FirstOrDefault(); }
                if (s == null) return "no such site (wayside list)";
                if (Player.Instance is not { } p) return "no hero";
                var f = s.W(0, 8f + s.GotoAdd); var from = World.OnGround(f.X, f.Z, .6f);
                float dx = s.X - from.X, dz = s.Z - from.Z;
                p.Teleport(from, Mathf.Atan2(-dx, -dz));
                return $"at {s.Id} ({s.Kind}, {s.Name}) {s.X:0},{s.Z:0}";
            }
            return "usage: /wayside | /wayside list | /wayside goto <n|kind> [k]";
        });
    }

    public static string Report()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"wayside: {Sites.Count} sites, {_loot.Count} loot chests, {Sites.Sum(s => s.Npcs.Count)} people, {Sites.Sum(s => s.Foes)} monsters; bridges: {Lands.BridgeSpots.Count} block bridges ({Lands.Bridges.Count} of them sea bridges)");
        sb.AppendLine("  all: " + string.Join(", ", Sites.GroupBy(s => s.Kind).OrderBy(g => g.Key).Select(g => $"{g.Key} {g.Count()}")));
        foreach (var g in Sites.GroupBy(s => s.Land))
            sb.AppendLine($"  {g.Key,-6} {g.Count(),2}: " + string.Join(", ", g.GroupBy(s => s.Kind).OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Count()}")));
        return sb.ToString().TrimEnd();
    }
}
