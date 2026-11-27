using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Godot;

namespace TwinRealms;

/// <summary>Pipeline stages, in the order they apply (focus F-02). Every stat contribution belongs to exactly one.</summary>
public enum StatStage { Base, Race, Class, Level, Gear, Gems, Passives, Life, Buffs }   // Life: life and action skill levels (F-24/F-25, LifeSkills.cs)

/// <summary>One step of a derived stat: a flat add, a percent in a named group, a "more" multiplier, or a cap adjustment.</summary>
public struct StatStep { public StatStage Stage; public string Src; public string Kind; public float V; }

/// <summary>A derived hero stat with every step that built it (shown by "/stats dump").</summary>
public class StatLine
{
    public string Id = ""; public int RoundMode;   // 0 none, 1 round, 2 truncate
    public readonly List<StatStep> Steps = new();
    public float Flat, Mul = 1, Raw, Value;
    public readonly List<(string group, float pct, float factor)> Groups = new();
    public float Min = float.MinValue, Max = float.MaxValue;
}

public class StatSheet { public int Sig; public readonly Dictionary<string, StatLine> Lines = new(); }

/// <summary>A-14: an attribute defined in data/stats.json "attributes": every point past the starting 5 adds Keys x points to the stat table.</summary>
public class AttrDef { public string Id = "", Name = "", Desc = "", Icon = "hp"; public Color Col = Colors.White; public readonly Dictionary<string, float> Keys = new(); }

/// <summary>
/// The hero's one stat pipeline (focus F-02; formulas in .devloop/systems/stats.md, caps in data/stats.json):
///   base > race > class > level (level and attribute points) > gear > gems > passives (skill tree, title) > buffs.
/// 1. Rebuild (Game.RecalcTree) collects every stat-table key contribution per stage, writes the totals into Game.TS and clamps them.
/// 2. Derived stats (max health, weapon damage, crit...) are built per PlayerStats from those stages: all flat steps add up first,
///    then each percent group multiplies (1 + sum/100, never below pctFloor), then "more" multipliers, then rounding and the cap.
///    The result is cached per PlayerStats and rebuilt only when Game.TS, the level, attributes or worn items change.
/// </summary>
public static class StatPipeline
{
    // ---- caps (data/stats.json)
    static bool _loaded; static float _pctFloor = .1f;
    static readonly Dictionary<string, (float lo, float hi)> _keyCaps = new(), _derivedCaps = new();
    static readonly Dictionary<string, (float lo, float hi)?> _capCache = new();
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/stats.json"); var r = doc.RootElement;
        _pctFloor = Mathf.Clamp(JsonUtil.F(r, "pctFloor", .1f), 0f, 1f);
        void Read(string name, Dictionary<string, (float, float)> into)
        {
            if (!r.TryGetProperty(name, out var o) || o.ValueKind != System.Text.Json.JsonValueKind.Object) return;
            foreach (var p in o.EnumerateObject())
                if (!p.Name.StartsWith("_") && p.Value.ValueKind == System.Text.Json.JsonValueKind.Array && p.Value.GetArrayLength() == 2)
                    into[p.Name] = (p.Value[0].GetSingle(), p.Value[1].GetSingle());
        }
        Read("keys", _keyCaps); Read("derived", _derivedCaps);
        // A-14: the newer attributes (Wisdom, Agility, Luck, Endurance): each point past the starting 5 gives its "keys" (data/stats.json "attributes")
        if (r.TryGetProperty("attributes", out var at) && at.ValueKind == System.Text.Json.JsonValueKind.Object)
            foreach (var p in at.EnumerateObject())
            {
                if (p.Name.StartsWith("_") || p.Value.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                var a = new AttrDef { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Desc = JsonUtil.S(p.Value, "d"), Icon = JsonUtil.S(p.Value, "icon", "hp"), Col = Color.FromHtml(JsonUtil.S(p.Value, "color", "c8c8c8")) };
                if (p.Value.TryGetProperty("keys", out var ks) && ks.ValueKind == System.Text.Json.JsonValueKind.Object) foreach (var x in ks.EnumerateObject()) if (x.Value.ValueKind == System.Text.Json.JsonValueKind.Number) a.Keys[x.Name] = x.Value.GetSingle();
                _attrs.Add(a);
            }
    }
    static readonly List<AttrDef> _attrs = new();
    /// <summary>A-14: the data-driven attributes (ids "wis", "agi", "lck", "end"; PlayerStats.AttrOf reads their points).</summary>
    public static IReadOnlyList<AttrDef> Attrs { get { Load(); return _attrs; } }
    public const int AttrBase = 5;
    /// <summary>The [min, max] of a stat-table key: the exact key, else res_*, *Chance, *P; null = uncapped.</summary>
    public static (float lo, float hi)? KeyCap(string k)
    {
        Load(); if (_capCache.TryGetValue(k, out var c)) return c;
        (float, float)? v = _keyCaps.TryGetValue(k, out var e) ? e
            : k.StartsWith("res_") && _keyCaps.TryGetValue("res_*", out var rs) ? rs
            : k.EndsWith("Chance") && _keyCaps.TryGetValue("*Chance", out var ch) ? ch
            : k.Length > 1 && k.EndsWith("P") && _keyCaps.TryGetValue("*P", out var pp) ? pp : null;
        _capCache[k] = v; return v;
    }
    static float ClampKey(string k, float v) => KeyCap(k) is { } c ? Mathf.Clamp(v, c.lo, c.hi) : v;
    /// <summary>A stat-table key total with its cap applied (Game.TS is clamped by Rebuild already; this also covers direct test edits).</summary>
    public static float Key(string k) => ClampKey(k, Game.TS[k]);

    // ---- stage contributions to stat-table keys (recorded by Rebuild, for the derived stats and the dump)
    static readonly List<(string key, StatStage st, string src, float v)> _mods = new();
    static readonly Dictionary<string, float> _clamped = new();   // key -> total before its cap
    public static int Ver { get; private set; }

    /// <summary>Game.RecalcTree: every permanent and timed modifier, stage by stage, into the stat table; then the caps.</summary>
    public static void Rebuild(PlayerStats d, TreeStats ts)
    {
        _mods.Clear(); _clamped.Clear(); Ver++;
        void Add(StatStage st, string src, IEnumerable<KeyValuePair<string, float>>? m) { if (m != null) foreach (var kv in m) if (kv.Value != 0) _mods.Add((kv.Key, st, src, kv.Value)); }
        var r = Races.Get(d.Race); Add(StatStage.Race, "race " + r.Name, r.Mods);
        foreach (var p in r.Passives) Add(StatStage.Race, "racial passive " + p.Name, p.Mods);   // F-35
        var c = Classes.Get(d.Class); Add(StatStage.Class, "class " + c.Name, c.Mods);
        foreach (var p in c.Passives) Add(StatStage.Class, "class passive " + p.Name, p.Mods);   // F-34
        // (A-13: a worn-out item, durability 0, gives nothing: no affixes, no gems, no base stats; Durability.cs)
        foreach (var kv in d.Eq) if (kv.Value?.Mods != null && !Durability.Broken(kv.Value)) Add(StatStage.Gear, kv.Value.Name, kv.Value.Mods);   // affixes, Golden Wings
        var tmp = new TreeStats(); LootTables.Apply(d, tmp); Add(StatStage.Gear, "set bonuses", tmp.All.ToList());
        foreach (var kv in d.Eq) if (!Durability.Broken(kv.Value) && kv.Value?.Gems is { } gems) foreach (var g in gems) Add(StatStage.Gems, kv.Value.Name + " gem", g);   // F-10
        tmp = new TreeStats(); TreeDb.Recalc(new HashSet<int>(d.Tree), tmp); Add(StatStage.Passives, $"skill tree ({d.Tree.Count} nodes)", tmp.All.ToList());
        tmp = new TreeStats(); Titles.Apply(d, tmp); Add(StatStage.Passives, "title " + d.Title, tmp.All.ToList());
        // life skills (F-24/F-25): each skill's level, plus the gem levels ("life_<id>" keys of the stages above), x its perLevel keys (data/life.json)
        LifeSkills.Load();
        foreach (var id in LifeSkills.Order)
        {
            float gem = 0; foreach (var m in _mods) if (m.key == "life_" + id) gem += m.v;
            int lv = LifeSkills.Level(d, id) + Math.Max(0, (int)gem);
            if (lv > 1) Add(StatStage.Life, $"{LifeSkills.Defs[id].Name} {lv}", LifeSkills.Mods(id, lv));
        }
        if (Player.Instance != null) foreach (var b in Player.Instance.Buffs) Add(StatStage.Buffs, "buff " + (Skills.Get(b.id)?.Name ?? b.id), b.mods);   // War Cry, Venom Blade...
        // A-14 attributes from data (Wisdom, Agility, Luck, Endurance): the hero's points past the starting 5, plus points granted as stat keys
        // of the stages above ("wis": 2 on gear, gems, passives, buffs), times the attribute's keys. Never counts below 0 points in total.
        Load();
        foreach (var a in _attrs)
        {
            float bonus = 0; foreach (var m in _mods) if (m.key == a.Id) bonus += m.v;
            float pts = Math.Max(0, d.AttrOf(a.Id) + bonus) - AttrBase; if (pts == 0) continue;
            string src = $"{a.Name} {d.AttrOf(a.Id)}{(bonus != 0 ? $" {bonus:+0.#;-0.#}" : "")} ({pts:+0.#;-0.#} past {AttrBase})";
            foreach (var kv in a.Keys) if (kv.Value != 0) _mods.Add((kv.Key, StatStage.Level, src, kv.Value * pts));
        }
        ts.Clear();
        foreach (var m in _mods) ts[m.key] += m.v;
        foreach (var kv in ts.All.ToList()) { float cl = ClampKey(kv.Key, kv.Value); if (cl != kv.Value) { _clamped[kv.Key] = kv.Value; ts[kv.Key] = cl; } }
    }

    // ---- derived stats: cached per PlayerStats
    static readonly ConditionalWeakTable<PlayerStats, StatSheet> _sheets = new();
    static int Sig(PlayerStats d)
    {
        var h = new HashCode(); var ts = Game.TS;
        h.Add(RuntimeHelpers.GetHashCode(ts)); h.Add(ts.Ver); h.Add(Ver); h.Add(d.Level); h.Add(d.Str); h.Add(d.Dex); h.Add(d.Vit); h.Add(d.Mag); h.Add(d.Race); h.Add(d.Class);
        foreach (var kv in d.Eq) { var it = kv.Value; if (it == null) continue; h.Add(RuntimeHelpers.GetHashCode(it)); h.Add(it.Dmg); h.Add(it.Def); h.Add(it.Hp); h.Add(it.Mp); h.Add(it.Sp); h.Add(it.Wt); h.Add(it.Mods?.Count ?? 0); h.Add(it.Gems?.Count ?? 0); h.Add(Durability.Broken(it)); h.Add(kv.Key); }
        h.Add(GemDb.Version);
        return h.ToHashCode();
    }
    public static int Builds { get; private set; }   // how often a sheet was rebuilt (perf check in the dump)
    /// <summary>The derived stats of this hero, rebuilt only when something they depend on changed.</summary>
    public static StatSheet Sheet(PlayerStats d)
    {
        int sig = Sig(d);
        if (_sheets.TryGetValue(d, out var s) && s.Sig == sig) return s;
        s = Build(d); s.Sig = sig; _sheets.AddOrUpdate(d, s); Builds++; return s;
    }
    public static float Get(PlayerStats d, string id) => Sheet(d).Lines.TryGetValue(id, out var l) ? l.Value : 0f;

    static StatSheet Build(PlayerStats d)
    {
        Load(); var sh = new StatSheet(); var race = Races.Get(d.Race); var cls = Classes.Get(d.Class);
        float T(string k, float def) => Tune.F("player." + k, def);
        int A(string a) => Math.Max(0, a switch { "Str" => d.Str, "Dex" => d.Dex, "Vit" => d.Vit, _ => d.Mag });   // attributes never count below 0
        StatLine New(string id, int round = 0)
        {
            var l = new StatLine { Id = id, RoundMode = round }; if (_derivedCaps.TryGetValue(id, out var c)) { l.Min = c.lo; l.Max = c.hi; }
            sh.Lines[id] = l; return l;
        }
        void Step(StatLine l, StatStage st, string src, float v, string kind = "flat") { if (v != 0 || kind == "more") l.Steps.Add(new StatStep { Stage = st, Src = src, Kind = kind, V = v }); }
        // attribute points split by where they came from: 5 base, race and class start bonus, the rest from level-up points
        void Attr(StatLine l, string a, float per)
        {
            int total = A(a), rb = race.Attr.GetValueOrDefault(a.ToLower()), cb = cls.Attr.GetValueOrDefault(a.ToLower()), rest = total - 5 - rb - cb;
            string P(float x) => x.ToString("0.###", CultureInfo.InvariantCulture);
            Step(l, StatStage.Base, $"5 {a} x {P(per)}", 5 * per); Step(l, StatStage.Race, $"{rb} {a} x {P(per)}", rb * per);
            Step(l, StatStage.Class, $"{cb} {a} x {P(per)}", cb * per); Step(l, StatStage.Level, $"{rest} {a} (points) x {P(per)}", rest * per);
            Key(l, a.ToLower(), "flat", per);   // attribute keys (str/dex/vit/mag: gems, F-10) count like points of that attribute
        }
        void Gear(StatLine l, Func<Item, float> f, float scale = 1, string kind = "flat") { foreach (var kv in d.Eq) if (kv.Value != null && !Durability.Broken(kv.Value) && f(kv.Value) != 0) Step(l, StatStage.Gear, kv.Value.Name, f(kv.Value) * scale, kind); }   // (A-13: not from worn-out items)
        // a stat-table key's contributions, stage by stage; whatever the table holds beyond them (a cap, a test edit) is one last step
        void Key(StatLine l, string key, string kind = "flat", float scale = 1, float mulBy = 1, string mulName = "")
        {
            float rec = 0;
            foreach (var m in _mods) if (m.key == key) { rec += m.v; Step(l, m.st, m.src + (mulName != "" ? $" ({m.v:0.##} {key} x {mulBy:0} {mulName})" : $" ({key} {m.v:0.##})"), m.v * scale * mulBy, kind); }
            float tot = StatPipeline.Key(key);
            if (Mathf.Abs(tot - rec) > 1e-4f) Step(l, StatStage.Buffs, _clamped.ContainsKey(key) ? $"{key} cap ({rec:0.##} -> {tot:0.##})" : $"{key} set directly ({tot - rec:+0.##;-0.##})", (tot - rec) * scale * mulBy, kind);
        }
        string LvlSrc(float per) => $"level {d.Level} x {per.ToString("0.###", CultureInfo.InvariantCulture)}";

        var hp = New("maxHp", 1); Step(hp, StatStage.Base, "hpBase", T("hpBase", 100)); Step(hp, StatStage.Level, LvlSrc(T("hpPerLevel", 8)), d.Level * T("hpPerLevel", 8));
        Attr(hp, "Vit", T("hpPerVit", 7)); Gear(hp, i => i.Hp); Key(hp, "hp"); Key(hp, "hpP", "pct:hpP");
        var mp = New("maxMp", 1); Step(mp, StatStage.Base, "mpBase", T("mpBase", 50)); Step(mp, StatStage.Level, LvlSrc(T("mpPerLevel", 2)), d.Level * T("mpPerLevel", 2));
        Attr(mp, "Mag", T("mpPerMag", 6)); Gear(mp, i => i.Mp); Key(mp, "mp"); Key(mp, "mpP", "pct:mpP");
        var st = New("maxSt", 1); Step(st, StatStage.Base, "staminaBase", T("staminaBase", 90)); Attr(st, "Dex", T("staminaPerDex", 3)); Key(st, "st");
        var df = New("defense", 2); Attr(df, "Vit", T("defensePerVit", .5f)); Gear(df, i => i.Def); Key(df, "def");
        var asp = New("atkSpeed"); Step(asp, StatStage.Base, "base", 1); Attr(asp, "Dex", T("attackSpeedPerDex", .012f)); Key(asp, "aspd", "pct:aspd");
        var offItem = DualWield.Off(d);   // A-12: a one-handed weapon in each hand
        if (offItem != null) Step(asp, StatStage.Gear, "dual wield (weapons.json dual.aspdP)", Weapons.Dual("aspdP", 15), "pct:dual wield");
        var cc = New("critChance"); Step(cc, StatStage.Base, "critBase", T("critBase", .05f)); Attr(cc, "Dex", T("critPerDex", .005f)); Key(cc, "crit", "flat", .01f);
        var cm = New("critMult"); Step(cm, StatStage.Base, "critDamage", T("critDamage", 1.8f)); Key(cm, "critD", "flat", .01f);
        var sp = New("spellPow"); Step(sp, StatStage.Base, "base", 1); Attr(sp, "Mag", T("spellPerMag", .08f)); Step(sp, StatStage.Level, LvlSrc(T("spellPerLevel", .02f)), d.Level * T("spellPerLevel", .02f));
        Key(sp, "spellP", "pct:spellP"); Gear(sp, i => i.Sp, 100, "pct:gear spell power"); Key(sp, "allP", "pct:allP");
        // weapon hit: flat (base, attributes x weapons.json scaling, item damage, conversions) x weapon factor x the matching bonus groups
        // One line per hand (A-12: "offDmg" is the off-hand weapon's own hit, x dual.offDamageMul). A worn-out weapon (A-13) hits as bare hands:
        // the fists' scaling and factor, no item damage. Ranged types that are not magic (bow, crossbow, javelin, knives) use the bow rules.
        void Wpn(string id, Item? item, string wt0, float handMul)
        {
            bool broken = Durability.Broken(item); string wt = broken ? "unarmed" : wt0; var w = Weapons.Get(wt); var wp = New(id); float wd = broken ? 0 : item?.Dmg ?? 0;
            bool magic = wt == "wand" || wt == "staff", bow = w.Ranged && !magic;
            Step(wp, StatStage.Base, magic ? "magicWeaponBase" : "weaponBase", magic ? T("magicWeaponBase", 4) : T("weaponBase", 5));
            if (magic) Attr(wp, "Mag", w.Mag);
            else { Attr(wp, "Str", w.Str); Attr(wp, "Dex", w.Dex); if (!bow) Attr(wp, "Mag", w.Mag); }
            if (wd != 0) Step(wp, StatStage.Gear, item!.Name + " damage", wd);
            if (bow) { Key(wp, "strToBow", "flat", 1, A("Str"), "Str"); Key(wp, "magToBow", "flat", 1, A("Mag"), "Mag"); }
            else if (!magic) Key(wp, "magToDmg", "flat", 1, A("Mag"), "Mag");
            Step(wp, StatStage.Base, broken ? $"{item!.Name} is worn out: bare hands factor" : $"{wt} damage factor (weapons.json)", w.Dmg, "more");
            if (handMul != 1) Step(wp, StatStage.Gear, "off hand (weapons.json dual.offDamageMul)", handMul, "more");
            if (bow) Key(wp, "rangedP", "pct:rangedP");
            else if (magic) { Key(wp, "spellP", "pct:spellP"); Gear(wp, i => i.Sp, 100, "pct:gear spell power"); }
            else Key(wp, "meleeP", "pct:meleeP");
            Key(wp, wt + "P", "pct:" + wt + "P");   // per weapon type, now for bows, wands and staves too (F-02 fix)
            if (w.Affinity.TryGetValue(d.Class, out var af) && af != 0) Step(wp, StatStage.Class, $"{cls.Name} affinity with {w.Name} (weapons.json)", af, "pct:class affinity");   // A-11
            Key(wp, "allP", "pct:allP");
        }
        Wpn("wpnDmg", d.Weapon, d.WType(), 1f);
        if (offItem != null) Wpn("offDmg", offItem, offItem.Wt ?? "sword", Weapons.Dual("offDamageMul", .7f));
        // shown for completeness (read through StatPipeline.Key by Skills.Compute and Player)
        var cdr = New("cdr"); Key(cdr, "cdr");
        var cs = New("castSpeed"); Step(cs, StatStage.Base, "base", 1); Key(cs, "castSpd", "flat", .01f);
        var mv = New("moveMul"); Step(mv, StatStage.Base, "base", 1); Key(mv, "move", "flat", .01f);

        foreach (var l in sh.Lines.Values) Finish(l);
        return sh;
    }

    static void Finish(StatLine l)
    {
        l.Steps.Sort((a, b) => a.Stage.CompareTo(b.Stage));   // stable enough: steps were added stage-ordered per source
        l.Flat = 0; l.Mul = 1; l.Groups.Clear(); var g = new Dictionary<string, float>(); var order = new List<string>();
        foreach (var s in l.Steps)
        {
            if (s.Kind == "flat") l.Flat += s.V;
            else if (s.Kind == "more") l.Mul *= s.V;
            else if (s.Kind.StartsWith("pct:")) { var k = s.Kind[4..]; if (!g.ContainsKey(k)) { g[k] = 0; order.Add(k); } g[k] += s.V; }
        }
        float v = l.Flat;
        foreach (var k in order) { float f = Mathf.Max(_pctFloor, 1 + g[k] / 100f); l.Groups.Add((k, g[k], f)); v *= f; }
        v *= l.Mul; if (float.IsNaN(v) || float.IsInfinity(v)) v = 0;
        l.Raw = v;
        if (l.RoundMode == 1) v = MathF.Round(v); else if (l.RoundMode == 2) v = MathF.Truncate(v);
        l.Value = Mathf.Clamp(v, l.Min, l.Max);
    }

    // ---- "/stats dump"
    static string N(float v) => v.ToString(Mathf.Abs(v) >= 100 ? "0.#" : "0.####", CultureInfo.InvariantCulture);
    public static string Dump(PlayerStats d)
    {
        var sh = Sheet(d); var sb = new StringBuilder(); var race = Races.Get(d.Race); var cls = Classes.Get(d.Class);
        sb.AppendLine($"=== STATS DUMP: {d.Name}, level {d.Level} {race.Name} {cls.Name}, weapon {d.WType()} ({d.Weapon?.Name ?? "none"}) ===");
        sb.AppendLine("pipeline: base > race > class > level > gear > gems > passives > life > buffs; flat adds, then each % group x(1+sum/100), then 'more' x, then round and cap");
        sb.AppendLine("attributes (A-14, data/stats.json): " + string.Join("; ", Attrs.Select(a => $"{a.Name} {d.AttrOf(a.Id)} -> per point past {AttrBase}: " + string.Join(", ", a.Keys.Select(k => $"{k.Key} {k.Value:+0.##;-0.##}")))));
        if (DualWield.Off(d) is { } dw) sb.AppendLine($"dual wield: off hand {dw.Name} ({dw.Wt}), attack speed +{Weapons.Dual("aspdP", 15):0}%, off-hand hit x{Weapons.Dual("offDamageMul", .7f):0.##}");
        foreach (var kv in d.Eq) if (kv.Value != null && Durability.Has(kv.Value)) sb.AppendLine($"durability {kv.Key}: {kv.Value.Name} {Durability.Cur(kv.Value):0.#}/{Durability.Max(kv.Value):0}{(Durability.Broken(kv.Value) ? "  WORN OUT (gives no stats)" : "")}");
        sb.AppendLine($"attributes: Str {d.Str} Dex {d.Dex} Vit {d.Vit} Mag {d.Mag} (5 base + race {Fmt(race.Attr)} + class {Fmt(cls.Attr)} + level-up points); unspent attr {d.AttrPts}, tree {d.SkillPts}, ranks {d.RankPts}");
        foreach (var l in sh.Lines.Values)
        {
            sb.AppendLine($"[{l.Id}] = {N(l.Value)}");
            foreach (var s in l.Steps) sb.AppendLine($"    {s.Stage.ToString().ToLower(),-8} {(s.Kind == "flat" ? (s.V >= 0 ? "+" : "") + N(s.V) : s.Kind == "more" ? "x" + N(s.V) : (s.V >= 0 ? "+" : "") + N(s.V) + "% " + s.Kind[4..]),-22} {s.Src}");
            var f = new StringBuilder($"    = flat {N(l.Flat)}"); foreach (var (g, p, fac) in l.Groups) f.Append($" x{N(fac)} ({g} {p:+0.##;-0.##}%{(fac > 1 + p / 100f + 1e-4f ? ", floored" : "")})");
            if (l.Mul != 1) f.Append($" x{N(l.Mul)} more"); f.Append($" = {N(l.Raw)}");
            if (l.RoundMode != 0) f.Append(l.RoundMode == 1 ? " -> round" : " -> truncate");
            f.Append($" -> cap [{N(l.Min)}, {N(l.Max)}] = {N(l.Value)}{(l.Value != (l.RoundMode == 1 ? MathF.Round(l.Raw) : l.RoundMode == 2 ? MathF.Truncate(l.Raw) : l.Raw) ? "  (CAPPED)" : "")}");
            sb.AppendLine(f.ToString());
        }
        sb.AppendLine("[stat table keys] total = sum of stages, then the key cap (data/stats.json keys)");
        foreach (var kv in Game.TS.All.Where(k => k.Value != 0 || _clamped.ContainsKey(k.Key)).OrderBy(k => k.Key, StringComparer.Ordinal))
        {
            var parts = _mods.Where(m => m.key == kv.Key).Select(m => $"{m.src} {m.v:+0.##;-0.##}");
            var cap = KeyCap(kv.Key); string capTxt = cap is { } c ? $" cap [{N(c.lo)}, {N(c.hi)}]" : "";
            sb.AppendLine($"    {kv.Key} = {N(kv.Value)}{capTxt}{(_clamped.TryGetValue(kv.Key, out var raw) ? $" (CAPPED from {N(raw)})" : "")}: {string.Join("; ", parts)}");
        }
        sb.AppendLine(GemDb.DumpLines(d));   // F-10: what sits in each worn item's sockets
        sb.AppendLine(LifeSkills.DumpLines(d));   // F-24/F-25: life and action skill levels and the keys they give
        sb.AppendLine($"[life bonuses] jump x{Player.JumpMul:0.###}, sprint stamina x{Player.SprintCostMul:0.###}, swim x{1 + Key("swimP") / 100f:0.###}, breath x{1 + Key("breathP") / 100f:0.###}, climb x{1 + Key("climbP") / 100f:0.###}, "
            + $"sneak speed x{Player.SneakSpeedMul:0.###}, notice range while sneaking x{Player.SneakNoticeMul:0.###}, lockpick sweet spot {Lockpick.SweetDeg():0.#} deg, dig time x{1f / (1 + Key("digP") / 100f):0.###}, "
            + $"mine extra drop {Key("mineDropChance"):0.#}% gem {Key("mineGemChance"):0.##}%, fish reel zone +{Key("fishEase"):0.#}% luck +{(int)Key("fishLuck")} lv, food x{1 + Key("foodP") / 100f:0.###} dur x{1 + Key("foodDurP") / 100f:0.###} double {Key("foodDoubleChance"):0.#}%, ride x{1 + Key("rideP") / 100f:0.###}");
        // skills on the bar: the resolved numbers (Skills.Compute) next to their inputs
        sb.AppendLine("[skills] dmg = base x rank (1 + rankP x (rank-1)) x supports x (1 + skillP); spell base = (dmg + lvl x level) x spellPow, weapon base = mult x wpnDmg");
        foreach (var id in d.Bar.Where(b => !string.IsNullOrEmpty(b)).Distinct())
        {
            var k = Skills.Get(id); if (k == null) continue; var stt = d.Skills.GetValueOrDefault(id); SkillCalc c;
            try { c = Skills.Compute(id); } catch (Exception e) { sb.AppendLine($"    {id}: compute failed {e.Message}"); continue; }
            string bas = k.Scale == "weapon" ? $"{N(k.Mult)} x wpnDmg {N(sh.Lines["wpnDmg"].Value)}" : $"({N(k.Dmg)} + {N(k.Lvl)} x {d.Level}) x spellPow {N(sh.Lines["spellPow"].Value)}";
            sb.AppendLine($"    {id} rank {stt?.Rank ?? 1}{(c.GemRank > 0 ? $" +{c.GemRank} from gems (skill_{id} / school_*)" : "")} [{string.Join(",", stt?.Sup.Where(x => x != "") ?? Enumerable.Empty<string>())}]: base {bas} -> dmg {N(c.Dmg)}; cd {N(k.Cd)}s -> {N(c.Cd)}s (cdr {N(Key("cdr"))}%); mana {N(k.Mp)} -> {N(c.Mp)} (mcost {N(Key("mcost"))}%); cast {N(k.Cast)}s -> {N(c.Cast)}s");
        }
        sb.AppendLine($"[xp] level {d.Level}: {d.Xp} / {d.XpNeed()}  (xpP {N(Key("xpP"))}%); next levels need " + string.Join(", ", Enumerable.Range(d.Level + 1, 4).Select(Progression.XpNeed)));
        sb.AppendLine($"[perf] sheets built so far: {Builds}, stage contributions: {_mods.Count}");
        sb.AppendLine(SelfCheck(d));
        return sb.ToString();
    }

    /// <summary>
    /// Pushes every stat key to -1e6 and +1e6 in turn (for this hero and a probe hero with -50 in every attribute) and checks that every
    /// derived stat, capped key and hotbar skill number stays finite, non-negative where it must, and inside its cap. The table is restored.
    /// </summary>
    public static string SelfCheck(PlayerStats d)
    {
        var ts = Game.TS; var snap = ts.All.ToList(); var bad = new List<string>(); int n = 0;
        string[] keys = { "hp", "hpP", "mp", "mpP", "st", "def", "aspd", "crit", "critD", "spellP", "allP", "meleeP", "rangedP", "swordP", "magToDmg", "strToBow",
            "cdr", "castSpd", "mcost", "skillP", "aoeP", "move", "evade", "res_fire", "xpP", "leech", "tenacity", "healIn" };
        var probe = new PlayerStats { Level = 1, Str = -50, Dex = -50, Vit = -50, Mag = -50 };
        void Ok(string what, float v, float lo, float hi) { n++; if (float.IsNaN(v) || float.IsInfinity(v) || v < lo - 1e-3f || v > hi + 1e-3f) bad.Add($"{what}={v}"); }
        try
        {
            foreach (var ext in new[] { -1e6f, 1e6f })
                foreach (var k in keys)
                {
                    float was = ts[k]; ts[k] = ext;
                    foreach (var pd in new[] { probe, d }) foreach (var l in Sheet(pd).Lines.Values) Ok($"{l.Id} ({k} {ext:0e0})", l.Value, l.Min, l.Max);
                    if (KeyCap(k) is { } c) Ok($"key {k} ({ext:0e0})", Key(k), c.lo, c.hi);
                    foreach (var id in d.Bar.Where(b => !string.IsNullOrEmpty(b) && Skills.Get(b) != null).Distinct())
                    {
                        var sc = Skills.Compute(id); string w = $"{id} ({k} {ext:0e0})";
                        Ok(w + " dmg", sc.Dmg, 0, float.MaxValue); Ok(w + " cd", sc.Cd, 0, float.MaxValue); Ok(w + " mana", sc.Mp, 0, float.MaxValue); Ok(w + " cast", sc.Cast, 0, float.MaxValue);
                    }
                    ts[k] = was;
                }
        }
        finally { ts.Clear(); foreach (var kv in snap) ts[kv.Key] = kv.Value; }
        return $"[check] {n} values under extreme stats (each of {keys.Length} keys at -1e6 and +1e6; this hero and a probe with -50 attributes): "
            + (bad.Count == 0 ? "all finite, none negative, all within caps" : $"{bad.Count} FAILED: " + string.Join(", ", bad.Take(12)));
    }
    static string Fmt(Dictionary<string, int> a) => a.Count == 0 ? "-" : string.Join(" ", a.Select(kv => $"{kv.Key} {kv.Value:+0;-0}"));
}
