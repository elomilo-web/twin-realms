using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One ability of a form (data/forms.json): what replaces the hotbar while transformed.</summary>
public class FormAbility
{
    public string Name = "", Desc = "", Kind = "melee", Type = "physical", Fx = "";
    public float Dmg, Range = 2.5f, Arc = 70, Radius, Aoe, Cd = 1, St, Mp, Leech, Knock, Stun, Heal, Dur;
    public bool Crit;
    public Dictionary<string, float> Status = new(), Buff = new();
}

/// <summary>An animal or spirit form (data/forms.json).</summary>
public class FormDef
{
    public string Id = "", Name = "", Kind = "animal", Desc = "", Body = "wolf", Tint = "";
    public Color Col = Colors.White;
    public float Scale = 1, Dur = 60, Cd = 40, Mp = 25, Cast = .6f, Speed = 1, Jump = 1;
    public bool Swim = true, Ghost, Fly, Glow;
    public Dictionary<string, float> Mods = new();
    public List<FormAbility> Abilities = new();
    /// <summary>The stat key that teaches the form and the id of its skill ("form_wolf").</summary>
    public string Key => "form_" + Id;
}

/// <summary>
/// A-16: transformations. The forms of data/forms.json, each registered as a skill "form_(id)" of kind "form" (cost, cooldown and cast
/// time from the form), so a learned form sits in the Skills menu and on the hotbar like any skill and casting it goes through the one
/// cast gate. A form is learned while the stat key form_(id) is above 0: a skill tree node (Druid, Shaman, Shapeshifter and the
/// advanced paths), or a unique item's stats. The hero's side of it (the body, movement, abilities) is PlayerForm.cs; friends see the
/// form from one number in the pose packet (RemoteHeroForm.cs).
/// </summary>
public static class Forms
{
    static readonly Dictionary<string, FormDef> _f = new(); public static readonly List<string> Order = new();
    static bool _loaded; static JsonElement _rules; static JsonDocument? _doc;
    public static FormDef? Get(string id) { Load(); return _f.GetValueOrDefault(id.StartsWith("form_") ? id[5..] : id); }
    public static IEnumerable<FormDef> All { get { Load(); foreach (var k in Order) yield return _f[k]; } }
    public static string NameOf(string id) => Get(id)?.Name ?? id;
    /// <summary>1-based index of a form for the pose packet (0 = no form), and back.</summary>
    public static int IndexOf(string id) { Load(); return Order.IndexOf(id) + 1; }
    public static FormDef? At(int index) { Load(); return index >= 1 && index <= Order.Count ? _f[Order[index - 1]] : null; }
    public static float Rule(string k, float def) { Load(); return _rules.ValueKind == JsonValueKind.Object ? JsonUtil.F(_rules, k, def) : def; }

    static Dictionary<string, float> Nums(JsonElement e, string key)
    {
        var d = new Dictionary<string, float>(); if (!e.TryGetProperty(key, out var o) || o.ValueKind != JsonValueKind.Object) return d;
        foreach (var x in o.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) d[x.Name] = x.Value.GetSingle();
        return d;
    }
    static bool B(JsonElement e, string k, bool def = false) => e.TryGetProperty(k, out var v) ? v.ValueKind == JsonValueKind.True || (v.ValueKind != JsonValueKind.False && def) : def;
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        if (!FileAccess.FileExists("res://data/forms.json")) return;
        _doc = JsonUtil.Load("res://data/forms.json"); var r = _doc.RootElement;
        if (r.TryGetProperty("rules", out var ru)) _rules = ru;
        if (!r.TryGetProperty("forms", out var fs)) return;
        foreach (var p in fs.EnumerateObject())
        {
            if (p.Name.StartsWith("_") || p.Value.ValueKind != JsonValueKind.Object) continue; var e = p.Value;
            var f = new FormDef
            {
                Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Kind = JsonUtil.S(e, "kind", "animal"), Desc = JsonUtil.S(e, "d"), Body = JsonUtil.S(e, "body", "wolf"), Tint = JsonUtil.S(e, "tint"),
                Scale = JsonUtil.F(e, "scale", 1), Dur = JsonUtil.F(e, "dur", 60), Cd = JsonUtil.F(e, "cd", 40), Mp = JsonUtil.F(e, "mp", 25), Cast = JsonUtil.F(e, "cast", .6f),
                Speed = JsonUtil.F(e, "speed", 1), Jump = JsonUtil.F(e, "jump", 1), Swim = B(e, "swim", true), Ghost = B(e, "ghost"), Fly = B(e, "fly"), Glow = B(e, "glow"), Mods = Nums(e, "mods"),
            };
            string col = JsonUtil.S(e, "col", "ffffff"); f.Col = Color.HtmlIsValid(col) ? Color.FromHtml(col) : Colors.White;
            if (e.TryGetProperty("abilities", out var ab) && ab.ValueKind == JsonValueKind.Array)
                foreach (var a in ab.EnumerateArray())
                    f.Abilities.Add(new FormAbility
                    {
                        Name = JsonUtil.S(a, "n"), Desc = JsonUtil.S(a, "d"), Kind = JsonUtil.S(a, "kind", "melee"), Type = JsonUtil.S(a, "type", "physical"), Fx = JsonUtil.S(a, "fx"),
                        Dmg = JsonUtil.F(a, "dmg"), Range = JsonUtil.F(a, "range", 2.5f), Arc = JsonUtil.F(a, "arc", 70), Radius = JsonUtil.F(a, "radius"), Aoe = JsonUtil.F(a, "aoe"), Cd = JsonUtil.F(a, "cd", 1),
                        St = JsonUtil.F(a, "st"), Mp = JsonUtil.F(a, "mp"), Leech = JsonUtil.F(a, "leech"), Knock = JsonUtil.F(a, "knock"), Stun = JsonUtil.F(a, "stun"), Heal = JsonUtil.F(a, "heal"), Dur = JsonUtil.F(a, "dur"),
                        Crit = B(a, "crit"), Status = Nums(a, "status"), Buff = Nums(a, "buff"),
                    });
            if (f.Abilities.Count == 0) { GD.PushWarning($"forms.json: form {f.Id} has no abilities; left out"); continue; }
            _f[f.Id] = f; Order.Add(f.Id);
        }
    }

    /// <summary>The id of the hidden party skill a form's heal ability sends to allies (ApplyEnchant: heal, then its buff).</summary>
    public static string AllyId(FormDef f, int ability) => f.Key + ".a" + ability;

    /// <summary>
    /// Every form becomes a skill (called with Spellcraft.Sync whenever a hero is loaded or made, Skills.GrantStarting): listed in
    /// the Skills menu only for a hero who has learned it. Heal abilities get a hidden party skill each, so a friend's game knows
    /// what our Mend or Rallying Call does to them (the same data on both sides).
    /// </summary>
    public static void Register(PlayerStats d)
    {
        Load();
        foreach (var f in All)
        {
            string abil = string.Join(", ", f.Abilities.ConvertAll(a => a.Name));
            var s = new SkillDef
            {
                Id = f.Key, Name = f.Name + " Form", Kind = "form", Type = "physical", Scale = "spell", Mp = f.Mp, Cd = f.Cd, Cast = f.Cast, Dur = f.Dur, Req = 1, Cost = 0,
                Desc = $"Take the shape of a {f.Name} for {f.Dur:0} s (key {(int)Rule("revertKey", 6)} changes back). {f.Desc} Abilities: {abil}. No gear skills, blocking, rolling or riding while transformed.",
                Tags = new List<string> { "form", f.Kind },
            };
            Skills.Register(s, d.Skills != null && d.Skills.ContainsKey(f.Key));
            for (int i = 0; i < f.Abilities.Count; i++)
            {
                var a = f.Abilities[i]; if (a.Kind != "heal") continue;
                Skills.Register(new SkillDef { Id = AllyId(f, i), Name = a.Name, Kind = "enchant", Type = "holy", Heal = a.Heal, Dur = a.Dur, Radius = a.Radius, Buff = new Dictionary<string, float>(a.Buff), Desc = a.Desc, Cost = 0 }, false);
            }
        }
    }

    /// <summary>The form is learned right now: its stat key is above 0 (tree nodes and worn items; not timed buffs' doing).</summary>
    public static bool Learned(FormDef f) => Game.TS[f.Key] > 0;

    /// <summary>
    /// Keeps the hero's form skills in step with what teaches them (PlayerForm calls it about once a second): a form whose key
    /// appeared is learned (a skill, on a free hotbar key), one whose node was refunded or whose item came off is forgotten.
    /// A form the player unlearned by hand in the Skills menu (PlayerStats.Forgot) stays unlearned until the key is gained anew.
    /// </summary>
    public static void Sync(PlayerStats d)
    {
        Load(); bool changed = false;
        foreach (var f in All)
        {
            bool has = Learned(f), known = d.Skills.ContainsKey(f.Key);
            if (has && !known && !d.Forgot.Contains(f.Key))
            {
                d.Skills[f.Key] = new SkillState(); changed = true;
                int slot = Array.FindIndex(d.Bar, b => string.IsNullOrEmpty(b)); if (slot >= 0) d.Bar[slot] = f.Key;
                Ui.Note($"New form learned: {f.Name}. " + (slot >= 0 ? $"It is on key {Inputs.KeyOf("spell" + (slot + 1))}." : $"Put it on your hotbar in the Skills menu ({Inputs.KeyOf(Inputs.SkillsUi)})."), f.Col.Lightened(.2f));
                Ui.Banner(f.Name + " Form", f.Kind == "spirit" ? "a spirit answers you" : "the wild answers you");
            }
            else if (!has && known)
            {
                d.Skills.Remove(f.Key); changed = true;
                for (int i = 0; i < d.Bar.Length; i++) if (d.Bar[i] == f.Key) d.Bar[i] = "";
                foreach (var m in d.Macros ?? new()) m.Steps.RemoveAll(x => x.Skill == f.Key);
                Ui.Note($"You no longer know the {f.Name} form (its tree node or item is gone)", new Color(1f, .7f, .5f));
            }
            if (!has) d.Forgot.Remove(f.Key);
        }
        if (changed) { Register(d); Game.Notify(); }
    }
}
