using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class TreeNode
{
    public int Id; public float X, Y;
    public string Type = "small", Name = "", Arm = "", Desc = "";
    public Color Col = Colors.White;
    public Dictionary<string, float> Mods = new();
    public List<int> Adj = new();
    public int Layer;   // 0 = the base tree (data/skilltree.json), 1 = the advanced tree (data/skilltree_adv.json, A-15)
}

/// <summary>An advanced path (A-15): the stronger form of one base path (Mage -> Archmage). Root = its gate node.</summary>
public class AdvPath { public string Id = "", Name = "", Base = "", BaseName = "", Desc = ""; public int Root; public Color Col = Colors.White; }

/// <summary>Aggregated skill tree bonuses. Unknown stats read as 0.</summary>
public class TreeStats
{
    readonly Dictionary<string, float> _v = new();
    /// <summary>Bumped on every write: the stat pipeline's cache (StatPipeline.cs) rebuilds when it changes.</summary>
    public int Ver { get; private set; }
    public float this[string k] { get => _v.TryGetValue(k, out var x) ? x : 0f; set { _v[k] = value; Ver++; } }
    public bool Has(string flag) => this[flag] > 0;
    public void Clear() { _v.Clear(); Ver++; }
    /// <summary>Every key with its total (for the stat pipeline and the stat dump).</summary>
    public IEnumerable<KeyValuePair<string, float>> All => _v;
}

public static class TreeDb
{
    public static List<TreeNode> Nodes = new();
    public static Dictionary<string, string> FlagText = new();
    public static Dictionary<string, int> Starts = new();
    public static int Version = 1;
    static bool _loaded;
    /// <summary>The tree node a class begins on (free; everything grows from it).</summary>
    public static int StartFor(string cls)
    {
        Load(); if (Starts.TryGetValue(cls, out var s)) return s;
        return Starts.TryGetValue(Classes.Get(cls).Start, out var s2) ? s2 : 0;   // F-34 classes without an arm of their own start on a kindred class's arm
    }

    /// <summary>
    /// Every stat key the game reads. Tree nodes, races, classes and skill buffs may only use these (the bot checks it).
    /// Per weapon type: "swordP" etc. Per damage type: "fireP" etc. Per status: "burnChance" (% chance on every hit) and "burnP" (strength).
    /// </summary>
    public static readonly HashSet<string> Known = BuildKnown();
    static HashSet<string> BuildKnown()
    {
        var k = new HashSet<string> { "meleeP", "rangedP", "spellP", "allP", "hp", "hpP", "mp", "mpP", "st", "def", "crit", "critD", "aspd", "move", "cdr", "leech", "mleech", "regenHp", "regenMp",
            "block", "thorns", "chain", "burn", "magToDmg", "magToBow", "strToBow", "mcost", "cleave", "multi", "pierce", "manaShield", "undying", "rage",
            "armorPen", "armorPenP", "magicPen", "magicPenP", "mr", "tenacity", "evade", "statusP", "healP", "xpP", "skillP", "aoeP", "castSpd",
            "finisherP", "finisherStun", "finisherUp", "powerP", "knockP", "reach", "healIn", "statusImmune", "ccImmune", "luck", "goldP",
            "breathP", "swimP", "climbP", "jumpP", "sneakP", "stealthP", "fishLuck", "rideP",   // life keys (F-25), used by racial passives (F-35)
            "unarmedP", "formP", "formDurP", "formCdr" };   // A-15 / A-16: fists (the Monk path), and damage, duration and cooldown of animal and spirit forms (PlayerForm.cs)
        foreach (var t in new[] { "physical", "fire", "frost", "lightning", "poison", "shadow", "holy" }) { k.Add(t + "P"); k.Add("res_" + t); }
        foreach (var s in new[] { "burn", "poison", "bleed", "chill", "freeze", "root", "shock", "judged", "weaken", "shred", "stun" }) { k.Add(s + "Chance"); k.Add(s + "P"); }
        foreach (var w in new[] { "sword", "katana", "greatsword", "axe", "mace", "dagger", "spear", "bow", "wand", "staff",
            "rapier", "scimitar", "halberd", "warhammer", "flail", "scythe", "claws", "greataxe", "whip", "crossbow", "javelin", "kunai",
            "colossal", "colossalaxe", "colossalhammer", "pan", "broom", "ladder", "door", "anchor", "lamppost", "fish", "chair", "cittern", "torch", "spade", "pitchfork" }) k.Add(w + "P");   // (A-11 weapon types, A-35 colossal and unexpected ones)
        foreach (var a in new[] { "wis", "agi", "lck", "end", "buffDurP", "rollCostP", "wearP" }) k.Add(a);   // A-14 attributes and their new keys
        return k;
    }

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/skilltree.json");
        var r = doc.RootElement;
        foreach (var n in r.GetProperty("nodes").EnumerateArray())
        {
            var t = new TreeNode
            {
                Id = n.GetProperty("id").GetInt32(), X = JsonUtil.F(n, "x"), Y = JsonUtil.F(n, "y"),
                Type = JsonUtil.S(n, "type", "small"), Name = JsonUtil.S(n, "name"), Arm = JsonUtil.S(n, "arm"), Desc = JsonUtil.S(n, "desc"),
            };
            var col = JsonUtil.S(n, "col", "#ffffff"); t.Col = Color.FromHtml(col);
            if (n.TryGetProperty("mods", out var mods)) foreach (var m in mods.EnumerateObject()) t.Mods[m.Name] = m.Value.GetSingle();
            if (n.TryGetProperty("adj", out var adj)) foreach (var a in adj.EnumerateArray()) t.Adj.Add(a.GetInt32());
            Nodes.Add(t);
        }
        if (r.TryGetProperty("flags", out var fl)) foreach (var f in fl.EnumerateObject()) FlagText[f.Name] = f.Value.GetString() ?? "";
        if (r.TryGetProperty("starts", out var st)) foreach (var s in st.EnumerateObject()) Starts[s.Name] = s.Value.GetInt32();
        Version = (int)JsonUtil.F(r, "version", 1);
        Nodes.Sort((a, b) => a.Id.CompareTo(b.Id));
        Rings[0] = ReadRings(r, new[] { 400f, 900f, 1520f });
        BaseCount = Nodes.Count; LoadAdvanced();
        // "+N to a skill's level" (skill_*, school_*) and "learn a form" (form_*) keys used by nodes are stat keys too
        foreach (var n in Nodes) foreach (var k in n.Mods.Keys) if (k.StartsWith("skill_") || k.StartsWith("school_") || k.StartsWith("form_")) Known.Add(k);
    }
    static float[] ReadRings(JsonElement r, float[] def)
    {
        if (!r.TryGetProperty("rings", out var a) || a.ValueKind != JsonValueKind.Array) return def;
        var l = new List<float>(); foreach (var x in a.EnumerateArray()) if (x.ValueKind == JsonValueKind.Number) l.Add(x.GetSingle()); return l.ToArray();
    }

    // ---------------------------------------------------------------- the advanced tree (A-15, data/skilltree_adv.json)
    /// <summary>How many nodes the base tree has: advanced nodes follow it (their ids in the file are local, BaseCount is added on load),
    /// so a hero's taken nodes of both layers live in the one PlayerStats.Tree list and feed the stat pipeline together.</summary>
    public static int BaseCount;
    public static readonly Dictionary<string, AdvPath> Paths = new();
    /// <summary>Points that must be invested in the base tree before any advanced node can be taken, and how many nodes of a base
    /// path open its advanced form when its keystone is not taken (data/skilltree_adv.json "unlock").</summary>
    public static int AdvPoints = 100, AdvArmNodes = 8;
    /// <summary>Radii of the faint background circles per layer.</summary>
    public static readonly float[][] Rings = { System.Array.Empty<float>(), System.Array.Empty<float>() };
    static void LoadAdvanced()
    {
        if (!FileAccess.FileExists("res://data/skilltree_adv.json")) return;
        using var doc = JsonUtil.Load("res://data/skilltree_adv.json"); var r = doc.RootElement;
        if (!r.TryGetProperty("nodes", out var ns)) return;
        int off = BaseCount; var add = new List<TreeNode>();
        foreach (var n in ns.EnumerateArray())
        {
            var t = new TreeNode
            {
                Id = n.GetProperty("id").GetInt32() + off, X = JsonUtil.F(n, "x"), Y = JsonUtil.F(n, "y"), Layer = 1,
                Type = JsonUtil.S(n, "type", "small"), Name = JsonUtil.S(n, "name"), Arm = JsonUtil.S(n, "arm"), Desc = JsonUtil.S(n, "desc"),
            };
            t.Col = Color.FromHtml(JsonUtil.S(n, "col", "#ffffff"));
            if (n.TryGetProperty("mods", out var mods)) foreach (var m in mods.EnumerateObject()) if (m.Value.ValueKind == JsonValueKind.Number) t.Mods[m.Name] = m.Value.GetSingle();
            if (n.TryGetProperty("adj", out var adj)) foreach (var a in adj.EnumerateArray()) t.Adj.Add(a.GetInt32() + off);
            add.Add(t);
        }
        add.Sort((a, b) => a.Id.CompareTo(b.Id));
        for (int i = 0; i < add.Count; i++) if (add[i].Id != off + i) { GD.PushError("skilltree_adv.json: node ids must run 0..n-1; the advanced tree is left out"); return; }
        Nodes.AddRange(add);
        if (r.TryGetProperty("unlock", out var u)) { AdvPoints = (int)JsonUtil.F(u, "points", 100); AdvArmNodes = (int)JsonUtil.F(u, "armNodes", 8); }
        if (r.TryGetProperty("flags", out var fl)) foreach (var f in fl.EnumerateObject()) FlagText[f.Name] = f.Value.GetString() ?? "";
        if (r.TryGetProperty("paths", out var ps)) foreach (var p in ps.EnumerateObject())
            Paths[p.Name] = new AdvPath { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Base = JsonUtil.S(p.Value, "base"), BaseName = JsonUtil.S(p.Value, "baseName", JsonUtil.S(p.Value, "base")), Desc = JsonUtil.S(p.Value, "d"), Root = (int)JsonUtil.F(p.Value, "root") + off, Col = Color.FromHtml(JsonUtil.S(p.Value, "col", "#ffffff")) };
        Rings[1] = ReadRings(r, System.Array.Empty<float>());
        Version = System.Math.Max(Version, (int)JsonUtil.F(r, "version", 1));   // either file changing its layout refunds the tree (Game.Load)
    }
    /// <summary>Points a hero has in one layer (0 base, 1 advanced).</summary>
    public static int Invested(IEnumerable<int> tree, int layer) { Load(); int n = 0; foreach (var id in tree) if (Get(id) is { } t && t.Layer == layer) n++; return n; }
    /// <summary>The advanced tree as a whole is open: enough points in the base tree.</summary>
    public static bool AdvOpen(IEnumerable<int> tree) => Invested(tree, 0) >= AdvPoints;
    public static AdvPath? PathOf(TreeNode n) => n.Layer == 1 && Paths.TryGetValue(n.Arm, out var p) ? p : null;
    /// <summary>Nodes of a base path a hero holds, and whether its keystone is among them.</summary>
    public static (int nodes, bool key) ArmHeld(IEnumerable<int> tree, string arm)
    {
        int n = 0; bool key = false;
        foreach (var id in tree) if (Get(id) is { Layer: 0 } t && t.Arm == arm) { n++; key |= t.Type == "key"; }
        return (n, key);
    }
    /// <summary>An advanced path's gate can be taken: the base tree holds AdvPoints points, and the hero has the base path's keystone
    /// or AdvArmNodes of its nodes. why: what is still missing, for the tree window.</summary>
    public static bool PathOpen(AdvPath p, IEnumerable<int> tree, out string why)
    {
        int inv = Invested(tree, 0); var (n, key) = ArmHeld(tree, p.Base); var miss = new List<string>();
        if (inv < AdvPoints) miss.Add($"{AdvPoints} points in the base tree (you have {inv})");
        if (!key && n < AdvArmNodes) miss.Add($"the {p.BaseName} keystone, or {AdvArmNodes} nodes of the {p.BaseName} path (you have {n})");
        why = miss.Count == 0 ? "" : "Needs " + string.Join(" and ", miss);
        return miss.Count == 0;
    }

    public static TreeNode? Get(int id) => id >= 0 && id < Nodes.Count ? Nodes[id] : null;

    public static void Recalc(HashSet<int> tree, TreeStats ts)
    {
        ts.Clear();
        foreach (var id in tree) { var n = Get(id); if (n == null) continue; foreach (var kv in n.Mods) ts[kv.Key] += kv.Value; }
    }

    /// <summary>
    /// A node can be taken when it touches a taken node or your class start. Your own start is free and never taken; another class's
    /// start costs a point and gives nothing (no stats, no effects): it is the toll a hybrid pays to build on across the centre.
    /// </summary>
    public static bool CanAlloc(TreeNode n, HashSet<int> tree, int pts, int start)
    {
        if (n.Id == start || tree.Contains(n.Id) || pts <= 0) return false;
        if (n.Layer == 1 && n.Type == "gate") return PathOf(n) is { } p && PathOpen(p, tree, out _);   // A-15: an advanced path begins at its gate
        foreach (var a in n.Adj) if (a == start || tree.Contains(a)) return true;
        return false;
    }

    /// <summary>A node can be refunded when everything else you took still connects back to your class start (advanced nodes: to
    /// their path's gate), and the advanced nodes you keep still have what opened them (A-15).</summary>
    public static bool CanDealloc(TreeNode n, HashSet<int> tree, int start) => DeallocWhy(n, tree, start) == "";
    /// <summary>Why a taken node cannot be refunded ("" = it can).</summary>
    public static string DeallocWhy(TreeNode n, HashSet<int> tree, int start)
    {
        if (!tree.Contains(n.Id)) return "Not taken";
        var rest = new HashSet<int>(tree); rest.Remove(n.Id);
        var seen = new HashSet<int>(); var q = new Stack<int>(); if (Get(start) != null) q.Push(start);
        foreach (var p in Paths.Values) if (rest.Contains(p.Root) && seen.Add(p.Root)) q.Push(p.Root);
        while (q.Count > 0)
        {
            var c = q.Pop();
            foreach (var a in Nodes[c].Adj) if (rest.Contains(a) && seen.Add(a)) q.Push(a);
        }
        int live = 0; foreach (var id in rest) if (Get(id) != null) live++;   // (ids no layout has any more are ignored)
        if (seen.Count != live) return n.Layer == 1 ? "Refund the nodes further along this path first: everything must stay connected to its gate" : "Refund the nodes further out first: everything you take must stay connected to your start";
        if (n.Layer == 0 && Invested(rest, 1) > 0)
        {
            if (Invested(rest, 0) < AdvPoints) return $"Your advanced nodes need {AdvPoints} points in the base tree: refund those first";
            foreach (var p in Paths.Values) if (rest.Contains(p.Root) && !PathOpen(p, rest, out _)) return $"{p.Name} needs this part of the {p.BaseName} path: refund {p.Name} first";
        }
        return "";
    }

    public static string StatText(string k, float v)
    {
        string s(float x) => x.ToString("0.##");
        return (k switch
        {
            "meleeP" => $"+{s(v)}% melee damage", "rangedP" => $"+{s(v)}% bow damage", "spellP" => $"+{s(v)}% spell and magic damage", "allP" => $"+{s(v)}% all damage",
            "hp" => $"+{s(v)} max health", "hpP" => $"{(v > 0 ? "+" : "")}{s(v)}% max health", "mp" => $"+{s(v)} max mana", "mpP" => $"+{s(v)}% max mana", "st" => $"+{s(v)} max stamina",
            "def" => $"+{s(v)} defense", "crit" => $"+{s(v)}% critical chance", "critD" => $"+{s(v)}% critical damage", "aspd" => $"+{s(v)}% attack speed",
            "move" => $"{(v > 0 ? "+" : "")}{s(v)}% movement speed", "cdr" => $"-{s(v)}% spell cooldowns", "leech" => $"{s(v)}% of damage dealt restores health",
            "mleech" => $"{s(v)}% of damage dealt restores mana", "regenHp" => $"+{s(v)} health per second", "regenMp" => $"+{s(v)} mana per second",
            "block" => $"+{s(v)}% block strength", "thorns" => $"Reflect {s(v)}% of melee damage", "chain" => $"+{s(v)} chain lightning jumps", "burn" => $"+{s(v)}% burn damage",
            "magToDmg" => $"Melee gains {s(v)} damage per Magic", "magToBow" => $"Bow gains {s(v)} damage per Magic", "strToBow" => $"Bow gains {s(v)} damage per Strength", "mcost" => $"{(v > 0 ? "+" : "")}{s(v)}% spell mana cost",
            "armorPen" => $"+{s(v)} armor penetration", "armorPenP" => $"+{s(v)}% armor penetration", "magicPen" => $"+{s(v)} magic penetration", "magicPenP" => $"+{s(v)}% magic penetration",
            "mr" => $"+{s(v)} magic resist", "tenacity" => $"+{s(v)}% tenacity (shorter stuns, slows and damage over time)", "evade" => $"+{s(v)}% chance to evade hits", "statusP" => $"+{s(v)}% status effect duration",
            "healP" => $"+{s(v)}% healing", "xpP" => $"+{s(v)}% experience", "skillP" => $"+{s(v)}% skill damage", "aoeP" => $"+{s(v)}% skill area", "castSpd" => $"+{s(v)}% cast speed",
            "finisherP" => $"+{s(v)}% combo finisher damage", "finisherStun" => $"Combo finishers stun for +{s(v)} s", "finisherUp" => $"Combo finishers knock foes up ({s(v)})", "powerP" => $"+{s(v)}% power attack damage",
            "knockP" => $"+{s(v)}% knockback", "reach" => $"+{s(v)}% melee reach", "healIn" => $"{(v > 0 ? "+" : "")}{s(v)}% healing received", "statusImmune" => "Immune to all statuses", "luck" => $"+{s(v)} luck (more and rarer drops for your whole party)", "goldP" => $"+{s(v)}% gold found", "ccImmune" => "Immune to stuns, roots, freezes, chills and flinching",
            "breathP" => $"+{s(v)}% breath under water", "swimP" => $"+{s(v)}% swimming speed", "climbP" => $"+{s(v)}% climbing speed", "jumpP" => $"+{s(v)}% jump height",
            "sneakP" => $"+{s(v)}% sneaking speed", "stealthP" => $"+{s(v)}% stealth", "fishLuck" => $"+{s(v)} fishing luck", "rideP" => $"+{s(v)}% riding speed",
            "undying" => "Cheat death: a killing blow leaves you at 30% health (every 3 min)",
            "unarmedP" => $"+{s(v)}% unarmed damage", "formP" => $"+{s(v)}% damage in animal and spirit forms",
            "formDurP" => $"+{s(v)}% form duration", "formCdr" => $"-{s(v)}% form cooldown",
            _ when k.StartsWith("form_") => $"Learn the {Forms.NameOf(k[5..])} form (Skills menu: put it on your hotbar)",
            "str" => $"+{s(v)} Strength", "dex" => $"+{s(v)} Dexterity", "vit" => $"+{s(v)} Vitality", "mag" => $"+{s(v)} Magic",
            "wis" => $"+{s(v)} Wisdom", "agi" => $"+{s(v)} Agility", "lck" => $"+{s(v)} Luck", "end" => $"+{s(v)} Endurance",   // A-14
            "buffDurP" => $"+{s(v)}% buff duration", "rollCostP" => $"{(v > 0 ? "+" : "")}{s(v)}% roll stamina cost", "wearP" => $"{(v > 0 ? "+" : "")}{s(v)}% gear wear",
            _ when k.StartsWith("skill_") => $"+{s(v)} to {Skills.Get(k[6..])?.Name ?? k[6..]} level",
            _ when k.StartsWith("school_") => $"+{s(v)} to all {k[7..]} skill levels",
            _ when k.StartsWith("life_") => $"+{s(v)} {char.ToUpper(k[5]) + k[6..]} level",
            _ when k.StartsWith("res_") => $"{(v > 0 ? "+" : "")}{s(v)}% {k[4..]} resistance",
            _ when k.EndsWith("Chance") => $"{s(v)}% chance on hit to inflict {Dmg.Status(k[..^6])?.Name.ToLower() ?? k[..^6]}",
            _ when k.EndsWith("P") && Dmg.Status(k[..^1]) != null && !System.Array.Exists(Dmg.Types, t => t + "P" == k) => $"+{s(v)}% {Dmg.Status(k[..^1])!.Name.ToLower()} strength",
            _ when k.EndsWith("P") => $"+{s(v)}% {k[..^1]} damage",
            _ => FlagText.TryGetValue(k, out var t) ? t : $"{k} {s(v)}",
        }).Replace("+-", "-").Replace("--", "+");   // a keystone's price ("-10% max health", "+20% form cooldown") reads right
    }
}
