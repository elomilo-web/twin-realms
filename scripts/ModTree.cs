using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// The modifier tree (data/modtree.json): every skill modifier (support) and basic attack modifier sits in a lane of its family,
/// in order. A modifier is unlocked with skill points once the one before it in its lane is unlocked and the hero is high enough.
/// Unlocked modifiers are socketed into skills in the Skills menu (L) and into attack chains in the Basic attacks menu (O).
/// </summary>
public static class ModTree
{
    public sealed class Node
    {
        public string Key = "", Id = "", Name = "", Desc = ""; public bool Attack; public int Cost = 1, Req = 1, Index;
        public List<string> Tags = new(); public Branch Branch = null!; public Node? Prev;
    }
    public sealed class Branch { public string Id = "", Name = "", Desc = "", Icon = "sword"; public Color Col = Colors.White; public List<string> Tags = new(); public List<Node> Nodes = new(); }

    public static readonly List<Branch> Branches = new();
    static readonly Dictionary<string, Node> _byKey = new();
    static bool _loaded;

    /// <summary>Key of a modifier: "s:id" for a skill support, "a:id" for an attack modifier (the two lists share some ids).</summary>
    public static string KeyOf(string id, bool attack) => (attack ? "a:" : "s:") + id;
    public static Node? Get(string key) { Load(); return _byKey.GetValueOrDefault(key); }

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using (var doc = JsonUtil.Load("res://data/modtree.json"))
            foreach (var e in doc.RootElement.GetProperty("branches").EnumerateArray())
            {
                var b = new Branch { Id = JsonUtil.S(e, "id"), Name = JsonUtil.S(e, "name"), Desc = JsonUtil.S(e, "desc"), Icon = JsonUtil.S(e, "icon", "sword"), Col = Color.FromHtml(JsonUtil.S(e, "col", "#ffffff")) };
                if (e.TryGetProperty("tags", out var t)) foreach (var x in t.EnumerateArray()) b.Tags.Add(x.GetString() ?? "");
                Branches.Add(b);
                if (e.TryGetProperty("supports", out var ss)) foreach (var x in ss.EnumerateArray()) if (Skills.Sup(x.GetString() ?? "") is { } sp) Add(b, Sup(sp));
                if (e.TryGetProperty("attackMods", out var am)) foreach (var x in am.EnumerateArray()) if (Attacks.Mod(x.GetString() ?? "") is { } m) Add(b, Atk(m));
            }
        if (Branches.Count == 0) Branches.Add(new Branch { Id = "misc", Name = "Modifiers" });
        // anything not placed joins the lane whose tags it shares (attack modifiers: the lane that holds attack modifiers)
        foreach (var sp in Skills.AllSupports)
        {
            if (_byKey.ContainsKey(KeyOf(sp.Id, false))) continue;
            var b = Branches.Find(x => x.Tags.Exists(sp.Tags.Contains)) ?? Branches[^1]; Add(b, Sup(sp));
        }
        foreach (var m in Attacks.Mods)
        {
            if (_byKey.ContainsKey(KeyOf(m.Id, true))) continue;
            var b = Branches.Find(x => x.Nodes.Exists(n => n.Attack)) ?? Branches[^1]; Add(b, Atk(m));
        }
    }
    static Node Sup(SupportDef s) => new() { Key = KeyOf(s.Id, false), Id = s.Id, Name = s.Name, Desc = s.Desc, Cost = s.Cost, Req = s.Req, Tags = s.Tags };
    static Node Atk(AtkModDef m) => new() { Key = KeyOf(m.Id, true), Id = m.Id, Name = m.Name, Desc = m.Desc, Cost = m.Cost, Req = m.Req, Attack = true, Tags = m.Types != null ? new List<string>(m.Types) : new List<string> { "all weapons" } };
    static void Add(Branch b, Node n) { n.Branch = b; n.Index = b.Nodes.Count; n.Prev = b.Nodes.Count > 0 ? b.Nodes[^1] : null; b.Nodes.Add(n); _byKey[n.Key] = n; }

    public static bool Unlocked(Node n) => n.Attack ? Game.D.AtkMods.Contains(n.Id) : Game.D.Supports.Contains(n.Id);
    /// <summary>Null when the node can be unlocked now, otherwise why not.</summary>
    public static string? Why(Node n)
    {
        var d = Game.D;
        if (Unlocked(n)) return "Unlocked";
        if (n.Prev != null && !Unlocked(n.Prev)) return $"Unlock {n.Prev.Name} first";
        if (d.Level < n.Req) return $"Requires level {n.Req}";
        if (d.RankPts < n.Cost) return $"Needs {n.Cost} skill point" + (n.Cost > 1 ? "s" : "");
        return null;
    }
    public static string? Unlock(Node n)
    {
        var why = Why(n); if (why != null) return why;
        return n.Attack ? Attacks.UnlockMod(n.Id) : Skills.UnlockSupport(n.Id);
    }
    public static int Count(bool unlockedOnly) { Load(); int c = 0; foreach (var b in Branches) foreach (var n in b.Nodes) if (!unlockedOnly || Unlocked(n)) c++; return c; }
}
