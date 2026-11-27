using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

// ---- saved with the hero (PlayerStats.Stead): plain public fields
public class CropSave { public int X, Y, Z; public string K = "", World = ""; public float T, P; public bool W; }
public class ForgeSave { public string In = "", Fuel = "", Out = ""; public int InN, FuelN, OutN; public float T, Prog, Burn, Xp; }
public class AnimalSave { public string Id = "", Kind = "", World = ""; public float X, Y, Z, Hx, Hy, Hz; public bool Baby; public float Born, ProdT, BreedT, LoveT; }
public class SteadSave
{
    public Dictionary<string, CropSave> Crops = new();
    public Dictionary<string, ForgeSave> Forges = new();
    public List<AnimalSave> Animals = new();
    public int AnimalSeq;
}

/// <summary>
/// The homestead blocks of after-focus A-24..A-27 seen from the block world: what a block needs to be placed (seeds on farmland),
/// what happens when one is placed or dug (crop records, forge contents), and the Interact uses of the new blocks (PlayerBlocks.cs
/// calls <see cref="Player.SteadBlockUse"/>): harvest a ripe crop, till soil in build mode, open and close a fence gate, use a
/// crafting station, the forge, a home chest (the hero's home storage, wherever the block stands) or a party chest.
/// </summary>
public static class Stead
{
    public static SteadSave S => Game.D.Stead ??= new SteadSave();
    /// <summary>Which world the records belong to: "" the hero's own, "online" the shared online server, "guest" a friend's world (nothing is recorded there).</summary>
    public static string WorldTag => Game.Online ? "online" : Net.IsClient ? "guest" : "";
    public static string CellKey(int x, int y, int z) => $"{x},{y},{z}";

    /// <summary>Why the chosen block may not go here by the homestead rules (null = fine): seeds need farmland under them.</summary>
    public static string? PlaceRule(string key, int x, int y, int z) => key == "" ? null : Farm.PlaceRule(key, x, y, z);
    /// <summary>A block was set by hand (or by a design): crops start growing.</summary>
    public static void Placed(Vector3I at, byte id) { if (Farm.Of(id) != null) Farm.Planted(at, id); }
    /// <summary>A block was dug out by hand: a crop gives its seed (and harvest) back, a forge what it held.</summary>
    public static void Dug(Vector3I b, BlockDef def, byte meta)
    {
        if (def.Shape == "crop") Farm.DugCrop(b, def, meta);
        else if (def.Key == "forge" && BlockWorld.I?.Area is { } a) Stations.ForgeGone(BlockUse.Key(a, b.X, b.Y, b.Z), new Vector3(b.X + .5f, b.Y + .5f, b.Z + .5f));
    }
}

/// <summary>Fence gates (shape "gate"): X and X_open twins like doors, one block, toggled with Interact.</summary>
public static class Gates
{
    public static bool Is(byte id) => id != 0 && id != 255 && Blocks.Def(id).Shape == "gate";
    public static void Toggle(BlockArea a, Vector3I b)
    {
        byte id = a.Get(b.X, b.Y, b.Z); if (!Is(id)) return; byte twin = BlockUse.Twin(id); if (twin == 0) return;
        BlockWorld.I?.Change(b.X, b.Y, b.Z, twin, a.GetMeta(b.X, b.Y, b.Z));
        Sfx.Play(BlockUse.IsOpen(twin) ? "door_open" : "door_close", .7f, 1.2f);
    }
}

/// <summary>
/// A-24 farming (data/farm.json; format in its "_help"). Tilled soil, seeds (the pack items of the crop blocks), growth by the hero's
/// played clock (so crops grow while their chunk is not loaded), water and rain, harvest, the farming life skill, seed sources.
/// </summary>
public static class Farm
{
    public sealed class Crop { public string Key = "", Produce = ""; public byte Id; public float Grow = 480; public int Y0 = 1, Y1 = 2, S0 = 1, S1 = 1, RegrowStage = 1; public bool Regrow; }
    static readonly Dictionary<byte, Crop> _crops = new(); static readonly HashSet<byte> _till = new();
    static JsonDocument? _doc; static JsonElement _root; static float _t;
    public static byte Farmland, Wet;
    public const string Skill = "farming";

    public static void Load()
    {
        if (_doc != null) return; Blocks.Load();
        _doc = JsonUtil.Load("res://data/farm.json"); _root = _doc.RootElement;
        Farmland = Blocks.Id("farmland"); Wet = Blocks.Id("farmland_wet");
        if (_root.TryGetProperty("till", out var tl)) foreach (var x in tl.EnumerateArray()) { byte id = Blocks.Id(x.GetString() ?? ""); if (id != 0) _till.Add(id); }
        if (_root.TryGetProperty("crops", out var cs)) foreach (var p in cs.EnumerateObject())
        {
            byte id = Blocks.Id(p.Name); if (id == 0) continue; var e = p.Value;
            var c = new Crop { Key = p.Name, Id = id, Produce = JsonUtil.S(e, "produce"), Grow = Mathf.Max(1f, JsonUtil.F(e, "grow", 480)), RegrowStage = (int)JsonUtil.F(e, "regrowStage", 1), Regrow = e.TryGetProperty("regrow", out var rg) && rg.ValueKind == JsonValueKind.True };
            if (e.TryGetProperty("yield", out var y) && y.GetArrayLength() >= 2) { c.Y0 = y[0].GetInt32(); c.Y1 = Math.Max(c.Y0, y[1].GetInt32()); }
            if (e.TryGetProperty("seeds", out var s) && s.GetArrayLength() >= 2) { c.S0 = s[0].GetInt32(); c.S1 = Math.Max(c.S0, s[1].GetInt32()); }
            _crops[id] = c;
        }
    }
    /// <summary>A number from data/farm.json by path ("water.mul").</summary>
    public static float N(string path, float d)
    {
        Load(); var e = _root;
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return d; e = n; }
        return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : e.ValueKind == JsonValueKind.True ? 1 : e.ValueKind == JsonValueKind.False ? 0 : d;
    }
    public static JsonElement? Node(string path)
    {
        Load(); var e = _root;
        foreach (var part in path.Split('.')) { if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(part, out var n)) return null; e = n; }
        return e;
    }
    public static Crop? Of(byte id) { Load(); return _crops.TryGetValue(id, out var c) ? c : null; }
    static int Rand(int lo, int hi) => hi <= lo ? lo : lo + (int)(GD.Randi() % (uint)(hi - lo + 1));
    static Vector3 Mid(int x, int y, int z) => new(x + .5f, y + .4f, z + .5f);

    // ---------------------------------------------------------------- tilling and planting
    public static bool CanTill(BlockArea a, Vector3I b) { Load(); return a.Key == "overworld" && _till.Contains(a.Get(b.X, b.Y, b.Z)) && a.Get(b.X, b.Y + 1, b.Z) == 0; }
    public static void Till(BlockArea a, Vector3I b)
    {
        if (!CanTill(a, b) || BlockWorld.I is not { } w) return;
        if (BlockFx.NoEdit() is { } no) { Ui.Note(no, new Color(1, .6f, .5f)); return; }
        if (TownZones.Deny(b.X, b.Z) is { } town) { TownZones.Notify(town); return; }
        if (a.Gen.Protected(b.X, b.Y, b.Z)) return;
        w.Change(b.X, b.Y, b.Z, Farmland); BlockFx.PlacePuff(b.X, b.Y + 1, b.Z, Farmland);
        LifeSkills.Award(Skill, N("xp.till", 1));
    }
    public static string? PlaceRule(string key, int x, int y, int z)
    {
        Load(); byte id = Blocks.Id(key); if (!_crops.ContainsKey(id)) return null;
        var a = BlockWorld.I?.Area; if (a == null) return null;
        if (a.Key != "overworld") return "Crops only grow under the open sky";
        byte below = a.Get(x, y - 1, z);
        return below == Farmland || below == Wet ? null : $"Seeds need farmland: in build mode press {Inputs.KeyOf(Inputs.Use)} on dirt or grass";
    }
    public static void Planted(Vector3I at, byte id)
    {
        var c = Of(id); string tag = Stead.WorldTag; if (c == null || tag == "guest") return;
        Stead.S.Crops[Stead.CellKey(at.X, at.Y, at.Z)] = new CropSave { X = at.X, Y = at.Y, Z = at.Z, K = c.Key, World = tag, T = Game.D.Played };
        LifeSkills.Award(Skill, N("xp.plant", 1.5f));
    }
    public static void DugCrop(Vector3I b, BlockDef def, byte meta)
    {
        Stead.S.Crops.Remove(Stead.CellKey(b.X, b.Y, b.Z));
        var c = Of(def.Id); if (c == null) return; var at = Mid(b.X, b.Y, b.Z);
        GatherDb.Give(Blocks.Item(c.Key), 1, at);
        if ((meta & 3) >= 3 && c.Produce != "") GatherDb.Give(c.Produce, Rand(c.Y0, c.Y1), at);
    }

    // ---------------------------------------------------------------- harvest
    public static int Stage(BlockArea a, Vector3I b) => a.GetMeta(b.X, b.Y, b.Z) & 3;
    public static void Harvest(BlockArea a, Vector3I b)
    {
        byte id = a.Get(b.X, b.Y, b.Z); var c = Of(id); var w = BlockWorld.I; if (c == null || w == null || Stage(a, b) < 3) return;
        var at = Mid(b.X, b.Y, b.Z); int n = Rand(c.Y0, c.Y1); if (GD.Randf() * 100f < StatPipeline.Key("farmYieldChance")) n++;
        if (c.Produce != "") GatherDb.Give(c.Produce, n, at);
        int seeds = Rand(c.S0, c.S1); string key = Stead.CellKey(b.X, b.Y, b.Z); Stead.S.Crops.TryGetValue(key, out var st);
        if (c.Regrow)
        {
            int rs = Math.Clamp(c.RegrowStage, 0, 2); w.Change(b.X, b.Y, b.Z, id, (byte)rs);
            if (st != null) { st.P = c.Grow * rs / 3f; st.T = Game.D.Played; }
        }
        else if (N("replant", 1) > .5f && seeds > 0 && st != null)
        {
            seeds--; w.Change(b.X, b.Y, b.Z, id, 0); st.P = 0; st.T = Game.D.Played;
        }
        else { w.Change(b.X, b.Y, b.Z, 0); Stead.S.Crops.Remove(key); }
        if (seeds > 0) GatherDb.Give(Blocks.Item(c.Key), seeds, at);
        LifeSkills.Award(Skill, N("xp.harvest", 6)); Sfx.Play("pickup", .7f, 1.1f);
        var col = Blocks.Tint(Blocks.Def(id)); var g = new Gradient(); g.SetColor(0, col); g.SetColor(1, new Color(col, 0));
        Vfx.Sparks(at, g, 10, 2.2f, .5f, .3f, -5f, false, 180f, null, .2f, "dirt");
    }

    // ---------------------------------------------------------------- growth
    static bool WaterNear(BlockArea a, int x, int y, int z, int r)
    {
        for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) for (int dy = 0; dy <= 1; dy++)
        {
            byte b = a.Get(x + dx, y + dy, z + dz); if (b == 0 || b == 255) continue;
            if (Blocks.Def(b).Fluid == "water") return true;
        }
        return false;
    }
    /// <summary>Every few seconds: each of the hero's crops gains growth for the time passed; the loaded ones show their stage and wet soil.</summary>
    public static void Tick(float dt)
    {
        _t -= dt; if (_t > 0) return; Load(); _t = Mathf.Max(.5f, N("tick", 2f));
        string tag = Stead.WorldTag; if (tag == "guest") return;
        var crops = Stead.S.Crops; if (crops.Count == 0) return;
        var w = BlockWorld.I; var a = w?.Area; bool over = w != null && a != null && a.Key == "overworld" && BlockWorld.Enabled;
        float now = Game.D.Played, mul = N("water.mul", 1.6f), skill = 1 + StatPipeline.Key("farmGrowP") / 100f; int range = (int)N("water.range", 4);
        bool rain = Weather.Rain > N("water.rainAt", .25f); List<string>? gone = null;
        foreach (var (k, c) in crops)
        {
            if (c.World != tag) continue;
            var def = Of(Blocks.Id(c.K)); if (def == null) { (gone ??= new()).Add(k); continue; }
            byte cur = over ? a!.Get(c.X, c.Y, c.Z) : (byte)255;
            if (cur != 255)
            {
                if (cur != def.Id) { (gone ??= new()).Add(k); continue; }
                byte below = a!.Get(c.X, c.Y - 1, c.Z);
                if (below != Farmland && below != Wet)
                {   // the soil went from under it: the plant comes out as its seed
                    w!.Change(c.X, c.Y, c.Z, 0); GatherDb.Give(Blocks.Item(c.K), 1, Mid(c.X, c.Y, c.Z)); (gone ??= new()).Add(k); continue;
                }
                bool wet = rain || WaterNear(a, c.X, c.Y - 1, c.Z, range); c.W = wet;
                if ((below == Wet) != wet && Wet != 0) w!.Change(c.X, c.Y - 1, c.Z, wet ? Wet : Farmland);
            }
            float d = Mathf.Clamp(now - c.T, 0f, 86400f); c.T = now; c.P += d * (c.W ? mul : 1f) * skill;
            if (cur != 255)
            {
                int stage = Math.Min(3, (int)(c.P / def.Grow * 3f));
                if (stage != (a!.GetMeta(c.X, c.Y, c.Z) & 3)) w!.Change(c.X, c.Y, c.Z, def.Id, (byte)stage);
            }
        }
        if (gone != null) foreach (var k in gone) crops.Remove(k);
    }
    public static string Describe(BlockArea a, Vector3I b)
    {
        var c = Of(a.Get(b.X, b.Y, b.Z)); if (c == null) return "";
        string name = GatherDb.Mat(c.Produce).Name; int st = Stage(a, b);
        bool wet = Stead.S.Crops.TryGetValue(Stead.CellKey(b.X, b.Y, b.Z), out var s) && s.W;
        return st >= 3 ? name + ": ripe" : $"{name}: growing ({st + 1}/4){(wet ? ", watered" : ", dry soil")}";
    }

    // ---------------------------------------------------------------- where seeds come from
    static string? PickSeed(JsonElement e)
    {
        if (!e.TryGetProperty("seeds", out var s) || s.ValueKind != JsonValueKind.Array || s.GetArrayLength() == 0) return null;
        string k = s[(int)(GD.Randi() % (uint)s.GetArrayLength())].GetString() ?? ""; return Blocks.Exists(k) ? k : null;
    }
    /// <summary>Gather.cs: a wild plant was gathered; it may give a seed of itself, or any herb a field seed.</summary>
    public static void WildSeeds(string mat, string skill, Vector3 at)
    {
        if (skill != "herbalism") return;
        if (Node("seedDrops.wild." + mat) is { ValueKind: JsonValueKind.Object } w && Blocks.Exists(JsonUtil.S(w, "seed")) && GD.Randf() < JsonUtil.F(w, "chance", .2f))
        { GatherDb.Give(Blocks.Item(JsonUtil.S(w, "seed")), 1, at); return; }
        if (Node("seedDrops.any") is { ValueKind: JsonValueKind.Object } any && GD.Randf() < JsonUtil.F(any, "chance", .1f) && PickSeed(any) is { } k) GatherDb.Give(Blocks.Item(k), 1, at);
    }
    /// <summary>Enemy.Die (a kill of this machine's hero): a small chance of a seed.</summary>
    public static void MobSeeds(Vector3 at)
    {
        if (Net.Dedicated) return;
        if (Node("seedDrops.mobs") is { ValueKind: JsonValueKind.Object } m && GD.Randf() < JsonUtil.F(m, "chance", .05f) && PickSeed(m) is { } k) GatherDb.Give(Blocks.Item(k), 1, at);
    }
    /// <summary>A treasure chest was opened: sometimes a few seeds lie in it.</summary>
    public static void ChestSeeds(Vector3 at)
    {
        if (Node("seedDrops.chests") is not { ValueKind: JsonValueKind.Object } c || GD.Randf() >= JsonUtil.F(c, "chance", .5f) || PickSeed(c) is not { } k) return;
        int lo = 1, hi = 2; if (c.TryGetProperty("n", out var n) && n.GetArrayLength() >= 2) { lo = n[0].GetInt32(); hi = n[1].GetInt32(); }
        GatherDb.Give(Blocks.Item(k), Rand(lo, hi), at);
    }
}

public partial class Player
{
    OmniLight3D? _nsLight; float _nsT;
    /// <summary>The Owl's Eye potion: a soft light that follows the hero for a while.</summary>
    public void NightSight(float range, float secs)
    {
        if (_nsLight == null || !IsInstanceValid(_nsLight)) { _nsLight = new OmniLight3D { LightColor = new Color(.78f, .84f, 1f), LightEnergy = .9f, ShadowEnabled = false, Position = new Vector3(0, 2.3f, 0) }; AddChild(_nsLight); }
        _nsLight.OmniRange = range; _nsLight.Visible = true; _nsT = secs;
    }

    /// <summary>Every physics frame (PlayerBlocks.BlockInput): crops grow, the ranch runs, the potion light fades, the design tools hide outside build mode.</summary>
    void SteadTick(float dt, bool captured)
    {
        if (_nsT > 0 && (_nsT -= dt) <= 0 && _nsLight != null && IsInstanceValid(_nsLight)) _nsLight.Visible = false;
        Farm.Tick(dt); Ranch.Tick(this, dt);
        DesignTick(dt, captured);
    }

    /// <summary>
    /// Interact on the homestead blocks (called by BlockUseInput with the aimed block in reach). Null = not one of ours (doors and
    /// chests go on); otherwise the value BlockUseInput returns.
    /// </summary>
    bool? SteadBlockUse(BlockArea a, Vector3I b, Vector3I n, byte id)
    {
        if (id == 0 || id == 255) return null; var def = Blocks.Def(id);
        string key = "[" + Inputs.KeyOf(Inputs.Use) + "] "; string p = ""; Action? act = null;
        if (def.Shape == "crop")
        {
            if (Farm.Stage(a, b) >= 3) { p = key + "Harvest " + GatherDb.Mat(Farm.Of(id)?.Produce ?? "").Name; act = () => Farm.Harvest(a, b); }
            else p = Farm.Describe(a, b);
        }
        else if (def.Shape == "gate") { p = key + (BlockUse.IsOpen(id) ? "Close gate" : "Open gate"); act = () => Gates.Toggle(a, b); }
        else if (def.Key == "home_chest") { p = key + "Open home storage"; act = () => { Sfx.Play("chest_open", .8f); Ui.I?.OpenStash(); }; }
        else if (def.Key == "party_chest") { p = key + (Net.Active ? "Open the party chest (shared)" : "Open the party chest"); act = () => { Sfx.Play("chest_open", .8f); if (Net.I != null) Net.I.OpenChest(); else Ui.I?.OpenPartyChest(); }; }
        else if (Stations.Of(id) is { } st)
        {
            p = key + "Use " + def.Name;
            if (st == "forge") { string fk = BlockUse.Key(a, b.X, b.Y, b.Z); act = () => Ui.I?.OpenForge(fk); }
            else act = () => Ui.I?.OpenStation(st);
        }
        else if (BuildMode && Farm.CanTill(a, b)) { p = key + "Till the soil"; act = () => Farm.Till(a, b); }
        else return null;
        bool mine = Prompt == ""; if (mine) Prompt = p;
        if (mine && act != null && Input.IsActionJustPressed(Inputs.Use)) act();
        return false;
    }
}
