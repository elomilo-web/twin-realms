using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A named dungeon unique from data/loot.json.</summary>
public class UniqueDef { public string Name = "", Slot = "weapon", Wt = "", Elem = ""; public int Rar = 3, MinDepth = 1; public float StatMul = 1.3f; }
/// <summary>A gear set: pieces that drop deep in one dungeon and give bonuses when worn together.</summary>
public class SetDef
{
    public string Id = "", Name = "", Prefix = "", Elem = ""; public Color Col = Colors.Aquamarine; public List<string> Pieces = new();
    public int MinDepth = 10; public float Chance, Elite, Boss, PerDepth, StatMul = 1.25f;
    public SortedDictionary<int, Dictionary<string, float>> Bonuses = new();
    public bool Craft; public Dictionary<string, int> CraftCost = new();   // craft-only sets: never drop; forged piece by piece
}

/// <summary>
/// Loot rules from data/loot.json: rarity weights, each dungeon's own drop table (slots, elements, named uniques, its set),
/// set gear and set bonuses, and what disassembling an item gives.
/// </summary>
public static class LootTables
{
    static bool _loaded; static JsonDocument? _doc;
    static float[] _w = { 55, 27, 12, 4.5f, 1.5f }; static float _bonusPer = .35f, _perDepth = .45f, _perTier = 1.5f;
    class DunTable { public Dictionary<string, float> Slots = new(), Elems = new(); public float UChance, UPerDepth; public List<UniqueDef> Uniques = new(); public string Set = ""; }
    static readonly Dictionary<string, DunTable> _dun = new();
    public static readonly Dictionary<string, SetDef> Sets = new();
    public static JsonElement Root { get { Load(); return _doc!.RootElement; } }

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        _doc = JsonUtil.Load("res://data/loot.json"); var r = _doc.RootElement;
        if (r.TryGetProperty("rarity", out var ra))
        {
            if (ra.TryGetProperty("weights", out var w)) { int i = 0; foreach (var x in w.EnumerateArray()) if (i < 5) _w[i++] = x.GetSingle(); }
            _bonusPer = JsonUtil.F(ra, "bonusPerPoint", _bonusPer);
        }
        if (r.TryGetProperty("dungeonDrop", out var dd)) { _perDepth = JsonUtil.F(dd, "bonusPerDepth", _perDepth); _perTier = JsonUtil.F(dd, "bonusPerTier", _perTier); }
        if (r.TryGetProperty("dungeons", out var ds))
            foreach (var p in ds.EnumerateObject())
            {
                var e = p.Value; var t = new DunTable { UChance = JsonUtil.F(e, "uniqueChance"), UPerDepth = JsonUtil.F(e, "uniquePerDepth"), Set = JsonUtil.S(e, "set") };
                if (e.TryGetProperty("slots", out var s)) foreach (var o in s.EnumerateObject()) t.Slots[o.Name] = o.Value.GetSingle();
                if (e.TryGetProperty("elements", out var el)) foreach (var o in el.EnumerateObject()) t.Elems[o.Name] = o.Value.GetSingle();
                if (e.TryGetProperty("uniques", out var u)) foreach (var x in u.EnumerateArray())
                    t.Uniques.Add(new UniqueDef { Name = JsonUtil.S(x, "name"), Slot = JsonUtil.S(x, "slot", "weapon"), Wt = JsonUtil.S(x, "wt"), Elem = JsonUtil.S(x, "elem"), Rar = (int)JsonUtil.F(x, "rar", 3), MinDepth = (int)JsonUtil.F(x, "minDepth", 1), StatMul = JsonUtil.F(x, "statMul", 1.3f) });
                _dun[p.Name] = t;
            }
        if (r.TryGetProperty("sets", out var ss))
            foreach (var p in ss.EnumerateObject())
            {
                if (p.Name.StartsWith("_")) continue;   // notes
                var e = p.Value; var s = new SetDef
                {
                    Id = p.Name, Name = JsonUtil.S(e, "name", p.Name), Prefix = JsonUtil.S(e, "prefix", p.Name), Elem = JsonUtil.S(e, "elem"), Col = Color.FromHtml(JsonUtil.S(e, "color", "#3fe0c0")),
                    MinDepth = (int)JsonUtil.F(e, "minDepth", 10), Chance = JsonUtil.F(e, "chance"), Elite = JsonUtil.F(e, "eliteChance"), Boss = JsonUtil.F(e, "bossChance"), PerDepth = JsonUtil.F(e, "perDepth"), StatMul = JsonUtil.F(e, "statMul", 1.25f),
                };
                if (e.TryGetProperty("pieces", out var pc)) foreach (var x in pc.EnumerateArray()) s.Pieces.Add(x.GetString() ?? "armor");
                if (e.TryGetProperty("bonuses", out var b)) foreach (var o in b.EnumerateObject()) { var m = new Dictionary<string, float>(); foreach (var k in o.Value.EnumerateObject()) m[k.Name] = k.Value.GetSingle(); s.Bonuses[int.Parse(o.Name)] = m; }
                s.Craft = e.TryGetProperty("craft", out var cf) && cf.ValueKind == JsonValueKind.True;
                if (e.TryGetProperty("craftCost", out var cc)) foreach (var o in cc.EnumerateObject()) s.CraftCost[o.Name] = o.Value.GetInt32();
                Sets[p.Name] = s;
            }
    }

    // ------------------------------------------------------------------ rarity and drops
    /// <summary>Rarity 0..4 by weight; each point of loot bonus multiplies rarity r's weight by (1 + bonusPerPoint x bonus x r).</summary>
    public static int RollRarity(float bonus)
    {
        Load(); var w = new float[5]; float sum = 0;
        for (int r = 0; r < 5; r++) { w[r] = _w[r] * (1 + _bonusPer * Mathf.Max(0, bonus) * r); sum += w[r]; }
        float x = GD.Randf() * sum; for (int r = 0; r < 4; r++) { if (x < w[r]) return r; x -= w[r]; }
        return 4;
    }
    /// <summary>Loot bonus a dungeon floor adds to every drop (deeper = rarer; each tier of 5 floors adds a big step).</summary>
    public static float DepthBonus(int depth) { Load(); return Mathf.Max(0, depth - 1) * _perDepth + Progression.Tier(depth) * _perTier; }

    static string Pick(Dictionary<string, float> w, string fallback)
    {
        float sum = w.Values.Sum(); if (sum <= 0) return fallback; float x = GD.Randf() * sum;
        foreach (var kv in w) { if (x < kv.Value) return kv.Key; x -= kv.Value; }
        return w.Keys.Last();
    }

    /// <summary>An item drop. Inside a dungeon it uses that dungeon's own table (its slots, elements and named uniques) and the floor's rarity bonus.</summary>
    public static Item Roll(int level, float bonus)
    {
        Load();
        if (!Dungeon.Active || !_dun.TryGetValue(Dungeon.Key, out var t)) return Items.Roll(level, bonus);
        int depth = Dungeon.Depth; bonus += DepthBonus(depth);
        // a named unique of this dungeon
        var pool = t.Uniques.FindAll(u => u.MinDepth <= depth);
        if (pool.Count > 0 && GD.Randf() < t.UChance + t.UPerDepth * depth) return MakeUnique(pool[(int)(GD.Randi() % (uint)pool.Count)], level);
        string slot = Pick(t.Slots, "weapon"), elem = Pick(t.Elems, "");
        int rar = RollRarity(bonus);
        // elemental gear needs at least Fine rarity; commons stay plain
        return Items.Roll(level, 0, slot, rar, rar == 0 ? "" : elem);
    }

    public static Item MakeUnique(UniqueDef u, int level)
    {
        var it = Items.Roll(level, 0, u.Slot, u.Rar, u.Elem, u.Wt == "" ? null : u.Wt);
        Items.Boost(it, u.StatMul); it.Name = u.Name; it.Unique = true; return it;
    }

    // ------------------------------------------------------------------ sets
    public static SetDef? SetOf(string? id) { Load(); return id != null && Sets.TryGetValue(id, out var s) ? s : null; }
    public static SetDef? SetForDungeon(string key) { Load(); return _dun.TryGetValue(key, out var t) ? SetOf(t.Set) : null; }
    public static int? SetMinDepth(string key) => SetForDungeon(key)?.MinDepth;
    public static string SetName(string key) => SetForDungeon(key)?.Name ?? "";
    public static Color SetColor(string key) => SetForDungeon(key)?.Col ?? Colors.Aquamarine;
    public static Color SetColorOf(string id) => SetOf(id)?.Col ?? Colors.Aquamarine;

    /// <summary>A set piece: legendary, stronger than rolled gear, named after its set.</summary>
    public static Item MakeSetPiece(SetDef s, int level, string? slot = null)
    {
        slot ??= s.Pieces[(int)(GD.Randi() % (uint)s.Pieces.Count)];
        var it = Items.Roll(level, 0, slot, 4, slot == "trinket" ? "" : s.Elem);
        Items.Boost(it, s.StatMul); it.Set = s.Id; it.Name = s.Prefix + " " + Items.BaseName(it); return it;
    }

    /// <summary>Deep dungeon floors only: a chance that a kill drops a piece of this dungeon's set (better for upgraded monsters and bosses).</summary>
    public static void TrySetDrop(Vector3 pos, int level, bool elite, bool boss)
    {
        if (!Dungeon.Active) return; var s = SetForDungeon(Dungeon.Key); if (s == null || Dungeon.Depth < s.MinDepth) return;
        float c = (boss ? s.Boss : elite ? s.Elite : s.Chance) * (1 + s.PerDepth * (Dungeon.Depth - s.MinDepth));
        if (GD.Randf() >= c) return;
        var it = MakeSetPiece(s, level); Loot.Drop(pos + Vector3.Up * .3f, item: it);
        Ui.Note($"A set piece dropped: {it.Name}", s.Col); Sfx.Play("level");
    }

    /// <summary>How many pieces of this set the hero wears.</summary>
    public static int Worn(PlayerStats d, string id) { int n = 0; foreach (var it in d.Eq.Values) if (it?.Set == id) n++; return n; }

    /// <summary>Adds the bonuses of every set the hero wears enough of (called from Game.RecalcTree).</summary>
    public static void Apply(PlayerStats d, TreeStats ts)
    {
        Load(); var counts = new Dictionary<string, int>();
        foreach (var it in d.Eq.Values) if (it?.Set != null) counts[it.Set] = counts.GetValueOrDefault(it.Set) + 1;
        foreach (var (id, n) in counts)
        {
            if (!Sets.TryGetValue(id, out var s)) continue;
            foreach (var (need, mods) in s.Bonuses) if (n >= need) foreach (var kv in mods) ts[kv.Key] += kv.Value;
        }
    }

    // ------------------------------------------------------------------ disassembly
    static void AddRange(Dictionary<string, int> got, JsonElement e, float lvlMul)
    {
        // [id, min, max]
        if (e.ValueKind != JsonValueKind.Array || e.GetArrayLength() < 3) return;
        string id = e[0].GetString() ?? ""; int lo = e[1].GetInt32(), hi = e[2].GetInt32(); if (id == "") return;
        int n = Mathf.Max(1, Mathf.RoundToInt(GD.RandRange(lo, hi) * lvlMul)); got[id] = got.GetValueOrDefault(id) + n;
    }
    /// <summary>The scraps one item breaks into: by slot or weapon type, extra by rarity, its element's core, set fragments.</summary>
    public static Dictionary<string, int> Scraps(Item it)
    {
        var got = new Dictionary<string, int>(); if (!Root.TryGetProperty("scraps", out var s)) return got;
        float lvlMul = 1 + JsonUtil.F(s, "levelPer", .04f) * Mathf.Max(0, it.Lvl - 1);
        var b = s.GetProperty("base");
        if (it.Wt != null && b.TryGetProperty(it.Wt, out var bw)) AddRange(got, bw, lvlMul); else if (b.TryGetProperty(it.Slot, out var bs)) AddRange(got, bs, lvlMul);
        if (s.TryGetProperty("rarity", out var ra) && it.Rar < ra.GetArrayLength()) foreach (var e in ra[Mathf.Clamp(it.Rar, 0, 4)].EnumerateArray()) AddRange(got, e, 1f);
        if (it.Elem != null && s.TryGetProperty("element", out var el) && el.TryGetProperty(it.Elem, out var core)) { string c = core.GetString() ?? ""; if (c != "") got[c] = got.GetValueOrDefault(c) + 1; }
        if (it.Set != null && s.TryGetProperty("set", out var se)) foreach (var e in se.EnumerateArray()) AddRange(got, e, 1f);
        if (it.Up > 0) got["ironscrap"] = got.GetValueOrDefault("ironscrap") + it.Up * 2;   // smith upgrades give some iron back
        return got;
    }
    /// <summary>Breaks an item into scraps and puts them in the material bag. Returns a short "+3 Iron Scrap, +1 Rune Shard".</summary>
    public static string Disassemble(Item it)
    {
        var got = Scraps(it); var d = Game.D.Mats; var parts = new List<string>();
        foreach (var (id, n) in got) { d[id] = d.GetValueOrDefault(id) + n; parts.Add($"+{n} {GatherDb.Mat(id).Name}"); }
        Sfx.Play("hit"); Game.Notify();
        return string.Join(", ", parts);
    }
}
