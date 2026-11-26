using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// Cooking (focus F-24; data/cooking.json). Cook at a fire: a campfire block or a placed cooking station within range (any block id in
/// "stations"), or a campfire prop (a low <see cref="Torch"/> that is not a block light: the old mesh villages and camps). Interact there
/// opens the cooking window (UiLife.cs). Recipes cost ingredients ("fish_*" = any fish), give a dish kept in the pack as the material
/// "food_&lt;id&gt;"; eating it (click it in the pack's Materials tab or Eat in the cooking book) heals and gives one food buff through
/// Player.Buffs (the pipeline's buffs stage). Recipes are learned from the start, at a cooking level, from the merchant's recipe books, or
/// by cooking an unknown dish once (discovery). Cooking gives cooking XP (LifeSkills.Award); its perks scale heal, buff and duration and
/// can give a double portion.
/// </summary>
public static class Cooking
{
    public const string Skill = "cooking";
    public sealed class Recipe
    {
        public string Id = "", Name = "", Learn = "start"; public readonly Dictionary<string, int> Cost = new(); public float Heal, Dur = 120; public int Xp = 5, Value = 10, Level, Price;
        public readonly Dictionary<string, float> Buff = new(); public Color Col = Colors.White;
        public string Food => "food_" + Id;
        public string Kind => Learn.StartsWith("level") ? "level" : Learn.StartsWith("book") ? "book" : Learn;
    }
    public static readonly Dictionary<string, Recipe> All = new();
    public static readonly List<string> Order = new();
    public static float Range = 3.2f;
    static readonly HashSet<byte> _stations = new();
    static bool _loaded;

    public static void Load()
    {
        if (_loaded) return; _loaded = true;
        using var doc = JsonUtil.Load("res://data/cooking.json"); var r = doc.RootElement;
        Range = JsonUtil.F(r, "range", 3.2f);
        foreach (var p in r.GetProperty("recipes").EnumerateObject())
        {
            var e = p.Value; var rc = new Recipe { Id = p.Name, Name = JsonUtil.S(e, "n", p.Name), Heal = JsonUtil.F(e, "heal"), Dur = JsonUtil.F(e, "dur", 120), Xp = (int)JsonUtil.F(e, "xp", 5), Value = (int)JsonUtil.F(e, "value", 10), Learn = JsonUtil.S(e, "learn", "start"), Col = Color.FromHtml(JsonUtil.S(e, "color", "#d0a070")) };
            if (e.TryGetProperty("cost", out var c)) foreach (var k in c.EnumerateObject()) rc.Cost[k.Name] = k.Value.GetInt32();
            if (e.TryGetProperty("buff", out var b)) foreach (var k in b.EnumerateObject()) rc.Buff[k.Name] = k.Value.GetSingle();
            var parts = rc.Learn.Split(':');
            if (parts.Length > 1 && int.TryParse(parts[1], out int n)) { if (parts[0] == "level") rc.Level = n; else rc.Price = n; }
            All[p.Name] = rc; Order.Add(p.Name);
        }
    }
    static void LoadStations()
    {
        if (_stations.Count > 0) return;
        using var doc = JsonUtil.Load("res://data/cooking.json");
        if (doc.RootElement.TryGetProperty("stations", out var s)) foreach (var x in s.EnumerateArray()) { byte id = Blocks.Id(x.GetString() ?? ""); if (id != 0) _stations.Add(id); }
        if (_stations.Count == 0) _stations.Add(255);   // nothing found: never scan again
    }
    /// <summary>Cooked dishes are materials too ("food_&lt;id&gt;"), so they stack in the pack and sell (GatherDb.Load).</summary>
    public static void AddMats(Dictionary<string, MatDef> mats) { Load(); foreach (var r in All.Values) mats[r.Food] = new MatDef { Id = r.Food, Name = r.Name, Value = r.Value, Col = r.Col }; }
    public static Recipe? ByFood(string mat) => mat.StartsWith("food_") && All.TryGetValue(mat[5..], out var r) ? r : null;
    public static Recipe? ByName(string name) { Load(); foreach (var r in All.Values) if (r.Name == name) return r; return null; }
    public static bool IsBlockStation(byte id) { LoadStations(); return _stations.Contains(id); }

    // ---------------------------------------------------------------- what is known
    public static bool Knows(PlayerStats d, string id) { Ensure(d); return d.Recipes.Contains(id); }
    /// <summary>The starting recipes and every level recipe up to the current cooking level (older saves get them on first look).</summary>
    public static void Ensure(PlayerStats d)
    {
        Load(); d.Recipes ??= new(); int lv = LifeSkills.Level(d, Skill);
        foreach (var r in All.Values) if ((r.Kind == "start" || (r.Kind == "level" && lv >= r.Level)) && !d.Recipes.Contains(r.Id)) d.Recipes.Add(r.Id);
    }
    /// <summary>A cooking level-up: the level recipes it unlocks.</summary>
    public static void LearnByLevel(PlayerStats d, int lv, bool quiet)
    {
        Load(); d.Recipes ??= new();
        foreach (var r in All.Values) if (r.Kind == "level" && lv >= r.Level && !d.Recipes.Contains(r.Id)) { d.Recipes.Add(r.Id); if (!quiet) Ui.Note($"New recipe: {r.Name}", UiTheme.Highlight); }
    }
    public static void Learn(PlayerStats d, string id) { Ensure(d); if (!d.Recipes.Contains(id)) d.Recipes.Add(id); }

    // ---------------------------------------------------------------- ingredients ("fish_*" = the fish you hold most of)
    /// <summary>The real materials this recipe takes from the pack, or null if something is missing.</summary>
    public static Dictionary<string, int>? Resolve(PlayerStats d, Recipe r)
    {
        var res = new Dictionary<string, int>();
        foreach (var (k, n) in r.Cost)
        {
            if (k == "fish_*")
            {
                var fish = d.Mats.Where(kv => kv.Key.StartsWith("fish_") && kv.Value - res.GetValueOrDefault(kv.Key) >= n).OrderByDescending(kv => kv.Value).Select(kv => kv.Key).FirstOrDefault();
                if (fish == null) return null; res[fish] = res.GetValueOrDefault(fish) + n; continue;
            }
            if (d.Mats.GetValueOrDefault(k) - res.GetValueOrDefault(k) < n) return null;
            res[k] = res.GetValueOrDefault(k) + n;
        }
        return res;
    }
    public static string CostText(Recipe r) => string.Join(", ", r.Cost.Select(kv => $"{kv.Value} {(kv.Key == "fish_*" ? "any fish" : GatherDb.Mat(kv.Key).Name)}"));
    public static string BuffText(Recipe r, bool scaled = true)
    {
        float k = scaled ? 1 + StatPipeline.Key("foodP") / 100f : 1, dk = scaled ? 1 + StatPipeline.Key("foodDurP") / 100f : 1;
        var parts = new List<string>(); if (r.Heal > 0) parts.Add($"heals {r.Heal * k * 100:0}% health");
        foreach (var kv in r.Buff) parts.Add(TreeDb.StatText(kv.Key, Affixes.Round(kv.Key, kv.Value * k)));
        return string.Join(", ", parts) + (r.Buff.Count > 0 ? $" for {r.Dur * dk / 60f:0.#} min" : "");
    }

    /// <summary>Cooks one dish at a fire: pays the ingredients, gives the dish (sometimes two), cooking XP; an unknown recipe is learned.</summary>
    public static string? Cook(PlayerStats d, Recipe r)
    {
        var pay = Resolve(d, r); if (pay == null) return "Missing ingredients: " + CostText(r);
        foreach (var (k, n) in pay) { d.Mats[k] -= n; if (d.Mats[k] <= 0) d.Mats.Remove(k); }
        bool dbl = GD.Randf() * 100f < StatPipeline.Key("foodDoubleChance"); int got = dbl ? 2 : 1;
        d.Mats[r.Food] = d.Mats.GetValueOrDefault(r.Food) + got;
        bool fresh = !Knows(d, r.Id); if (fresh) d.Recipes.Add(r.Id);
        int xp0 = LifeSkills.Xp(d, Skill); LifeSkills.Award(Skill, r.Xp * (fresh ? 2 : 1));
        LastCook = $"{r.Name} x{got}{(dbl ? " (double portion)" : "")}, paid {string.Join(", ", pay.Select(kv => $"{kv.Value} {GatherDb.Mat(kv.Key).Name}"))}, cooking xp {xp0} -> {LifeSkills.Xp(d, Skill)}";
        Ui.Note(fresh ? $"You discovered a recipe: {r.Name}!" : $"Cooked {r.Name}{(dbl ? " (a double portion)" : "")}  ({d.Mats[r.Food]})", r.Col.Lightened(.3f));
        Sfx.Play("fish_catch", .5f, .8f); Game.Notify();
        return null;
    }
    public static string LastCook = "";

    /// <summary>Eats one dish from the pack: heals and gives its buff (replacing any other food buff).</summary>
    public static bool Eat(string food)
    {
        var d = Game.D; var r = ByFood(food); var p = Player.Instance;
        if (r == null || p == null || p.Dead || d.Mats.GetValueOrDefault(food) <= 0) return false;
        d.Mats[food]--; if (d.Mats[food] <= 0) d.Mats.Remove(food);
        p.EatFood(r);
        Game.Notify(); return true;
    }

    // ---------------------------------------------------------------- where you can cook
    /// <summary>The nearest fire to cook at within range of p: a station block or a campfire prop; null if none.</summary>
    public static Vector3? FireNear(Vector3 p)
    {
        Load(); float best = Range * Range; Vector3? at = null;
        if (BlockWorld.Enabled && BlockWorld.I?.Area is { } a)
        {
            LoadStations(); int r = Mathf.CeilToInt(Range); int px = Mathf.FloorToInt(p.X), py = Mathf.FloorToInt(p.Y + .5f), pz = Mathf.FloorToInt(p.Z);
            for (int x = px - r; x <= px + r; x++) for (int y = py - 2; y <= py + 2; y++) for (int z = pz - r; z <= pz + r; z++)
            {
                byte id = a.Get(x, y, z); if (id == 0 || id == 255 || !_stations.Contains(id)) continue;
                var c = new Vector3(x + .5f, y + .5f, z + .5f); float d2 = c.DistanceSquaredTo(p + Vector3.Up * .5f);
                if (d2 < best) { best = d2; at = c; }
            }
        }
        if (Player.Instance?.GetTree() is { } tree)
            foreach (var n in tree.GetNodesInGroup("torches"))
            {
                if (n is not Torch t || !t.IsCampfire || !t.IsVisibleInTree()) continue;
                float d2 = t.GlobalPosition.DistanceSquaredTo(p + Vector3.Up * .5f); if (d2 < best) { best = d2; at = t.GlobalPosition; }
            }
        return at;
    }
}
