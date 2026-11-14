using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class SkillState { public int Rank = 1; public List<string> Sup = new(); }

public class SkillDef
{
    public string Id = "", Name = "", Kind = "projectile", Type = "physical", Scale = "spell", Desc = "";
    public List<string> Tags = new();
    public float Mp, St, Cd = 1, Cast = .3f, Dmg, Lvl, Mult = 1, Radius, Speed = 30, Spread, Dur, Tick = .5f, Heal, Knock, Stun, Up, Arc = 60, Range = 3, Delay, Leech;
    public float Taunt;   // taunt strength factor (x the skill's resolved power): foes in the radius turn on the caster for its duration
    public Dictionary<string, float> X = new();   // extra numbers for the newer kinds ("x" in skills.json): ward, hot, cleanse, store, pull, hpCost, execute...
    public float XOr(string k, float d) => X.TryGetValue(k, out var v) ? v : d;
    public int Count = 1, Pierce, Chain, Req = 1, Cost = 1; public bool Homing, Self, Behind, Crit, Arrow;
    public Dictionary<string, float[]> Status = new(); public Dictionary<string, float> Buff = new();
}
public class SupportDef { public string Id = "", Name = "", Desc = ""; public List<string> Tags = new(); public int Cost = 1, Req = 1; public Dictionary<string, float> Num = new(); public string Auto = "", Convert = ""; public Dictionary<string, float[]> Status = new(); }

/// <summary>A skill with rank, supports and passives resolved: everything needed to fire it.</summary>
public class SkillCalc
{
    public SkillDef Def = null!; public string Type = "physical";
    public float Dmg, Mp, St, Cd, Cast, Radius, Speed, Spread, Knock, Stun, Up, Leech, Execute, Heal, Dur, Tick, AutoCd;
    public float CritC;      // extra crit chance (%) from modifiers
    public float SelfHeal;   // % of max health restored each time the skill goes off
    public float ManaBack;   // % of the mana and stamina cost refunded each time the skill goes off
    public int Count, Pierce, Chain, Echo; public bool Homing; public string Auto = "";
    // where a modifier has no natural meaning for a kind of skill it is turned into one that does (Skills.Compute):
    public float Range, Arc;        // melee and dash reach (area modifiers widen them)
    public float Delay;             // strikes: time before the hit lands (speed modifiers shorten it)
    public float Jump = 10f;        // how far chained arcs leap between foes
    public float SpinStep = .26f;   // whirlwind: time per turn
    public float MoveMul = 1f;      // leap and dash travel time
    public bool Burst; public float BurstR = 4f;   // heals and buffs: a burst around you that carries the on-hit modifiers
    public Vector3? At;      // aimed skills: where the player put the preview while holding the key
    public float Charge;     // charged skills: 0..1, how long the key was held (empowers the cast)
    public List<(string id, float chance, float durMul, float pow)> Status = new();
    /// <summary>A hit from this skill; a critical one deals the hero's crit multiplier.</summary>
    public HitInfo Hit(float dmg, Vector3 from, bool crit) => new() { Damage = crit ? dmg * Game.D.CritMult() : dmg, From = from, Knock = Knock, Stun = Stun, Up = Up, Crit = crit, IsPlayer = true, Attacker = Player.Instance, Type = Type, Statuses = Status, Leech = Leech, Execute = Execute, Fire = Type == "fire", Stagger = Stun > 0 || Up > 0 || Knock >= 6f };
}

/// <summary>Active skills and support modifiers from data/skills.json, the player's skill state, and how supports change a skill.</summary>
public static class Skills
{
    static readonly Dictionary<string, SkillDef> _s = new(); static readonly Dictionary<string, SupportDef> _p = new();
    public static readonly List<string> SkillOrder = new(), SupportOrder = new();
    public static float RankP = 15; public static int MaxRank = 5;
    public static SkillDef? Get(string id) { Load(); return _s.GetValueOrDefault(id); }
    public static SupportDef? Sup(string id) { Load(); return _p.GetValueOrDefault(id); }
    public static IEnumerable<SkillDef> All { get { Load(); foreach (var k in SkillOrder) yield return _s[k]; } }
    public static IEnumerable<SupportDef> AllSupports { get { Load(); foreach (var k in SupportOrder) yield return _p[k]; } }

    static Dictionary<string, float[]> St(JsonElement e)
    {
        var d = new Dictionary<string, float[]>(); if (!e.TryGetProperty("status", out var s)) return d;
        foreach (var x in s.EnumerateObject()) { var l = new List<float>(); foreach (var v in x.Value.EnumerateArray()) l.Add(v.GetSingle()); d[x.Name] = l.ToArray(); }
        return d;
    }
    static void Load()
    {
        if (_s.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/skills.json"); var r = doc.RootElement;
        RankP = JsonUtil.F(r, "rankP", 15); MaxRank = (int)JsonUtil.F(r, "maxRank", 5);
        foreach (var p in r.GetProperty("skills").EnumerateObject())
        {
            var e = p.Value; float F(string k, float d = 0) => JsonUtil.F(e, k, d); bool B(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
            var s = new SkillDef
            {
                Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Kind = JsonUtil.S(e, "kind", "projectile"), Type = JsonUtil.S(e, "type", "physical"), Scale = JsonUtil.S(e, "scale", "spell"), Desc = JsonUtil.S(e, "d"),
                Mp = F("mp"), St = F("st"), Cd = F("cd", 1), Cast = F("cast", .3f), Dmg = F("dmg"), Lvl = F("lvl"), Mult = F("mult", 1), Radius = F("radius"), Speed = F("speed", 30), Spread = F("spread"), Dur = F("dur"), Tick = F("tick", .5f),
                Heal = F("heal"), Knock = F("knock"), Stun = F("stun"), Up = F("up"), Arc = F("arc", 60), Range = F("range", 3), Delay = F("delay"), Leech = F("leech"), Taunt = F("taunt"),
                Count = (int)F("count", 1), Pierce = (int)F("pierce"), Chain = (int)F("chain"), Req = (int)F("req", 1), Cost = (int)F("cost", 1), Homing = B("homing"), Self = B("self"), Behind = B("behind"), Crit = B("crit"), Arrow = B("arrow"), Status = St(e),
            };
            if (e.TryGetProperty("tags", out var t)) foreach (var x in t.EnumerateArray()) s.Tags.Add(x.GetString() ?? "");
            if (e.TryGetProperty("buff", out var bf)) foreach (var x in bf.EnumerateObject()) s.Buff[x.Name] = x.Value.GetSingle();
            if (e.TryGetProperty("x", out var xs)) foreach (var x in xs.EnumerateObject()) s.X[x.Name] = x.Value.GetSingle();
            _s[p.Name] = s; SkillOrder.Add(p.Name);
        }
        foreach (var p in r.GetProperty("supports").EnumerateObject())
        {
            var e = p.Value; var s = new SupportDef { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Desc = JsonUtil.S(e, "d"), Cost = (int)JsonUtil.F(e, "cost", 1), Req = (int)JsonUtil.F(e, "req", 1) };
            if (e.TryGetProperty("tags", out var t)) foreach (var x in t.EnumerateArray()) s.Tags.Add(x.GetString() ?? "");
            if (e.TryGetProperty("mods", out var m))
            {
                foreach (var x in m.EnumerateObject())
                    if (x.Value.ValueKind == JsonValueKind.Number) s.Num[x.Name] = x.Value.GetSingle();
                    else if (x.Name == "auto") s.Auto = x.Value.GetString() ?? "";
                    else if (x.Name == "convert") s.Convert = x.Value.GetString() ?? "";
                s.Status = St(m);
            }
            _p[p.Name] = s; SupportOrder.Add(p.Name);
        }
    }

    // ---------------------------------------------------------------- rules
    public static int Sockets(int rank) => Math.Min(3, 1 + rank / 2);
    /// <summary>Every modifier fits every skill and they all combine (a support's tags only place it in the modifier tree).</summary>
    public static bool Fits(SupportDef s, SkillDef k) => true;
    public static bool Known(string id) => Game.D.Skills.ContainsKey(id);

    /// <summary>Old saves and new characters: class skills are learned and put on the bar.</summary>
    public static void GrantStarting(PlayerStats d)
    {
        Load();
        foreach (var id in Classes.Get(d.Class).Skills) if (_s.ContainsKey(id) && !d.Skills.ContainsKey(id)) d.Skills[id] = new SkillState();
        if (d.Skills.Count == 0) foreach (var id in new[] { "fire", "nova", "heal", "zap" }) d.Skills[id] = new SkillState();
        if (d.Bar == null || d.Bar.Length < 6) { var nb = new string[6]; if (d.Bar != null) Array.Copy(d.Bar, nb, d.Bar.Length); d.Bar = nb; }
        bool empty = Array.TrueForAll(d.Bar, b => string.IsNullOrEmpty(b));
        if (empty) { int i = 0; foreach (var id in Classes.Get(d.Class).Skills) if (d.Skills.ContainsKey(id) && i < 6) d.Bar[i++] = id; if (i == 0) foreach (var id in d.Skills.Keys) if (i < 6) d.Bar[i++] = id; }
    }

    public static string? Unlock(string id)
    {
        var d = Game.D; var s = Get(id); if (s == null) return "Unknown skill";
        if (Known(id)) return "Already learned"; if (d.Level < s.Req) return $"Requires level {s.Req}"; if (d.RankPts < s.Cost) return "Not enough skill ranks";
        d.RankPts -= s.Cost; d.Skills[id] = new SkillState(); for (int i = 0; i < d.Bar.Length; i++) if (string.IsNullOrEmpty(d.Bar[i])) { d.Bar[i] = id; break; }
        return null;
    }
    public static string? RankUp(string id)
    {
        var d = Game.D; if (!d.Skills.TryGetValue(id, out var st)) return "Not learned"; if (st.Rank >= MaxRank) return "Max rank"; if (d.RankPts < 1) return "Not enough skill ranks";
        d.RankPts--; st.Rank++; return null;
    }
    public static string? UnlockSupport(string id)
    {
        var d = Game.D; var s = Sup(id); if (s == null) return "Unknown"; if (d.Supports.Contains(id)) return "Already unlocked"; if (d.Level < s.Req) return $"Requires level {s.Req}"; if (d.RankPts < s.Cost) return "Not enough skill ranks";
        d.RankPts -= s.Cost; d.Supports.Add(id); return null;
    }
    public static string? Socket(string skill, int slot, string? support)
    {
        var d = Game.D; if (!d.Skills.TryGetValue(skill, out var st)) return "Not learned"; var k = Get(skill)!;
        while (st.Sup.Count < Sockets(st.Rank)) st.Sup.Add("");
        if (slot >= Sockets(st.Rank)) return "Socket locked";
        if (!string.IsNullOrEmpty(support)) { var s = Sup(support); if (s == null || !d.Supports.Contains(support)) return "Support not unlocked"; if (!Fits(s, k)) return "Does not fit this skill"; if (st.Sup.Contains(support)) return "Already socketed"; }
        st.Sup[slot] = support ?? ""; return null;
    }

    /// <summary>
    /// Resolves a skill: base damage (spell: dmg + lvl x character level, times spell power; weapon: mult x weapon damage), rank bonus,
    /// support modifiers (more/less damage multiply; area, speed, cost and cooldown add up), passive bonuses (cdr, mcost, skillP, area), and statuses.
    /// </summary>
    public static SkillCalc Compute(string id)
    {
        Load(); var d = Game.D; var TS = Game.TS; var k = _s[id]; var st = d.Skills.GetValueOrDefault(id) ?? new SkillState();
        var c = new SkillCalc { Def = k, Type = k.Type, Radius = k.Radius, Speed = k.Speed, Spread = k.Spread, Knock = k.Knock, Stun = k.Stun, Up = k.Up, Leech = k.Leech, Heal = k.Heal, Dur = k.Dur, Tick = k.Tick, Count = k.Count, Pierce = k.Pierce, Chain = k.Chain, Homing = k.Homing };
        float more = 1 + RankP * (st.Rank - 1) / 100f, aoe = 1 + TS["aoeP"] / 100f, spd = 1, cost = 1 + TS["mcost"] / 100f, cd = 1 - TS["cdr"] / 100f, cast = 1, knock = 1, dur = 1, tick = 1, heal = 1;
        float supMore = 1, supAoe = 1; bool off = false, offCrit = false;   // the modifiers' own share, for the stand-in effects below
        foreach (var kv in k.Status) c.Status.Add((kv.Key, kv.Value.Length > 0 ? kv.Value[0] : 1, kv.Value.Length > 1 ? kv.Value[1] : 1, kv.Value.Length > 2 ? kv.Value[2] : 0));
        foreach (var sid in st.Sup)
        {
            var s = Sup(sid); if (s == null || !Fits(s, k)) continue; float N(string key) => s.Num.GetValueOrDefault(key);
            supMore *= 1 + N("dmgP") / 100f; supAoe += N("aoeP") / 100f;
            off |= s.Status.Count > 0 || s.Convert != "" || N("stun") > 0 || N("up") > 0 || N("knockP") > 0 || N("execute") > 0 || N("pierce") > 0 || N("leech") > 0;
            offCrit |= N("critC") > 0 || N("homing") > 0;
            if (N("homing") > 0 && k.Homing) c.CritC += 10;   // a skill that already seeks: it seeks weak spots instead
            more *= 1 + N("dmgP") / 100f; aoe += N("aoeP") / 100f; spd += N("projSpeedP") / 100f; cost += N("costP") / 100f; cd += N("cdP") / 100f; cast += N("castP") / 100f; knock += N("knockP") / 100f;
            c.Count += (int)N("projCount"); c.Pierce += (int)N("pierce"); c.Chain += (int)N("chain"); c.Echo += (int)N("echo"); c.Homing |= N("homing") > 0;
            c.Stun += N("stun"); c.Up += N("up"); c.Leech += N("leech"); c.Execute += N("execute");
            dur += N("durP") / 100f; tick += N("tickP") / 100f; heal += N("healP") / 100f; c.CritC += N("critC"); c.SelfHeal += N("selfHeal"); c.ManaBack += N("manaBack");
            // several triggers combine: each fires on its own; the shown auto-cast is the first one, its delay the shortest
            if (s.Auto != "") { if (c.Auto == "") c.Auto = s.Auto; float a = N("autoCd"); c.AutoCd = c.AutoCd > 0 ? Mathf.Min(c.AutoCd, a) : a; }
            if (s.Convert != "") c.Type = s.Convert;
            foreach (var kv in s.Status) c.Status.Add((kv.Key, kv.Value[0], kv.Value.Length > 1 ? kv.Value[1] : 1, kv.Value.Length > 2 ? kv.Value[2] : 0));
        }
        c.Execute += k.XOr("execute", 0);
        string kind = k.Kind; bool support = kind is "heal" or "buff" or "enchant" or "ward";
        float baseDmg = k.Scale == "weapon" ? d.WpnDmg() * k.Mult : (k.Dmg + k.Lvl * d.Level) * d.SpellPow();
        if (support && baseDmg <= 0) baseDmg = (8 + 1.6f * d.Level) * d.SpellPow();   // heals and buffs: what their burst hits for
        c.Dmg = baseDmg * more * (1 + TS["skillP"] / 100f);
        c.Radius = k.Radius * Mathf.Max(.3f, aoe); c.Knock *= knock;
        c.Dur = k.Dur * Mathf.Max(.3f, dur); c.Tick = k.Tick; c.Heal = k.Heal * Mathf.Max(0, heal); c.ManaBack = Mathf.Min(c.ManaBack, 90f);
        c.Speed = k.Speed; c.Range = k.Range; c.Arc = k.Arc; c.Delay = k.Delay;
        c.Mp = k.Mp * Mathf.Max(0, cost) * (1f - (d.Weapon?.Mc ?? 0f)); c.St = k.St * Mathf.Max(0, cost);
        float cdMul = Mathf.Max(.2f, cd), castMul = Mathf.Max(.3f, cast);

        // ---- every modifier does something on every kind of skill: where its natural effect has no meaning, it gets a stand-in
        // speed: projectiles fly faster; anything else is quicker to use, strikes land sooner, leaps and dashes travel faster
        if (kind == "projectile") c.Speed = k.Speed * Mathf.Max(.3f, spd);
        else if (spd > 1) { float q = 1 + (spd - 1) * .5f; castMul /= q; c.Delay = k.Delay / spd; c.MoveMul = 1f / q; }
        // pierce: projectiles pass through foes; anything else sunders armor (25% chance per point)
        if (kind != "projectile") { int extra = c.Pierce - k.Pierce; if (extra > 0) c.Status.Add(("shred", Mathf.Min(1f, .25f * extra), 1, 0)); c.Pierce = 0; }
        // homing: projectiles seek; placed zones drift after foes; chains leap farther; anything else finds weak spots (+10% crit)
        if (c.Homing && !k.Homing && kind != "projectile")
        {
            if (kind == "zone" && !k.Self) { }
            else { c.Homing = false; if (kind == "chain") c.Jump *= 1.5f; else c.CritC += 10; }
        }
        // area, on skills without an area of their own: projectiles explode, melee and dash reach farther and wider, chains leap farther,
        // heals heal more, buffs last longer
        float sa = Mathf.Max(.3f, supAoe);
        if (k.Radius <= 0 && supAoe != 1)
            switch (kind)
            {
                case "projectile": if (supAoe > 1) c.Radius = 3f * (supAoe - 1); break;
                case "melee": c.Range = k.Range * sa; c.Arc = Mathf.Min(180, k.Arc * sa); break;
                case "dash": c.Range = k.Range * sa; break;
                case "chain": c.Jump *= sa; break;
                case "heal": c.Heal *= 1 + (supAoe - 1) * .5f; break;
                case "buff": c.Dur *= 1 + (supAoe - 1) * .5f; break;
            }
        c.BurstR = 4f * Mathf.Max(.5f, sa);
        // knockback on a skill that had none: a shove
        if (k.Knock <= 0 && knock > 1) c.Knock = (knock - 1) * 2.5f;
        // tick rate: zones pulse faster, whirlwind turns faster, anything else recovers faster
        if (kind == "zone") c.Tick = k.Tick * Mathf.Max(.25f, tick);
        else if (kind == "spin") c.SpinStep = .26f * Mathf.Max(.4f, tick);
        else if (tick != 1) cdMul *= Mathf.Max(.2f, 1 + (tick - 1) * .5f);
        // duration: statuses last longer; heals heal more; instant skills with nothing lasting daze longer
        if (dur != 1)
        {
            for (int i = 0; i < c.Status.Count; i++) { var s = c.Status[i]; c.Status[i] = (s.id, s.chance, s.durMul * Mathf.Max(.3f, dur), s.pow); }
            if (kind == "heal") c.Heal *= 1 + (dur - 1) * .5f;
            else if (k.Dur <= 0 && c.Status.Count == 0 && dur > 1) c.Stun += .5f * (dur - 1);
        }
        // healing power on a skill that does not heal: it heals you a little when used
        if (k.Heal <= 0 && heal > 1) c.SelfHeal += (heal - 1) * 5f;
        // heals: damage modifiers make the heal stronger; on-hit modifiers add a burst of light around you that carries them
        if (kind == "heal") c.Heal *= Mathf.Max(.1f, supMore);
        if (support) c.Burst = off || (kind == "buff" && offCrit);
        // more "projectiles" on buffs: each one adds a quarter to the duration (the other kinds use Count directly, see PlayerSkills.Execute)
        if (kind == "buff" && c.Count > k.Count) c.Dur *= 1 + .25f * (c.Count - k.Count);

        c.Cd = k.Cd * cdMul; c.Cast = k.Cast * castMul / (k.Scale == "weapon" ? d.AtkSpeed() : 1f + TS["castSpd"] / 100f);
        return c;
    }

    /// <summary>What a modifier does on this particular skill when its usual effect does not apply there ("" when it works as described).</summary>
    public static string OnSkill(SupportDef s, SkillDef k)
    {
        Load(); var l = new List<string>(); float N(string key) => s.Num.GetValueOrDefault(key); string kind = k.Kind; bool proj = kind == "projectile";
        int n = (int)N("projCount");
        if (n > 0 && !proj && kind != "spin")
            l.Add(kind switch
            {
                "nova" => $"{n} more novas burst around you", "chain" => $"{n} more bolts, each from a new foe", "heal" => $"{n} follow-up heal pulses",
                "zone" => $"{n} more zones around the first", "strike" => $"{n} more strikes around the first", "melee" => $"the swing sends out {n} blade waves",
                "leap" => $"{n} aftershocks around where you land", "dash" => k.Behind ? $"{n} shadow strikes on the target" : $"{n} afterimages sweep the path again",
                _ => $"lasts {25 * n}% longer",
            });
        if (N("projSpeedP") > 0 && !proj) l.Add(kind == "strike" ? "faster to use and lands sooner" : kind is "leap" or "dash" ? "faster to use and to travel" : "faster to use");
        if (N("pierce") > 0 && !proj) l.Add("hits sunder armor");
        if (N("homing") > 0 && k.Homing) l.Add("it already seeks: +10% crit chance instead");
        else if (N("homing") > 0 && !proj) l.Add(kind == "zone" && !k.Self ? "the zone drifts after foes" : kind == "chain" ? "arcs leap 50% farther" : "+10% crit chance");
        if (N("aoeP") != 0 && k.Radius <= 0)
        {
            string? a = kind switch { "projectile" => N("aoeP") > 0 ? "projectiles explode" : null, "melee" => "reaches farther and wider", "dash" => "dashes farther", "chain" => "arcs leap farther", "heal" => "heals more", "buff" => "lasts longer", _ => null };
            if (a != null) l.Add(N("aoeP") < 0 ? a.Replace("farther", "less far").Replace("more", "less").Replace("longer", "shorter") : a);
        }
        if (N("knockP") > 0 && k.Knock <= 0) l.Add("shoves foes back");
        if (N("tickP") != 0 && kind != "zone") l.Add(kind == "spin" ? "spins faster" : "shorter cooldown");
        if (N("durP") > 0 && k.Dur <= 0) l.Add(kind == "heal" ? "heals more" : "statuses last longer" + (k.Status.Count == 0 && s.Status.Count == 0 ? "; hits daze foes longer" : ""));
        if (N("healP") > 0 && k.Heal <= 0) l.Add("heals you a little on use");
        if (N("chain") > 0 && !proj && kind is not ("chain" or "melee")) l.Add($"arcs leap to {(int)N("chain")} foes nearby");
        if (N("echo") > 0 && kind is "leap" or "dash") l.Add(kind == "leap" ? "slams again" : "strikes again");
        if (s.Auto != "" && kind is "leap" or "dash") l.Add("when it triggers, you slam where you stand");
        if (kind == "heal" && N("dmgP") != 0) l.Add(N("dmgP") > 0 ? "heals more" : "heals less");
        bool offensive = s.Status.Count > 0 || s.Convert != "" || N("stun") > 0 || N("up") > 0 || N("knockP") > 0 || N("execute") > 0 || N("pierce") > 0 || N("leech") > 0 || (kind == "buff" && (N("critC") > 0 || N("homing") > 0));
        if (kind is "heal" or "buff" && offensive) l.Add("a burst around you carries the on-hit effects");
        if (kind == "heal" && N("critC") > 0) l.Add("heals can crit for 50% more");
        return string.Join("; ", l.Distinct());
    }

    /// <summary>Everything a resolved skill does, as text: two different texts mean the skill behaves differently (bot check: every modifier changes every skill).</summary>
    public static string Fingerprint(SkillCalc c) =>
        string.Join("|", c.Type, c.Dmg.ToString("0.###"), c.Mp.ToString("0.###"), c.St.ToString("0.###"), c.Cd.ToString("0.###"), c.Cast.ToString("0.###"), c.Radius.ToString("0.###"), c.Speed.ToString("0.###"),
            c.Knock.ToString("0.###"), c.Stun.ToString("0.###"), c.Up.ToString("0.###"), c.Leech, c.Execute, c.Heal.ToString("0.####"), c.Dur.ToString("0.###"), c.Tick.ToString("0.###"), c.Auto, c.AutoCd, c.Count, c.Pierce, c.Chain, c.Echo, c.Homing,
            c.CritC, c.SelfHeal.ToString("0.###"), c.ManaBack, c.Range.ToString("0.###"), c.Arc.ToString("0.###"), c.Delay.ToString("0.###"), c.Jump.ToString("0.###"), c.SpinStep.ToString("0.###"), c.MoveMul.ToString("0.###"), c.Burst, c.Burst ? c.BurstR.ToString("0.#") : "",
            string.Join(",", c.Status.Select(s => $"{s.id}:{s.chance:0.##}:{s.durMul:0.##}:{s.pow:0.##}")));
}
