using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One node of a pet skill tree: up to Max ranks, open once Req points are spent in its branch.</summary>
public sealed class PetNode
{
    public string Id = "", Name = "", Desc = "", Branch = "", Skill = "";
    public int Max = 1, Req; public bool Cap;
    public Dictionary<string, float> Mods = new();     // per rank: hpP atkP defP spdP magP (stats, %), powerP (all skills), cdP (cooldowns, - = faster), critP, leechP, guardP, regenP, mountSpdP, mountStP, xpP, lootRange
    public Dictionary<string, float> SkillP = new();   // per rank: skill id -> power %
}
public sealed class PetBranch { public string Id = "", Name = ""; public List<PetNode> Nodes = new(); }
public sealed class PetFamily { public string Id = "", Name = "", Desc = ""; public List<PetBranch> Branches = new(); }

/// <summary>
/// Pet skill trees (A-20; data/pets.json "tree"): every pet has one point per level after the first (tree.pointsPerLevel) to spend in
/// its family's tree (beast, flyer, spirit, construct: three branches of nodes and a capstone each). Ranks are saved on the pet
/// (PetState.Tree); the points themselves are worked out from its level, so pets from older saves simply have all of theirs free.
/// Effects go through PetDb.StatsOf (stat %), the world companion (Pet.cs: power, cooldowns, crits, leech, guard, regen), riding
/// (PetDb.Mount) and unlock skills. Respec costs gold (tree.respecBase + respecPerPoint x points spent).
/// </summary>
public static class PetTree
{
    static readonly Dictionary<string, PetFamily> _fam = new(); static readonly Dictionary<string, PetNode> _nodes = new();
    static readonly Dictionary<string, string> _familyOf = new(); static bool _loaded;
    static float _perLevel = 1, _respecBase = 50, _respecPer = 15;

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        try
        {
            if (!PetDb.Root.TryGetProperty("tree", out var t)) return;
            _perLevel = JsonUtil.F(t, "pointsPerLevel", 1); _respecBase = JsonUtil.F(t, "respecBase", 50); _respecPer = JsonUtil.F(t, "respecPerPoint", 15);
            if (t.TryGetProperty("familyOf", out var fo)) foreach (var p in fo.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) _familyOf[p.Name] = p.Value.GetString() ?? "beast";
            if (!t.TryGetProperty("families", out var fs)) return;
            foreach (var fp in fs.EnumerateObject())
            {
                if (fp.Name.StartsWith("_")) continue;
                var f = new PetFamily { Id = fp.Name, Name = JsonUtil.S(fp.Value, "name", fp.Name), Desc = JsonUtil.S(fp.Value, "d") };
                if (fp.Value.TryGetProperty("branches", out var bs)) foreach (var be in bs.EnumerateArray())
                {
                    var b = new PetBranch { Id = JsonUtil.S(be, "id"), Name = JsonUtil.S(be, "name") };
                    if (be.TryGetProperty("nodes", out var ns)) foreach (var ne in ns.EnumerateArray())
                    {
                        var n = new PetNode { Id = $"{f.Id}.{b.Id}.{JsonUtil.S(ne, "id")}", Name = JsonUtil.S(ne, "name"), Desc = JsonUtil.S(ne, "d"), Branch = b.Id, Skill = JsonUtil.S(ne, "skill"),
                            Max = Math.Max(1, (int)JsonUtil.F(ne, "max", 1)), Req = (int)JsonUtil.F(ne, "req"), Cap = ne.TryGetProperty("cap", out var cp) && cp.ValueKind == JsonValueKind.True };
                        if (ne.TryGetProperty("mods", out var ms)) foreach (var m in ms.EnumerateObject()) if (m.Value.ValueKind == JsonValueKind.Number) n.Mods[m.Name] = m.Value.GetSingle();
                        if (ne.TryGetProperty("skillP", out var sp)) foreach (var m in sp.EnumerateObject()) if (m.Value.ValueKind == JsonValueKind.Number) n.SkillP[m.Name] = m.Value.GetSingle();
                        b.Nodes.Add(n); _nodes[n.Id] = n;
                    }
                    f.Branches.Add(b);
                }
                _fam[f.Id] = f;
            }
        }
        catch (Exception e) { GD.PushError("pets.json tree: " + e.Message); }
    }

    /// <summary>The family of a species: tree.familyOf by archetype, else by nature of the beast (fliers, elemental spirits).</summary>
    public static string FamilyOf(string arch, string elem, bool flies)
    {
        Load(); if (_familyOf.TryGetValue(arch, out var f) && _fam.ContainsKey(f)) return f;
        if (flies && _fam.ContainsKey("flyer")) return "flyer";
        return _fam.ContainsKey("beast") ? "beast" : _fam.Keys.FirstOrDefault() ?? "beast";
    }
    public static PetFamily? Family(PetState p) { Load(); return _fam.GetValueOrDefault(p.Sp().Family); }
    public static PetNode? Node(string id) { Load(); return _nodes.GetValueOrDefault(id); }

    public static int Points(PetState p) { Load(); return Math.Max(0, Mathf.FloorToInt((p.Level - 1) * _perLevel)); }
    public static int Spent(PetState p) { int n = 0; if (p.Tree != null) foreach (var kv in p.Tree) if (Node(kv.Key) != null) n += kv.Value; return n; }
    public static int Free(PetState p) => Points(p) - Spent(p);
    public static int Rank(PetState p, PetNode n) => p.Tree != null && p.Tree.TryGetValue(n.Id, out int r) ? r : 0;
    public static int SpentIn(PetState p, string branch) { int n = 0; if (p.Tree != null) foreach (var kv in p.Tree) if (Node(kv.Key) is { } nd && nd.Branch == branch && nd.Id.StartsWith(p.Sp().Family + ".")) n += kv.Value; return n; }

    /// <summary>Why this node cannot take a point now (null = it can).</summary>
    public static string? Why(PetState p, PetNode n)
    {
        if (!n.Id.StartsWith(p.Sp().Family + ".")) return "Not this pet's tree";
        if (Rank(p, n) >= n.Max) return "Mastered";
        if (Free(p) <= 0) return "No pet points (one per level)";
        int inB = SpentIn(p, n.Branch); if (inB < n.Req) return $"Needs {n.Req} points in this branch ({inB})";
        return null;
    }
    public static bool Buy(PetState p, PetNode n)
    {
        if (Why(p, n) != null) return false;
        p.Tree ??= new(); p.Tree[n.Id] = Rank(p, n) + 1;
        if (n.Skill != "" && PetDb.Skill(n.Skill) != null && !p.Learned.Contains(n.Skill)) { p.Learned.Add(n.Skill); if (p.Skills.Count < 4) p.Skills.Add(n.Skill); }
        Refresh(p); return true;
    }
    public static int RespecCost(PetState p) { Load(); return Mathf.RoundToInt(_respecBase + _respecPer * Spent(p)); }
    /// <summary>All points back for gold; skills the tree taught (and the species does not learn by itself) are forgotten.</summary>
    public static bool Respec(PetState p)
    {
        int cost = RespecCost(p); if (Spent(p) <= 0 || Game.D.Gold < cost) return false;
        Game.D.Gold -= cost;
        foreach (var kv in p.Tree.ToList())
            if (Node(kv.Key) is { Skill: not "" } n && !p.Sp().Learn.Exists(l => l.skill == n.Skill && l.lvl <= p.Level)) { p.Learned.Remove(n.Skill); p.Skills.Remove(n.Skill); }
        p.Tree.Clear(); p.LearnUpTo(); if (p.Skills.Count == 0 && p.Learned.Count > 0) p.Skills.Add(p.Learned[0]);
        Refresh(p); return true;
    }
    static void Refresh(PetState p) { if (PetWorld.Out != null && GodotObject.IsInstanceValid(PetWorld.Out) && PetWorld.Out.S == p) PetWorld.Out.Refresh(); Game.Notify(); }

    /// <summary>The sum of a modifier over the pet's ranks (per rank x rank).</summary>
    public static float Mod(PetState p, string key)
    {
        if (p.Tree == null || p.Tree.Count == 0) return 0; float s = 0;
        foreach (var kv in p.Tree) if (Node(kv.Key) is { } n && n.Mods.TryGetValue(key, out float v)) s += v * kv.Value;
        return s;
    }
    /// <summary>Power multiplier of one skill for this pet: powerP (all skills) plus the node bonuses for that skill.</summary>
    public static float SkillPower(PetState p, string skill)
    {
        if (p.Tree == null || p.Tree.Count == 0) return 1; float s = 0;
        foreach (var kv in p.Tree) if (Node(kv.Key) is { } n) { if (n.Mods.TryGetValue("powerP", out float v)) s += v * kv.Value; if (n.SkillP.TryGetValue(skill, out float w)) s += w * kv.Value; }
        return 1 + s / 100f;
    }
    /// <summary>"+4% attack per rank" style text of a node's effects.</summary>
    public static string Effects(PetNode n)
    {
        var parts = new List<string>();
        foreach (var kv in n.Mods) parts.Add($"{(kv.Value >= 0 ? "+" : "")}{kv.Value:0.#}{(kv.Key == "lootRange" ? " m" : "%")} {Label(kv.Key)}");
        foreach (var kv in n.SkillP) parts.Add($"+{kv.Value:0.#}% {PetDb.Skill(kv.Key)?.Name ?? kv.Key} power");
        if (n.Skill != "") parts.Add("teaches " + (PetDb.Skill(n.Skill)?.Name ?? n.Skill));
        return string.Join(", ", parts) + (n.Max > 1 ? " per rank" : "");
    }
    static string Label(string k) => k switch
    {
        "hpP" => "health", "atkP" => "attack", "defP" => "defence", "spdP" => "speed", "magP" => "magic", "powerP" => "skill power", "cdP" => "skill cooldowns",
        "critP" => "critical chance", "leechP" => "life leech", "guardP" => "damage taken", "regenP" => "regeneration", "mountSpdP" => "riding speed",
        "mountStP" => "mount stamina", "xpP" => "experience", "lootRange" => "loot range", _ => k,
    };
}
