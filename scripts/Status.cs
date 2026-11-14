using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

public class StatusDef { public string Id = "", Name = "", Kind = "dot", Type = "physical", Desc = ""; public float Dur = 3, Pow = .2f; public int Stacks = 1, FreezeAt; public Color Col = Colors.White; }
public class StatusInst { public StatusDef Def = null!; public float T, Pow, Base; public int Stacks = 1; }

/// <summary>Damage types, resistances, penetration and status effects (League of Legends style mitigation).</summary>
public static class Dmg
{
    public static readonly string[] Types = { "physical", "fire", "frost", "lightning", "poison", "shadow", "holy" };
    static readonly Dictionary<string, StatusDef> _s = new();
    public static StatusDef? Status(string id) { Load(); return _s.TryGetValue(id, out var s) ? s : null; }
    public static IEnumerable<StatusDef> Statuses { get { Load(); return _s.Values; } }
    static void Load()
    {
        if (_s.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/status.json");
        foreach (var p in doc.RootElement.GetProperty("statuses").EnumerateObject())
        {
            var e = p.Value;
            _s[p.Name] = new StatusDef { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Kind = JsonUtil.S(e, "kind", "dot"), Type = JsonUtil.S(e, "type", "physical"), Desc = JsonUtil.S(e, "d"), Dur = JsonUtil.F(e, "dur", 3), Pow = JsonUtil.F(e, "pow", .2f), Stacks = (int)JsonUtil.F(e, "stacks", 1), FreezeAt = (int)JsonUtil.F(e, "freezeAt"), Col = Color.FromHtml(JsonUtil.S(e, "col", "ffffff")) };
        }
    }

    /// <summary>
    /// Mitigation. Physical damage is reduced by armor, every other type by magic resist, with the League of Legends curve:
    /// x 100/(100+R) for R >= 0 and x (2 - 100/(100-R)) below 0. Penetration first removes a percentage, then a flat amount.
    /// Then the per-type resistance % (capped at 90%, negative = weakness). "true" damage ignores all of it.
    /// </summary>
    public static float Mitigate(float raw, string type, float armor, float mr, float resPct, float pen, float penP, float mpen, float mpenP)
    {
        if (type == "true") return raw;
        bool phys = type == "physical"; float r = phys ? armor : mr;
        float eff = r * (1 - Mathf.Clamp(phys ? penP : mpenP, 0, 100) / 100f) - (phys ? pen : mpen);
        float mult = eff >= 0 ? 100f / (100f + eff) : 2f - 100f / (100f - eff);
        return raw * mult * (1 - Mathf.Clamp(resPct, -100, 90) / 100f);
    }

    // ---- statuses as bits, for the network (enemy snapshots, hero poses): one bit per status, in name order (same on every machine)
    static List<StatusDef>? _order;
    static List<StatusDef> Order() { if (_order == null) { Load(); _order = new List<StatusDef>(_s.Values); _order.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id)); } return _order; }
    public static float Mask(StatusSet s) { var o = Order(); int m = 0; foreach (var st in s.List) { int i = o.IndexOf(st.Def); if (i >= 0 && i < 24) m |= 1 << i; } return m; }
    /// <summary>The statuses in a mask, for display only (no timers: the owner's game ticks them).</summary>
    public static void FromMask(float mask, StatusSet into)
    {
        var o = Order(); int m = (int)mask; into.List.Clear();
        for (int i = 0; i < o.Count && i < 24; i++) if ((m & (1 << i)) != 0) into.List.Add(new StatusInst { Def = o[i], T = 1, Stacks = 1 });
    }

    public static Color TypeColor(string t) => t switch
    {
        "fire" => new Color(1f, .6f, .25f), "frost" => new Color(.6f, .88f, 1f), "lightning" => new Color(.8f, .78f, 1f), "poison" => new Color(.55f, .9f, .35f),
        "shadow" => new Color(.75f, .5f, 1f), "holy" => new Color(1f, .92f, .55f), "true" => new Color(1f, .45f, .5f), _ => Colors.White,
    };
}

/// <summary>Statuses on one creature (enemy or player): stacking, durations, damage over time and the modifiers they impose.</summary>
public class StatusSet
{
    public readonly List<StatusInst> List = new();
    float _tick;

    /// <summary>Adds or refreshes a status. pow overrides the default strength (for dots: fraction of baseDmg per second per stack).</summary>
    /// <summary>Statuses this creature ignores right now (the hero's immunity enchantments).</summary>
    public Func<string, bool>? Block;
    public void Add(string id, float durMul, float pow, float baseDmg, float tenacity = 0)
    {
        var d = Dmg.Status(id); if (d == null || Block?.Invoke(id) == true) return;
        float dur = d.Dur * durMul * (1 - Mathf.Clamp(tenacity, 0, 80) / 100f);
        var cur = List.Find(s => s.Def.Id == id);
        if (cur == null) { List.Add(new StatusInst { Def = d, T = dur, Pow = pow > 0 ? pow : d.Pow, Base = baseDmg }); }
        else { cur.T = Mathf.Max(cur.T, dur); cur.Stacks = Math.Min(d.Stacks, cur.Stacks + 1); cur.Pow = Mathf.Max(cur.Pow, pow > 0 ? pow : d.Pow); cur.Base = Mathf.Max(cur.Base, baseDmg); }
        if (d.FreezeAt > 0 && Has(id, d.FreezeAt)) { List.RemoveAll(s => s.Def.Id == id); Add("freeze", 1, 0, 0, tenacity); }
    }
    public bool Has(string id, int stacks = 1) => List.Exists(s => s.Def.Id == id && s.Stacks >= stacks);
    public void Clear() => List.Clear();
    float Sum(string kind) { float v = 0; foreach (var s in List) if (s.Def.Kind == kind) v += s.Pow * (kind == "shred" ? s.Stacks : 1); return v; }
    public float SpeedMul { get { if (Has("root") || Stunned) return 0; float m = 1; foreach (var s in List) if (s.Def.Kind == "slow") m *= 1 - Mathf.Clamp(s.Pow * s.Stacks / Mathf.Max(1, s.Def.Stacks) * 1.5f, 0, .8f); return m; } }
    public bool Stunned => List.Exists(s => s.Def.Kind == "stun");
    public float TakenMul => 1 + Sum("amp");
    public float DealtMul => 1 - Mathf.Clamp(Sum("weaken"), 0, .8f);
    public float Shred => Sum("shred");
    /// <summary>The colour of the strongest visible status, for tinting the body (alpha = strength).</summary>
    public Color Tint { get { if (List.Count == 0) return new Color(0, 0, 0, 0); var s = List[^1]; return new Color(s.Def.Col, .35f); } }

    /// <summary>Advances timers; every 0.5 s calls dot(type, damage, colour) for each damage-over-time status.</summary>
    public void Tick(float dt, Action<string, float, Color> dot)
    {
        for (int i = List.Count - 1; i >= 0; i--) { List[i].T -= dt; if (List[i].T <= 0) List.RemoveAt(i); }
        _tick += dt; if (_tick < .5f) return; _tick = 0;
        foreach (var s in List.ToArray()) if (s.Def.Kind == "dot") dot(s.Def.Type, Mathf.Max(1, s.Base * s.Pow * s.Stacks * .5f), s.Def.Col);
    }
}
