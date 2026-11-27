using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// A-26 crafting stations (data/stations.json; format in its "_help"): which block is which station, whether one is within reach,
/// the forge's furnace (input / fuel / output per forge block, PlayerStats.Stead.Forges, run by the played clock so it keeps
/// smelting while the hero is away) and the alchemy table's potions (materials "pot_&lt;id&gt;", drunk from the pack).
/// The windows are in UiStations.cs; the recipe lists in UiCraft.cs.
/// </summary>
public static class Stations
{
    public sealed class Def { public string Id = "", Name = "", Block = ""; public int Grid = 3; public string[] Tabs = Array.Empty<string>(); }
    public sealed class Smelt { public string In = "", Out = ""; public int Need = 1, N = 1; public float Time = 10, Xp = 1; }
    public sealed class Potion
    {
        public string Id = ""; public int Out = 1; public float Heal, Mana, Stamina, Dur = 120, Light, Xp = 5;
        public readonly Dictionary<string, int> Cost = new(); public readonly Dictionary<string, float> Buff = new();
        public string Mat => "pot_" + Id;
    }
    public static readonly Dictionary<string, Def> All = new();
    public static readonly Dictionary<string, Smelt> Smelts = new();
    public static readonly Dictionary<string, float> Fuel = new();
    public static readonly Dictionary<string, Potion> Potions = new();   // by material id ("pot_stamina")
    static readonly Dictionary<byte, string> _byBlock = new(); static readonly Dictionary<string, string> _tabNames = new();
    public static float Range = 4.5f; public static int Slot = 64; static bool _loaded;

    public static void Load()
    {
        if (_loaded) return; _loaded = true; Blocks.Load();
        using var doc = JsonUtil.Load("res://data/stations.json"); var r = doc.RootElement;
        Range = JsonUtil.F(r, "range", 4.5f);
        if (r.TryGetProperty("stations", out var ss)) foreach (var p in ss.EnumerateObject())
        {
            var d = new Def { Id = p.Name, Name = JsonUtil.S(p.Value, "n", p.Name), Block = JsonUtil.S(p.Value, "block"), Grid = (int)JsonUtil.F(p.Value, "grid", 3) };
            if (p.Value.TryGetProperty("tabs", out var tb) && tb.ValueKind == JsonValueKind.Array) d.Tabs = tb.EnumerateArray().Select(x => x.GetString() ?? "").ToArray();
            All[p.Name] = d; byte id = Blocks.Id(d.Block); if (id != 0) _byBlock[id] = p.Name;
        }
        if (!All.ContainsKey("hand")) All["hand"] = new Def { Id = "hand", Name = "Hand Crafting", Grid = 2, Tabs = new[] { "hand" } };
        if (r.TryGetProperty("tabNames", out var tn)) foreach (var p in tn.EnumerateObject()) _tabNames[p.Name] = p.Value.GetString() ?? p.Name;
        if (r.TryGetProperty("forge", out var fg))
        {
            Slot = Math.Max(1, (int)JsonUtil.F(fg, "slot", 64));
            if (fg.TryGetProperty("fuel", out var fu)) foreach (var p in fu.EnumerateObject()) if (p.Value.ValueKind == JsonValueKind.Number) Fuel[p.Name] = p.Value.GetSingle();
            if (fg.TryGetProperty("smelt", out var sm)) foreach (var p in sm.EnumerateObject())
                Smelts[p.Name] = new Smelt { In = p.Name, Out = JsonUtil.S(p.Value, "out"), Need = Math.Max(1, (int)JsonUtil.F(p.Value, "need", 1)), N = Math.Max(1, (int)JsonUtil.F(p.Value, "n", 1)), Time = Mathf.Max(.5f, JsonUtil.F(p.Value, "time", 10)), Xp = JsonUtil.F(p.Value, "xp", 1) };
        }
        if (r.TryGetProperty("potions", out var ps)) foreach (var p in ps.EnumerateObject())
        {
            var e = p.Value; var po = new Potion { Id = p.Name, Out = Math.Max(1, (int)JsonUtil.F(e, "out", 1)), Heal = JsonUtil.F(e, "heal"), Mana = JsonUtil.F(e, "mana"), Stamina = JsonUtil.F(e, "stamina"), Dur = JsonUtil.F(e, "dur", 120), Light = JsonUtil.F(e, "light"), Xp = JsonUtil.F(e, "xp", 5) };
            if (e.TryGetProperty("cost", out var c)) foreach (var k in c.EnumerateObject()) po.Cost[k.Name] = k.Value.GetInt32();
            if (e.TryGetProperty("buff", out var b)) foreach (var k in b.EnumerateObject()) po.Buff[k.Name] = k.Value.GetSingle();
            Potions[po.Mat] = po;
        }
    }
    /// <summary>The station a block is ("table", "smith", "alchemy", "forge"), or null.</summary>
    public static string? Of(byte id) { Load(); return _byBlock.TryGetValue(id, out var s) ? s : null; }
    public static Def Get(string id) { Load(); return All.TryGetValue(id, out var d) ? d : All["hand"]; }
    public static string TabName(string tab) { Load(); return _tabNames.GetValueOrDefault(tab, tab); }
    /// <summary>The station that shows a recipe list (an old Crafting window tab).</summary>
    public static string For(string tab) { Load(); foreach (var d in All.Values) if (d.Tabs.Contains(tab)) return d.Id; return "hand"; }
    /// <summary>Whether a block of this station stands within reach of the hero ("hand" always).</summary>
    public static bool Near(string station)
    {
        Load(); if (station == "hand") return true;
        if (!All.TryGetValue(station, out var d) || Player.Instance is not { } pl || !BlockWorld.Enabled || BlockWorld.I?.Area is not { } a) return false;
        byte want = Blocks.Id(d.Block); if (want == 0) return false;
        var p = pl.GlobalPosition + Vector3.Up; int r = Mathf.CeilToInt(Range), px = Mathf.FloorToInt(p.X), py = Mathf.FloorToInt(p.Y), pz = Mathf.FloorToInt(p.Z);
        for (int x = px - r; x <= px + r; x++) for (int y = py - 2; y <= py + 2; y++) for (int z = pz - r; z <= pz + r; z++)
            if (a.Get(x, y, z) == want && new Vector3(x + .5f, y + .5f, z + .5f).DistanceTo(p) <= Range + .5f) return true;
        return false;
    }

    // ---------------------------------------------------------------- the forge
    public static ForgeSave ForgeAt(string key) { var f = Stead.S.Forges; if (!f.TryGetValue(key, out var s) || s == null) f[key] = s = new ForgeSave { T = Game.D.Played }; return s; }
    /// <summary>Runs a forge up to now: burns fuel, turns batches of the input into the output while there is room.</summary>
    public static void ForgeRun(ForgeSave f)
    {
        Load(); float now = Game.D.Played, dt = Mathf.Clamp(now - f.T, 0f, 86400f); f.T = now;
        for (int guard = 0; dt > 0 && guard < 20000; guard++)
        {
            Smelts.TryGetValue(f.In, out var s);
            bool can = s != null && f.InN >= s.Need && (f.OutN == 0 || (f.Out == s.Out && f.OutN + s.N <= Slot));
            if (!can || s == null) { f.Prog = 0; f.Burn = Mathf.Max(0, f.Burn - dt); break; }
            if (f.Burn <= 0)
            {
                if (f.FuelN > 0 && Fuel.TryGetValue(f.Fuel, out float secs) && secs > 0) { f.FuelN--; if (f.FuelN == 0) f.Fuel = ""; f.Burn = secs; }
                else { f.Prog = 0; break; }
            }
            float step = Mathf.Min(dt, Mathf.Min(f.Burn, s.Time - f.Prog)); if (step <= 0) step = .01f;
            f.Burn -= step; f.Prog += step; dt -= step;
            if (f.Prog >= s.Time - .001f)
            {
                f.Prog = 0; f.InN -= s.Need; if (f.InN <= 0) { f.InN = 0; f.In = ""; }
                f.Out = s.Out; f.OutN += s.N; f.Xp += s.Xp;
            }
        }
    }
    /// <summary>The forge block was dug out: what it held drops into the pack.</summary>
    public static void ForgeGone(string key, Vector3 at)
    {
        if (!Stead.S.Forges.TryGetValue(key, out var f) || f == null) return; ForgeRun(f); Stead.S.Forges.Remove(key);
        if (f.InN > 0) GatherDb.Give(f.In, f.InN, at); if (f.FuelN > 0) GatherDb.Give(f.Fuel, f.FuelN, at); if (f.OutN > 0) GatherDb.Give(f.Out, f.OutN, at);
    }

    // ---------------------------------------------------------------- potions
    public static string PotionText(Potion po)
    {
        var parts = new List<string>();
        if (po.Heal > 0) parts.Add($"heals {po.Heal * 100:0}% health"); if (po.Mana > 0) parts.Add($"restores {po.Mana * 100:0}% mana"); if (po.Stamina > 0) parts.Add($"restores {po.Stamina * 100:0}% stamina");
        foreach (var kv in po.Buff) parts.Add(TreeDb.StatText(kv.Key, kv.Value));
        if (po.Light > 0) parts.Add($"a soft light follows you ({po.Light:0} m)");
        return string.Join(", ", parts) + (po.Buff.Count > 0 || po.Light > 0 ? $" for {po.Dur / 60f:0.#} min" : "");
    }
    /// <summary>Drinks one potion from the pack.</summary>
    public static bool Drink(string mat)
    {
        Load(); var d = Game.D; var p = Player.Instance;
        if (!Potions.TryGetValue(mat, out var po) || p == null || p.Dead || d.Mats.GetValueOrDefault(mat) <= 0) return false;
        d.Mats[mat]--; if (d.Mats[mat] <= 0) d.Mats.Remove(mat);
        if (po.Heal > 0) p.Hp = Mathf.Min(d.MaxHp(), p.Hp + d.MaxHp() * po.Heal);
        if (po.Mana > 0) p.Mp = Mathf.Min(d.MaxMp(), p.Mp + d.MaxMp() * po.Mana);
        if (po.Stamina > 0) p.St = Mathf.Min(d.MaxSt(), p.St + d.MaxSt() * po.Stamina);
        if (po.Buff.Count > 0) { string id = "potion:" + po.Id; p.Buffs.RemoveAll(b => b.id == id); p.Buffs.Add((id, new Dictionary<string, float>(po.Buff), po.Dur)); }
        if (po.Light > 0) p.NightSight(po.Light, po.Dur);
        Sfx.Play("heal", .7f, 1.25f); Ui.Note($"You drink the {GatherDb.Mat(mat).Name}: {PotionText(po)}", GatherDb.Mat(mat).Col.Lightened(.3f));
        Game.Notify(); return true;
    }
}
