using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-17: the texts that explain Spellcraft, from data/spellhelp.json: one plain sentence per card type, a "when to use it" line per
/// card, the "How spellcraft works" panel and the first-time hint. The panel's {tokens} are filled from data/spellcards.json, so the
/// numbers it quotes are the rules' own. Also the numbers a card's tooltip states (a carrier's base cost, cooldown, cast time and
/// damage; an effect's status chances; a modifier's changes), read from the same data the spell is built from.
/// </summary>
public static class SpellHelp
{
    static bool _loaded; static string _first = "";
    static readonly Dictionary<string, string> _types = new(), _cards = new(), _fams = new();
    public static readonly List<(string title, List<string> lines)> Panel = new();
    static void Load()
    {
        if (_loaded) return; _loaded = true;
        if (!FileAccess.FileExists("res://data/spellhelp.json")) return;
        using var doc = JsonUtil.Load("res://data/spellhelp.json"); var r = doc.RootElement;
        _first = JsonUtil.S(r, "firstHint");
        void Map(string key, Dictionary<string, string> into) { if (r.TryGetProperty(key, out var o) && o.ValueKind == JsonValueKind.Object) foreach (var p in o.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.String) into[p.Name] = p.Value.GetString() ?? ""; }
        Map("types", _types); Map("cards", _cards); Map("families", _fams);
        if (r.TryGetProperty("panel", out var pn) && pn.ValueKind == JsonValueKind.Array)
            foreach (var s in pn.EnumerateArray())
            {
                var lines = new List<string>(); if (s.TryGetProperty("lines", out var ls)) foreach (var x in ls.EnumerateArray()) lines.Add(Fill(x.GetString() ?? ""));
                Panel.Add((JsonUtil.S(s, "title"), lines));
            }
    }
    public static string FirstHint { get { Load(); return _first; } }
    public static string TypeText(string type) { Load(); return _types.GetValueOrDefault(type, ""); }
    public static string FamilyText(string fam) { Load(); return _fams.GetValueOrDefault(fam, ""); }
    public static string CardText(string id) { Load(); return _cards.GetValueOrDefault(id, ""); }
    public static IEnumerable<(string title, List<string> lines)> Sections { get { Load(); return Panel; } }

    // ---- numbers from data/spellcards.json (through JsonUtil, so data/tuning.json overrides count)
    static JsonDocument? _sc;
    static float Num(string path, float def)
    {
        _sc ??= JsonUtil.Load("res://data/spellcards.json"); var e = _sc.RootElement;
        foreach (var part in path.Split('.'))
        {
            if (e.ValueKind == JsonValueKind.Array && int.TryParse(part, out int i)) { if (e.GetArrayLength() == 0) return def; e = e[Math.Min(i, e.GetArrayLength() - 1)]; continue; }
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return def; e = n;
        }
        return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : def;
    }
    static string N(float v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    static string Fill(string s)
    {
        if (!s.Contains('{')) return s;
        var t = new Dictionary<string, string>
        {
            ["effectP"] = N(Num("stack.effectP", 22)), ["statusDurP"] = N(Num("stack.statusDurP", 12)), ["costP"] = N(Num("stack.costP", 10)),
            ["secCost2"] = N(Num("sections.costP.1", 60)), ["secCost3"] = N(Num("sections.costP.2", 75)), ["secCd2"] = N(Num("sections.cdP.1", 30)), ["secCd3"] = N(Num("sections.cdP.2", 40)),
            ["zoneDur"] = N(Num("zones.dur", 4)), ["zoneTick"] = N(Num("zones.tick", 1)), ["slotsBase"] = N(Num("slots.base", 4)), ["slotsPer"] = N(Num("slots.perLevels", 5)),
            ["copiesBase"] = N(Num("mastery.base", 2)), ["copiesPer"] = N(Num("mastery.perLevels", 6)), ["sec3Level"] = N(Num("sections.unlockLevel.2", 12)), ["maxSpells"] = N(Num("maxSpells", 8)),
            ["chainCarriers"] = string.Join(", ", Spellcraft.SecondCarriers.Select(x => Spellcraft.Card(x)?.Name ?? x)), ["rankP"] = N(Skills.RankP),
        };
        foreach (var kv in t) s = s.Replace("{" + kv.Key + "}", kv.Value);
        return s;
    }

    /// <summary>What a modifier number means, in words.</summary>
    static string ModText(string k, float v)
    {
        string sg = v > 0 ? "+" : "";
        return k switch
        {
            "dmgP" => $"{sg}{N(v)}% damage", "costP" => $"{sg}{N(v)}% cost", "aoeP" => $"{sg}{N(v)}% area", "cdP" => $"{sg}{N(v)}% cooldown", "castP" => $"{sg}{N(v)}% cast time",
            "durP" => $"{sg}{N(v)}% duration", "tickP" => $"{sg}{N(v)}% time between ticks", "projSpeedP" => $"{sg}{N(v)}% missile speed", "projCount" => $"{sg}{N(v)} missiles",
            "pierce" => $"pierces {N(v)} foes", "chain" => $"leaps to {N(v)} more foes", "echo" => $"repeats {N(v)} time{(v > 1 ? "s" : "")}", "homing" => "seeks foes", "critC" => $"{sg}{N(v)}% critical chance",
            "selfHeal" => $"heals you {N(v)}% of max health per cast", "manaBack" => $"{N(v)}% of the cost returned", "knockP" => $"{sg}{N(v)}% knockback", "stun" => $"stuns {N(v)} s", "up" => $"knocks up ({N(v)})",
            "leech" => $"{N(v)}% life steal", "execute" => $"executes foes under {N(v)}% health", "healP" => $"{sg}{N(v)}% healing", "autoCd" => $"at most every {N(v)} s", _ => $"{k} {N(v)}",
        };
    }

    /// <summary>The numbers a card stands for, one line each (empty when it has none worth stating).</summary>
    public static List<string> Numbers(CardDef c)
    {
        var l = new List<string>();
        if (c.Type == "carrier" && Skills.Get(c.From) is { } k)
        {
            float G(string key, float v) => c.Set.TryGetValue(key, out var o) ? o : v;
            float mp = G("mp", k.Mp), st = G("st", k.St), cd = G("cd", k.Cd), cast = G("cast", k.Cast), dmg = G("dmg", k.Dmg), lvl = G("lvl", k.Lvl), mult = G("mult", k.Mult), rad = G("radius", k.Radius), dur = G("dur", k.Dur), range = G("range", k.Range);
            int count = (int)G("count", k.Count);
            var cost = new List<string>(); if (mp > 0) cost.Add($"{N(mp)} mana"); if (st > 0) cost.Add($"{N(st)} stamina"); cost.Add($"{N(cd)} s cooldown"); cost.Add($"{N(cast)} s cast");
            l.Add("Base: " + string.Join(", ", cost));
            bool hits = k.Kind is not ("enchant" or "buff" or "ward" or "heal");
            if (hits) l.Add(k.Scale == "weapon" ? $"Damage: {N(mult * 100)}% of your weapon's damage" + (count > 1 ? $" x{count}" : "") : $"Damage: ({N(dmg)} + {N(lvl)} per level) x spell power" + (count > 1 ? $" x{count}" : ""));
            var shape = new List<string>(); if (rad > 0) shape.Add($"{N(rad)} m radius"); if (dur > 0) shape.Add($"lasts {N(dur)} s"); if (k.Kind is "leap" or "dash" or "blinkto" or "trap" or "turret" or "rush") shape.Add($"{N(range)} m range");
            if (shape.Count > 0) l.Add("Shape: " + string.Join(", ", shape));
            if (Spellcraft.SecondCarriers.Contains(c.Id)) l.Add("Can be a chained section (II or III).");
        }
        else if (c.Type == "effect")
        {
            foreach (var (sid, a) in c.Status)
            {
                string nm = Dmg.Status(sid)?.Name ?? sid; float ch = a.Length > 0 ? a[0] : 1;
                l.Add($"{nm}: {N(ch * 100)}% chance per hit" + (a.Length > 1 && Mathf.Abs(a[1] - 1) > .01f ? $", {N(a[1] * 100)}% duration" : ""));
            }
            var ex = new List<string>(); if (c.Leech > 0) ex.Add($"{N(c.Leech)}% life steal"); if (c.Knock > 0) ex.Add($"knockback {N(c.Knock)}"); if (c.Stun > 0) ex.Add($"stuns {N(c.Stun)} s"); if (c.Heal > 0) ex.Add($"on an enchant: heals {N(c.Heal * 100)}% of max health");
            if (ex.Count > 0) l.Add(string.Join(", ", ex));
            if (c.Buff.Count > 0) l.Add("On an Enchant: " + string.Join(", ", c.Buff.Select(kv => TreeDb.StatText(kv.Key, kv.Value))));
            if (c.WardBuff.Count > 0) l.Add("On a Ward: " + string.Join(", ", c.WardBuff.Select(kv => TreeDb.StatText(kv.Key, kv.Value))));
            l.Add($"Each extra copy: +{N(Num("stack.effectP", 22))}% damage, +{N(Num("stack.statusDurP", 12))}% status duration.");
        }
        else if (c.Type == "modifier" && c.Mod is { } m)
        {
            var parts = m.Num.Where(kv => kv.Value != 0).Select(kv => ModText(kv.Key, kv.Value)).ToList();
            foreach (var (sid, a) in m.Status) parts.Add($"{Dmg.Status(sid)?.Name ?? sid} {N((a.Length > 0 ? a[0] : 1) * 100)}% chance");
            if (m.Convert != "") parts.Add("turns the damage into " + m.Convert); if (m.Auto != "") parts.Add("casts by itself: " + m.Auto);
            if (parts.Count > 0) l.Add("Exactly: " + string.Join(", ", parts) + ".");
            if (Spellcraft.IsZoneSection(new List<string> { "area", c.Id })) l.Add($"With an Area carrier in the same section: a zone that lasts {N(Num("zones.dur", 4))} s and repeats every {N(Num("zones.tick", 1))} s.");
        }
        l.Add($"Every card past the first two in a section: +{N(Num("stack.costP", 10))}% cost.");
        return l;
    }
}

public partial class Ui
{
    /// <summary>The Spellcraft window's top bar: the "How it works" button, and once per hero the first-time hint (kept in PlayerStats.Flags).</summary>
    void ScHelpHook(Control top)
    {
        var hb = Btn("How it works", OpenSpellHelp, true); hb.TooltipText = "What carriers, effects and modifiers are, how cost and power are worked out, zones, and an example";
        hb.AddThemeColorOverride("font_color", UiTheme.Highlight); top.AddChild(hb);
        var d = Game.D; d.Flags ??= new();
        if (!d.Flags.ContainsKey("hint:spellcraft") && SpellHelp.FirstHint != "")
        {
            d.Flags["hint:spellcraft"] = "1";
            Banner("Spellcraft", "build your own skills from cards"); Note(SpellHelp.FirstHint, UiTheme.Highlight);
        }
    }

    /// <summary>A-17: the "How spellcraft works" panel (data/spellhelp.json "panel").</summary>
    public void OpenSpellHelp()
    {
        var v = Frame("spellhelp", "How Spellcraft Works", new Vector2(920, 640), OpenSpellHelp); HideInfo();
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); v.AddChild(top);
        top.AddChild(Btn("Back to Spellcraft", () => OpenSpellcraft(null), true));
        var hint = Lbl("Hover any card in the Spellcraft window for what it does and its exact numbers.", 13, Dim, top); hint.VerticalAlignment = VerticalAlignment.Center;
        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; v.AddChild(sc);
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; box.AddThemeConstantOverride("separation", 6); sc.AddChild(box);
        foreach (var t in new[] { "carrier", "effect", "modifier" })
        {
            string txt = SpellHelp.TypeText(t); if (txt == "") continue;
            var col = t == "carrier" ? new Color("8fb0ff") : t == "effect" ? new Color("ff9a6a") : new Color("d8c070");
            Row(box, txt, col, 14).CustomMinimumSize = new Vector2(820, 0);
        }
        foreach (var (title, lines) in SpellHelp.Sections)
        {
            Header(title, box);
            foreach (var line in lines) Row(box, line, UiTheme.Text, 14).CustomMinimumSize = new Vector2(820, 0);
        }
        if (!SpellHelp.Sections.Any()) Row(box, "The help texts are missing (data/spellhelp.json).", Dim, 14);
    }

    /// <summary>Extra lines for a card's tooltip: what its type means, when to use it, and its numbers (A-17).</summary>
    static void ScCardHelp(CardDef c, List<(string, Color, int)> l)
    {
        string plain = SpellHelp.CardText(c.Id); if (plain != "") l.Add((plain, new Color(.86f, .9f, .8f), 13));
        foreach (var n in SpellHelp.Numbers(c)) l.Add((n, new Color(.75f, .92f, 1f), 12));
        if (c.Type == "carrier" && SpellHelp.FamilyText(c.Family) is { Length: > 0 } ft) l.Add((ft, UiTheme.Dim, 12));
        string tt = SpellHelp.TypeText(c.Type); if (tt != "") l.Add((tt, UiTheme.Dim, 11));
    }
}
