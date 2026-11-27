using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>A smith smelting recipe (data/durability.json "smelt"): materials and gold in, metal bars out.</summary>
public class SmeltDef { public string Out = ""; public int N = 1, Gold; public Dictionary<string, int> In = new(); }

/// <summary>
/// A-13 gear durability (data/durability.json). Every weapon and armor piece has durability points: the most it can hold comes from its
/// tier (rarity), item level and hone level; Item.Dur holds what is left (-1 = full, so new items and old saves start full).
/// It wears on use: a weapon per blow landed or shot loosed, armor per blow taken, everything a little on death; Endurance (wearP) slows it.
/// At 0 the item does NOT break or vanish: it stays worn but counts for nothing in the stat pipeline (StatPipeline skips its damage,
/// defence, bonuses, affixes and gems; a worn-out weapon hits as bare hands) until the smith repairs it with metal bars.
/// </summary>
public static class Durability
{
    static bool _loaded;
    static float[] _byRar = { 60, 80, 105, 135, 170 }, _goldPt = { .3f, .5f, .8f, 1.2f, 1.8f }; static int[] _bars = { 1, 2, 2, 3, 3 };
    static string[] _tierBar = { "ironbar", "steelbar", "silverbar", "mithrilbar", "runedbar" };
    static float _perLevel = 3, _perUp = .1f, _low = .2f, _goldLvl = .03f;
    static readonly Dictionary<string, float> _slotMul = new() { ["weapon"] = 1, ["shield"] = 1.3f, ["armor"] = 1.2f, ["head"] = .9f, ["hands"] = .8f, ["feet"] = .8f, ["cape"] = .7f };
    static readonly Dictionary<string, float> _wear = new();
    static readonly Dictionary<string, Dictionary<string, int>> _exSlot = new(), _exType = new(); static readonly List<Dictionary<string, int>> _exRar = new();
    public static readonly List<SmeltDef> Smelts = new();

    static Dictionary<string, int> Cost(JsonElement e) { var d = new Dictionary<string, int>(); if (e.ValueKind == JsonValueKind.Object) foreach (var x in e.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) d[x.Name] = x.Value.GetInt32(); return d; }
    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/durability.json"); var r = doc.RootElement;
        static float[] Arr(JsonElement e, string k, float[] def) => e.TryGetProperty(k, out var a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0 ? a.EnumerateArray().Select(x => x.GetSingle()).ToArray() : def;
        if (r.TryGetProperty("max", out var m))
        {
            _byRar = Arr(m, "byRarity", _byRar); _perLevel = JsonUtil.F(m, "perLevel", _perLevel); _perUp = JsonUtil.F(m, "perUpgrade", _perUp);
            if (m.TryGetProperty("slotMul", out var sm) && sm.ValueKind == JsonValueKind.Object) { _slotMul.Clear(); foreach (var x in sm.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) _slotMul[x.Name] = x.Value.GetSingle(); }
        }
        if (r.TryGetProperty("wear", out var w) && w.ValueKind == JsonValueKind.Object) foreach (var x in w.EnumerateObject()) if (x.Value.ValueKind == JsonValueKind.Number) _wear[x.Name] = x.Value.GetSingle();
        _low = JsonUtil.F(r, "low", _low);
        if (r.TryGetProperty("tierBar", out var tb) && tb.ValueKind == JsonValueKind.Array && tb.GetArrayLength() > 0) _tierBar = tb.EnumerateArray().Select(x => x.GetString() ?? "ironbar").ToArray();
        if (r.TryGetProperty("smelt", out var sl) && sl.ValueKind == JsonValueKind.Array)
            foreach (var e in sl.EnumerateArray())
            {
                var s = new SmeltDef { Out = JsonUtil.S(e, "out"), N = Math.Max(1, (int)JsonUtil.F(e, "n", 1)), Gold = (int)JsonUtil.F(e, "gold") };
                if (e.TryGetProperty("in", out var i)) s.In = Cost(i);
                if (s.Out != "" && s.In.Count > 0) Smelts.Add(s);
            }
        if (r.TryGetProperty("repair", out var rp))
        {
            _bars = Arr(rp, "bars", _bars.Select(x => (float)x).ToArray()).Select(x => (int)x).ToArray(); _goldPt = Arr(rp, "goldPerPoint", _goldPt); _goldLvl = JsonUtil.F(rp, "goldPerLevel", _goldLvl);
            if (rp.TryGetProperty("extraBySlot", out var es) && es.ValueKind == JsonValueKind.Object) foreach (var x in es.EnumerateObject()) _exSlot[x.Name] = Cost(x.Value);
            if (rp.TryGetProperty("extraByType", out var et) && et.ValueKind == JsonValueKind.Object) foreach (var x in et.EnumerateObject()) _exType[x.Name] = Cost(x.Value);
            if (rp.TryGetProperty("extraByRarity", out var er) && er.ValueKind == JsonValueKind.Array) foreach (var x in er.EnumerateArray()) _exRar.Add(Cost(x));
        }
    }
    static T At<T>(T[] a, int i) => a[Math.Clamp(i, 0, a.Length - 1)];
    static float W(string k, float def) { Load(); return _wear.TryGetValue(k, out var v) ? v : def; }

    // ---------------------------------------------------------------- state
    /// <summary>This item wears out (weapons, shields, armor pieces; not trinkets or pet gear).</summary>
    public static bool Has(Item? it) { if (it == null) return false; Load(); return _slotMul.TryGetValue(it.Slot, out var m) && m > 0; }
    public static float Max(Item it)
    {
        Load(); if (!_slotMul.TryGetValue(it.Slot, out var m) || m <= 0) return 0;
        return MathF.Max(1, MathF.Round((At(_byRar, it.Rar) + _perLevel * Math.Max(1, it.Lvl)) * m * (1 + _perUp * Math.Max(0, it.Up))));
    }
    public static float Cur(Item it) { float mx = Max(it); return it.Dur < 0 ? mx : MathF.Min(it.Dur, mx); }
    public static float Frac(Item it) { float mx = Max(it); return mx <= 0 ? 1 : Mathf.Clamp(Cur(it) / mx, 0, 1); }
    /// <summary>Worn out: at 0 durability. The item stays equipped but gives nothing until repaired.</summary>
    public static bool Broken(Item? it) => it != null && it.Dur >= 0 && it.Dur <= .0001f && Has(it);
    public static bool Low(Item it) => Has(it) && Frac(it) <= _low;

    // ---------------------------------------------------------------- wear
    static float WearMul => Mathf.Max(.2f, 1 + StatPipeline.Key("wearP") / 100f);
    /// <summary>Takes durability off a worn item; tells the player when it gets low and when it wears out (and refreshes the stats then).</summary>
    public static void Wear(Item? it, float points)
    {
        if (it == null || points <= 0 || !Has(it) || Broken(it)) return;
        float mx = Max(it), was = Cur(it), now = MathF.Max(0, was - points * WearMul); it.Dur = now;
        if (now <= 0)
        {
            Ui.Note($"{it.Name} is worn out: it gives no stats until Dorrin repairs it", new Color(1f, .45f, .35f)); Sfx.Play("block", .8f, .7f);
            Game.Notify();   // its stats leave the pipeline now
        }
        else if (was / mx > _low && now / mx <= _low) Ui.Note($"{it.Name} is badly worn ({Mathf.RoundToInt(now / mx * 100)}%): have the smith repair it", new Color(1f, .75f, .4f));
    }
    public static void WeaponHit(Item? it) => Wear(it, W("weaponPerHit", .25f));
    public static void Shot(Item? it) => Wear(it, W("rangedPerShot", .15f));
    /// <summary>A blow landed on the hero: every armor piece wears; a blocked blow also wears what blocked it (shield or off-hand weapon).</summary>
    public static void ArmorHit(PlayerStats d, bool blocked)
    {
        float a = W("armorPerHit", .5f);
        foreach (var kv in d.Eq.ToList()) if (kv.Value != null && kv.Key != "weapon" && kv.Value.Slot != "weapon") Wear(kv.Value, a);
        if (blocked) Wear(d.Eq.GetValueOrDefault("shield"), W("shieldPerBlock", 1f));
    }
    /// <summary>Death: everything worn loses a share of its durability.</summary>
    public static void Death(PlayerStats d)
    {
        float pct = W("deathPct", 8) / 100f; if (pct <= 0) return;
        foreach (var it in d.Eq.Values.ToList()) if (it != null && Has(it)) Wear(it, Max(it) * pct / WearMul);   // the death share is not lowered by wearP
    }

    // ---------------------------------------------------------------- display
    static Color BarCol(float f) => f <= 0 ? new Color(.85f, .2f, .2f) : f <= _low ? new Color(1f, .55f, .25f) : f <= .5f ? new Color(.95f, .85f, .35f) : new Color(.45f, .85f, .5f);
    /// <summary>The durability bar along the bottom of an item cell (hidden while the item is at full durability).</summary>
    public static void DrawBar(Control c, Item? it, Rect2 r)
    {
        if (it == null || !Has(it)) return; float f = Frac(it); if (f >= .999f) return;
        var bg = new Rect2(r.Position.X + 3, r.End.Y - 5, r.Size.X - 6, 3);
        c.DrawRect(bg, new Color(0, 0, 0, .8f)); if (f > 0) c.DrawRect(new Rect2(bg.Position, new Vector2(bg.Size.X * f, bg.Size.Y)), BarCol(f));
        if (f <= 0) { c.DrawRect(r.Grow(-1), new Color(.5f, .05f, .05f, .35f)); c.DrawLine(r.Position + new Vector2(5, 5), r.End - new Vector2(5, 5), new Color(1f, .3f, .25f, .8f), 2f); }
    }
    /// <summary>Extra tooltip lines of an item: its size (A-11), off-hand use (A-12) and durability (A-13).</summary>
    public static List<(string text, int size, Color col)> TipLines(Item it)
    {
        var l = new List<(string, int, Color)>();
        if (it.Slot == "weapon" && it.Wt != null)
        {
            string sn = Weapons.SizeName(it.Size);
            if (Mathf.Abs(it.Size - 1f) > .005f)
                l.Add(($"{(sn != "" ? sn : "Size")}: reach {(Weapons.SizeReach(it) - 1) * 100:+0;-0}%, swing speed {(Weapons.SizeSpeed(it) - 1) * 100:+0;-0}%", 12, new Color(.8f, .86f, .95f)));
            if (DualWield.CanOffHand(it)) l.Add(("One-handed: Shift+click to wield it in the off hand (dual wield)", 12, UiTheme.Dim));
        }
        if (Has(it))
        {
            float f = Frac(it);
            l.Add((f <= 0 ? $"Durability 0 / {Max(it):0}   WORN OUT: gives no stats until repaired" : $"Durability {MathF.Ceiling(Cur(it)):0} / {Max(it):0}" + (f <= _low ? "   (badly worn)" : ""), 12, f >= .999f ? UiTheme.Dim : BarCol(f)));
        }
        return l;
    }
    /// <summary>HUD warning: the worst of the worn gear, when something is low or worn out; null when all is well.</summary>
    public static (string text, Color col)? Warning(PlayerStats d)
    {
        int broken = 0, low = 0; string first = "";
        foreach (var it in d.Eq.Values) { if (it == null || !Has(it)) continue; if (Broken(it)) { if (broken++ == 0) first = it.Name; } else if (Low(it)) { if (low++ == 0 && first == "") first = it.Name; } }
        if (broken > 0) return ($"WORN OUT: {first}{(broken > 1 ? $" +{broken - 1} more" : "")} (no stats until repaired at a smith)", new Color(1f, .4f, .32f));
        if (low > 0) return ($"Gear badly worn: {first}{(low > 1 ? $" +{low - 1} more" : "")}", new Color(1f, .75f, .4f));
        return null;
    }

    // ---------------------------------------------------------------- repair (smith)
    public static string BarFor(Item it) { Load(); return At(_tierBar, it.Rar); }
    /// <summary>A smelted metal bar (never part of the smith's ten-for-one material trade).</summary>
    public static bool IsBar(string id) { Load(); return Array.IndexOf(_tierBar, id) >= 0 || Smelts.Exists(s => s.Out == id); }
    /// <summary>Gold and materials to bring this item back to full: bars of its tier's metal, other ingredients by slot / weapon type and tier.</summary>
    public static (int gold, Dictionary<string, int> mats) RepairCost(Item it)
    {
        Load(); var mats = new Dictionary<string, int>(); float mx = Max(it), miss = mx - Cur(it); if (mx <= 0 || miss < .5f) return (0, mats);
        float share = Mathf.Clamp(miss / mx, 0, 1);
        void Add(Dictionary<string, int>? c) { if (c != null) foreach (var (id, n) in c) mats[id] = mats.GetValueOrDefault(id) + Math.Max(1, Mathf.CeilToInt(n * share)); }
        mats[BarFor(it)] = Math.Max(1, Mathf.CeilToInt(At(_bars, it.Rar) * share));
        Add(it.Wt != null && _exType.TryGetValue(it.Wt, out var bt) ? bt : _exSlot.GetValueOrDefault(it.Slot));
        if (_exRar.Count > 0) Add(_exRar[Math.Clamp(it.Rar, 0, _exRar.Count - 1)]);
        int gold = Math.Max(1, Mathf.CeilToInt(At(_goldPt, it.Rar) * miss * (1 + _goldLvl * it.Lvl) * Math.Max(.5f, 1 + StatPipeline.Key("smithCost") / 100f)));
        return (gold, mats);
    }
    public static void Repair(Item it) { it.Dur = -1f; }
}
