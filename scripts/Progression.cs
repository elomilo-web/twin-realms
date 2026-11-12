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
    public static float PartyXpPool = .9f, PartyDropMul = 1.15f; public static int PartyDunOffset = 3;
    /// <summary>Each member's share of a party kill's experience: the pool split evenly (solo: all of it).</summary>
    public static int PartyShare(int xp, int members) { Load(); return members > 1 ? Math.Max(1, (int)Math.Round(xp * PartyXpPool / members)) : xp; }
    /// <summary>Monster level of a party's shared dungeon: average party level + offset, deeper floors add more.</summary>
    public static int PartyDungeonLevel(float avgLevel, int depth) { Load(); return Math.Max(1, (int)Math.Round(avgLevel) + PartyDunOffset + DunPerDepth * Math.Max(0, depth - 1) + _tierLevels * Tier(depth)); }
    static float _upBase = 1, _upPerDepth = .5f; static int _upMax = 4, _upBoss = 2;
    public static readonly List<MobMod> Gear = new(), Upgrades = new();
    // monster curve and power floor (progression.json "scaling")
    static float _hpLin = .2f, _hpExp = 1.045f, _dmgLin = .13f, _dmgExp = 1.04f;
    static bool _powerOn = true; static float _refHp = 50, _hpHits = 5, _refDmg = 10, _hitPct = .06f, _shape = .75f, _defRespect = .5f, _maxHitPct = .45f;
    static float _armK = 14, _armPerLvl = 6;
    // overworld follow, party multiplier, dungeon tiers
    static bool _owFollow = true; static float _owOffset, _owArea = .5f; static int _owBoss = 3;
    public static float PartyStatMul = 1.5f; static int _partyMin = 2;
    public static int DunMobMul = 3; static int _tierEvery = 5, _tierLevels = 2, _tierUps = 1; static float _tierHp = 1.7f, _tierDmg = 1.4f;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        var r = JsonUtil.Load("res://data/progression.json").RootElement;
        if (r.TryGetProperty("xp", out var x)) { _xb = JsonUtil.F(x, "base", _xb); _xl = JsonUtil.F(x, "lin", _xl); _xq = JsonUtil.F(x, "quad", _xq); _xc = JsonUtil.F(x, "cube", _xc); }
        if (r.TryGetProperty("levelUp", out var l)) { AttrPerLevel = (int)JsonUtil.F(l, "attr", 3); RankPerLevel = (int)JsonUtil.F(l, "rank", 1); TreePerLevel = (int)JsonUtil.F(l, "tree", 2); }
        if (r.TryGetProperty("party", out var pa)) { PartyXpPool = JsonUtil.F(pa, "xpPool", .9f); PartyDropMul = JsonUtil.F(pa, "dropMul", 1.15f); PartyDunOffset = (int)JsonUtil.F(pa, "dungeonOffset", 3); PartyStatMul = JsonUtil.F(pa, "statMul", 1.5f); _partyMin = (int)JsonUtil.F(pa, "minPlayers", 2); }
        if (r.TryGetProperty("scaling", out var s))
        {
            _hpLin = JsonUtil.F(s, "hpLin", _hpLin); _hpExp = JsonUtil.F(s, "hpExp", _hpExp); _dmgLin = JsonUtil.F(s, "dmgLin", _dmgLin); _dmgExp = JsonUtil.F(s, "dmgExp", _dmgExp);
            if (s.TryGetProperty("power", out var p))
            {
                _powerOn = !p.TryGetProperty("on", out var on) || on.ValueKind != JsonValueKind.False;
                _refHp = JsonUtil.F(p, "refHp", _refHp); _hpHits = JsonUtil.F(p, "hpHits", _hpHits); _refDmg = JsonUtil.F(p, "refDmg", _refDmg); _hitPct = JsonUtil.F(p, "hitPct", _hitPct);
                _shape = JsonUtil.F(p, "shape", _shape); _defRespect = JsonUtil.F(p, "defenseRespect", _defRespect); _maxHitPct = JsonUtil.F(p, "maxHitPct", _maxHitPct);
            }
            if (s.TryGetProperty("armor", out var a)) { _armK = JsonUtil.F(a, "k", _armK); _armPerLvl = JsonUtil.F(a, "perLevel", _armPerLvl); }
        }
        if (r.TryGetProperty("overworld", out var o)) { _owFollow = !o.TryGetProperty("follow", out var fo) || fo.ValueKind != JsonValueKind.False; _owOffset = JsonUtil.F(o, "offset"); _owArea = JsonUtil.F(o, "areaWeight", .5f); _owBoss = (int)JsonUtil.F(o, "bossBonus", 3); }
        if (!r.TryGetProperty("dungeon", out var d)) return;
        DunOffset = (int)JsonUtil.F(d, "levelOffset", 2); DunPerDepth = (int)JsonUtil.F(d, "perDepth", 1); DunBoss = (int)JsonUtil.F(d, "bossBonus", 1);
        DunMobMul = Math.Max(1, (int)JsonUtil.F(d, "mobMul", 3));
        if (d.TryGetProperty("tiers", out var t)) { _tierEvery = Math.Max(1, (int)JsonUtil.F(t, "every", 5)); _tierHp = JsonUtil.F(t, "hpMul", 1.7f); _tierDmg = JsonUtil.F(t, "dmgMul", 1.4f); _tierLevels = (int)JsonUtil.F(t, "levels", 2); _tierUps = (int)JsonUtil.F(t, "extraUpgrades", 1); }
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

    /// <summary>Monster level in a dungeon: your level + offset, deeper floors add more, and every tier (5 floors) jumps further.</summary>
    public static int DungeonLevel(int heroLevel, int depth) { Load(); return Math.Max(1, heroLevel + DunOffset + DunPerDepth * Math.Max(0, depth - 1) + _tierLevels * Tier(depth)); }

    // ------------------------------------------------------------------ monster scaling
    /// <summary>Dungeon tier: floors 1-5 are tier 0, 6-10 tier 1, and so on (progression.json dungeon.tiers.every).</summary>
    public static int Tier(int depth) { Load(); return Math.Max(0, depth - 1) / _tierEvery; }
    public static float TierHp(int depth) => Mathf.Pow(_tierHp, Tier(depth));
    public static float TierDmg(int depth) => Mathf.Pow(_tierDmg, Tier(depth));
    public static int TierUpgrades(int depth) => _tierUps * Tier(depth);

    /// <summary>Health factor at a monster level: linear growth times a gentle exponential, so it keeps bending upward.</summary>
    public static float HpCurve(int lvl) { Load(); float n = Math.Max(0, lvl - 1); return (1 + _hpLin * n) * Mathf.Pow(_hpExp, n); }
    public static float DmgCurve(int lvl) { Load(); float n = Math.Max(0, lvl - 1); return (1 + _dmgLin * n) * Mathf.Pow(_dmgExp, n); }

    /// <summary>Share of a blow the hero takes through armor: K / (K + armor), K grows with the attacker's level.</summary>
    public static float ArmorTaken(float armor, int attackerLvl) { Load(); float k = _armK + _armPerLvl * Math.Max(1, attackerLvl); return k / (k + Math.Max(0, armor)); }

    /// <summary>Health floor from the hero's power: at least hpHits x their weapon hit, more for tough monster types.</summary>
    public static float PowerHp(float baseHp, float heroHit) { Load(); if (!_powerOn || baseHp <= 0) return 0; return _hpHits * heroHit * Mathf.Pow(baseHp / _refHp, _shape); }
    /// <summary>Damage floor from the hero's health: every blow lands for a share of it after armor (armor is half credited back).</summary>
    public static float PowerDmg(float baseDmg, float heroMaxHp, float heroTaken)
    {
        Load(); if (!_powerOn || baseDmg <= 0) return 0;
        float pct = Mathf.Min(_maxHitPct, _hitPct * Mathf.Pow(baseDmg / _refDmg, _shape));
        return pct * heroMaxHp / Mathf.Pow(Mathf.Max(.05f, heroTaken), 1 - _defRespect);
    }

    /// <summary>The level monsters measure against: your own, or in multiplayer the average of everyone in the world.</summary>
    public static float RefLevel()
    {
        if (Net.Active && Net.I!.Players.Count > 1) { float s = 0; foreach (var p in Net.I.Players.Values) s += p.Level; return s / Net.I.Players.Count; }
        return Game.D.Level;
    }
    /// <summary>Overworld monster level: the area's level or the heroes' level (plus a little for remote areas), whichever is higher.</summary>
    public static int OverworldLevel(int areaLvl, bool boss)
    {
        Load(); if (!_owFollow) return areaLvl;
        int follow = Mathf.RoundToInt(RefLevel() + _owOffset + Math.Max(0, areaLvl - 1) * _owArea);
        return Math.Max(areaLvl, follow) + (boss ? _owBoss : 0);
    }
    /// <summary>Monsters are this much tougher while enough heroes share the world (multiplayer).</summary>
    public static float PartyMul() { Load(); return Net.Active && Net.I!.Players.Count >= _partyMin ? PartyStatMul : 1f; }

    /// <summary>The gear tier every monster on this depth wears (null when no tier applies).</summary>
    public static MobMod? GearFor(int depth) { Load(); MobMod? best = null; foreach (var g in Gear) if (g.Depth <= depth) best = g; return best; }

    /// <summary>Rolls this monster's upgrades: more of them the deeper you go, extra for bosses. Never the same one twice.</summary>
    public static List<MobMod> RollUpgrades(int depth, bool boss, Random rng)
    {
        Load(); var pool = new List<MobMod>(Upgrades); var res = new List<MobMod>();
        float want = _upBase + _upPerDepth * Math.Max(0, depth - 1); int n = (int)want + (rng.NextDouble() < want - (int)want ? 1 : 0);
        n = Math.Min(n, _upMax) + (boss ? _upBoss : 0) + TierUpgrades(depth);
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
