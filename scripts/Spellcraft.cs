using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A spell the player built from cards (Spellcraft menu, B). Secs[0] = section I; each later section (II, III) goes off where
/// the one before it ends. Each holds card ids from data/spellcards.json; copies of a card appear more than once.</summary>
public class CraftedSpell
{
    public string Id = "", Name = "";
    /// <summary>The player's icon for it ("" = automatic): lib:/ed:/png: data (SpellIcon.cs), saved with the spell, capped in size.</summary>
    public string Icon = "";
    public List<List<string>> Secs = new();
    /// <summary>Saves from before three sections (A = first, B = second): moved into <see cref="Secs"/> by <see cref="Migrate"/>.</summary>
    public List<string> A = new(), B = new();

    /// <summary>Section i (0-based), created empty if missing.</summary>
    public List<string> Sec(int i) { Migrate(); while (Secs.Count <= i) Secs.Add(new()); return Secs[i]; }
    /// <summary>Old A/B saves become sections; null lists are mended. Safe to call any number of times.</summary>
    public void Migrate()
    {
        Secs ??= new(); for (int i = 0; i < Secs.Count; i++) Secs[i] ??= new();
        if ((A != null && A.Count > 0) || (B != null && B.Count > 0))
        {
            if (Secs.All(x => x.Count == 0)) { Secs.Clear(); Secs.Add(A ?? new()); if (B != null && B.Count > 0) Secs.Add(B); }
            A = new(); B = new();
        }
        A ??= new(); B ??= new();
    }
    /// <summary>The sections up to the last one holding cards (trailing empty sections left off).</summary>
    public List<List<string>> Used() { Migrate(); int n = Secs.Count; while (n > 0 && Secs[n - 1].Count == 0) n--; return Secs.Take(n).ToList(); }
    public IEnumerable<string> AllCards { get { Migrate(); return Secs.SelectMany(x => x); } }
    public int Count => AllCards.Count();
    public CraftedSpell Copy() { Migrate(); return new CraftedSpell { Id = Id, Name = Name, Icon = Icon ?? "", Secs = Secs.Select(x => x.ToList()).ToList() }; }
}

public class CardDef
{
    public string Id = "", Type = "", Family = "", Name = "", Adj = "", Desc = "", Hint = "", From = "", EType = "physical";
    public int Req = 1; public float W = 1, Leech, Knock, Stun, Heal;
    public Dictionary<string, float> Set = new(), Buff = new(), WardBuff = new(), X = new();
    public Dictionary<string, float[]> Status = new();
    public Dictionary<string, string> Fx = new();
    public SupportDef? Mod;   // modifier cards: what they do, as a support (registered with Skills as "card.<id>")
}

/// <summary>A node of the spell tree (data/spellcards.json "tree"): ranks bought with spell tree points, only crafted spells feel them.</summary>
public class CraftNode
{
    public string Id = "", Branch = "", Name = "", Desc = "", Stat = "", Req = ""; public int Max = 1, At; public float Per;
    /// <summary>The description at a rank (rank 0 shows what the first rank gives).</summary>
    public string Text(int rank) => Desc.Replace("{v}", (Per * Math.Max(1, rank)).ToString("0.#"));
}

/// <summary>
/// Spellcraft, in the spirit of Two Worlds II: skills built from cards. A carrier card says how the skill is delivered (it copies the
/// numbers of an existing skill), an effect card says what it does (damage type, statuses, buffs), modifier cards change it; extra copies
/// stack. Crafted spells are registered with <see cref="Skills"/> as ordinary skills (ids "cs1", "cs2"...), so they rank up, take
/// support modifiers, sit on the hotbar and in macros like any other skill. Numbers in data/spellcards.json.
/// </summary>
public static class Spellcraft
{
    public const string Prefix = "cs", ModPrefix = "card.", Second = ".2";
    static readonly Dictionary<string, CardDef> _c = new();
    public static readonly List<string> Order = new();
    static readonly Dictionary<string, string> _base = new();    // crafted skill id -> the skill its carrier copies (icons, per-skill special cases)
    static readonly Dictionary<string, string> _fx = new();      // crafted skill id -> the skill whose animations it plays
    static readonly HashSet<string> _registered = new();
    static JsonElement _root; static JsonDocument? _doc;
    public static int MaxSpells = 8;

    // ---------------------------------------------------------------- sections (data/spellcards.json "sections")
    /// <summary>Sections a spell may chain (I, II, III).</summary>
    public static int MaxSections { get { Load(); return Math.Clamp((int)R("sections.max", 3), 1, 6); } }
    /// <summary>The skill id of section i of a crafted spell: the spell's own id for section I, "id.2", "id.3" after it.</summary>
    public static string SecId(string id, int i) => i == 0 ? id : id + "." + (i + 1);
    public static bool IsSectionId(string id) => id.Length > 2 && id[^2] == '.' && char.IsDigit(id[^1]);
    /// <summary>The crafted spell a section belongs to (its rank and sockets apply to every section).</summary>
    public static string RootOf(string id) => IsSectionId(id) ? id[..^2] : id;
    public static string Roman(int i) => i switch { 0 => "I", 1 => "II", 2 => "III", 3 => "IV", 4 => "V", _ => (i + 1).ToString() };
    /// <summary>A per-section number from "sections" (a list indexed by section; the last entry repeats).</summary>
    public static float SecNum(string key, int i, float def)
    {
        Load();
        if (!_root.TryGetProperty("sections", out var s) || !s.TryGetProperty(key, out var a)) return def;
        if (a.ValueKind == JsonValueKind.Number) return a.GetSingle();
        if (a.ValueKind != JsonValueKind.Array || a.GetArrayLength() == 0) return def;
        var e = a[Math.Min(i, a.GetArrayLength() - 1)]; return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : def;
    }
    /// <summary>Hero level that opens section i.</summary>
    public static int SectionLevel(int i) => (int)SecNum("unlockLevel", i, i < 2 ? 1 : 12);
    /// <summary>How many sections this hero can chain now.</summary>
    public static int SectionsOpen(PlayerStats d) { int n = 1; while (n < MaxSections && d.Level >= SectionLevel(n)) n++; return n; }

    static void Load()
    {
        if (_doc != null) return;
        _doc = JsonUtil.Load("res://data/spellcards.json"); _root = _doc.RootElement; MaxSpells = (int)JsonUtil.F(_root, "maxSpells", 8);
        foreach (var p in _root.GetProperty("cards").EnumerateObject())
        {
            var e = p.Value; var c = new CardDef
            {
                Id = p.Name, Type = JsonUtil.S(e, "type"), Family = JsonUtil.S(e, "family", "spell"), Name = JsonUtil.S(e, "n", p.Name), Adj = JsonUtil.S(e, "adj", JsonUtil.S(e, "n", p.Name)),
                Desc = JsonUtil.S(e, "d"), Hint = JsonUtil.S(e, "stackHint"), From = JsonUtil.S(e, "from"), EType = JsonUtil.S(e, "etype", "physical"),
                Req = (int)JsonUtil.F(e, "req", 1), W = JsonUtil.F(e, "w", 1), Leech = JsonUtil.F(e, "leech"), Knock = JsonUtil.F(e, "knock"), Stun = JsonUtil.F(e, "stun"), Heal = JsonUtil.F(e, "heal"),
                Set = Nums(e, "set"), Buff = Nums(e, "buff"), WardBuff = Nums(e, "ward"), X = Nums(e, "x"), Status = Stat(e, "status"),
            };
            if (e.TryGetProperty("fx", out var fx)) foreach (var q in fx.EnumerateObject()) c.Fx[q.Name] = q.Value.GetString() ?? "";
            if (c.Type == "carrier" && Skills.Get(c.From) == null) { GD.PrintErr($"[SPELLCRAFT] card {c.Id}: unknown skill '{c.From}'"); continue; }
            if (c.Type == "modifier")
            {
                var s = new SupportDef { Id = ModPrefix + c.Id, Name = c.Name, Desc = c.Desc };
                if (e.TryGetProperty("mods", out var m))
                {
                    foreach (var x in m.EnumerateObject())
                        if (x.Value.ValueKind == JsonValueKind.Number) s.Num[x.Name] = x.Value.GetSingle();
                        else if (x.Name == "auto") s.Auto = x.Value.GetString() ?? "";
                        else if (x.Name == "convert") s.Convert = x.Value.GetString() ?? "";
                    s.Status = Stat(m, "status");
                }
                c.Mod = s; Skills.RegisterSupport(s);
            }
            _c[c.Id] = c; Order.Add(c.Id);
        }
        if (_root.TryGetProperty("tree", out var tr))
        {
            TreePer = (int)JsonUtil.F(tr, "perLevels", 2);
            if (tr.TryGetProperty("branches", out var br)) foreach (var b in br.EnumerateObject()) Branches.Add((b.Name, b.Value.GetString() ?? b.Name));
            if (tr.TryGetProperty("nodes", out var ns))
                foreach (var p in ns.EnumerateObject())
                {
                    var e = p.Value;
                    Nodes.Add(new CraftNode { Id = p.Name, Branch = JsonUtil.S(e, "branch"), Name = JsonUtil.S(e, "n", p.Name), Desc = JsonUtil.S(e, "d"), Stat = JsonUtil.S(e, "stat"), Req = JsonUtil.S(e, "req"), Max = (int)JsonUtil.F(e, "max", 1), At = (int)JsonUtil.F(e, "at", 0), Per = JsonUtil.F(e, "per", 0) });
                }
        }
    }

    // ---------------------------------------------------------------- the spell tree (data/spellcards.json "tree")
    public static readonly List<CraftNode> Nodes = new();
    public static readonly List<(string id, string name)> Branches = new();
    static int TreePer = 2;
    public static int TreeEvery { get { Load(); return Math.Max(1, TreePer); } }
    static PlayerStats? _hero;   // whose tree applies while spells are built (set when a hero's spells are synced)
    public static IEnumerable<CraftNode> TreeNodes { get { Load(); return Nodes; } }
    public static IEnumerable<(string id, string name)> TreeBranches { get { Load(); return Branches; } }
    public static int Rank(PlayerStats d, string node) => (d.CraftTree ??= new()).GetValueOrDefault(node);
    public static int TreePoints(PlayerStats d) { Load(); return d.Level / Math.Max(1, TreePer); }
    public static int TreeSpent(PlayerStats d) => (d.CraftTree ??= new()).Values.Sum();
    public static int TreeSpentIn(PlayerStats d, string branch) => Nodes.Where(n => n.Branch == branch).Sum(n => Rank(d, n.Id));
    public static int TreeFree(PlayerStats d) => TreePoints(d) - TreeSpent(d);
    /// <summary>Total of a spell tree stat for this hero (e.g. dmgP 18 = +18%).</summary>
    public static float TreeStat(PlayerStats? d, string stat) { if (d == null) return 0; Load(); float t = 0; foreach (var n in Nodes) if (n.Stat == stat) t += n.Per * Rank(d, n.Id); return t; }
    static float T(string stat) => TreeStat(_hero ?? Game.D, stat);
    /// <summary>Why this node cannot take another rank now, or null.</summary>
    public static string? CanBuy(PlayerStats d, CraftNode n)
    {
        int r = Rank(d, n.Id);
        if (r >= n.Max) return "Fully learned";
        if (TreeFree(d) <= 0) return $"No spell tree points left (one every {TreePer} levels)";
        if (n.Req != "" && Rank(d, n.Req) <= 0) return $"Needs {Nodes.FirstOrDefault(x => x.Id == n.Req)?.Name ?? n.Req} first";
        if (TreeSpentIn(d, n.Branch) < n.At) return $"Spend {n.At} points in this branch first";
        return null;
    }
    public static string? Buy(PlayerStats d, CraftNode n)
    {
        var why = CanBuy(d, n); if (why != null) return why;
        d.CraftTree[n.Id] = Rank(d, n.Id) + 1; Rebuild(d); return null;
    }
    /// <summary>Gives every spell tree point back (free). Spells that no longer fit their card limits stay as they are until edited.</summary>
    public static void ResetTree(PlayerStats d) { (d.CraftTree ??= new()).Clear(); Rebuild(d); }
    /// <summary>Spell tree changed: rebuild the hero's crafted spells with the new numbers.</summary>
    public static void Rebuild(PlayerStats d) { _hero = d; foreach (var s in d.Spells) Register(s); }
    /// <summary>Spells the amulet holds for this hero (base + Wider Amulet).</summary>
    public static int SpellCap(PlayerStats d) { Load(); return MaxSpells + (int)TreeStat(d, "spells"); }
    static Dictionary<string, float> Nums(JsonElement e, string key)
    {
        var d = new Dictionary<string, float>(); if (!e.TryGetProperty(key, out var o) || o.ValueKind != JsonValueKind.Object) return d;
        foreach (var x in o.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) d[x.Name] = x.Value.GetSingle();
        return d;
    }
    static Dictionary<string, float[]> Stat(JsonElement e, string key)
    {
        var d = new Dictionary<string, float[]>(); if (!e.TryGetProperty(key, out var s)) return d;
        foreach (var x in s.EnumerateObject()) d[x.Name] = x.Value.EnumerateArray().Select(v => v.GetSingle()).ToArray();
        return d;
    }
    static float R(string path, float def)
    {
        Load(); var e = _root;
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return def; e = n; }
        return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : def;
    }

    static List<string> RList(string path)
    {
        Load(); var e = _root; var l = new List<string>();
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return l; e = n; }
        if (e.ValueKind == JsonValueKind.Array) foreach (var x in e.EnumerateArray()) l.Add(x.GetString() ?? "");
        return l;
    }
    static bool RBool(string path, bool def)
    {
        Load(); var e = _root;
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return def; e = n; }
        return e.ValueKind switch { JsonValueKind.True => true, JsonValueKind.False => false, JsonValueKind.Number => e.GetSingle() != 0, _ => def };
    }

    // ---------------------------------------------------------------- F-13 lingering zones (data/spellcards.json "zones")
    /// <summary>A number from "zones" (tick, minTick, dur, rankP, maxPerCaster, maxTicks).</summary>
    public static float ZoneNum(string key, float def) => R("zones." + key, def);
    /// <summary>A zone's follow-ups cannot make zones of their own unless this is false (or the section holds a nest card).</summary>
    public static bool ZoneNoNested => RBool("zones.noNested", true);
    /// <summary>True when this section's cards make a lingering zone: an AoE carrier plus a duration card in the same section.</summary>
    public static bool IsZoneSection(List<string> cards)
    {
        if (cards.Count == 0) return false; var car = RList("zones.carriers"); var du = RList("zones.durationCards");
        if (car.Count == 0) car.Add("area"); if (du.Count == 0) du.Add("time");
        return cards.Any(car.Contains) && cards.Any(du.Contains);
    }
    /// <summary>Index of the first zone section of a spell (it and every section after it repeat on each tick), or -1.</summary>
    public static int ZoneSectionOf(CraftedSpell s) { var u = s.Used(); for (int i = 0; i < u.Count; i++) if (IsZoneSection(u[i])) return i; return -1; }
    /// <summary>Tick seconds and duration of a zone section with these duration / tick multipliers at this rank.</summary>
    public static (float tick, float dur) ZoneTiming(float durMul, float tickMul, int rank)
    {
        float tick = Mathf.Max(ZoneNum("minTick", .25f), ZoneNum("tick", 1f) * Mathf.Max(.25f, tickMul));
        float dur = ZoneNum("dur", 4f) * Mathf.Max(.3f, durMul) * (1 + ZoneNum("rankP", 5f) / 100f * Mathf.Max(0, rank - 1));
        return (tick, dur);
    }

    public static CardDef? Card(string id) { Load(); return _c.GetValueOrDefault(id); }
    public static IEnumerable<CardDef> All { get { Load(); foreach (var k in Order) yield return _c[k]; } }
    public static bool IsCrafted(string id) => id.StartsWith(Prefix) && _base.ContainsKey(id);
    /// <summary>The skill a crafted skill's carrier copies (or the id itself): used for animations, icons and per-skill special cases.</summary>
    public static string Base(string id) => _base.TryGetValue(id, out var b) ? b : id;
    /// <summary>The skill whose animations (data/spellfx.json) a crafted skill plays: spells take their element's look, weapon arts keep their carrier's.</summary>
    public static string FxOf(string id) => _fx.TryGetValue(id, out var b) ? b : id;

    // ---------------------------------------------------------------- rules
    /// <summary>Cards a spell of this family may hold (all sections together; open sections add "sections.cards").</summary>
    public static int MaxCards(PlayerStats d, string family)
    {
        Load(); float per = Mathf.Max(1, R("slots.perLevels", 5));
        int bonus = (int)R($"classSlots.{d.Class}.{family}", 0), tree = (int)TreeStat(d, "cards"), sec = 0;
        for (int i = 0; i < SectionsOpen(d); i++) sec += (int)SecNum("cards", i, 0);
        return Math.Min((int)R("slots.max", 10) + tree + sec, (int)R("slots.base", 4) + (int)(d.Level / per) + bonus + tree + sec);
    }
    /// <summary>Copies of one card a spell may hold (magic mastery, plus the spell tree).</summary>
    public static int MaxCopies(PlayerStats d) { Load(); int tree = (int)TreeStat(d, "copies"); return Math.Min((int)R("mastery.max", 6) + tree, (int)R("mastery.base", 2) + (int)(d.Level / Mathf.Max(1, R("mastery.perLevels", 6))) + tree); }
    public static IEnumerable<string> SecondCarriers { get { Load(); if (_root.TryGetProperty("second", out var s) && s.TryGetProperty("carriers", out var a)) foreach (var x in a.EnumerateArray()) yield return x.GetString() ?? ""; } }

    /// <summary>Copies of a card owned and not held by another crafted spell (the one being edited, skip, counts as free).</summary>
    public static int Free(PlayerStats d, string card, string skip = "")
    {
        int n = d.Cards.GetValueOrDefault(card);
        foreach (var s in d.Spells) if (s.Id != skip) n -= s.AllCards.Count(x => x == card);
        return n;
    }
    public static string FamilyOf(CraftedSpell s) { var c = s.Sec(0).Select(Card).FirstOrDefault(x => x?.Type == "carrier"); return c?.Family ?? "spell"; }

    /// <summary>Why the cards in section idx cannot make a spell, or null when they can.</summary>
    public static string? CheckSection(List<string> cards, int idx)
    {
        var defs = cards.Select(Card).ToList(); if (defs.Any(x => x == null)) return "Unknown card";
        var car = defs.Where(x => x!.Type == "carrier").Select(x => x!.Id).Distinct().ToList();
        var eff = defs.Where(x => x!.Type == "effect").Select(x => x!.Id).Distinct().ToList();
        string sec = $"Section {Roman(idx)}";
        if (car.Count == 0) return $"{sec} needs a carrier card (how it is delivered)";
        if (car.Count > 1) return $"{sec} can hold only one kind of carrier (more copies of it are fine)";
        if (eff.Count == 0) return $"{sec} needs an effect card (what it does)";
        if (eff.Count > 1) return $"{sec} can hold only one kind of effect (more copies of it are fine)";
        if (idx > 0 && !SecondCarriers.Contains(car[0])) return $"{Card(car[0])!.Name} cannot be a chained section (use {string.Join(", ", SecondCarriers.Select(x => Card(x)?.Name ?? x))})";
        return null;
    }
    /// <summary>Why the sections themselves do not make a spell (empty gaps, too many sections, a bad section), ignoring the hero.</summary>
    static string? CheckShape(CraftedSpell s)
    {
        var used = s.Used();
        if (used.Count == 0) return "Put a carrier and an effect card in section I";
        if (used.Count > MaxSections) return $"A spell holds at most {MaxSections} sections";
        for (int i = 0; i < used.Count; i++)
        {
            if (used[i].Count == 0) return $"Section {Roman(i)} is empty: fill it or empty the sections after it";
            var err = CheckSection(used[i], i); if (err != null) return err;
        }
        return null;
    }
    /// <summary>Everything that stops this spell from being forged by this hero, or null.</summary>
    public static string? Check(PlayerStats d, CraftedSpell s)
    {
        Load();
        var err = CheckShape(s); if (err != null) return err;
        var used = s.Used();
        if (used.Count > SectionsOpen(d)) return $"Section {Roman(SectionsOpen(d))} opens at level {SectionLevel(SectionsOpen(d))}";
        string fam = FamilyOf(s); int max = MaxCards(d, fam), n = s.Count;
        if (n > max) return $"Too many cards: {n} of {max} for a {fam} spell at your level";
        int mc = MaxCopies(d);
        foreach (var g in s.AllCards.GroupBy(x => x))
        {
            var c = Card(g.Key)!; int need = g.Count();
            if (c.Req > d.Level) return $"{c.Name} needs level {c.Req}";
            if (used.Any(sec => sec.Count(x => x == g.Key) > mc)) return $"At most {mc} copies of one card per section at your level";
            if (Free(d, g.Key, s.Id) < need) return $"Not enough {c.Name} cards (you have {Free(d, g.Key, s.Id)} free)";
        }
        return null;
    }
    public static string AutoName(CraftedSpell s)
    {
        string Part(List<string> l)
        {
            var car = l.Select(Card).FirstOrDefault(x => x?.Type == "carrier"); var eff = l.Select(Card).FirstOrDefault(x => x?.Type == "effect");
            return car == null ? "" : (eff != null ? eff.Adj + " " : "") + car.Name;
        }
        var parts = s.Used().Select(Part).ToList();
        if (parts.Count == 0 || parts[0] == "") return "New spell";
        return string.Join(" + ", parts.Where(x => x != ""));
    }

    // ---------------------------------------------------------------- building skills
    static SkillDef Clone(SkillDef t) => new()
    {
        Id = t.Id, Name = t.Name, Kind = t.Kind, Type = t.Type, Scale = t.Scale, Desc = t.Desc, Tags = new(t.Tags),
        Mp = t.Mp, St = t.St, Cd = t.Cd, Cast = t.Cast, Dmg = t.Dmg, Lvl = t.Lvl, Mult = t.Mult, Radius = t.Radius, Speed = t.Speed, Spread = t.Spread, Dur = t.Dur, Tick = t.Tick, Heal = t.Heal,
        Knock = t.Knock, Stun = t.Stun, Up = t.Up, Arc = t.Arc, Range = t.Range, Delay = t.Delay, Leech = t.Leech, Taunt = t.Taunt, X = new(t.X),
        Count = t.Count, Pierce = t.Pierce, Chain = t.Chain, Req = t.Req, Cost = t.Cost, Homing = t.Homing, Self = t.Self, Behind = t.Behind, Crit = t.Crit, Arrow = t.Arrow,
        Status = t.Status.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone()), Buff = new(t.Buff),
    };
    static readonly string[] Elements = { "fire", "frost", "lightning", "poison", "holy", "shadow", "arcane" };

    /// <summary>One section as a skill: the carrier's skill with the effect laid over it, stacking bonuses and the modifier cards.</summary>
    static SkillDef BuildSection(string id, List<string> cards, int sec, out string fxId)
    {
        Load(); var defs = cards.Select(x => _c[x]).ToList();
        var car = defs.First(x => x.Type == "carrier"); var eff = defs.First(x => x.Type == "effect");
        int nCar = defs.Count(x => x == car), nEff = defs.Count(x => x == eff);
        var k = Clone(Skills.Get(car.From)!); k.Id = id; k.Crafted = true; k.Taunt = 0;
        foreach (var (key, v) in car.Set) SetNum(k, key, v);
        // ---- the effect: damage type, statuses, extra knock, life steal, and (for blessings and wards) what they grant
        k.Type = eff.EType; k.Tags.RemoveAll(t => Elements.Contains(t) || t == "physical"); k.Tags.Add(eff.EType);
        k.Status = eff.Status.ToDictionary(kv => kv.Key, kv => (float[])kv.Value.Clone());
        k.Leech += eff.Leech; k.Knock += eff.Knock; k.Stun += eff.Stun;
        bool bless = k.Kind is "enchant" or "buff";
        if (bless) { k.Buff = new(eff.Buff); foreach (var (xk, xv) in eff.X) k.X[xk] = xv; k.X.Remove("cleanse"); if (eff.Heal > 0) k.Heal = eff.Heal; }
        else if (k.Kind == "ward") k.Buff = new(eff.WardBuff);
        // ---- boosters: extra copies of the effect hit harder, extra copies of the carrier reach farther
        float em = 1 + (R("stack.effectP", 22) + T("effectP")) / 100f * (nEff - 1), sd = 1 + R("stack.statusDurP", 12) / 100f * (nEff - 1);
        k.Dmg *= em; k.Lvl *= em; k.Mult *= em; if (bless || k.Kind == "ward") k.Heal *= em;
        foreach (var s in k.Status.Values) if (s.Length > 1) s[1] *= sd;
        foreach (var b in k.Buff.Keys.ToList()) if (b is not ("statusImmune" or "ccImmune")) k.Buff[b] *= em;
        if (k.X.ContainsKey("hot")) k.X["hot"] *= em;
        int extra = nCar - 1;
        if (extra > 0)
        {
            float G(string key) => 1 + R("stack.carrier." + key, 0) / 100f * extra;
            k.Radius *= G("radius"); k.Speed *= G("speed"); k.Dur *= G("dur"); k.Range *= G("range");
        }
        // ---- cost: every card past the first two raises it
        float cm = 1 + R("stack.costP", 10) / 100f * Mathf.Max(0, cards.Count - 2); k.Mp *= cm; k.St *= cm;
        // ---- the spell tree (Spellcraft menu > Spell tree)
        float tp = Mathf.Max(.1f, 1 + (T("dmgP") + (sec > 0 ? T("secondP") : 0) + SecNum("powerP", sec, 0)) / 100f);
        k.Dmg *= tp; k.Lvl *= tp; k.Mult *= tp; if (bless || k.Kind == "ward") k.Heal *= tp; if (k.X.ContainsKey("hot")) k.X["hot"] *= tp;
        float tc = Mathf.Max(.2f, 1 + T("costP") / 100f); k.Mp *= tc; k.St *= tc;
        k.Cd *= Mathf.Max(.2f, 1 + T("cdP") / 100f); k.Cast *= Mathf.Max(.2f, 1 + T("castP") / 100f);
        k.Radius *= 1 + T("aoeP") / 100f;
        float ts = 1 + T("statusP") / 100f; foreach (var s in k.Status.Values) if (s.Length > 1) s[1] *= ts;
        k.CardMods = defs.Where(x => x.Type == "modifier").Select(x => x.Mod!.Id).ToList();
        // F-13: an AoE carrier with a duration card in this section lingers as a zone (PlayerSpellZones.cs)
        k.X.Remove("zone"); k.X.Remove("nestZones");
        if (IsZoneSection(cards)) k.X["zone"] = 1;
        { var nest = RList("zones.nestCards"); if (cards.Any(nest.Contains)) k.X["nestZones"] = 1; }
        k.Req = defs.Max(x => x.Req); k.Cost = 0;
        k.Name = (eff.Adj + " " + car.Name).Trim();
        var mods = defs.Where(x => x.Type == "modifier").GroupBy(x => x.Name).Select(g => g.Count() > 1 ? $"{g.Key} x{g.Count()}" : g.Key).ToList();
        k.Desc = $"{car.Desc} {eff.Desc}" + (nCar > 1 ? $" ({nCar} {car.Name} cards)" : "") + (nEff > 1 ? $" ({nEff} {eff.Name} cards: +{(em - 1) * 100:0}% power)" : "") + (mods.Count > 0 ? "  Modifiers: " + string.Join(", ", mods) + "." : "");
        fxId = car.Family == "spell" ? eff.Fx.GetValueOrDefault(k.Kind, car.From) : car.From;
        if (Skills.Get(fxId) == null) fxId = car.From;
        _base[id] = car.From;
        return k;
    }
    static void SetNum(SkillDef k, string key, float v)
    {
        switch (key)
        {
            case "mp": k.Mp = v; break; case "st": k.St = v; break; case "cd": k.Cd = v; break; case "cast": k.Cast = v; break; case "dmg": k.Dmg = v; break; case "lvl": k.Lvl = v; break;
            case "mult": k.Mult = v; break; case "radius": k.Radius = v; break; case "speed": k.Speed = v; break; case "dur": k.Dur = v; break; case "tick": k.Tick = v; break;
            case "range": k.Range = v; break; case "count": k.Count = (int)v; break; case "chain": k.Chain = (int)v; break; case "heal": k.Heal = v; break; case "knock": k.Knock = v; break;
            default: k.X[key] = v; break;
        }
    }

    /// <summary>Builds a crafted spell into skills (sections II and III as hidden skills "id.2", "id.3", each one's Then naming the next)
    /// and registers them. Returns null if the cards do not make a spell.</summary>
    public static SkillDef? Register(CraftedSpell s, bool listed = true)
    {
        Load(); if (CheckShape(s) != null) return null;
        var used = s.Used();
        var k = BuildSection(s.Id, used[0], 0, out var bid); var prev = k;
        for (int i = 1; i < used.Count; i++)
        {
            var f = BuildSection(SecId(s.Id, i), used[i], i, out var fb); f.Name = k.Name + $" (section {Roman(i)})";
            prev.Then = f.Id; float cp = SecNum("costP", i, 60) / 100f;
            k.Mp += f.Mp * cp; k.St += f.St * cp; k.Cd += f.Cd * SecNum("cdP", i, 30) / 100f;
            k.Desc += (i == 1 ? "  Where it ends: " : "  Then: ") + f.Desc; k.Req = Math.Max(k.Req, f.Req);
            Skills.Register(f, false); _fx[f.Id] = fb; _registered.Add(f.Id); prev = f;
        }
        prev.Then = "";
        for (int i = used.Count; i < 6; i++) { var x = SecId(s.Id, i); Skills.Unregister(x); _base.Remove(x); _fx.Remove(x); _registered.Remove(x); }
        k.Name = s.Name != "" ? s.Name : AutoName(s);
        Skills.Register(k, listed); _fx[k.Id] = bid; _registered.Add(k.Id);
        return k;
    }
    public static void Unregister(string id)
    {
        for (int i = 0; i < 6; i++) { var x = SecId(id, i); Skills.Unregister(x); _base.Remove(x); _fx.Remove(x); _registered.Remove(x); }
    }

    /// <summary>A hero was loaded or made: their crafted spells become skills (anyone else's are dropped), and new heroes get their starter cards.</summary>
    public static void Sync(PlayerStats d)
    {
        Load(); _hero = d; d.CraftTree ??= new();
        foreach (var id in _registered.ToList()) if (!IsSectionId(id) && id != EditId && !d.Spells.Exists(s => s.Id == id)) Unregister(id);
        d.Cards ??= new(); d.Spells ??= new();
        if (!d.CardsGiven) { GiveStart(d); d.CardsGiven = true; }
        foreach (var s in d.Spells)
        {
            s.Migrate();   // saves from before three sections (A, B) load as sections I and II
            s.Icon = SpellIcon.Clean(s.Icon);   // icons arrive inside saves (also from the online server): size-capped, broken ones dropped
            if (Register(s) == null) { GD.PrintErr($"[SPELLCRAFT] {s.Id} no longer makes a spell; left off"); continue; }
            if (!d.Skills.ContainsKey(s.Id)) d.Skills[s.Id] = new SkillState();
        }
        // a crafted skill whose spell is gone (an old save, a removed card) is not a skill any more
        foreach (var id in d.Skills.Keys.Where(x => x.StartsWith(Prefix) && Skills.Get(x) == null).ToList()) { d.Skills.Remove(id); for (int i = 0; i < d.Bar.Length; i++) if (d.Bar[i] == id) d.Bar[i] = ""; }
    }
    static void GiveStart(PlayerStats d)
    {
        if (!_root.TryGetProperty("start", out var st)) return;
        void Kit(string k) { if (st.TryGetProperty(k, out var o)) foreach (var x in o.EnumerateObject()) if (_c.ContainsKey(x.Name)) d.Cards[x.Name] = d.Cards.GetValueOrDefault(x.Name) + x.Value.GetInt32(); }
        Kit("all");
        var fams = new List<string>();
        if (_root.TryGetProperty("classFamilies", out var cf) && cf.TryGetProperty(d.Class, out var arr) && arr.ValueKind == JsonValueKind.Array) foreach (var x in arr.EnumerateArray()) fams.Add(x.GetString() ?? "");
        if (fams.Count == 0) fams.Add("spell");
        foreach (var f in fams) Kit(f);
    }

    // ---------------------------------------------------------------- the spell being edited (preview in the menu)
    public const string EditId = "cs.edit";
    /// <summary>The menu's working copy as a hidden skill, so its numbers can be shown with Skills.Compute. Null if it is not a spell yet.</summary>
    public static SkillDef? Preview(CraftedSpell s)
    {
        var tmp = s.Copy(); tmp.Id = EditId;
        Unregister(EditId); return Register(tmp, false);
    }

    /// <summary>Forges (or re-forges) a spell: it becomes a learned skill, put on a free hotbar key if it is new. Null on success, else why not.</summary>
    public static string? Forge(PlayerStats d, CraftedSpell work, string editing)
    {
        var s = editing == "" ? new CraftedSpell { Id = "" } : d.Spells.FirstOrDefault(x => x.Id == editing);
        if (s == null) return "That spell is gone";
        if (editing == "" && d.Spells.Count >= SpellCap(d)) return $"Your amulet holds {SpellCap(d)} spells: dismantle one first (Wider Amulet in the spell tree holds more)";
        var test = work.Copy(); test.Id = editing; test.Secs = test.Used();
        var err = Check(d, test); if (err != null) return err;
        if (editing == "") { int n = d.SpellSeq; do n++; while (d.Spells.Exists(x => x.Id == Prefix + n)); d.SpellSeq = n; s.Id = Prefix + n; d.Spells.Add(s); }
        s.Name = work.Name.Trim() == "" || work.Name == AutoName(work) ? "" : work.Name.Trim(); s.Secs = test.Secs; s.A = new(); s.B = new(); s.Icon = SpellIcon.Clean(work.Icon);
        if (Register(s) == null) return "Those cards do not make a spell";
        if (!d.Skills.ContainsKey(s.Id)) { d.Skills[s.Id] = new SkillState(); d.Forgot.Remove(s.Id); for (int i = 0; i < d.Bar.Length; i++) if (string.IsNullOrEmpty(d.Bar[i])) { d.Bar[i] = s.Id; break; } }
        work.Id = s.Id; return null;
    }
    /// <summary>Takes a spell apart: its cards are free again, ranks bought for it come back.</summary>
    public static void Dismantle(PlayerStats d, string id)
    {
        if (d.Skills.TryGetValue(id, out var st)) { d.RankPts += st.Rank - 1; d.Skills.Remove(id); }
        d.Spells.RemoveAll(x => x.Id == id); for (int i = 0; i < d.Bar.Length; i++) if (d.Bar[i] == id) d.Bar[i] = "";
        foreach (var m in d.Macros ?? new()) m.Steps.RemoveAll(x => x.Skill == id);
        Unregister(id);
    }

    // ---------------------------------------------------------------- finding cards
    /// <summary>A card from the drop table for a hero of this level, or null.</summary>
    public static CardDef? RollCard(int level)
    {
        Load(); int ahead = (int)R("drops.reqAhead", 2);
        var types = new[] { "carrier", "effect", "modifier" }; float tw = types.Sum(t => R("drops.types." + t, 1)), r = GD.Randf() * tw; string type = types[^1];
        foreach (var t in types) { r -= R("drops.types." + t, 1); if (r <= 0) { type = t; break; } }
        var pool = _c.Values.Where(c => c.Type == type && c.Req <= level + ahead).ToList(); if (pool.Count == 0) return null;
        float sum = pool.Sum(c => c.W), x = GD.Randf() * sum;
        foreach (var c in pool) { x -= c.W; if (x <= 0) return c; }
        return pool[^1];
    }
    public static void Give(string card, int n = 1, Vector3? at = null)
    {
        var c = Card(card); if (c == null) return; var d = Game.D; d.Cards[card] = d.Cards.GetValueOrDefault(card) + n;
        var col = CardColor(c);
        Ui.Note($"Spell card: {c.Name} ({Cap(c.Type)})" + (n > 1 ? $" x{n}" : "") + $"   ·   {Inputs.KeyOf(Inputs.SpellcraftUi)} to craft", col);
        var p = Player.Instance; if (p != null) FloatText.Spawn(p, (at ?? p.GlobalPosition) + new Vector3(0, 1.8f, 0), "Card: " + c.Name, col, 1f);
        Sfx.Play("pickup");
    }
    /// <summary>A monster we get the kill reward for fell: maybe a card (Enemy.Die, Net.LocalKill).</summary>
    public static void OnKill(Vector3 at, bool boss, bool elite)
    {
        var d = Game.D; float ch = boss ? R("drops.boss", 1) : elite ? R("drops.elite", .2f) : R("drops.kill", .045f);
        int n = boss ? (int)R("drops.bossCards", 2) : 1; ch = Luck.Chance(ch, Luck.Party());
        for (int i = 0; i < n; i++) if (GD.Randf() < ch) { var c = RollCard(d.Level); if (c != null) Give(c.Id, 1, at); }
    }
    public static void OnChest(Vector3 at) { if (GD.Randf() < R("drops.chest", .4f)) { var c = RollCard(Game.D.Level); if (c != null) Give(c.Id, 1, at); } }

    // ---------------------------------------------------------------- looks
    public static Color CardColor(CardDef c) => c.Type switch
    {
        "effect" => Dmg.TypeColor(c.EType),
        "carrier" => c.Family == "martial" ? new Color("e0a060") : c.Family == "archery" ? new Color("9ad070") : new Color("8fb0ff"),
        _ => new Color("d8c070"),
    };
    public static string CardIcon(CardDef c) => c.Type switch
    {
        "effect" => c.EType switch { "fire" => "fire", "frost" => "nova", "lightning" => "zap", "poison" => "poison", "shadow" => "shadow", "holy" => c.Id == "life" ? "hp" : "heal", _ => c.Id == "stone" ? "mace" : "sword" },
        "carrier" => Skills.Get(c.From) is { } t ? Icons.ForSkill(t) : "trinket",
        _ => "trinket",
    };
    /// <summary>Hotbar icon of a crafted skill: the player's own icon for the spell (sections II/III use the root spell's), else weapon
    /// arts and archery the skill their carrier copies, spells an automatic icon from their delivery and element.</summary>
    public static string IconFor(SkillDef k)
    {
        string root = k.Id.Contains('.') && k.Id != EditId ? k.Id[..k.Id.IndexOf('.')] : k.Id;
        var own = (_hero ?? Game.D)?.Spells?.FirstOrDefault(s => s.Id == root)?.Icon;
        if (!string.IsNullOrEmpty(own)) return own;
        var car = Skills.Get(Base(k.Id));
        bool weapon = k.Scale == "weapon";
        if (weapon && car != null && !car.Crafted) return Icons.ForSkill(car);
        return SpellIcon.Auto(k);
    }
    static string Cap(string s) => s == "" ? s : char.ToUpper(s[0]) + s[1..];
}
