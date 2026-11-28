using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Building blocks from monsters (focus F-44; data/blockdrops.json, format in its "_help"). On the online server nobody mines, so kills
/// hand out blocks instead: tables by biome (where the monster stood), realm, monster type and kind. Active only on the online server
/// and for its players (their dungeon kills too) unless the data says "always". The blocks ride the normal kill rewards as materials
/// "blk_&lt;key&gt;" (Enemy.Die: the mats string of Net.KillRewards / DunKill, or GatherDb.Give for a private kill).
/// </summary>
public static class BlockDrops
{
    sealed class Drop { public string Block = ""; public int Lo = 1, Hi = 1; public float W = 1; }
    sealed class Mob { public int Rolls; public List<Drop> Drops = new(); }
    static readonly Dictionary<string, List<Drop>> _biomes = new(), _realms = new();
    static readonly Dictionary<string, Mob> _mobs = new();
    static string _mode = "online"; static float _chance = .7f; static int _rolls = 2, _bossRolls = 10; static bool _loaded;
    public static readonly List<string> Problems = new();

    public static bool Active { get { Load(); return _mode == "always" || (_mode == "online" && (Net.Dedicated || (Game.Online && Net.IsClient))); } }

    static List<Drop> ReadList(JsonElement arr, string where)
    {
        var l = new List<Drop>(); if (arr.ValueKind != JsonValueKind.Array) return l;
        foreach (var e in arr.EnumerateArray())
        {
            var d = new Drop { Block = JsonUtil.S(e, "block"), W = Mathf.Max(0, JsonUtil.F(e, "w", 1)) };
            if (e.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Array && n.GetArrayLength() > 0) { d.Lo = n[0].GetInt32(); d.Hi = n.GetArrayLength() > 1 ? n[1].GetInt32() : d.Lo; }
            if (d.Hi < d.Lo) d.Hi = d.Lo;
            if (!Blocks.Exists(d.Block)) { Problems.Add($"{where}: no block '{d.Block}'"); continue; }
            if (d.W > 0 && d.Hi > 0) l.Add(d);
        }
        return l;
    }
    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        try
        {
            using var doc = JsonUtil.Load("res://data/blockdrops.json"); var r = doc.RootElement;
            _mode = JsonUtil.S(r, "active", "online"); _chance = JsonUtil.F(r, "chance", .7f); _rolls = (int)JsonUtil.F(r, "rolls", 2); _bossRolls = (int)JsonUtil.F(r, "bossRolls", 10);
            if (r.TryGetProperty("biomes", out var b)) foreach (var p in b.EnumerateObject()) _biomes[p.Name] = ReadList(p.Value, "biome " + p.Name);
            if (r.TryGetProperty("realms", out var rl)) foreach (var p in rl.EnumerateObject()) _realms[p.Name] = ReadList(p.Value, "realm " + p.Name);
            if (r.TryGetProperty("mobs", out var m)) foreach (var p in m.EnumerateObject())
                _mobs[p.Name] = new Mob { Rolls = (int)JsonUtil.F(p.Value, "rolls", 0), Drops = p.Value.TryGetProperty("drops", out var dl) ? ReadList(dl, "mob " + p.Name) : new() };
        }
        catch (Exception e) { Problems.Add("blockdrops.json: " + e.Message); }
        foreach (var p in Problems) GD.PushWarning("[BLOCKDROPS] " + p);
    }

    /// <summary>The blocks one kill gives: block key -> amount (empty when the kill drops none). rnd: 0..1 numbers (tests pass a fixed sequence).</summary>
    public static Dictionary<string, int> Pick(string type, string kind, bool boss, int realm, string biome, Func<float>? rnd = null)
    {
        Load(); rnd ??= GD.Randf; var got = new Dictionary<string, int>();
        if (!boss && rnd() > _chance) return got;
        var pool = new List<Drop>(); int rolls = _rolls + (boss ? _bossRolls : 0);
        pool.AddRange(_biomes.TryGetValue(biome, out var bl) && bl.Count > 0 ? bl : _biomes.GetValueOrDefault("default") ?? new());
        if (_realms.TryGetValue(realm.ToString(), out var rl)) pool.AddRange(rl);
        if (_mobs.TryGetValue("kind:" + kind, out var km)) { pool.AddRange(km.Drops); rolls += km.Rolls; }
        if (_mobs.TryGetValue(type, out var tm)) { pool.AddRange(tm.Drops); rolls += tm.Rolls; }
        float total = 0; foreach (var d in pool) total += d.W;
        if (total <= 0) return got;
        for (int i = 0; i < rolls; i++)
        {
            float t = rnd() * total; Drop pick = pool[^1];
            foreach (var d in pool) { t -= d.W; if (t <= 0) { pick = d; break; } }
            int n = pick.Lo + (int)(rnd() * (pick.Hi - pick.Lo + 1)); n = Math.Clamp(n, pick.Lo, pick.Hi);
            if (n > 0) got[pick.Block] = got.GetValueOrDefault(pick.Block) + n;
        }
        return got;
    }

    /// <summary>Enemy.Die: adds this kill's blocks to the shared rewards (netKill) or gives them to the local hero.</summary>
    public static void Roll(Enemy e, bool netKill, StringBuilder mats)
    {
        if (!Net.Dedicated) WeaponFinds.OnKill(e, netKill);   // A-35: named weapons and odd finds from this kind of monster (data/weapon_finds.json)
        if (!Active || (!netKill && Net.Dedicated)) return;   // the server itself has no pack
        var at = e.GlobalPosition;
        foreach (var (block, n) in Pick(e.Type, e.Def.Kind, e.Def.Boss, e.Def.Realm, FishDb.Biome(at.X, at.Z)))
        {
            if (netKill) mats.Append(Blocks.Item(block)).Append(':').Append(n).Append(';'); else GatherDb.Give(Blocks.Item(block), n, at);
        }
    }
}
