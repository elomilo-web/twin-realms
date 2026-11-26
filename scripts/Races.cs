using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class RaceDef
{
    public string Id = "", Name = "", Desc = ""; public int Pts;
    public Dictionary<string, int> Attr = new(); public Dictionary<string, float> Mods = new();
    public List<PassiveDef> Passives = new();   // the racial passive (F-35)
    public Appearance Look = new();
}

/// <summary>Playable races from data/races.json: attributes, permanent bonuses, racial passive and appearance (body proportions, attachments).</summary>
public static class Races
{
    static readonly Dictionary<string, RaceDef> _d = new(); public static readonly List<string> Order = new();
    public static RaceDef Get(string id) { Load(); return _d.TryGetValue(id, out var r) ? r : _d["human"]; }
    public static IEnumerable<RaceDef> All { get { Load(); foreach (var k in Order) yield return _d[k]; } }

    static void Load()
    {
        if (_d.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/races.json");
        foreach (var p in doc.RootElement.GetProperty("races").EnumerateObject())
        {
            var e = p.Value; var r = new RaceDef { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Desc = JsonUtil.S(e, "d"), Pts = (int)JsonUtil.F(e, "pts") };
            if (e.TryGetProperty("a", out var a)) foreach (var x in a.EnumerateObject()) r.Attr[x.Name] = x.Value.GetInt32();
            if (e.TryGetProperty("mods", out var m)) foreach (var x in m.EnumerateObject()) r.Mods[x.Name] = x.Value.GetSingle();
            r.Passives = PassiveDef.List(e, "passive");
            if (e.TryGetProperty("look", out var l))
            {
                Color C(string k, Color d) => l.TryGetProperty(k, out var v) ? Color.FromHtml(v.GetString() ?? "") : d;
                bool B(string k) => l.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
                // true = the classic kind; a string names the kind
                string K(string k, string classic) => !l.TryGetProperty(k, out var v) ? "" : v.ValueKind == JsonValueKind.True ? classic : v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                string ears = K("ears", "pointy"), horns = K("horns", "curl");
                r.Look = new Appearance { Skin = C("skin", r.Look.Skin), Hair = C("hair", r.Look.Hair), Eyes = C("eyes", r.Look.Eyes), Height = JsonUtil.F(l, "height", 1), Bulk = JsonUtil.F(l, "bulk", 1),
                    PointyEars = ears != "", Tusks = B("tusks"), Beard = B("beard"), Horns = horns != "", GlowEyes = B("glow"),
                    HeadScale = JsonUtil.F(l, "head", 1), Ears = ears, HornKind = horns, Tail = K("tail", "fox"), Wings = K("wings", "fae") };
            }
            _d[p.Name] = r; Order.Add(p.Name);
        }
    }

    /// <summary>Appearance for the hero: race body plus the class colours.</summary>
    public static Appearance Look(PlayerStats d) => LookFor(d.Race, d.Class, d.Body == "female");

    /// <summary>Appearance of a race and class (the hero, character creation, dev lineups).</summary>
    public static Appearance LookFor(string race, string cls, bool female)
    {
        var ap = Get(race).Look.Copy(); var c = Classes.Get(cls);
        ap.Female = female; ap.Tunic = c.Tunic; ap.Cape = c.Cape; ap.Pants = c.Tunic.Darkened(.6f).Lerp(new Color(.25f, .2f, .18f), .5f);
        return ap;
    }
}
