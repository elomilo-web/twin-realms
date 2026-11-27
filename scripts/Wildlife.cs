using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Places wildlife from data/wildlife.json: the home island's old herds ("herds": deer, rabbits, boars, shade stags, built
/// with the world), and the animals of every land by biome ("animals", A-21: herds kept as camp records and made into nodes only
/// while a hero is near, Zones.StreamTick). The animals themselves are Enemies with flee / neutral / hunting behaviour and material
/// drops defined in enemies.json; they respawn like other overworld creatures. Also the livestock helpers for ranching.</summary>
public static class Wildlife
{
    static Node3D? _over;

    public static void Spawn(Node3D over)
    {
        _over = over;
        Ranch.Spawner ??= SpawnLivestock;   // saved ranch animals and babies are made here (Ranch.cs)
        using var doc = JsonUtil.Load("res://data/wildlife.json"); var root = doc.RootElement;
        var rg = World.Rng((uint)JsonUtil.F(root, "seed", 5511)); float R(float a, float b) => a + rg() * (b - a);
        foreach (var h in root.GetProperty("herds").EnumerateArray())
        {
            string type = JsonUtil.S(h, "type"); int groups = (int)JsonUtil.F(h, "groups"); string where = JsonUtil.S(h, "where", "meadow");
            float minH = JsonUtil.F(h, "minH", 2), maxH = JsonUtil.F(h, "maxH", 30); int s0 = 1, s1 = 1;
            if (h.TryGetProperty("size", out var sz)) { s0 = sz[0].GetInt32(); s1 = sz[1].GetInt32(); }
            int placed = 0, tries = 0;
            while (placed < groups && tries++ < groups * 60)
            {
                float x = R(-World.Half + 20, World.Half - 20), z = R(-World.Half + 20, World.Half - 20), gh = World.GroundAt(x, z);
                if (gh < minH || gh > maxH || !World.FreeSpot(x, z, 6f)) continue;
                bool forest = World.Fbm(x * .02f + 3, z * .02f + 9, 3) >= .42f; if ((where == "forest") != forest) continue;
                if (new Vector2(x, z).Length() < 45f) continue; // keep the village outskirts calm
                int n = s0 + (int)(rg() * (s1 - s0 + 1));
                for (int i = 0; i < n; i++)
                {
                    float ax = x + R(-4, 4), az = z + R(-4, 4);
                    over.AddChild(new Enemy { Type = type, Level = Game.LevelAt(ax, az, 0), Position = World.OnGround(ax, az, 0.3f), Rotation = new Vector3(0, R(0, Mathf.Tau), 0) });
                }
                placed++;
            }
        }
        if (root.TryGetProperty("animals", out var an) && an.ValueKind == JsonValueKind.Array) SpawnAnimals(an, (uint)JsonUtil.F(root, "animalSeed", 7741), JsonUtil.F(root, "animalDensity", 1f));
    }

    /// <summary>The biome at a point: on a land its region's (or the land's own); on the home island meadow or forest.</summary>
    public static string BiomeAt(float x, float z)
    {
        var L = Lands.LandAt(x, z);
        if (L == null) return World.Fbm(x * .02f + 3, z * .02f + 9, 3) >= .42f ? "forest" : "meadow";
        string b = L.Default; float best = 1e9f;
        foreach (var rg in L.Regions) { float d = new Vector2(x - rg.x, z - rg.z).Length(); if (d < rg.r * .85f && rg.r < best) { best = rg.r; b = rg.b; } }
        return b;
    }

    /// <summary>
    /// "animals": { type (or types: a mixed herd), land ("home" or a land id from data/lands.json), biome ("any" or a biome id),
    /// groups, size [min, max], minH / maxH (ground height), shore (true: only within a few metres of the water line) }.
    /// Seeded, in file order, so every machine places the same herds (they are camp slots: World.NetEnemies numbers match).
    /// </summary>
    static void SpawnAnimals(JsonElement list, uint seed, float density)
    {
        var rg = World.Rng(seed); float R(float a, float b) => a + rg() * (b - a);
        var clear = new List<(float x, float z, float r)>();
        foreach (var p in World.Pois) if (p.Icon is "village" or "arena" or "city" or "castle" or "stilts") clear.Add((p.X, p.Z, p.R + 25));
        foreach (var L in Lands.All) foreach (var p in L.Places) if (p.Kind is "city" or "village" or "castle" or "stilts") clear.Add((p.X, p.Z, p.R + 30));
        int total = 0;
        foreach (var a in list.EnumerateArray())
        {
            var types = new List<string>();
            if (a.TryGetProperty("types", out var ts) && ts.ValueKind == JsonValueKind.Array) foreach (var t in ts.EnumerateArray()) types.Add(t.GetString() ?? "");
            else types.Add(JsonUtil.S(a, "type"));
            types.RemoveAll(t => !EnemyDb.Has(t)); if (types.Count == 0) continue;
            string land = JsonUtil.S(a, "land", "home"), biome = JsonUtil.S(a, "biome", "any"); bool home = land == "home", shore = a.TryGetProperty("shore", out var sh) && sh.ValueKind == JsonValueKind.True;
            Lands.Land? L = home ? null : Lands.All.Find(l => l.Id == land); if (!home && L == null) continue;
            int groups = Mathf.RoundToInt(JsonUtil.F(a, "groups", 4) * density), s0 = 1, s1 = 1;
            if (a.TryGetProperty("size", out var sz) && sz.ValueKind == JsonValueKind.Array && sz.GetArrayLength() >= 2) { s0 = sz[0].GetInt32(); s1 = sz[1].GetInt32(); }
            float minH = JsonUtil.F(a, "minH", shore ? .6f : 2f), maxH = JsonUtil.F(a, "maxH", shore ? 3f : 60f);
            float cx = home ? 0 : L!.Cx, cz = home ? 0 : L!.Cz, half = (home ? World.Half : L!.Half) - 20;
            int placed = 0, tries = 0;
            while (placed < groups && tries++ < groups * 80)
            {
                float x = cx + R(-half, half), z = cz + R(-half, half), gh = World.GroundAt(x, z);
                if (gh < minH || gh > maxH) continue;
                if (home) { if (Lands.LandAt(x, z) != null || !World.FreeSpot(x, z, 6f) || new Vector2(x, z).Length() < 55f) continue; }
                else if (Lands.LandAt(x, z) != L || gh < Lands.WaterAt(x, z) + .4f) continue;
                if (biome != "any" && BiomeAt(x, z) != biome) continue;
                bool blocked = false; foreach (var c in clear) if ((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < c.r * c.r) { blocked = true; break; }
                if (blocked) continue;
                var camp = Zones.NewCamp(x, z); int n = s0 + (int)(rg() * (s1 - s0 + 1)); placed++;
                for (int i = 0; i < n; i++)
                {
                    float ax = x + R(-5, 5), az = z + R(-5, 5); string type = types[(int)(rg() * types.Count) % types.Count];
                    if (World.GroundAt(ax, az) < minH - .5f) { ax = x; az = z; }
                    Zones.AddSlot(camp, new CampSlot { Type = type, Level = Game.LevelAt(ax, az, 0), AnyRealm = !home, Pos = World.OnGround(ax, az, .3f) }); total++;
                }
            }
        }
        GD.Print($"[WILDLIFE] {total} animals in herds (streamed near heroes)");
    }

    // ------------------------------------------------------------------ livestock (ranching, A-25)
    /// <summary>Is this enemy type a farm animal (data/enemies.json "livestock")?</summary>
    public static bool IsLivestock(string type) => EnemyDb.Has(type) && EnemyDb.Get(type).Livestock;
    public static bool IsLivestock(Enemy? e) => e != null && e.IsLivestock;
    /// <summary>Every living farm animal in the scene right now.</summary>
    public static IEnumerable<Enemy> Livestock(SceneTree tree)
    {
        foreach (var n in tree.GetNodesInGroup("livestock")) if (n is Enemy e && GodotObject.IsInstanceValid(e) && !e.Dead) yield return e;
    }
    /// <summary>A farm animal of this kind standing at a point (Ranch.Spawner): not part of any wild herd, not networked, never leashed.</summary>
    public static ILivestock? SpawnLivestock(string kind, Vector3 at, bool baby)
    {
        Node? parent = _over != null && GodotObject.IsInstanceValid(_over) ? _over : Game.Root;
        if (parent == null || !IsLivestock(kind)) return null;
        var e = new Enemy { Type = kind, Level = Game.LevelAt(at.X, at.Z, 0), AnyRealm = true, NoLeash = true, Position = at, Rotation = new Vector3(0, GD.Randf() * Mathf.Tau, 0) };
        parent.AddChild(e); e.Baby = baby; return e;
    }
}
