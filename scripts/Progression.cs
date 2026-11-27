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
    /// <summary>A-15: tree points per level went up (2 -> 3) with the advanced tree. A hero saved with a tree layout older than
    /// TreeRetroBefore gets TreeRetroPerLevel points for every level already gained, once, when the tree is refunded (Game.Load).</summary>
    public static int TreeRetroPerLevel, TreeRetroBefore;
    public static int TreeRetro(int savedTreeVer, int level) { Load(); return savedTreeVer < TreeRetroBefore ? TreeRetroPerLevel * Math.Max(0, level - 1) : 0; }
    public static int DunOffset = 2, DunPerDepth = 1, DunBoss = 1;
    public static float PartyXpPool = .9f, PartyDropMul = 1.15f; public static int PartyDunOffset = 3;
    /// <summary>Each member's share of a party kill's experience: the pool split evenly (solo: all of it).</summary>
    public static int PartyShare(int xp, int members) { Load(); return members > 1 ? Math.Max(1, (int)Math.Round(xp * PartyXpPool / members)) : xp; }
    /// <summary>Monster level of a party's shared dungeon: average party level + offset, deeper floors add more.</summary>
    public static int PartyDungeonLevel(float avgLevel, int depth) { Load(); return Math.Max(1, (int)Math.Round(avgLevel) + PartyDunOffset + DunPerDepth * Math.Max(0, depth - 1) + _tierLevels * Tier(depth)); }
    static float _upBase = 1, _upPerDepth = .5f; static int _upMax = 4, _upBoss = 2;
    public static readonly List<MobMod> Gear = new(), Upgrades = new();
    // monster scaling (progression.json "scaling"): measured against a reference hero of the monster's level, so nothing compounds
    static float _hit0 = 12, _hitPer = 7.5f, _hp0 = 110, _hpPer = 50;                       // the reference hero: weapon hit and max health by level
    static float _refHp = 50, _hpHits = 4.5f, _shape = .7f, _bossFrom = 580, _refBossHp = 1500, _bossHits = 80, _bossShape = .35f;
    static float _refDmg = 10, _hitPct = .065f, _dmgShape = .6f, _maxHitPct = .2f;
    static bool _powerOn = true; static float _hpFloor = .6f, _dmgFloor = .5f;
    static float _armK = 14, _armPerLvl = 6;
    // overworld follow, party multiplier, dungeon tiers
    static bool _owFollow; static float _owOffset, _owArea = .5f; static int _owBoss = 3;
    public static float PartyStatMul = 1.5f; static int _partyMin = 2;
    public static int DunMobMul = 3; static int _tierEvery = 5, _tierLevels = 2, _tierUps = 1; static float _tierHp = 1.7f, _tierDmg = 1.4f;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        var r = JsonUtil.Load("res://data/progression.json").RootElement;
        if (r.TryGetProperty("xp", out var x)) { _xb = JsonUtil.F(x, "base", _xb); _xl = JsonUtil.F(x, "lin", _xl); _xq = JsonUtil.F(x, "quad", _xq); _xc = JsonUtil.F(x, "cube", _xc); }
        if (r.TryGetProperty("levelUp", out var l)) { AttrPerLevel = (int)JsonUtil.F(l, "attr", 3); RankPerLevel = (int)JsonUtil.F(l, "rank", 1); TreePerLevel = (int)JsonUtil.F(l, "tree", 2); TreeRetroPerLevel = (int)JsonUtil.F(l, "treeRetroPerLevel", 0); TreeRetroBefore = (int)JsonUtil.F(l, "treeRetroBeforeVersion", 0); }
        if (r.TryGetProperty("party", out var pa)) { PartyXpPool = JsonUtil.F(pa, "xpPool", .9f); PartyDropMul = JsonUtil.F(pa, "dropMul", 1.15f); PartyDunOffset = (int)JsonUtil.F(pa, "dungeonOffset", 3); PartyStatMul = JsonUtil.F(pa, "statMul", 1.5f); _partyMin = (int)JsonUtil.F(pa, "minPlayers", 2); }
        if (r.TryGetProperty("scaling", out var s))
        {
            if (s.TryGetProperty("hero", out var h)) { _hit0 = JsonUtil.F(h, "hit", _hit0); _hitPer = JsonUtil.F(h, "hitPerLevel", _hitPer); _hp0 = JsonUtil.F(h, "hp", _hp0); _hpPer = JsonUtil.F(h, "hpPerLevel", _hpPer); }
            if (s.TryGetProperty("health", out var hh))
            {
                _refHp = JsonUtil.F(hh, "refHp", _refHp); _hpHits = JsonUtil.F(hh, "hits", _hpHits); _shape = JsonUtil.F(hh, "shape", _shape);
                _bossFrom = JsonUtil.F(hh, "bossFrom", _bossFrom); _refBossHp = JsonUtil.F(hh, "refBossHp", _refBossHp); _bossHits = JsonUtil.F(hh, "bossHits", _bossHits); _bossShape = JsonUtil.F(hh, "bossShape", _bossShape);
            }
            if (s.TryGetProperty("damage", out var dd)) { _refDmg = JsonUtil.F(dd, "refDmg", _refDmg); _hitPct = JsonUtil.F(dd, "hitPct", _hitPct); _dmgShape = JsonUtil.F(dd, "shape", _dmgShape); _maxHitPct = JsonUtil.F(dd, "maxHitPct", _maxHitPct); }
            if (s.TryGetProperty("power", out var p))
            {
                _powerOn = !p.TryGetProperty("on", out var on) || on.ValueKind != JsonValueKind.False;
                _hpFloor = JsonUtil.F(p, "hpFloor", _hpFloor); _dmgFloor = JsonUtil.F(p, "dmgFloor", _dmgFloor);
            }
            if (s.TryGetProperty("armor", out var a)) { _armK = JsonUtil.F(a, "k", _armK); _armPerLvl = JsonUtil.F(a, "perLevel", _armPerLvl); }
        }
        if (r.TryGetProperty("overworld", out var o)) { _owFollow = o.TryGetProperty("follow", out var fo) && fo.ValueKind == JsonValueKind.True; _owOffset = JsonUtil.F(o, "offset"); _owArea = JsonUtil.F(o, "areaWeight", .5f); _owBoss = (int)JsonUtil.F(o, "bossBonus", 3); }
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

    // Monster health and damage are measured against a reference hero of the monster's level (progression.json scaling.hero: weapon hit and
    // max health, both linear in level like a real hero's). Health = hits x reference hit; damage = share x reference health. The old rule
    // (base x (1 + lin*n) x exp^n, then a floor of the hero's own power, then tier x gear x upgrades x party x threat) compounded: a level 49
    // dragon had 368,245 health (1000+ hits) and hit for 28% of a hero's health.
    /// <summary>The reference hero's weapon hit at a level.</summary>
    public static float RefHit(int lvl) { Load(); return _hit0 + _hitPer * Math.Max(0, lvl - 1); }
    /// <summary>The reference hero's max health at a level.</summary>
    public static float RefHeroHp(int lvl) { Load(); return _hp0 + _hpPer * Math.Max(0, lvl - 1); }
    /// <summary>Hits from the reference hero a monster of this base health takes: tougher kinds take more, bosses (base health from bossFrom up) on their own scale.</summary>
    public static float Hits(float baseHp)
    {
        Load(); if (baseHp <= 0) return 0;
        return baseHp >= _bossFrom ? _bossHits * Mathf.Pow(baseHp / _refBossHp, _bossShape) : _hpHits * Mathf.Pow(baseHp / _refHp, _shape);
    }
    /// <summary>Share of the reference hero's max health one blow deals (before the hero's armor).</summary>
    public static float HitPct(float baseDmg) { Load(); return baseDmg <= 0 ? 0 : Mathf.Min(_maxHitPct, _hitPct * Mathf.Pow(baseDmg / _refDmg, _dmgShape)); }
    public static float EnemyHp(float baseHp, int lvl) => Hits(baseHp) * RefHit(lvl);
    public static float EnemyDmg(float baseDmg, int lvl) => HitPct(baseDmg) * RefHeroHp(lvl);
    /// <summary>Growth factor with level (level 1 = 1): linear, like the hero's.</summary>
    public static float HpCurve(int lvl) => RefHit(lvl) / RefHit(1);
    public static float DmgCurve(int lvl) => RefHeroHp(lvl) / RefHeroHp(1);

    /// <summary>Share of a blow the hero takes through armor: K / (K + armor), K grows with the attacker's level.</summary>
    public static float ArmorTaken(float armor, int attackerLvl) { Load(); float k = _armK + _armPerLvl * Math.Max(1, attackerLvl); return k / (k + Math.Max(0, armor)); }

    /// <summary>Health floor from the hero's own power: a far over-geared hero still needs hpFloor x the usual hits.</summary>
    public static float PowerHp(float baseHp, float heroHit) { Load(); if (!_powerOn || baseHp <= 0) return 0; return _hpFloor * Hits(baseHp) * heroHit; }
    /// <summary>Damage floor from the hero's own health: a hero with far more health than the reference still loses dmgFloor x the usual share per blow (armor applies on top).</summary>
    public static float PowerDmg(float baseDmg, float heroMaxHp, float heroTaken) { Load(); if (!_powerOn || baseDmg <= 0) return 0; return _dmgFloor * HitPct(baseDmg) * heroMaxHp; }

    /// <summary>
    /// The scaling table (godot --headless --path . -- --scaling-table): a normal foe (skeleton, base 72 hp / 10 dmg), an elite (brute, 210 / 22) and a
    /// boss (dragon, 4200 / 52) against the reference hero of the same level, in dungeons at depth 1, 5 and 10 (gear tier, tier multipliers and the
    /// average upgrade health), plus the overworld dragon (Vorthrax). Hits = health / hero hit; dmg % = one blow / hero health before armor.
    /// </summary>
    public static string Table()
    {
        Load(); var sb = new System.Text.StringBuilder();
        sb.AppendLine("hero L | hit / hp   | where      | mon L | skeleton hp (hits) dmg% | brute hp (hits) dmg%  | dragon boss hp (hits) dmg%");
        float avgUpHp = 0; foreach (var u in Upgrades) avgUpHp += u.HpP; avgUpHp = Upgrades.Count > 0 ? avgUpHp / Upgrades.Count : 0;
        foreach (int L in new[] { 10, 25, 40, 50 })
        {
            float hit = RefHit(L), hp = RefHeroHp(L);
            void Row(string where, int ml, float hpM, float dmM, float ups)
            {
                string C(float bh, float bd, float extraUps) { float upM = 1 + (ups + extraUps) * avgUpHp / 100f; float h = EnemyHp(bh * hpM * upM, ml) * TierMulHp, d = EnemyDmg(bd * dmM, ml) * TierMulDmg; return $"{h,8:0} ({h / hit,5:0.0}) {d / hp * 100,4:0.0}%"; }
                sb.AppendLine($"{L,6} | {hit,4:0} / {hp,5:0} | {where,-10} | {ml,5} | {C(72, 10, 0)} | {C(210, 22, 0)} | {C(4200, 52, _upBoss)}");
            }
            Row("overworld", L, 1, 1, 0); TierMulHp = TierMulDmg = 1;
            foreach (int dep in new[] { 1, 5, 10 })
            {
                var g = GearFor(dep); TierMulHp = TierHp(dep); TierMulDmg = TierDmg(dep);
                float ups = Math.Min(_upMax, _upBase + _upPerDepth * (dep - 1)) + TierUpgrades(dep);
                Row("depth " + dep, DungeonLevel(L, dep), 1 + (g?.HpP ?? 0) / 100f, 1 + (g?.DmgP ?? 0) / 100f, ups);
                TierMulHp = TierMulDmg = 1;
            }
        }
        return sb.ToString();
    }
    static float TierMulHp = 1, TierMulDmg = 1;

    /// <summary>The level monsters measure against: your own, or in multiplayer the average of everyone in the world.</summary>
    public static float RefLevel()
    {
        if (Net.Active && Net.I!.Players.Count > 1) { float s = 0; foreach (var p in Net.I.Players.Values) s += p.Level; return s / Net.I.Players.Count; }
        return Game.D.Level;
    }
    /// <summary>Overworld monster level: the level zone's own (data/zones.json), fixed. With progression.json overworld.follow on,
    /// the old rule instead: the area's level or the heroes' level (plus a little for remote areas), whichever is higher.</summary>
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
