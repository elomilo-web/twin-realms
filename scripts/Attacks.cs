using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A weapon class's basic attack as the player built it.</summary>
public class AttackBuild { public int Rank { get; set; } = 1; public List<string> Chain { get; set; } = new(); public List<string> Mods { get; set; } = new(); }

/// <summary>One step of a chain, resolved: the swing to play (timing, poses, arc, reach...) and everything the modifiers add.</summary>
public class AttackStep
{
    public string Id = "", Name = ""; public AttackDef Def = new(); public int AnimStep;
    public float Speed = 1, Stamina = 1, ElemMul = 1;
    public List<(string id, float chance)> Status = new();
    public int Shots = 1; public float Spread, Aoe; public bool Pierce;
}

public class StepDef
{
    public string Id = "", Name = "", Desc = ""; public JsonElement Raw; public int Cost, Req;
}
public class AtkModDef
{
    public string Id = "", Name = "", Desc = ""; public int Cost = 1, Req = 1; public HashSet<string>? Types;
    public float DmgP, SpeedP, StaminaP, RangeP, ArcP, KnockP, FinisherP, RampP, ElemP, Stun;
    public Dictionary<string, float> Status = new();
    public bool Fits(string wt) => Types == null || Types.Contains(wt);
}

/// <summary>
/// Basic attacks (data/attacks.json): every weapon class has one, run by the attack button while that class is equipped.
/// Light attacks walk the player's chain of steps, then start over; socketed modifiers change them, like supports on spells.
/// Unlocks and ranks cost skill points (PlayerStats.RankPts, shared with spells).
/// </summary>
public static class Attacks
{
    static JsonDocument? _doc; static JsonElement C => (_doc ??= JsonUtil.Load("res://data/attacks.json")).RootElement;
    static readonly Dictionary<string, (string name, List<string> start, Dictionary<string, StepDef> steps, List<string> order)> _styles = new();
    static readonly Dictionary<string, AtkModDef> _mods = new(); static readonly List<string> _modOrder = new();

    static void Load()
    {
        if (_styles.Count > 0) return;
        foreach (var s in C.GetProperty("styles").EnumerateObject())
        {
            var steps = new Dictionary<string, StepDef>(); var order = new List<string>();
            foreach (var p in s.Value.GetProperty("steps").EnumerateObject())
            { steps[p.Name] = new StepDef { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Desc = JsonUtil.S(p.Value, "d"), Raw = p.Value.Clone(), Cost = (int)JsonUtil.F(p.Value, "cost", 0), Req = (int)JsonUtil.F(p.Value, "req", 1) }; order.Add(p.Name); }
            var start = s.Value.GetProperty("start").EnumerateArray().Select(x => x.GetString() ?? "").ToList();
            _styles[s.Name] = (JsonUtil.S(s.Value, "name", s.Name), start, steps, order);
        }
        foreach (var p in C.GetProperty("mods").EnumerateObject())
        {
            if (p.Name.StartsWith("_")) continue; var e = p.Value; float F(string k) => JsonUtil.F(e, k, 0);
            var m = new AtkModDef { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Desc = JsonUtil.S(e, "d"), Cost = (int)JsonUtil.F(e, "cost", 1), Req = (int)JsonUtil.F(e, "req", 1),
                DmgP = F("dmgP"), SpeedP = F("speedP"), StaminaP = F("staminaP"), RangeP = F("rangeP"), ArcP = F("arcP"), KnockP = F("knockP"), FinisherP = F("finisherP"), RampP = F("rampP"), ElemP = F("elemP"), Stun = F("stun") };
            if (e.TryGetProperty("types", out var t)) m.Types = t.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet();
            if (e.TryGetProperty("status", out var st)) foreach (var x in st.EnumerateObject()) m.Status[x.Name] = x.Value.GetSingle();
            _mods[p.Name] = m; _modOrder.Add(p.Name);
        }
    }

    // ------------------------------------------------------------------ data
    public static int MaxRank => (int)JsonUtil.F(C, "maxRank", 5);
    static int ByRank(string key, int rank) { var a = C.GetProperty(key); return a[Math.Clamp(rank - 1, 0, a.GetArrayLength() - 1)].GetInt32(); }
    public static int ChainLen(int rank) => ByRank("chainByRank", rank);
    public static int Sockets(int rank) => ByRank("socketsByRank", rank);
    public static string StyleName(string wt) { Load(); return _styles.TryGetValue(wt, out var s) ? s.name : wt; }
    public static IEnumerable<StepDef> Steps(string wt) { Load(); if (_styles.TryGetValue(wt, out var s)) foreach (var k in s.order) yield return s.steps[k]; }
    public static StepDef? Step(string wt, string id) { Load(); return _styles.TryGetValue(wt, out var s) && s.steps.TryGetValue(id, out var d) ? d : null; }
    public static IEnumerable<AtkModDef> Mods { get { Load(); foreach (var k in _modOrder) yield return _mods[k]; } }
    public static AtkModDef? Mod(string id) { Load(); return _mods.GetValueOrDefault(id); }
    public static bool HasStyle(string wt) { Load(); return _styles.ContainsKey(wt); }

    // ------------------------------------------------------------------ the player's build
    /// <summary>The build for a weapon class, created with the free starting chain on first use (auto-assigned on equip).</summary>
    public static AttackBuild Build(string wt)
    {
        Load(); var d = Game.D;
        if (!d.Attacks.TryGetValue(wt, out var b))
        {
            b = new AttackBuild(); if (_styles.TryGetValue(wt, out var s)) b.Chain.AddRange(s.start.Take(ChainLen(1)));
            d.Attacks[wt] = b;
        }
        if (_styles.TryGetValue(wt, out var st)) foreach (var k in st.start) if (!d.AtkSteps.Contains(wt + "." + k)) d.AtkSteps.Add(wt + "." + k);
        b.Chain.RemoveAll(x => Step(wt, x) == null); if (b.Chain.Count == 0 && _styles.TryGetValue(wt, out var s2)) b.Chain.Add(s2.start[0]);
        while (b.Mods.Count > Sockets(b.Rank)) b.Mods.RemoveAt(b.Mods.Count - 1);
        return b;
    }
    public static bool StepKnown(string wt, string id) => Game.D.AtkSteps.Contains(wt + "." + id);

    public static string? UnlockStep(string wt, string id)
    {
        var d = Game.D; var s = Step(wt, id); if (s == null) return "Unknown"; if (StepKnown(wt, id)) return "Already learned";
        if (d.Level < s.Req) return $"Requires level {s.Req}"; if (d.RankPts < s.Cost) return "Not enough skill points";
        d.RankPts -= s.Cost; d.AtkSteps.Add(wt + "." + id); return null;
    }
    public static string? UnlockMod(string id)
    {
        var d = Game.D; var m = Mod(id); if (m == null) return "Unknown"; if (d.AtkMods.Contains(id)) return "Already unlocked";
        if (d.Level < m.Req) return $"Requires level {m.Req}"; if (d.RankPts < m.Cost) return "Not enough skill points";
        d.RankPts -= m.Cost; d.AtkMods.Add(id); return null;
    }
    public static string? RankUp(string wt)
    {
        var d = Game.D; var b = Build(wt); if (b.Rank >= MaxRank) return "Max rank"; if (d.RankPts < 1) return "Not enough skill points";
        d.RankPts--; b.Rank++; return null;
    }
    /// <summary>Puts a step at a chain position (pos = Count appends). Null id removes that position.</summary>
    public static string? SetChain(string wt, int pos, string? id)
    {
        var b = Build(wt);
        if (id == null) { if (b.Chain.Count <= 1) return "A chain needs at least one step"; if (pos < b.Chain.Count) b.Chain.RemoveAt(pos); return null; }
        if (!StepKnown(wt, id)) return "Not learned";
        if (pos >= b.Chain.Count) { if (b.Chain.Count >= ChainLen(b.Rank)) return "Chain is full (rank up for more)"; b.Chain.Add(id); }
        else b.Chain[pos] = id;
        return null;
    }
    public static string? Socket(string wt, string id)
    {
        var b = Build(wt); var m = Mod(id); if (m == null || !Game.D.AtkMods.Contains(id)) return "Not unlocked"; if (!m.Fits(wt)) return "Does not fit this weapon";
        if (b.Mods.Contains(id)) { b.Mods.Remove(id); return null; }
        if (b.Mods.Count >= Sockets(b.Rank)) return "No free socket (rank up for more)";
        b.Mods.Add(id); return null;
    }

    // ------------------------------------------------------------------ resolving a swing
    /// <summary>
    /// Step i of the chain for this weapon (or the power attack): the weapon's swing it borrows (timing, poses), with the step's own numbers,
    /// the rank bonus and every socketed modifier applied.
    /// </summary>
    public static AttackStep Resolve(WeaponDef w, int i, bool power)
    {
        Load(); var b = Build(w.Id); var r = new AttackStep();
        float rankMul = 1 + JsonUtil.F(C, "rankDmgP", 8) * (b.Rank - 1) / 100f;
        AttackDef src; JsonElement raw = default; bool has = false;
        if (power) { src = w.Power; r.Id = "power"; r.Name = "Power attack"; }
        else
        {
            string id = b.Chain[((i % b.Chain.Count) + b.Chain.Count) % b.Chain.Count]; var sd = Step(w.Id, id);
            r.Id = id; r.Name = sd?.Name ?? id;
            if (sd != null) { raw = sd.Raw; has = true; }
            var bs = has && raw.TryGetProperty("base", out var bv) ? bv : default;
            int bi = bs.ValueKind == JsonValueKind.Number ? bs.GetInt32() : 0; bool usePower = bs.ValueKind == JsonValueKind.String && bs.GetString() == "power";
            src = usePower ? w.Power : w.Combo[Math.Clamp(bi, 0, w.Combo.Count - 1)]; r.AnimStep = usePower ? 0 : Math.Clamp(bi, 0, w.Combo.Count - 1);
        }
        float F(string k, float d) => has ? JsonUtil.F(raw, k, d) : d;
        var a = new AttackDef
        {
            Dur = src.Dur, Wind = src.Wind, Hit = src.Hit, Keys = src.Keys,
            Dmg = F("dmg", src.Dmg) * rankMul, Arc = F("arc", src.Arc), Range = F("range", src.Range), Knock = F("knock", src.Knock), Stun = F("stun", src.Stun), Up = F("up", src.Up),
            Lunge = F("lunge", src.Lunge), Spin = F("spin", src.Spin), CritBonus = F("critBonus", src.CritBonus),
            Finisher = has && raw.TryGetProperty("finisher", out var fv) ? fv.ValueKind == JsonValueKind.True : src.Finisher, Pierce = src.Pierce,
        };
        r.Speed = F("speed", 1); r.Stamina = F("stamina", 1);
        r.Shots = (int)F("shots", 1); r.Spread = F("spread", 0); r.Aoe = F("aoe", 0); r.Pierce = has && raw.TryGetProperty("pierce", out var pv) && pv.ValueKind == JsonValueKind.True;
        if (has && raw.TryGetProperty("status", out var st)) foreach (var x in st.EnumerateObject()) r.Status.Add((x.Name, x.Value.GetSingle()));
        // modifiers
        float dmgP = 0, spdP = 0, stP = 0, rngP = 0, arcP = 0, kP = 0;
        foreach (var mid in b.Mods)
        {
            var m = Mod(mid); if (m == null || !m.Fits(w.Id)) continue;
            dmgP += m.DmgP + m.RampP * (power ? 0 : i % b.Chain.Count); spdP += m.SpeedP; stP += m.StaminaP; rngP += m.RangeP; arcP += m.ArcP; kP += m.KnockP;
            if (a.Finisher || power) { dmgP += m.FinisherP; a.Stun += m.Stun; }
            r.ElemMul *= 1 + m.ElemP / 100f;
            foreach (var kv in m.Status) r.Status.Add((kv.Key, kv.Value));
        }
        a.Dmg *= Mathf.Max(.1f, 1 + dmgP / 100f); a.Range *= 1 + rngP / 100f; a.Arc = Mathf.Min(180, a.Arc * (1 + arcP / 100f)); a.Knock *= 1 + kP / 100f;
        r.Speed *= Mathf.Max(.3f, 1 + spdP / 100f); r.Stamina *= Mathf.Max(.2f, 1 + stP / 100f);
        r.Def = a; return r;
    }
}
