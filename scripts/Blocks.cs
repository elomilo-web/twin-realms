using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>One block type from data/blocks.json. Id 0 is always air.</summary>
public class BlockDef
{
    public byte Id; public string Key = "", Name = "";
    public bool Solid = true, See, Slippery, Shatter;
    public string Fluid = "";            // "water", "lava" or ""
    public string Step = "";             // footstep surface: grass/dirt/stone/wood/sand/snow/gravel/ice/water (data "step"; default by tool family)
    public float Hardness = 1f, Burn, Light, DropChance = 1f;
    public string Shape = "";            // "" = full cube, "post" = small standing post (torch)
    public Color LightCol = new Color(1f, .63f, .31f); public float LightRange, LightEnergy;   // "light": { color, range, energy } makes a real OmniLight3D (BlockWorld)
    public string Tool = "", Drops = "", BurnsTo = "", MeltsTo = "?", FreezesTo = "", RotsTo = "?", WithersTo = "?", BlessTo = "?", PacksTo = "";
    public JsonElement Mat;              // how it looks (see Blocks.Material)
    public bool Breakable => Hardness >= 0;
    public bool Opaque => Solid && !See;
    public bool IsFluid => Fluid != "";
}

/// <summary>
/// The block types (data/blocks.json) and the numbers of the block world. Blocks are 1 m cubes on whole-metre coordinates:
/// block (x, y, z) fills [x, x+1) x [y, y+1) x [z, z+1). Adding a block type needs only the data file.
/// </summary>
public static class Blocks
{
    public static readonly List<BlockDef> Defs = new();          // index = id; 0 = air
    static readonly Dictionary<string, byte> _ids = new();
    static JsonDocument? _doc; static JsonElement _root;
    public static JsonElement Root { get { Load(); return _root; } }
    public static byte Water, Lava, Bedrock, Stone, Dirt, Ice, Obsidian;
    public static bool HasLights;
    public static int Chunk = 16, Radius = 5, MinY = -24, MaxY = 160;

    public static void Load()
    {
        if (_doc != null) return;
        _doc = JsonUtil.Load("res://data/blocks.json"); _root = _doc.RootElement;
        Defs.Add(new BlockDef { Id = 0, Key = "air", Name = "Air", Solid = false, See = true, Hardness = -1 });
        _ids["air"] = 0; _ids[""] = 0;
        foreach (var p in _root.GetProperty("blocks").EnumerateObject())
        {
            if (Defs.Count >= 255) { GD.PushWarning("[BLOCKS] more than 254 block types; the rest are ignored"); break; }
            var e = p.Value; var d = new BlockDef
            {
                Id = (byte)Defs.Count, Key = p.Name, Name = JsonUtil.S(e, "n", p.Name),
                Solid = !e.TryGetProperty("solid", out var so) || so.ValueKind != JsonValueKind.False,
                See = B(e, "see"), Slippery = B(e, "slippery"), Shatter = B(e, "shatter"),
                Fluid = JsonUtil.S(e, "fluid"), Hardness = JsonUtil.F(e, "hardness", 1f), Burn = JsonUtil.F(e, "burn"), Light = JsonUtil.F(e, "light"), Shape = JsonUtil.S(e, "shape"),
                Tool = JsonUtil.S(e, "tool"), Drops = e.TryGetProperty("drops", out var dr) ? dr.GetString() ?? "" : p.Name, DropChance = JsonUtil.F(e, "dropChance", 1f),
                BurnsTo = JsonUtil.S(e, "burnsTo"), MeltsTo = JsonUtil.S(e, "meltsTo", "?"), FreezesTo = JsonUtil.S(e, "freezesTo"), RotsTo = JsonUtil.S(e, "rotsTo", "?"),
                WithersTo = JsonUtil.S(e, "withersTo", "?"), BlessTo = JsonUtil.S(e, "blessTo", "?"), PacksTo = JsonUtil.S(e, "packsTo"),
                Mat = e.TryGetProperty("mat", out var m) ? m.Clone() : default,
            };
            if (e.TryGetProperty("light", out var lj) && lj.ValueKind == JsonValueKind.Object)
            {
                d.LightCol = Color.FromHtml(JsonUtil.S(lj, "color", "ffa050")); d.LightRange = JsonUtil.F(lj, "range", 8f); d.LightEnergy = JsonUtil.F(lj, "energy", 1.6f); d.Light = d.LightEnergy;
                HasLights = true;
            }
            d.Step = JsonUtil.S(e, "step", d.Fluid == "water" ? "water" : d.Tool == "pickaxe" ? "stone" : d.Tool == "axe" ? "wood" : d.Tool == "shovel" ? "dirt" : "stone");
            Defs.Add(d); _ids[p.Name] = d.Id;
        }
        Water = Id("water"); Lava = Id("lava"); Bedrock = Id("bedrock"); Stone = Id("stone"); Dirt = Id("dirt"); Ice = Id("ice"); Obsidian = Id("obsidian");
        var w = _root.GetProperty("world");
        Chunk = (int)JsonUtil.F(w, "chunk", 16); Radius = (int)JsonUtil.F(w, "radius", 5); MinY = (int)JsonUtil.F(w, "minY", -24); MaxY = (int)JsonUtil.F(w, "maxY", 160);
        RegisterMaterials();
    }
    static bool B(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;

    public static byte Id(string key) { Load(); return _ids.TryGetValue(key, out var i) ? i : (byte)0; }
    /// <summary>A block id for a "turns into" name: "" = air, "?" = no change (returns -1).</summary>
    public static int To(string key) => key == "?" ? -1 : Id(key);
    public static BlockDef Def(byte id) { Load(); return id < Defs.Count ? Defs[id] : Defs[0]; }
    public static bool Exists(string key) { Load(); return _ids.ContainsKey(key) && key != "" && key != "air"; }

    /// <summary>A number from data/blocks.json by path ("power.mul").</summary>
    public static float F(string path, float d)
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

    // ---------------------------------------------------------------- pack items: blocks stack in the pack as materials (Game.D.Mats)
    /// <summary>The material id a block is kept under in the pack ("blk_stone").</summary>
    public static string Item(string key) => "blk_" + key;
    public static string Item(byte id) => Item(Def(id).Key);
    /// <summary>What a drop name means: a block id becomes its pack item, anything else is a material from data/gather.json.</summary>
    public static string DropItem(string drop) => drop == "" ? "" : Exists(drop) ? Item(drop) : drop;
    static void RegisterMaterials()
    {
        GatherDb.Load();
        foreach (var d in Defs)
        {
            if (d.Id == 0 || d.IsFluid) continue;
            GatherDb.Mats[Item(d.Key)] = new MatDef { Id = Item(d.Key), Name = d.Name, Value = 1, Col = Tint(d) };
        }
        // drops that are not block types but are also not in gather.json (e.g. "gold_ore" kept as its block)
    }
    /// <summary>A block's main colour (icons, particles, the build-mode label).</summary>
    public static Color Tint(BlockDef d)
    {
        var m = d.Mat; if (m.ValueKind != JsonValueKind.Object) return d.Fluid == "water" ? new Color(.3f, .5f, .7f) : Colors.Gray;
        if (m.TryGetProperty("side", out var s)) m = s;
        return Color.FromHtml(JsonUtil.S(m, "color", "#808080"));
    }

    // ---------------------------------------------------------------- digging
    /// <summary>Seconds to break a block with the hero's current weapon (its type counts as a tool, data/blocks.json "dig").</summary>
    public static float DigTime(BlockDef b, string weaponType)
    {
        if (!b.Breakable) return float.PositiveInfinity;
        float speed = 1f;
        if (b.Tool != "" && Node("dig.tools." + b.Tool) is { } arr && arr.ValueKind == JsonValueKind.Array)
            foreach (var x in arr.EnumerateArray()) if (x.GetString() == weaponType) { speed = F("dig.toolSpeed", 4f); break; }
        return Mathf.Max(.05f, b.Hardness / speed);
    }

    // ---------------------------------------------------------------- looks
    static readonly Dictionary<string, Material> _mats = new();
    public enum Face { Top, Side, Bottom }
    /// <summary>The material for one face of a block (cached; shared by every chunk).</summary>
    public static Material? Material(BlockDef b, Face f)
    {
        if (b.Fluid == "water") return WaterMat;
        var m = b.Mat; if (m.ValueKind != JsonValueKind.Object) return null;
        string fk = f == Face.Top ? "top" : f == Face.Bottom ? "bottom" : "side";
        if (m.TryGetProperty(fk, out var sub) || (f != Face.Side && m.TryGetProperty("side", out sub))) m = sub;
        string key = m.GetRawText();
        if (_mats.TryGetValue(key, out var have)) return have;
        var mat = Build(m); _mats[key] = mat; return mat;
    }
    static StandardMaterial3D Build(JsonElement m)
    {
        StandardMaterial3D mat;
        string g = JsonUtil.S(m, "gothic"), tex = JsonUtil.S(m, "tex"), gfx = JsonUtil.S(m, "gfx");
        if (g != "" && GothicMats.For(g) is StandardMaterial3D gm) mat = (StandardMaterial3D)gm.Duplicate();
        else mat = new StandardMaterial3D { Roughness = .9f };
        if (tex != "")
        {
            string p = $"res://assets/textures/{tex}_col.jpg", n = $"res://assets/textures/{tex}_nrm.jpg";
            if (ResourceLoader.Exists(p)) mat.AlbedoTexture = GD.Load<Texture2D>(p);
            if (ResourceLoader.Exists(n)) { mat.NormalEnabled = true; mat.NormalTexture = GD.Load<Texture2D>(n); }
        }
        else if (gfx != "") { var (a, nn) = Gfx.Get(gfx); mat.AlbedoTexture = a; mat.NormalEnabled = true; mat.NormalTexture = nn; }
        float tile = JsonUtil.F(m, "tile", 2f); float s = 1f / Mathf.Max(.1f, tile);
        mat.Uv1Triplanar = true; mat.Uv1WorldTriplanar = true; mat.Uv1TriplanarSharpness = 8f; mat.Uv1Scale = new Vector3(s, s, s);
        mat.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        mat.VertexColorUseAsAlbedo = true;   // per-vertex light: face shading and ambient occlusion baked by the mesher
        var col = Color.FromHtml(JsonUtil.S(m, "color", "#ffffff"));
        float alpha = JsonUtil.F(m, "alpha", 1f);
        mat.AlbedoColor = new Color(col, alpha);
        if (m.TryGetProperty("rough", out var r)) mat.Roughness = r.GetSingle();
        if (alpha < .999f) { mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha; mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled; }
        float glow = JsonUtil.F(m, "glow", 0f);
        if (glow > 0) { mat.EmissionEnabled = true; mat.Emission = col; mat.EmissionEnergyMultiplier = glow; }
        World.Track(mat);   // Shadow realm tint like every other surface
        return mat;
    }
    static Material? _water;
    /// <summary>Water blocks use the overworld water shader (waves, depth tint, foam) without the cut-out around the player.</summary>
    public static Material? WaterMat
    {
        get
        {
            if (_water != null) return _water;
            if (World.WaterMaterial is ShaderMaterial w) { var d = (ShaderMaterial)w.Duplicate(); d.SetShaderParameter("vox_cut", 0f); World.RealmShaders.Add(d); _water = d; }
            else _water = new StandardMaterial3D { AlbedoColor = new Color(.2f, .4f, .55f, .7f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, Roughness = .05f };
            return _water;
        }
    }
    /// <summary>Forget cached materials (a new world: the water material belongs to the old one).</summary>
    public static void ResetLooks() { _water = null; }
}
