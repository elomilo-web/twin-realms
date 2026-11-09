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
}

/// <summary>Aggregated skill tree bonuses. Unknown stats read as 0.</summary>
public class TreeStats
{
    readonly Dictionary<string, float> _v = new();
    public float this[string k] { get => _v.TryGetValue(k, out var x) ? x : 0f; set => _v[k] = value; }
    public bool Has(string flag) => this[flag] > 0;
    public void Clear() => _v.Clear();
}

public static class TreeDb
{
    public static List<TreeNode> Nodes = new();
    public static Dictionary<string, string> FlagText = new();
    public static Dictionary<string, int> Starts = new();
    public static int Version = 1;
    static bool _loaded;
    /// <summary>The tree node a class begins on (free; everything grows from it).</summary>
    public static int StartFor(string cls) { Load(); return Starts.TryGetValue(cls, out var s) ? s : 0; }

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
            "finisherP", "finisherStun", "finisherUp", "powerP", "knockP", "reach" };
        foreach (var t in new[] { "physical", "fire", "frost", "lightning", "poison", "shadow", "holy" }) { k.Add(t + "P"); k.Add("res_" + t); }
        foreach (var s in new[] { "burn", "poison", "bleed", "chill", "freeze", "root", "shock", "judged", "weaken", "shred", "stun" }) { k.Add(s + "Chance"); k.Add(s + "P"); }
        foreach (var w in new[] { "sword", "greatsword", "axe", "mace", "dagger", "spear", "bow", "wand", "staff" }) k.Add(w + "P");
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
    }

    public static TreeNode? Get(int id) => id >= 0 && id < Nodes.Count ? Nodes[id] : null;

    public static void Recalc(HashSet<int> tree, TreeStats ts)
    {
        ts.Clear();
        foreach (var id in tree) { var n = Get(id); if (n == null) continue; foreach (var kv in n.Mods) ts[kv.Key] += kv.Value; }
    }

    /// <summary>A node can be taken when it touches a taken node or your class start. Start nodes themselves are never taken.</summary>
    public static bool CanAlloc(TreeNode n, HashSet<int> tree, int pts, int start)
    {
        if (n.Type == "start" || tree.Contains(n.Id) || pts <= 0) return false;
        foreach (var a in n.Adj) if (a == start || tree.Contains(a)) return true;
        return false;
    }

    /// <summary>A node can be refunded when everything else you took still connects back to your class start.</summary>
    public static bool CanDealloc(TreeNode n, HashSet<int> tree, int start)
    {
        if (!tree.Contains(n.Id)) return false;
        var rest = new HashSet<int>(tree); rest.Remove(n.Id);
        var seen = new HashSet<int>(); var q = new Stack<int>(); q.Push(start);
        while (q.Count > 0)
        {
            var c = q.Pop();
            foreach (var a in Nodes[c].Adj) if (rest.Contains(a) && seen.Add(a)) q.Push(a);
        }
        return seen.Count == rest.Count;
    }

    public static string StatText(string k, float v)
    {
        string s(float x) => x.ToString("0.##");
        return k switch
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
            "knockP" => $"+{s(v)}% knockback", "reach" => $"+{s(v)}% melee reach",
            _ when k.StartsWith("res_") => $"{(v > 0 ? "+" : "")}{s(v)}% {k[4..]} resistance",
            _ when k.EndsWith("Chance") => $"{s(v)}% chance on hit to inflict {Dmg.Status(k[..^6])?.Name.ToLower() ?? k[..^6]}",
            _ when k.EndsWith("P") && Dmg.Status(k[..^1]) != null && !System.Array.Exists(Dmg.Types, t => t + "P" == k) => $"+{s(v)}% {Dmg.Status(k[..^1])!.Name.ToLower()} strength",
            _ when k.EndsWith("P") => $"+{s(v)}% {k[..^1]} damage",
            _ => FlagText.TryGetValue(k, out var t) ? t : $"{k} {s(v)}",
        };
    }
}
