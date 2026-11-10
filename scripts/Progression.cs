using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One dungeon gear tier or monster upgrade from data/progression.json.</summary>
public class MobMod
{
    public string Id = "", Name = "", DmgType = "";
    public int Depth;
    public float HpP, DmgP, SpdP, CdP, WindP, Armor, Mr, Reach, XpP, LootBonus;
    public Dictionary<string, float> Res = new(), OnHit = new();
}

/// <summary>Levelling curve, level-up rewards and dungeon monster scaling (data/progression.json).</summary>
public static class Progression
{
    static bool _loaded;
    static float _xb = 40, _xl = 20, _xq = 6, _xc = .15f;
    public static int AttrPerLevel = 3, RankPerLevel = 1, TreePerLevel = 2;
    public static int DunOffset = 2, DunPerDepth = 1, DunBoss = 1;
    static float _upBase = 1, _upPerDepth = .5f; static int _upMax = 4, _upBoss = 2;
    public static readonly List<MobMod> Gear = new(), Upgrades = new();

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        var r = JsonUtil.Load("res://data/progression.json").RootElement;
        if (r.TryGetProperty("xp", out var x)) { _xb = JsonUtil.F(x, "base", _xb); _xl = JsonUtil.F(x, "lin", _xl); _xq = JsonUtil.F(x, "quad", _xq); _xc = JsonUtil.F(x, "cube", _xc); }
        if (r.TryGetProperty("levelUp", out var l)) { AttrPerLevel = (int)JsonUtil.F(l, "attr", 3); RankPerLevel = (int)JsonUtil.F(l, "rank", 1); TreePerLevel = (int)JsonUtil.F(l, "tree", 2); }
        if (!r.TryGetProperty("dungeon", out var d)) return;
        DunOffset = (int)JsonUtil.F(d, "levelOffset", 2); DunPerDepth = (int)JsonUtil.F(d, "perDepth", 1); DunBoss = (int)JsonUtil.F(d, "bossBonus", 1);
        if (d.TryGetProperty("gear", out var g)) foreach (var e in g.EnumerateArray()) Gear.Add(Mod(e));
        Gear.Sort((a, b) => a.Depth.CompareTo(b.Depth));
        if (d.TryGetProperty("upgrades", out var u))
        {
            _upBase = JsonUtil.F(u, "base", 1); _upPerDepth = JsonUtil.F(u, "perDepth", .5f); _upMax = (int)JsonUtil.F(u, "max", 4); _upBoss = (int)JsonUtil.F(u, "bossExtra", 2);
            if (u.TryGetProperty("list", out var ls)) foreach (var e in ls.EnumerateArray()) Upgrades.Add(Mod(e));
        }
    }

    static MobMod Mod(JsonElement e)
    {
        var m = new MobMod
        {
            Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name"), DmgType = JsonUtil.S(e, "dmgType"), Depth = (int)JsonUtil.F(e, "depth"),
            HpP = JsonUtil.F(e, "hpP"), DmgP = JsonUtil.F(e, "dmgP"), SpdP = JsonUtil.F(e, "spdP"), CdP = JsonUtil.F(e, "cdP"), WindP = JsonUtil.F(e, "windP"),
            Armor = JsonUtil.F(e, "armor"), Mr = JsonUtil.F(e, "mr"), Reach = JsonUtil.F(e, "reach"), XpP = JsonUtil.F(e, "xpP"), LootBonus = JsonUtil.F(e, "lootBonus"),
        };
        if (e.TryGetProperty("res", out var rs)) foreach (var p in rs.EnumerateObject()) m.Res[p.Name] = p.Value.GetSingle();
        if (e.TryGetProperty("onHit", out var oh)) foreach (var p in oh.EnumerateObject()) m.OnHit[p.Name] = p.Value.GetSingle();
        return m;
    }

    /// <summary>Experience needed to go from <paramref name="level"/> to the next: gentle for the first levels, then steeper.</summary>
    public static int XpNeed(int level)
    {
        Load(); float n = Math.Max(0, level - 1);
        return Math.Max(10, (int)Math.Round(_xb + _xl * n + _xq * n * n + _xc * n * n * n));
    }

    /// <summary>Monster level in a dungeon: your level + offset, deeper floors add more.</summary>
    public static int DungeonLevel(int heroLevel, int depth) { Load(); return Math.Max(1, heroLevel + DunOffset + DunPerDepth * Math.Max(0, depth - 1)); }

    /// <summary>The gear tier every monster on this depth wears (null when no tier applies).</summary>
    public static MobMod? GearFor(int depth) { Load(); MobMod? best = null; foreach (var g in Gear) if (g.Depth <= depth) best = g; return best; }

    /// <summary>Rolls this monster's upgrades: more of them the deeper you go, extra for bosses. Never the same one twice.</summary>
    public static List<MobMod> RollUpgrades(int depth, bool boss, Random rng)
    {
        Load(); var pool = new List<MobMod>(Upgrades); var res = new List<MobMod>();
        float want = _upBase + _upPerDepth * Math.Max(0, depth - 1); int n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
        n = Math.Min(n, _upMax) + (boss ? _upBoss : 0);
        while (n-- > 0 && pool.Count > 0) { int i = rng.Next(pool.Count); res.Add(pool[i]); pool.RemoveAt(i); }
        return res;
    }

    /// <summary>A copy of the shared enemy definition with gear and upgrades applied (the shared one is never touched).</summary>
    public static EnemyDef Apply(EnemyDef src, IEnumerable<MobMod> mods, out float xpMul, out float lootBonus)
    {
        var d = src.Clone(); xpMul = 1; lootBonus = 0;
        foreach (var m in mods)
        {
            d.Hp *= 1 + m.HpP / 100f; d.Dmg *= 1 + m.DmgP / 100f; d.Spd *= 1 + m.SpdP / 100f;
            d.Cd *= Mathf.Max(.3f, 1 + m.CdP / 100f); d.Wind *= Mathf.Max(.3f, 1 + m.WindP / 100f);
            d.Armor += m.Armor; d.Mr += m.Mr; if (d.Reach > 0) d.Reach *= 1 + m.Reach / 100f;
            if (m.DmgType != "") d.DmgType = m.DmgType;
            foreach (var kv in m.Res) { d.Res ??= new(); d.Res[kv.Key] = d.Res.GetValueOrDefault(kv.Key) + kv.Value; }
            foreach (var kv in m.OnHit) { d.OnHit ??= new(); d.OnHit[kv.Key] = Mathf.Min(1, d.OnHit.GetValueOrDefault(kv.Key) + kv.Value); }
            xpMul += m.XpP / 100f; lootBonus += m.LootBonus;
        }
        return d;
    }
}
