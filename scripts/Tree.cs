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
    static bool _loaded;

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
    }

    public static TreeNode? Get(int id) => id >= 0 && id < Nodes.Count ? Nodes[id] : null;

    public static void Recalc(HashSet<int> tree, TreeStats ts)
    {
        ts.Clear();
        foreach (var id in tree) { var n = Get(id); if (n == null) continue; foreach (var kv in n.Mods) ts[kv.Key] += kv.Value; }
    }

    public static bool CanAlloc(TreeNode n, HashSet<int> tree, int pts)
    {
        if (n.Id == 0 || tree.Contains(n.Id) || pts <= 0) return false;
        foreach (var a in n.Adj) if (a == 0 || tree.Contains(a)) return true;
        return false;
    }

    public static bool CanDealloc(TreeNode n, HashSet<int> tree)
    {
        if (n.Id == 0 || !tree.Contains(n.Id)) return false;
        var rest = new HashSet<int>(tree); rest.Remove(n.Id);
        var seen = new HashSet<int>(); var q = new Stack<int>(); q.Push(0);
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
            _ => FlagText.TryGetValue(k, out var t) ? t : $"{k} {s(v)}",
        };
    }
}
