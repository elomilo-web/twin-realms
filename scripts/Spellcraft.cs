using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A spell the player built from cards (Spellcraft menu, B). A = the first section, B = the optional second section that
/// goes off where the first one ends. Both hold card ids from data/spellcards.json; copies of a card appear more than once.</summary>
public class CraftedSpell
{
    public string Id = "", Name = "";
    public List<string> A = new(), B = new();
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
    }
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

    public static CardDef? Card(string id) { Load(); return _c.GetValueOrDefault(id); }
    public static IEnumerable<CardDef> All { get { Load(); foreach (var k in Order) yield return _c[k]; } }
    public static bool IsCrafted(string id) => id.StartsWith(Prefix) && _base.ContainsKey(id);
    /// <summary>The skill a crafted skill's carrier copies (or the id itself): used for animations, icons and per-skill special cases.</summary>
    public static string Base(string id) => _base.TryGetValue(id, out var b) ? b : id;
    /// <summary>The skill whose animations (data/spellfx.json) a crafted skill plays: spells take their element's look, weapon arts keep their carrier's.</summary>
    public static string FxOf(string id) => _fx.TryGetValue(id, out var b) ? b : id;

    // ---------------------------------------------------------------- rules
    /// <summary>Cards a spell of this family may hold (both sections together).</summary>
    public static int MaxCards(PlayerStats d, string family)
    {
        Load(); float per = Mathf.Max(1, R("slots.perLevels", 5));
        int bonus = (int)R($"classSlots.{d.Class}.{family}", 0);
        return Math.Min((int)R("slots.max", 10), (int)R("slots.base", 4) + (int)(d.Level / per) + bonus);
    }
    /// <summary>Copies of one card a spell may hold (magic mastery).</summary>
    public static int MaxCopies(PlayerStats d) { Load(); return Math.Min((int)R("mastery.max", 6), (int)R("mastery.base", 2) + (int)(d.Level / Mathf.Max(1, R("mastery.perLevels", 6)))); }
    public static IEnumerable<string> SecondCarriers { get { Load(); if (_root.TryGetProperty("second", out var s) && s.TryGetProperty("carriers", out var a)) foreach (var x in a.EnumerateArray()) yield return x.GetString() ?? ""; } }

    /// <summary>Copies of a card owned and not held by another crafted spell (the one being edited, skip, counts as free).</summary>
    public static int Free(PlayerStats d, string card, string skip = "")
    {
        int n = d.Cards.GetValueOrDefault(card);
        foreach (var s in d.Spells) if (s.Id != skip) n -= s.A.Count(x => x == card) + s.B.Count(x => x == card);
        return n;
    }
    public static string FamilyOf(CraftedSpell s) { var c = s.A.Select(Card).FirstOrDefault(x => x?.Type == "carrier"); return c?.Family ?? "spell"; }

    /// <summary>Why the cards in a section cannot make a spell, or null when they can.</summary>
    static string? CheckSection(List<string> cards, bool second)
    {
        var defs = cards.Select(Card).ToList(); if (defs.Any(x => x == null)) return "Unknown card";
        var car = defs.Where(x => x!.Type == "carrier").Select(x => x!.Id).Distinct().ToList();
        var eff = defs.Where(x => x!.Type == "effect").Select(x => x!.Id).Distinct().ToList();
        string sec = second ? "The second section" : "A spell";
        if (car.Count == 0) return $"{sec} needs a carrier card (how it is delivered)";
        if (car.Count > 1) return $"{sec} can hold only one kind of carrier (more copies of it are fine)";
        if (eff.Count == 0) return $"{sec} needs an effect card (what it does)";
        if (eff.Count > 1) return $"{sec} can hold only one kind of effect (more copies of it are fine)";
        if (second && !SecondCarriers.Contains(car[0])) return $"{Card(car[0])!.Name} cannot be a second section (use {string.Join(", ", SecondCarriers.Select(x => Card(x)?.Name ?? x))})";
        return null;
    }
    /// <summary>Everything that stops this spell from being forged by this hero, or null.</summary>
    public static string? Check(PlayerStats d, CraftedSpell s)
    {
        Load();
        var err = CheckSection(s.A, false); if (err != null) return err;
        if (s.B.Count > 0 && (err = CheckSection(s.B, true)) != null) return err;
        string fam = FamilyOf(s); int max = MaxCards(d, fam), n = s.A.Count + s.B.Count;
        if (n > max) return $"Too many cards: {n} of {max} for a {fam} spell at your level";
        int mc = MaxCopies(d);
        foreach (var g in s.A.Concat(s.B).GroupBy(x => x))
        {
            var c = Card(g.Key)!; int need = g.Count();
            if (c.Req > d.Level) return $"{c.Name} needs level {c.Req}";
            if (s.A.Count(x => x == g.Key) > mc || s.B.Count(x => x == g.Key) > mc) return $"At most {mc} copies of one card per section at your level";
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
        string a = Part(s.A), b = Part(s.B);
        return a == "" ? "New spell" : b == "" ? a : $"{a} + {b}";
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
    static SkillDef BuildSection(string id, List<string> cards, out string fxId)
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
        float em = 1 + R("stack.effectP", 22) / 100f * (nEff - 1), sd = 1 + R("stack.statusDurP", 12) / 100f * (nEff - 1);
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
        k.CardMods = defs.Where(x => x.Type == "modifier").Select(x => x.Mod!.Id).ToList();
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

    /// <summary>Builds a crafted spell into skills (its second section as a hidden skill "id.2") and registers them. Returns null if the cards do not make a spell.</summary>
    public static SkillDef? Register(CraftedSpell s, bool listed = true)
    {
        Load(); if (CheckSection(s.A, false) != null || (s.B.Count > 0 && CheckSection(s.B, true) != null)) return null;
        var k = BuildSection(s.Id, s.A, out var bid);
        if (s.B.Count > 0)
        {
            var f = BuildSection(s.Id + Second, s.B, out var fb); f.Name = k.Name + " (second section)";
            k.Then = f.Id; k.Mp += f.Mp * R("second.costP", 60) / 100f; k.St += f.St * R("second.costP", 60) / 100f; k.Cd += f.Cd * R("second.cdP", 30) / 100f;
            k.Desc += $"  Where it ends: {f.Desc}"; k.Req = Math.Max(k.Req, f.Req);
            Skills.Register(f, false); _fx[f.Id] = fb; _registered.Add(f.Id);
        }
        else { Skills.Unregister(s.Id + Second); _base.Remove(s.Id + Second); _fx.Remove(s.Id + Second); }
        k.Name = s.Name != "" ? s.Name : AutoName(s);
        Skills.Register(k, listed); _fx[k.Id] = bid; _registered.Add(k.Id);
        return k;
    }
    public static void Unregister(string id)
    {
        foreach (var x in new[] { id, id + Second }) { Skills.Unregister(x); _base.Remove(x); _fx.Remove(x); _registered.Remove(x); }
    }

    /// <summary>A hero was loaded or made: their crafted spells become skills (anyone else's are dropped), and new heroes get their starter cards.</summary>
    public static void Sync(PlayerStats d)
    {
        Load();
        foreach (var id in _registered.ToList()) if (!id.EndsWith(Second) && id != EditId && !d.Spells.Exists(s => s.Id == id)) Unregister(id);
        d.Cards ??= new(); d.Spells ??= new();
        if (!d.CardsGiven) { GiveStart(d); d.CardsGiven = true; }
        foreach (var s in d.Spells)
        {
            s.A ??= new(); s.B ??= new();
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
        var tmp = new CraftedSpell { Id = EditId, Name = s.Name, A = s.A, B = s.B };
        Unregister(EditId); return Register(tmp, false);
    }

    /// <summary>Forges (or re-forges) a spell: it becomes a learned skill, put on a free hotbar key if it is new. Null on success, else why not.</summary>
    public static string? Forge(PlayerStats d, CraftedSpell work, string editing)
    {
        var s = editing == "" ? new CraftedSpell { Id = "" } : d.Spells.FirstOrDefault(x => x.Id == editing);
        if (s == null) return "That spell is gone";
        if (editing == "" && d.Spells.Count >= MaxSpells) return $"Your amulet holds {MaxSpells} spells: dismantle one first";
        var test = new CraftedSpell { Id = editing, Name = work.Name, A = work.A.ToList(), B = work.B.ToList() };
        var err = Check(d, test); if (err != null) return err;
        if (editing == "") { int n = d.SpellSeq; do n++; while (d.Spells.Exists(x => x.Id == Prefix + n)); d.SpellSeq = n; s.Id = Prefix + n; d.Spells.Add(s); }
        s.Name = work.Name.Trim() == "" || work.Name == AutoName(work) ? "" : work.Name.Trim(); s.A = test.A; s.B = test.B;
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
    /// <summary>Hotbar icon of a crafted skill: spells show their element, weapon arts and archery the skill their carrier copies.</summary>
    public static string IconFor(SkillDef k)
    {
        var car = Skills.Get(Base(k.Id));
        bool weapon = k.Scale == "weapon";
        if (weapon && car != null && !car.Crafted) return Icons.ForSkill(car);
        return k.Type switch { "shadow" => "shadow", "poison" => "poison", "holy" => "heal", "frost" => "nova", "lightning" => "zap", "fire" => "fire", _ => "sword" };
    }
    static string Cap(string s) => s == "" ? s : char.ToUpper(s[0]) + s[1..];
}
