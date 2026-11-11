using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;

namespace TwinRealms;

public class Item
{
    public string Name { get; set; } = "";
    public string Slot { get; set; } = "weapon";     // weapon, armor, shield, trinket
    public string? Wt { get; set; }                  // sword, bow, wand, staff
    public int Rar { get; set; }
    public int Lvl { get; set; } = 1;
    public int Dmg { get; set; }
    public int Def { get; set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public float Sp { get; set; }                    // spell power bonus
    public float Mc { get; set; }                    // mana cost reduction
    public bool Two { get; set; }                    // two-handed
    public int Up { get; set; }                      // smith upgrades
    public string? Elem { get; set; }                // element (data/gear_fx.json): colours the glow; weapons can inflict its status
    public string? Model { get; set; }               // weapons: a kit model name (data/weapons.json "kitModels"), e.g. "sword_azure"; null = picked from the type's list

    [JsonIgnore] public int Value => Math.Max(1, (int)Math.Round((Dmg * 4 + Def * 4 + Hp * 0.6 + Mp * 0.6) * (1 + Rar * 0.8)));
    [JsonIgnore] public Color Color => Items.RarityColor(Rar);
    public string Describe()
    {
        var p = new List<string>();
        if (Sp > 0) p.Add("+" + Mathf.RoundToInt(Sp * 100) + "% spell");
        if (Dmg > 0) p.Add("+" + Dmg + " dmg");
        if (Def > 0) p.Add("+" + Def + " def");
        if (Hp > 0) p.Add("+" + Hp + " hp");
        if (Mp > 0) p.Add("+" + Mp + " mp");
        if (GearFx.Get(Elem) is { } el) p.Add(Slot == "weapon" ? $"{Mathf.RoundToInt(el.Proc * 100)}% {el.Status}" : el.Word.ToLower() + " glow");
        return $"{Name} (L{Lvl}) {string.Join(", ", p)}";
    }
}

public static class Items
{
    static float[] _mult = { 1, 1.3f, 1.7f, 2.3f, 3.2f };
    static string[] _rc = { "#cfcfcf", "#4fd06a", "#5b86ff", "#b866f5", "#ff9a1f" };
    static string[] _pre = { "Rusty", "Sturdy", "Fine", "Runed", "Mythic" };
    static Dictionary<string, string[]> _wn = new(), _in = new();
    static bool _loaded;

    public static Color RarityColor(int r) { Load(); return Color.FromHtml(_rc[Mathf.Clamp(r, 0, 4)]); }
    public static string RarityName(int r) => new[] { "Common", "Fine", "Rare", "Epic", "Legendary" }[Mathf.Clamp(r, 0, 4)];

    static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/items.json");
        var r = doc.RootElement;
        if (r.TryGetProperty("rarityMult", out var m)) { int i = 0; foreach (var x in m.EnumerateArray()) if (i < 5) _mult[i++] = x.GetSingle(); }
        if (r.TryGetProperty("rarityColors", out var c)) { int i = 0; foreach (var x in c.EnumerateArray()) if (i < 5) _rc[i++] = x.GetString() ?? "#ffffff"; }
        if (r.TryGetProperty("prefixes", out var p)) { int i = 0; foreach (var x in p.EnumerateArray()) if (i < 5) _pre[i++] = x.GetString() ?? ""; }
        if (r.TryGetProperty("weaponNames", out var wn)) foreach (var o in wn.EnumerateObject()) { var l = new List<string>(); foreach (var x in o.Value.EnumerateArray()) l.Add(x.GetString() ?? ""); _wn[o.Name] = l.ToArray(); }
        if (r.TryGetProperty("itemNames", out var inn)) foreach (var o in inn.EnumerateObject()) { var l = new List<string>(); foreach (var x in o.Value.EnumerateArray()) l.Add(x.GetString() ?? ""); _in[o.Name] = l.ToArray(); }
    }

    public static Item Roll(int level, float bonus = 0, string? slot = null)
    {
        Load();
        float x = GD.Randf();
        int idx = x < .55f ? 0 : x < .82f ? 1 : x < .94f ? 2 : x < .985f ? 3 : 4;
        if (GD.Randf() < bonus * .25f && idx < 4) idx++;
        level = Math.Max(1, level);
        if (slot == null) { float q = GD.Randf(); slot = q < .28f ? "weapon" : q < .44f ? "armor" : q < .55f ? "shield" : q < .67f ? "head" : q < .78f ? "hands" : q < .89f ? "feet" : "trinket"; }
        float m = _mult[idx];
        var it = new Item { Slot = slot, Rar = idx, Lvl = level };
        if (slot == "weapon")
        {
            // any weapon type from data/weapons.json; damage factors live there, so item damage rolls the same for all
            var types = new List<string>(Weapons.Types); string wt = types[(int)(GD.Randf() * types.Count) % types.Count];
            it.Wt = wt; var wd = Weapons.Get(wt);
            float dm = wt == "wand" ? .62f : wt == "staff" ? .8f : 1f;
            it.Dmg = (int)Math.Floor((3 + level * 2.2f) * m * dm);
            if (wt == "wand") it.Sp = (float)Math.Round((.06f + level * .006f) * m, 2);
            if (wt == "staff") { it.Sp = (float)Math.Round((.12f + level * .01f) * m, 2); it.Mc = .1f; }
            it.Two = wd.Hands > 1;
        }
        else if (slot == "armor") { it.Def = (int)Math.Floor((1 + level * 1.1f) * m); it.Hp = (int)Math.Floor(level * 2 * m); }
        else if (slot == "shield") { it.Def = (int)Math.Floor((1 + level * .9f) * m); it.Hp = (int)Math.Floor(level * m); }
        else if (slot == "head") { it.Def = (int)Math.Floor((1 + level * .6f) * m); it.Mp = (int)Math.Floor(level * 1.5f * m); }
        else if (slot == "hands") { it.Def = (int)Math.Floor((1 + level * .4f) * m); it.Dmg = idx >= 2 ? (int)Math.Floor(level * .4f * m) : 0; }
        else if (slot == "feet") { it.Def = (int)Math.Floor((1 + level * .5f) * m); it.Hp = (int)Math.Floor(level * 1.2f * m); }
        else { it.Hp = (int)Math.Floor((6 + level * 3) * m); it.Mp = (int)Math.Floor((4 + level * 2) * m); it.Def = idx >= 2 ? (int)Math.Floor(level * .5f * m) : 0; }
        string baseName;
        if (slot == "weapon") { var names = _wn.GetValueOrDefault(it.Wt!, new[] { "Weapon" }); baseName = names[Math.Min(names.Length - 1, (int)(GD.Randf() * 5))]; }
        else { var names = _in.GetValueOrDefault(slot, new[] { "Item" }); baseName = names[GD.RandRange(0, names.Length - 1)]; }
        if (slot != "trinket") it.Elem = GearFx.Roll(idx);
        it.Name = _pre[idx] + " " + (GearFx.Get(it.Elem) is { } el ? el.Word + " " : "") + baseName + (idx == 4 ? " of the Rift" : "");
        return it;
    }
}
