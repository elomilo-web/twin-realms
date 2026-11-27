using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A named passive (class or race): a few permanent stat-table keys (F-34/F-35).</summary>
public class PassiveDef
{
    public string Name = "", Desc = ""; public Dictionary<string, float> Mods = new();
    public static List<PassiveDef> List(JsonElement e, string key)
    {
        var l = new List<PassiveDef>();
        if (!e.TryGetProperty(key, out var a)) return l;
        void One(JsonElement p)
        {
            var d = new PassiveDef { Name = JsonUtil.S(p, "n"), Desc = JsonUtil.S(p, "d") };
            if (p.TryGetProperty("mods", out var m)) foreach (var x in m.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) d.Mods[x.Name] = x.Value.GetSingle();
            l.Add(d);
        }
        if (a.ValueKind == JsonValueKind.Array) foreach (var p in a.EnumerateArray()) One(p);
        else if (a.ValueKind == JsonValueKind.Object) One(a);
        return l;
    }
}

public class ClassDef
{
    public string Id = "", Name = "", Desc = "", Start = "";
    public Dictionary<string, int> Attr = new(); public Dictionary<string, float> Mods = new();
    public string WName = "", Wt = "sword"; public int WDmg; public float WSp; public bool Shield;
    public List<string> Skills = new();
    public List<PassiveDef> Passives = new();
    /// <summary>Starting gear set besides the weapon and shield: slot -> (name, def, hp, mp, dmg).</summary>
    public Dictionary<string, (string n, int def, int hp, int mp, int dmg)> Gear = new();
    public Color Tunic = new(.58f, .16f, .17f), Cape = new(.18f, .22f, .4f);
    /// <summary>A-17: how the class plays, as (label, text) lines: role, weapon, resource, signature skills, skill tree, tip (data/classes.json "guide").</summary>
    public List<(string label, string text)> Guide = new();
}

/// <summary>Classes from data/classes.json: starting attributes, weapon, gear set, class passives, starting skills and colours.</summary>
public static class Classes
{
    static readonly Dictionary<string, ClassDef> _d = new(); public static readonly List<string> Order = new();
    public static ClassDef Get(string id) { Load(); return _d.TryGetValue(id, out var c) ? c : _d["warrior"]; }
    public static IEnumerable<ClassDef> All { get { Load(); foreach (var k in Order) yield return _d[k]; } }

    static void Load()
    {
        if (_d.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/classes.json");
        foreach (var p in doc.RootElement.GetProperty("classes").EnumerateObject())
        {
            var e = p.Value; var c = new ClassDef { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Desc = JsonUtil.S(e, "d"), Start = JsonUtil.S(e, "start", p.Name), Shield = e.TryGetProperty("shield", out var s) && s.GetBoolean() };
            if (e.TryGetProperty("a", out var a)) foreach (var x in a.EnumerateObject()) c.Attr[x.Name] = x.Value.GetInt32();
            if (e.TryGetProperty("mods", out var m)) foreach (var x in m.EnumerateObject()) c.Mods[x.Name] = x.Value.GetSingle();
            if (e.TryGetProperty("skills", out var sk)) foreach (var x in sk.EnumerateArray()) c.Skills.Add(x.GetString() ?? "");
            if (e.TryGetProperty("w", out var w)) { c.WName = JsonUtil.S(w, "name", "Weapon"); c.Wt = JsonUtil.S(w, "wt", "sword"); c.WDmg = (int)JsonUtil.F(w, "dmg"); c.WSp = JsonUtil.F(w, "sp"); }
            if (e.TryGetProperty("tunic", out var t)) c.Tunic = Color.FromHtml(t.GetString() ?? "");
            if (e.TryGetProperty("cape", out var cp)) c.Cape = Color.FromHtml(cp.GetString() ?? "");
            c.Passives = PassiveDef.List(e, "passives");
            if (e.TryGetProperty("gear", out var g) && g.ValueKind == JsonValueKind.Object)
                foreach (var x in g.EnumerateObject())
                    c.Gear[x.Name] = (JsonUtil.S(x.Value, "n", x.Name), (int)JsonUtil.F(x.Value, "def"), (int)JsonUtil.F(x.Value, "hp"), (int)JsonUtil.F(x.Value, "mp"), (int)JsonUtil.F(x.Value, "dmg"));
            _d[p.Name] = c; Order.Add(p.Name);
        }
        // A-17: how each class plays ("guide": role, weapon, resource, signature, tree, tip)
        if (doc.RootElement.TryGetProperty("guide", out var gd) && gd.ValueKind == JsonValueKind.Object)
            foreach (var p in gd.EnumerateObject())
                if (_d.TryGetValue(p.Name, out var c) && p.Value.ValueKind == JsonValueKind.Object)
                    foreach (var (key, label) in GuideKeys) { string t = JsonUtil.S(p.Value, key); if (t != "") c.Guide.Add((label, t)); }
    }
    static readonly (string key, string label)[] GuideKeys = { ("role", "Role"), ("weapon", "Weapon"), ("resource", "Resource"), ("signature", "Signature skills"), ("tree", "Skill tree"), ("tip", "Tip") };

    /// <summary>The class's starting equipment: weapon (none for fists), shield, and its gear set (F-34).</summary>
    public static Dictionary<string, Item?> StartGear(ClassDef c)
    {
        var eq = new Dictionary<string, Item?>();
        eq["weapon"] = c.Wt == "unarmed" ? null : new Item { Name = c.WName, Slot = "weapon", Wt = c.Wt, Dmg = c.WDmg, Sp = c.WSp, Two = Weapons.Get(c.Wt).Hands > 1 };
        eq["shield"] = c.Shield ? new Item { Name = "Oak Buckler", Slot = "shield", Def = 2 } : null;
        foreach (var kv in c.Gear) eq[kv.Key] = new Item { Name = kv.Value.n, Slot = kv.Key, Def = kv.Value.def, Hp = kv.Value.hp, Mp = kv.Value.mp, Dmg = kv.Value.dmg };
        return eq;
    }
}
