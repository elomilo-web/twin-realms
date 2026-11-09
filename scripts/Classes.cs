using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class ClassDef
{
    public string Id = "", Name = "", Desc = "", Start = "";
    public Dictionary<string, int> Attr = new(); public Dictionary<string, float> Mods = new();
    public string WName = "", Wt = "sword"; public int WDmg; public float WSp; public bool Shield;
    public List<string> Skills = new();
    public Color Tunic = new(.58f, .16f, .17f), Cape = new(.18f, .22f, .4f);
}

/// <summary>Classes from data/classes.json: starting attributes, weapon, class passive, starting skills and colours.</summary>
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
            _d[p.Name] = c; Order.Add(p.Name);
        }
    }
}
