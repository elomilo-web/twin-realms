using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Builds the hand-placed overworld: Oakhaven, the arena, dungeon mouths, the dragon lair, standing stones, obelisks, chests, camps and NPCs.</summary>
public static class Builder
{
    static Shader? PortalShader;
    static StandardMaterial3D Post = null!, Wall = null!, Roof = null!, RoofB = null!, Wood = null!, Stone = null!, Dark = null!, Cloth = null!, Hay = null!, Sand = null!, Rock = null!, Metal = null!, Crim = null!, Gold = null!, Beam = null!;
    static Func<float> _rg = World.Rng(424242u);
    static float R(float a, float b) => a + _rg() * (b - a);
    const float Tau = Mathf.Tau;
    public static readonly List<(float x, float z, string kind)> Camps = new();

    public static void BuildAll(Node3D over)
    {
        _rg = World.Rng(424242u);   // restart the seed: every build (and every machine in a multiplayer game) places the same world
        Wall = World.Mat(new Color(.79f, .71f, .56f), new Color(.29f, .26f, .35f)); Roof = World.Mat(new Color(.36f, .28f, .24f), new Color(.1f, .08f, .15f)); /* gothic: weathered slate-brown, was a bright red */ RoofB = World.Mat(new Color(.33f, .38f, .44f), new Color(.1f, .09f, .18f));
        Wood = World.Mat(new Color(.36f, .27f, .2f), new Color(.17f, .14f, .22f)); Stone = World.Mat(new Color(.55f, .54f, .52f), new Color(.23f, .2f, .31f), 1f);
        Dark = World.Mat(new Color(.23f, .16f, .1f), new Color(.1f, .07f, .14f)); Cloth = World.Mat(new Color(.42f, .14f, .13f), new Color(.29f, .11f, .36f)); Hay = World.Mat(new Color(.6f, .52f, .32f), new Color(.29f, .23f, .35f));
        Sand = World.Mat(new Color(.84f, .75f, .53f), new Color(.23f, .18f, .28f), 1f); Rock = World.Mat(new Color(.54f, .54f, .52f), new Color(.23f, .19f, .28f), 1f); Gold = World.Mat(new Color(.7f, .56f, .3f), new Color(.54f, .35f, .66f), .4f, .8f);
        Metal = World.Mat(new Color(.48f, .5f, .53f), new Color(.29f, .27f, .38f), .4f, .7f); Crim = World.Mat(new Color(.6f, .12f, .16f), new Color(.23f, .08f, .35f)); Beam = World.Mat(new Color(.29f, .21f, .14f), new Color(.1f, .08f, .13f));
        // procedural surface detail (scripts/Gfx.cs): plaster, stone courses, planks, wood grain, shingles, rock, sand, cloth
        Gfx.Tex(Wall, "plaster", 2.5f, true, .6f); Gfx.Tex(Stone, "stone", 2.6f); Gfx.Tex(Wood, "planks", 1.6f); Gfx.Tex(Beam, "grain", 1.2f); Gfx.Tex(Dark, "grain", 1.2f);
        Gfx.Tex(Roof, "shingle", 2.4f, false, 1.4f); Gfx.Tex(RoofB, "shingle", 2.4f, false, 1.4f); Gfx.Tex(Rock, "rock", 4f, true, 1.2f); Gfx.Tex(Sand, "noise", 3f);
        Gfx.Tex(Cloth, "cloth", .8f); Gfx.Tex(Hay, "grain", .8f); Gfx.Tex(Crim, "cloth", .8f);
        Post = World.Mat(new Color(.2f, .15f, .1f), new Color(.1f, .07f, .14f)); Gfx.Tex(Post, "grain", 1.2f);
        Post.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha; Post.DistanceFadeMinDistance = 1.5f; Post.DistanceFadeMaxDistance = 4.5f;
        Village(over); Arena(over); foreach (var p in World.Pois) if (p.Dun != null) Entrance(over, p);
        Wayshrine.BuildAll(over);
        Lair(over); Stones(over); Obelisks(over); Chests(over); Npcs(over); Spawns(over);
        Tavern.Build(over);        // A-33 / A-26: the Starlight Tavern west of the square, and the town's station blocks (Tavern.cs)
        Wayside.Build(over);       // F-33: camps, farms, watchtowers, ruins, shrines, inns and crypts along the roads
        WorldSites.Build(over);    // A-36: chapels, graveyards, carts, corpses, statues, stones, cellars, war camps, relics, message stones, vistas
        Structures.Finish(over);   // F-19: every block structure is stamped; the far meshes build and the block world may start
        Breakables.Finish(over);   // A-36: the breakable props stream in near the hero from here
    }

    // ---------------------------------------------------------------- F-19 block structures (scripts/StructGen.cs)
    static byte Bk(string k) => Blocks.Id(k);
    static int Fl(float x, float z) => Structures.Floor(World.GroundAt(x + .5f, z + .5f));
    /// <summary>Oakhaven from blocks: the same houses (place, size, facing), the well, stalls, forge hearth, windmill tower, lamp posts and paddock fence.</summary>
    static void BlockVillage((float x, float z, float w, float d, float h, float r)[] hs)
    {
        var b = new Blueprint(); int oy = Fl(0, 0);
        int Dy(int x, int z) => Fl(x, z) - oy;
        foreach (var h in hs)
        {
            int hx = Mathf.FloorToInt(h.x), hz = Mathf.FloorToInt(h.z); uint seed = (uint)Math.Abs((int)(h.x * 7 + h.z * 13));
            int W = Math.Max(5, Mathf.RoundToInt(h.w) | 1), D = Math.Max(5, Mathf.RoundToInt(h.d) | 1);
            b.Merge(StructGen.House(StructGen.StyleFor("home", seed), W, D, h.h > 3.6f ? 2 : 1, seed), hx, Dy(hx, hz), hz, Structures.RotOfYaw(h.r));
            NpcHomes.Add("village", 0, oy + Dy(hx, hz), 0, hx, hz, Structures.RotOfYaw(h.r), D / 2);   // F-27: NPC homes
        }
        NpcHomes.SetFrame("village", 0, oy, 0, 0);
        b.Merge(StructGen.Well(Bk("stone_bricks"), Bk("roof_slab")), 0, 0, 0);
        b.Merge(StructGen.Stall(Bk("awning_red"), true, 1), -9, Dy(-9, 8), 8, 2);   // Brynn's stall: counter towards her, plaza side
        foreach (var (sx, sz, yaw, aw, lit) in new[] { (-3.5f, -9.6f, .08f, "awning_red", true), (9.8f, -5.6f, -1.1f, "awning_tan", true), (11.2f, 8.8f, -1.9f, "awning_blue", false) })
        { int x = Mathf.FloorToInt(sx), z = Mathf.FloorToInt(sz); b.Merge(StructGen.Stall(Bk(aw), lit, (uint)x), x, Dy(x, z), z, Structures.RotOfYaw(yaw)); }
        // Dorrin's forge: a cobblestone hearth with a chimney (the fire, anvil and bench stay)
        { int dy = Dy(24, 2); b.Box(23, dy, 1, 24, dy, 2, Bk("cobblestone")); b.Box(23, dy + 1, 1, 24, dy + 1, 2, Bk("cobble_slab")); for (int y = 1; y <= 4; y++) b.Set(23, dy + y, 1, Bk("cobblestone")); }
        // the windmill: a plaster tower (log corners) narrowing to a 3 x 3 top under a tiled cap; the sails still turn on its front
        {
            int cx = 26, cz = -18, dy = Dy(cx, cz); byte pl = Bk("plaster"), log = Bk("log");
            b.PadBox(cx - 3, cz - 3, cx + 3, cz + 3, Bk("cobblestone"), Bk("cobblestone"), 14, dy);
            for (int y = 0; y <= 10; y++)
            {
                int r = y < 7 ? 2 : 1;
                for (int z = -r; z <= r; z++) for (int x = -r; x <= r; x++)
                {
                    bool edge = Math.Abs(x) == r || Math.Abs(z) == r; if (!edge) { b.Set(cx + x, dy + y, cz + z, 0); continue; }
                    b.Set(cx + x, dy + y, cz + z, Math.Abs(x) == r && Math.Abs(z) == r ? log : y == 3 && (x == 0 || z == 0) ? Bk("glass") : pl);
                }
            }
            b.Box(cx - 2, dy + 7, cz - 2, cx + 2, dy + 7, cz + 2, Bk("roof_slab")); for (int z = -1; z <= 1; z++) for (int x = -1; x <= 1; x++) b.Set(cx + x, dy + 7, cz + z, pl);
            b.Box(cx - 1, dy + 11, cz - 1, cx + 1, dy + 11, cz + 1, Bk("roof_tiles")); b.Set(cx, dy + 12, cz, Bk("roof_slab"));
            b.Set(cx, dy, cz - 2, Bk("door"), 3); b.Set(cx, dy + 1, cz - 2, Bk("door"), 3 | 4);
        }
        foreach (var (lx, lz) in new[] { (-11f, 0f), (11, 0), (0, -12), (3.5f, 13), (-22, -2), (22, 12), (30, 10), (-30, 6) })
        { int x = Mathf.FloorToInt(lx), z = Mathf.FloorToInt(lz); b.Merge(StructGen.LampPost(), x, Dy(x, z), z); }
        for (int x = 31; x <= 45; x++) foreach (int z in new[] { 5, 14 }) { b.Set(x, Dy(x, z), z, Bk("fence")); b.Post(x, Dy(x, z) - 1, z, Blocks.Dirt); }
        for (int z = 6; z <= 13; z++) if (z is < 9 or > 10) { b.Set(45, Dy(45, z), z, Bk("fence")); b.Post(45, Dy(45, z) - 1, z, Blocks.Dirt); }
        Structures.Place(b, 0, oy, 0);
    }

    // ---------------------------------------------------------------- helpers
    public static MeshInstance3D Box(Node parent, Vector3 size, Vector3 pos, Material mat, float rotY = 0f)
    {
        var m = new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat, Position = pos, Rotation = new Vector3(0, rotY, 0) };
        parent.AddChild(m); return m;
    }
    public static StaticBody3D Solid(Node parent, Vector3 size, Vector3 pos, Material mat, float rotY = 0f)
    {
        var b = new StaticBody3D { Position = pos, Rotation = new Vector3(0, rotY, 0), CollisionLayer = 1 };
        b.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = size }, MaterialOverride = mat });
        b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });
        parent.AddChild(b); return b;
    }
    static Node3D Group(Node parent, float x, float z, float yaw = 0f, float dy = 0f) { var g = new Node3D { Position = World.OnGround(x, z, dy), Rotation = new Vector3(0, yaw, 0) }; parent.AddChild(g); return g; }
    static Torch Fire(Node parent, Vector3 pos, float size = 1f, float range = 12f, Color? col = null, bool light = true) { var t = new Torch { Position = pos, Size = size, Range = range, Col = col ?? new Color(1f, .6f, .2f), Light = light }; parent.AddChild(t); return t; }
    static void Cyl(Node parent, float r, float h, Vector3 pos, Material mat, bool solid = false)
    {
        if (solid) { var b = new StaticBody3D { Position = pos, CollisionLayer = 1 }; b.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 12 }, MaterialOverride = mat }); b.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = r, Height = h } }); parent.AddChild(b); }
        else parent.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = h, RadialSegments = 12 }, MaterialOverride = mat, Position = pos });
    }

    // ---------------------------------------------------------------- Oakhaven
    /// <summary>A house from the Medieval Village kit (data/env.json "house"): 2 m wall modules, door in the middle of the front (+Z), glass that lights up at night, tiled roof with gable walls.</summary>
    static void KitHouse(Node parent, float x, float z, float w, float d, float h, float rot)
    {
        var hc = Kit.Cfg.GetProperty("house"); float S = JsonUtil.F(hc, "storey", 3.12f);
        var roofs = new HashSet<string>(); foreach (var r in hc.GetProperty("roofs").EnumerateArray()) roofs.Add(r.GetString() ?? "");
        int W = w >= 8.5f ? 8 : w >= 5f ? 6 : 4, D = d >= 8.5f ? 8 : d >= 5f ? 6 : 4;
        while (!roofs.Contains($"{W}x{D}") && D < 10) D += 2;   // round the footprint to a roof that exists
        int floors = h > 3.6f ? 2 : 1;
        var styles = hc.GetProperty("styles"); var st = styles[Math.Abs((int)(x * 7 + z * 13)) % styles.GetArrayLength()];
        string P(string k) => JsonUtil.S(st, k, JsonUtil.S(hc, k));
        var g = Group(parent, x, z, rot, -0.1f);
        const float y0 = .3f; float top = y0 + floors * S, aDoor = 0;
        Box(g, new Vector3(W + .5f, 1.6f, D + .5f), new Vector3(0, y0 - .8f, 0), Stone);   // plinth; also covers sloping ground
        for (int f = 0; f < floors; f++)
            for (int side = 0; side < 4; side++)   // 0 front (+Z), 1 right (+X), 2 back, 3 left
            {
                float yaw = side * Mathf.Pi / 2; var b = new Basis(Vector3.Up, yaw);
                int len = side % 2 == 0 ? W : D, n = len / 2; float off = (side % 2 == 0 ? D : W) / 2f - .09f;
                for (int i = 0; i < n; i++)
                {
                    float a = -len / 2f + 1 + 2 * i; var pos = b * new Vector3(a, y0 + f * S, off);
                    bool door = f == 0 && side == 0 && i == n / 2, win = !door && (f == 0 && side == 0 || i % 2 == 1);
                    Kit.Add(g, P(door ? "door" : win ? "window" : f > 0 ? "upper" : "wall"), pos, yaw);
                    if (win) { Kit.Add(g, P("windowGlass"), pos, yaw); Kit.Add(g, P("shutters"), pos, yaw); }
                    if (door) { aDoor = a; Kit.Add(g, P("doorLeaf"), b * new Vector3(a - .51f, y0 + f * S, off), yaw); }
                }
            }
        for (int f = 0; f < floors; f++) foreach (var sx in new[] { -1f, 1f }) foreach (var sz in new[] { -1f, 1f }) Kit.Add(g, P("corner"), new Vector3(sx * W / 2f, y0 + f * S, sz * D / 2f));
        Kit.Add(g, P("roof") + W + "x" + D, new Vector3(0, top, 0));
        float gi = JsonUtil.F(hc, "gableIn", 0f);
        Kit.Add(g, P("gable") + W, new Vector3(0, top, D / 2f - gi)); Kit.Add(g, P("gable") + W, new Vector3(0, top, -D / 2f + gi), Mathf.Pi);
        Kit.Add(g, P("chimney"), new Vector3(W / 4f, top + .3f + W * .1f, -D / 4f));
        Kit.Add(g, P("lantern"), new Vector3(aDoor + 1.15f, y0 + 1.3f, D / 2f), 0, .55f);
        Box(g, new Vector3(1.8f, .3f, .9f), new Vector3(aDoor, .15f, D / 2f + .45f), Stone);   // door step
        var body = new StaticBody3D { Position = new Vector3(0, y0 + floors * S / 2f, 0), CollisionLayer = 1 }; body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(W + .2f, floors * S + .6f, D + .2f) } }); g.AddChild(body);
        // performance: the ~50 modules become one mesh (a surface per shared material), and the walls hide what stands behind them (occlusion culling)
        Kit.MergeStatic(g);
        if (!Net.Dedicated) g.AddChild(new OccluderInstance3D { Occluder = new BoxOccluder3D { Size = new Vector3(W - .5f, floors * S - .2f, D - .5f) }, Position = new Vector3(0, y0 + floors * S / 2f, 0) });
    }

    /// <summary>
    /// A whole-house model (data/env.json "house" "models": [name, scale] pairs, front and door facing +Z), picked by position and
    /// scaled so its footprint fills w x d (times the listed scale). Stone plinth and a box collider as for the modular houses.
    /// </summary>
    static bool WholeHouse(Node parent, float x, float z, float w, float d, float rot)
    {
        if (!Kit.Cfg.TryGetProperty("house", out var hc) || !hc.TryGetProperty("models", out var ml)) return false;
        var models = Kit.Models(ml); if (models.Count == 0) return false;
        var m = models[Math.Abs((int)(x * 7 + z * 13)) % models.Count]; var p = Kit.Get(m.Name); if (p == null) return false;
        var box = p.Box; float s = Mathf.Min(w / Mathf.Max(.1f, box.Size.X), d / Mathf.Max(.1f, box.Size.Z)) * m.Scale;
        var g = Group(parent, x, z, rot, -0.1f);
        Box(g, new Vector3(box.Size.X * s + .3f, 1.6f, box.Size.Z * s + .3f), new Vector3(0, -.75f, 0), Stone);   // plinth under sloping ground
        Kit.Add(g, m.Name, new Vector3(0, .05f, 0), 0, s);
        var body = new StaticBody3D { Position = new Vector3(box.GetCenter().X * s, .05f + box.Size.Y * s * .35f, box.GetCenter().Z * s), CollisionLayer = 1 };
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(box.Size.X * s, box.Size.Y * s * .7f, box.Size.Z * s) } }); g.AddChild(body);
        return true;
    }

    static void House(Node parent, float x, float z, float w, float d, float h, float rot)
    {
        if (Kit.Available && WholeHouse(parent, x, z, w, d, rot)) return;
        if (Kit.Available) { KitHouse(parent, x, z, w, d, h, rot); return; }
        var g = Group(parent, x, z, rot, -0.1f);
        int v = Math.Abs((int)(x * 7 + z * 13)) % 4; var rf = v == 1 ? RoofB : Roof; float rh = Mathf.Max(2.3f, w * .4f), top = h + .5f;
        Box(g, new Vector3(w + .4f, 1.0f, d + .4f), new Vector3(0, .5f, 0), Stone);
        Box(g, new Vector3(w, h, d), new Vector3(0, h / 2 + .5f, 0), Wall);
        Box(g, new Vector3(w + .14f, .24f, d + .14f), new Vector3(0, top - .12f, 0), Beam); Box(g, new Vector3(w + .12f, .22f, d + .12f), new Vector3(0, 1.12f, 0), Beam);
        foreach (var cx in new[] { -1f, 1f }) foreach (var cz in new[] { -1f, 1f }) Box(g, new Vector3(.26f, h, .26f), new Vector3(cx * w / 2, h / 2 + .5f, cz * d / 2), Beam);
        // gable roof with thickness, overhang, plaster gables and a ridge beam (the old one was a solid prism)
        var roof = new MeshInstance3D { Mesh = Gfx.GableRoof(w, d, rh, .8f, .7f, .2f), Position = new Vector3(0, top - .02f, 0) };
        roof.SetSurfaceOverrideMaterial(0, rf); roof.SetSurfaceOverrideMaterial(1, Wall); g.AddChild(roof);
        Box(g, new Vector3(.28f, .26f, d + 1.55f), new Vector3(0, top + rh + .2f, 0), Beam);
        Box(g, new Vector3(.85f, rh + 1.9f, .85f), new Vector3(w / 4, top + (rh + 1.9f) / 2 - .4f, -d / 4), Stone);
        Box(g, new Vector3(1.05f, .16f, 1.05f), new Vector3(w / 4, top + rh + 1.5f, -d / 4), Stone);
        float dz = d / 2 + .02f;
        Box(g, new Vector3(1.1f, 2.15f, .12f), new Vector3(0, 1.55f, dz), Wood); Box(g, new Vector3(1.4f, .22f, .2f), new Vector3(0, 2.7f, dz), Beam);
        Box(g, new Vector3(2.0f, .28f, 1.0f), new Vector3(0, .14f, d / 2 + .6f), Stone);
        g.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .13f, Height = .3f }, MaterialOverride = World.WindowMat, Position = new Vector3(1.15f, 2.2f, dz + .15f) });
        DoorLantern(g, new Vector3(-.95f, 2.5f, dz + .15f));
        float wy = h * .6f + .5f;
        void Win(float px, float pz, float ry) { var wg = new Node3D { Position = new Vector3(px, wy, pz), Rotation = new Vector3(0, ry, 0) }; g.AddChild(wg);
            Box(wg, new Vector3(.9f, 1f, .08f), Vector3.Zero, World.WindowMat); Box(wg, new Vector3(1.1f, .1f, .14f), new Vector3(0, .55f, .03f), Beam); Box(wg, new Vector3(1.1f, .1f, .14f), new Vector3(0, -.55f, .03f), Beam);
            Box(wg, new Vector3(.1f, 1.1f, .14f), new Vector3(-.5f, 0, .03f), Beam); Box(wg, new Vector3(.1f, 1.1f, .14f), new Vector3(.5f, 0, .03f), Beam); Box(wg, new Vector3(.45f, 1.0f, .05f), new Vector3(-.78f, 0, .1f), Wood, .5f); Box(wg, new Vector3(.45f, 1.0f, .05f), new Vector3(.78f, 0, .1f), Wood, -.5f); }
        if (w > 5.5f) { Win(-w / 3, dz, 0); Win(w / 3, dz, 0); Win(0, -dz, Mathf.Pi); } else Win(-w / 3 - .1f, dz, 0);
        Win(w / 2 + .02f, 0, Mathf.Pi / 2); Win(-w / 2 - .02f, 0, -Mathf.Pi / 2);
        // collision: rotate footprint with the house
        var body = new StaticBody3D { Position = new Vector3(0, h / 2 + .5f, 0), CollisionLayer = 1 }; body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(w + .2f, h + 1f, d + .2f) } }); g.AddChild(body);
    }

    static void Village(Node3D over)
    {
        float P = Mathf.Pi;
        (float x, float z, float w, float d, float h, float r)[] hs = { (-15, -9, 7, 6, 3.2f, 0), (-18, 7, 6, 6, 3f, P / 2), (15, -10, 7, 6, 3.3f, 0), (18, 6, 6, 7, 3.1f, -P / 2), (-3, -21, 9, 6.5f, 4.2f, 0), (8, -20, 6, 6, 3f, 0), (-7, 19, 6.5f, 6, 3.1f, P), (9, 19, 7, 6, 3.2f, P) };
        bool blk = Structures.On;   // F-19: houses, well, stalls, hearth, windmill, lamps and fences as blocks
        if (blk) BlockVillage(hs);
        else
        {
        foreach (var a in hs) House(over, a.x, a.z, a.w, a.d, a.h, a.r);
        // plaza cobbles
        var cob = World.Mat(new Color(.5f, .45f, .4f),   // warmer and darker: the light stone blew out to white in the brighter grade
            new Color(.2f, .17f, .25f), 1f); Gfx.Tex(cob, "cobble", 3.2f, true, 1.6f);
        over.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 9f, BottomRadius = 9f, Height = .06f, RadialSegments = 32 }, MaterialOverride = cob, Position = World.OnGround(0, 0, .03f) });
        // well
        var well = Group(over, 0, 0); Cyl(well, 1.5f, .9f, new Vector3(0, .45f, 0), Stone, true); Cyl(well, 1.2f, .05f, new Vector3(0, .86f, 0), World.Mat(new Color(.04f, .1f, .16f)));
        Box(well, new Vector3(.2f, 2.4f, .2f), new Vector3(-1.2f, 1.5f, 0), Wood); Box(well, new Vector3(.2f, 2.4f, .2f), new Vector3(1.2f, 1.5f, 0), Wood); Box(well, new Vector3(3.2f, .25f, 1.8f), new Vector3(0, 2.7f, 0), Roof);
        }
        Campfire(over, 7, 5);
        // the hero's home: the cottage north of the plaza (at -7, 19, door facing the plaza); storage chest beside the door
        over.AddChild(new StashChest { Position = World.OnGround(-4.6f, 14.0f), Rotation = new Vector3(0, Mathf.Pi, 0) });
        over.AddChild(new PartyChestNode { Position = World.OnGround(-2.1f, 13.9f), Rotation = new Vector3(0, Mathf.Pi, 0) });
        if (Kit.Available) KitVillageProps(over);
        // A-36: the village's stores are breakable props (a shop's in town: smashing them where someone sees is a crime)
        Breakables.Add("barrel", 22f, 3f); Breakables.Add("sack", -21f, -13f, .5f); Breakables.Add("sack", 20.2f, -21f, 2f);
        Breakables.Add("barrel_apples", -12.6f, 6.2f); Breakables.Add("fruit_crate", -12.4f, 9.9f, .4f); Breakables.Add("sack", 25.2f, 4.4f, 1f);
        var st = Group(over, -9, 8);
        if (blk) { }
        else if (Kit.Available)
        {
            float sy = JsonUtil.F(Kit.Cfg.GetProperty("props"), "stallYaw", 0f) * Mathf.Pi / 180f;
            Kit.Add(st, Kit.Str("props", "stall"), new Vector3(.6f, 0, .3f), sy);
            Kit.Add(st, Kit.Str("props", "barrelApples"), new Vector3(-2.3f, 0, .6f), 0, 1.1f);
            Kit.Add(st, Kit.Str("props", "fruit"), new Vector3(-2.2f, 0, -.5f), .4f, 1.2f);
            var cb = new StaticBody3D { Position = new Vector3(0, .5f, .3f), CollisionLayer = 1 }; cb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(3.6f, 1f, 1f) } }); st.AddChild(cb);
        }
        else
        {
        foreach (var s in new[] { (-1.8f, -1f), (1.8f, -1f), (-1.8f, 1f), (1.8f, 1f) }) Box(st, new Vector3(.2f, 2.8f, .2f), new Vector3(s.Item1, 1.4f, s.Item2), Wood);
        Box(st, new Vector3(4.4f, .15f, 3f), new Vector3(0, 2.85f, 0), Cloth); Solid(st, new Vector3(3.6f, .9f, .9f), new Vector3(0, .5f, .3f), Wood); Box(st, new Vector3(3.6f, .1f, .9f), new Vector3(0, .97f, .3f), Cloth);
        }
        var fg = Group(over, 24, 2); if (!blk) Solid(fg, new Vector3(2.4f, 1.3f, 2f), new Vector3(0, .65f, 0), Stone);
        if (Kit.Available) { Kit.Add(fg, Kit.Str("props", "anvil"), new Vector3(2, 0, 0), Mathf.Pi / 2, 1f, true); Kit.Add(fg, Kit.Str("props", "workbench"), new Vector3(-.4f, 0, -2.1f), 0, 1f, true); Kit.Add(fg, Kit.Str("props", "weaponStand"), new Vector3(3.6f, 0, -1.4f), -.5f, 1f, true); }
        else Box(fg, new Vector3(1.1f, .6f, .6f), new Vector3(2, .8f, 0), Metal);
        if (!blk) Box(fg, new Vector3(.5f, 3f, .5f), new Vector3(-.6f, 2.4f, -.5f), Stone);
        Fire(fg, new Vector3(.2f, blk ? 1.7f : 1.45f, 0), 1.4f, 16f);
        var wm = Group(over, 26, -18);
        if (!blk)
        {
        var tower = new StaticBody3D { CollisionLayer = 1 }; tower.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.7f, BottomRadius = 2.7f, Height = 9f, RadialSegments = 10 }, MaterialOverride = Wall, Position = new Vector3(0, 4.5f, 0) });
        tower.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 2.4f, Height = 9f }, Position = new Vector3(0, 4.5f, 0) }); wm.AddChild(tower);
        wm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 3.1f, Height = 3f, RadialSegments = 10 }, MaterialOverride = Roof, Position = new Vector3(0, 10.5f, 0) });
        }
        var hub = new Spinner { Axis = Vector3.Forward, Speed = .45f, Position = blk ? new Vector3(.5f, 9f, 2.3f) - new Vector3(0, wm.Position.Y - Fl(26, -18), 0) : new Vector3(0, 8f, 2.4f) }; wm.AddChild(hub);
        for (int i = 0; i < 4; i++) { var arm = new Node3D { Rotation = new Vector3(0, 0, i * P / 2) }; arm.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.5f, 6f, .12f) }, MaterialOverride = Wood, Position = new Vector3(0, 3f, 0) }); hub.AddChild(arm); }
        if (!blk) foreach (var p in new[] { (-11f, 0f), (11, 0), (0, -12), (3.5f, 13), (-22, -2), (22, 12), (30, 10), (-30, 6) }) Lamp(over, p.Item1, p.Item2);
        _doorLanterns = 0;
        foreach (var p in new[] { (-20f, -14f), (21, -22), (-22, 16), (15, 25) }) { Crate(over, p.Item1, p.Item2, 1.1f); Crate(over, p.Item1 + 1.3f, p.Item2 + .4f, .8f); Barrel(over, p.Item1 - 1, p.Item2 + 1); }
        for (int i = 0; i < 4; i++) { var hb = Group(over, -24 + i * 1.8f, 20 + (i % 2) * .8f, 0, .4f); hb.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .9f, BottomRadius = .9f, Height = .8f, RadialSegments = 8 }, MaterialOverride = Hay, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 110f, VisibilityRangeEndMargin = 15f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self }); }
        if (blk) { }
        else if (Kit.Available) { for (int i = 0; i < 7; i++) foreach (var zz in new[] { -4.2f, 4.2f }) Kit.Add(over, Kit.Str("props", "fence"), World.OnGround(31.03f + i * 2.06f, 10 + zz, -.02f)); }
        else for (int i = 0; i < 10; i++) foreach (var zz in new[] { -4.2f, 4.2f }) Box(over, new Vector3(.15f, 1.1f, .15f), World.OnGround(30 + i * 1.6f, 10 + zz, .5f), Wood);
        DressVillage(over, hs);
    }

    // ---- plaza and street dressing: market stalls, banners, firewood, cart, puddles, straw (own RNG so the world seed sequence is untouched)
    static StandardMaterial3D? _dvPuddle, _dvJar; static readonly Dictionary<uint, StandardMaterial3D> _dvAwn = new();
    static StandardMaterial3D DvTint(uint key, Color c, bool twoSided = false)
    {
        if (_dvAwn.TryGetValue(key, out var m)) return m;
        m = (StandardMaterial3D)Cloth.Duplicate(); m.AlbedoColor = c; if (twoSided) m.CullMode = BaseMaterial3D.CullModeEnum.Disabled; _dvAwn[key] = m; return m;
    }
    static MeshInstance3D DvMesh(Node parent, Mesh mesh, Vector3 pos, Material mat, Vector3? rot = null, Vector3? scale = null)
    {
        var m = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos, Rotation = rot ?? Vector3.Zero, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 120f };
        if (scale != null) m.Scale = scale.Value; parent.AddChild(m); return m;
    }
    static void DvMulti(Node parent, Mesh mesh, Material mat, List<Transform3D> xf, float range = 120f)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = xf.Count };
        for (int i = 0; i < xf.Count; i++) mm.SetInstanceTransform(i, xf[i]);
        parent.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = range });
    }
    static void MarketStall(Node parent, float x, float z, float yaw, Color awn, Color trim, Func<float> rnd, bool lantern)
    {
        var g = Group(parent, x, z, yaw);
        var aM = DvTint((uint)(awn.ToRgba32() ^ 0x1u), awn, true); var tM = DvTint((uint)(trim.ToRgba32() ^ 0x2u), trim, true);
        foreach (var (px, pz, ph) in new[] { (-1.5f, -.9f, 2.7f), (1.5f, -.9f, 2.7f), (-1.5f, 1.1f, 2.2f), (1.5f, 1.1f, 2.2f) }) Box(g, new Vector3(.12f, ph, .12f), new Vector3(px, ph / 2, pz), Post);
        DvMesh(g, new BoxMesh { Size = new Vector3(3.5f, .06f, 2.4f) }, new Vector3(0, 2.46f, .1f), aM, new Vector3(.22f, 0, 0));
        DvMesh(g, new BoxMesh { Size = new Vector3(3.5f, .3f, .05f) }, new Vector3(0, 2.1f, 1.3f), tM);   // front valance
        Box(g, new Vector3(3.1f, .05f, .05f), new Vector3(0, 2.62f, -.9f), Wood);
        Solid(g, new Vector3(3f, .9f, .8f), new Vector3(0, .45f, .6f), Wood);
        Box(g, new Vector3(3.1f, .05f, .9f), new Vector3(0, .93f, .6f), tM);
        // wares: crates, sacks (squashed spheres), jars
        for (int i = 0; i < 2; i++) Box(g, new Vector3(.5f, .4f, .5f), new Vector3(-1.1f + i * .6f, 1.18f, .6f), Wood, R2(rnd, 0, 1));
        for (int i = 0; i < 3; i++) DvMesh(g, new SphereMesh { Radius = .26f, Height = .52f, RadialSegments = 8, Rings = 4 }, new Vector3(.1f + i * .45f, 1.1f, .55f + (i % 2) * .1f), Hay, null, new Vector3(1.1f, .7f, 1f));
        _dvJar ??= World.Mat(new Color(.3f, .2f, .14f), new Color(.1f, .08f, .13f), .6f);
        for (int i = 0; i < 3; i++) DvMesh(g, new CylinderMesh { TopRadius = .08f, BottomRadius = .12f, Height = .26f, RadialSegments = 8 }, new Vector3(-.5f + i * .22f, 1.08f, .85f), _dvJar);
        Box(g, new Vector3(.8f, .5f, .5f), new Vector3(0, .25f, -.45f), Wood);   // stock behind the counter
        if (lantern)
        {
            Box(g, new Vector3(.04f, .3f, .04f), new Vector3(1.5f, 2.0f, 1.1f), Metal);
            DvMesh(g, new SphereMesh { Radius = .1f, Height = .24f, RadialSegments = 6, Rings = 3 }, new Vector3(1.5f, 1.8f, 1.1f), World.LampMat);
            var lt = World.Lamp(g, new Vector3(1.5f, 1.8f, 1.2f), new Color(1f, .62f, .32f), 8f, 1.6f); lt.ShadowEnabled = false; lt.DistanceFadeEnabled = true; lt.DistanceFadeBegin = 80f; lt.DistanceFadeLength = 20f;
        }
    }
    static float R2(Func<float> r, float a, float b) => a + r() * (b - a);
    static void Banner(Node parent, float hx, float hz, float yaw, float lx, float lz, Color c, Color stripe)
    {
        var g = Group(parent, hx, hz, yaw);        var bm = DvTint((uint)(c.ToRgba32() ^ 0x3u), c, true); var sm = DvTint((uint)(stripe.ToRgba32() ^ 0x4u), stripe, true);
        Box(g, new Vector3(.9f, .06f, .06f), new Vector3(lx, 3.3f, lz + .06f), Beam);
        DvMesh(g, new QuadMesh { Size = new Vector2(.75f, 1.9f) }, new Vector3(lx, 2.32f, lz + .09f), bm);
        DvMesh(g, new QuadMesh { Size = new Vector2(.12f, 1.4f) }, new Vector3(lx, 2.45f, lz + .1f), sm);
        DvMesh(g, new QuadMesh { Size = new Vector2(.4f, .1f) }, new Vector3(lx, 2.55f, lz + .105f), sm);
        DvMesh(g, new PrismMesh { Size = new Vector3(.75f, .25f, .02f) }, new Vector3(lx, 1.27f, lz + .09f), bm, new Vector3(0, 0, Mathf.Pi));
    }
    static void DressVillage(Node3D over, (float x, float z, float w, float d, float h, float r)[] hs)
    {
        var rnd = World.Rng(777u);
        if (Structures.On) goto Wood;   // F-19: the stalls are blocks (BlockVillage); the banners hung on the old house fronts
        // stalls around the plaza edge (clear of well, campfire, benches, table, lamps, the west stall and the cottage path)
        MarketStall(over, -3.5f, -9.6f, .08f, new Color(.4f, .13f, .12f), new Color(.52f, .4f, .18f), rnd, true);
        MarketStall(over, 9.8f, -5.6f, -1.1f, new Color(.5f, .37f, .17f), new Color(.3f, .1f, .1f), rnd, true);
        MarketStall(over, 11.2f, 8.8f, -1.9f, new Color(.28f, .36f, .37f), new Color(.45f, .35f, .2f), rnd, false);
        // banners beside the doors of three houses
        Banner(over, -15, -9, 0, 2.2f, 6.0f / 2 + .0f, new Color(.3f, .07f, .09f), new Color(.65f, .52f, .24f));
        Banner(over, 15, -10, 0, -2.4f, 3.0f, new Color(.16f, .2f, .22f), new Color(.6f, .5f, .25f));
        Banner(over, 8, -20, 0, 2.0f, 3.0f, new Color(.4f, .3f, .1f), new Color(.2f, .06f, .07f));
        // firewood stacks
        Wood:
        foreach (var (fx, fz, fy) in new[] { (-17f, -5.2f, .1f), (17.5f, -6.2f, -.1f), (5f, -16.2f, .2f), (-13f, 13.5f, 1.2f) })
        {
            var g = Group(over, fx, fz, fy); var lm = new CylinderMesh { TopRadius = .1f, BottomRadius = .1f, Height = 1.1f, RadialSegments = 6 }; var xf = new List<Transform3D>();
            for (int row = 0; row < 3; row++) for (int i = 0; i < 5 - row; i++) xf.Add(new Transform3D(new Basis(Vector3.Forward, Mathf.Pi / 2), new Vector3(0, .12f + row * .19f, -.5f + (i + row * .5f) * .21f)));
            DvMulti(g, lm, Dark, xf); Box(g, new Vector3(.08f, .55f, .08f), new Vector3(-.6f, .27f, -.5f), Post); Box(g, new Vector3(.08f, .55f, .08f), new Vector3(-.6f, .27f, .5f), Post);
        }
        // a handcart left at the plaza edge
        var c = Group(over, -13.5f, -3.2f, .5f);
        var cb = new StaticBody3D { Position = new Vector3(0, .75f, 0), CollisionLayer = 1 }; cb.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2.3f, .7f, 1.3f) } }); c.AddChild(cb);
        Box(c, new Vector3(2.2f, .14f, 1.2f), new Vector3(0, .7f, 0), Wood);
        Box(c, new Vector3(2.2f, .35f, .07f), new Vector3(0, .95f, .6f), Wood); Box(c, new Vector3(2.2f, .35f, .07f), new Vector3(0, .95f, -.6f), Wood); Box(c, new Vector3(.07f, .35f, 1.2f), new Vector3(-1.1f, .95f, 0), Wood);
        Box(c, new Vector3(1.7f, .06f, .06f), new Vector3(1.9f, .6f, .45f), Post, .0f); Box(c, new Vector3(1.7f, .06f, .06f), new Vector3(1.9f, .6f, -.45f), Post);
        foreach (var wz in new[] { -.7f, .7f }) foreach (var wx in new[] { -.6f, .6f }) DvMesh(c, new CylinderMesh { TopRadius = .38f, BottomRadius = .38f, Height = .08f, RadialSegments = 10 }, new Vector3(wx, .38f, wz), Post, new Vector3(Mathf.Pi / 2, 0, 0));
        DvMesh(c, new SphereMesh { Radius = .3f, Height = .6f, RadialSegments = 8, Rings = 4 }, new Vector3(-.4f, 1.0f, 0), Hay, null, new Vector3(1.2f, .7f, 1f));
        // puddles: dark glossy flat discs
        _dvPuddle ??= World.Mat(new Color(.03f, .035f, .045f), new Color(.02f, .02f, .04f), .1f);
        foreach (var (px, pz, pr) in new[] { (-4.5f, 4.5f, 1.3f), (4.2f, -3.6f, 1.0f), (12.5f, 3.0f, 1.5f), (-8f, -9.5f, 1.1f) })
            DvMesh(over, new CylinderMesh { TopRadius = pr, BottomRadius = pr, Height = .015f, RadialSegments = 14 }, World.OnGround(px, pz, Mathf.Sqrt(px * px + pz * pz) < 9f ? .075f : .04f), _dvPuddle, null, new Vector3(1f, 1f, .7f));
        // scattered straw: thin flat slivers around the stalls and cart
        var sx = new List<Transform3D>();
        foreach (var (cx, cz) in new[] { (-3.5f, -8f), (9.8f, -4f), (11.2f, 7f), (-13.5f, -2f) })
            for (int i = 0; i < 7; i++) { var p = World.OnGround(cx + R2(rnd, -1.6f, 1.6f), cz + R2(rnd, -1.2f, 1.2f), .09f); sx.Add(new Transform3D(new Basis(Vector3.Up, R2(rnd, 0, Tau)), p)); }
        DvMulti(over, new BoxMesh { Size = new Vector3(.45f, .008f, .035f) }, Hay, sx);
    }
    /// <summary>Extra village dressing from the kits: a wagon by the hay, benches and a table on the plaza edge, buckets and sacks by doors.</summary>
    static void KitVillageProps(Node3D over)
    {
        void At(string key, float x, float z, float yaw = 0, float s = 1, bool solid = true) => Kit.Add(over, Kit.Str("props", key), World.OnGround(x, z, -.02f), yaw, s, solid);
        At("wagon", -26.5f, 23.5f, 1.2f);
        At("bench", 5.5f, -5.5f, .6f); At("bench", -6f, -5.2f, -.6f);
        At("table", 9.5f, 3.5f, 1.3f); At("bench", 10.6f, 3f, 1.3f, 1f);
        At("bucket", 1.9f, 1.2f, 0, 1.2f, false);   // (the bags are breakable sacks now: Village)
        At("cauldron", 8.4f, 5.6f, 0, .9f);
    }
    static void Lamp(Node parent, float x, float z)
    {
        // post fades out (dithered) when the camera is right next to it, so lamps never hide the hero
        var g = Group(parent, x, z); Box(g, new Vector3(.16f, 3.0f, .16f), new Vector3(0, 1.5f, 0), Post); Box(g, new Vector3(.34f, .3f, .34f), new Vector3(0, .15f, 0), Post);
        Box(g, new Vector3(.5f, .07f, .07f), new Vector3(.18f, 2.95f, 0), Post);
        g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .12f, BottomRadius = .2f, Height = .42f, RadialSegments = 6 }, MaterialOverride = World.LampMat, Position = new Vector3(.38f, 2.68f, 0) });
        g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .02f, BottomRadius = .26f, Height = .18f, RadialSegments = 6 }, MaterialOverride = Post, Position = new Vector3(.38f, 2.97f, 0) });
        var lt = World.Lamp(g, new Vector3(.38f, 2.6f, 0), new Color(1f, .62f, .32f), 10f, 2.2f);
        lt.DistanceFadeEnabled = true; lt.DistanceFadeBegin = 100f; lt.DistanceFadeLength = 20f; lt.OmniAttenuation = 1.3f;
    }
    static int _doorLanterns;   // door lanterns added this build (capped; reset after the lamp posts, which are placed after the houses)
    static void DoorLantern(Node3D g, Vector3 pos)
    {
        if (_doorLanterns >= 4) return; _doorLanterns++;
        g.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .1f, Height = .24f, RadialSegments = 6, Rings = 3 }, MaterialOverride = World.LampMat, Position = pos, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 120f });
        var lt = World.Lamp(g, pos + new Vector3(0, -.1f, .4f), new Color(1f, .62f, .32f), 8f, 1.8f); lt.DistanceFadeEnabled = true; lt.DistanceFadeBegin = 100f; lt.DistanceFadeLength = 20f;
    }
    static void Campfire(Node parent, float x, float z)
    {
        if (Structures.On) { int bx = Mathf.FloorToInt(x), bz = Mathf.FloorToInt(z); Structures.Place(StructGen.Campfire(), bx, Fl(bx, bz), bz); return; }   // F-19: a campfire block in a ring of cobble slabs
        var g = Group(parent, x, z); for (int i = 0; i < 6; i++) { float a = i / 6f * Tau; g.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .22f, Height = .3f, RadialSegments = 6, Rings = 3 }, MaterialOverride = Stone, Position = new Vector3(Mathf.Cos(a) * .6f, .08f, Mathf.Sin(a) * .6f), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 90f, VisibilityRangeEndMargin = 12f, VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self }); }
        for (int i = 0; i < 3; i++) g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .08f, BottomRadius = .08f, Height = .9f }, MaterialOverride = Wood, Position = new Vector3(0, .25f, 0), Rotation = new Vector3(0, i * 2.1f, 1.2f) });
        Fire(g, new Vector3(0, .5f, 0), 1.2f, 14f);
    }
    static void Crate(Node parent, float x, float z, float s)
    {
        float yaw = R(0, 3);   // always draw the number, so the seed sequence (and every later placement) stays the same
        Breakables.Add(s < 1f ? "crate_small" : "crate", x, z, yaw);   // A-36: a breakable prop (in town it is a shop's: Crime.Vandalism)
    }
    static void Barrel(Node parent, float x, float z) => Breakables.Add("barrel", x, z, x);   // A-36

    // ---------------------------------------------------------------- Arena
    public static StaticBody3D? GateBody; static Node3D? _portcullis;
    public static Vector3 ArenaPos => new Vector3(World.GetPoi("arena")!.X, 0, World.GetPoi("arena")!.Z);
    public static float ArenaH => World.GroundAt(ArenaPos.X, ArenaPos.Z);
    static StandardMaterial3D _arSand = null!, _arStone = null!, _arBase = null!, _arBanner = null!, _arStain = null!, _arWorn = null!;
    static void ArenaMats()
    {
        if (_arSand != null) return;
        StandardMaterial3D Tint(string gname, Material fb, Color c) { var m = GothicMats.For(gname) is StandardMaterial3D s ? (StandardMaterial3D)s.Duplicate() : (StandardMaterial3D)fb.Duplicate(); m.AlbedoColor = c; return m; }
        _arSand = Tint("G_Plaster", Sand, new Color(.62f, .52f, .38f)); _arSand.Uv1Scale = new Vector3(.45f, .45f, .45f); _arSand.Roughness = .95f; _arSand.NormalScale = .8f; _arSand.Uv1TriplanarSharpness = 4f; _arSand.Uv1Triplanar = true; _arSand.Uv1WorldTriplanar = true;
        _arStone = Tint("G_Stone", Stone, new Color(.62f, .6f, .57f)); _arBase = Tint("G_Brick", Stone, new Color(.3f, .27f, .26f));
        _arBanner = Tint("G_Cloth", Cloth, new Color(.34f, .07f, .09f)); _arBanner.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
        _arStain = new StandardMaterial3D { AlbedoColor = new Color(.06f, .045f, .035f, .38f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _arWorn = new StandardMaterial3D { AlbedoColor = new Color(.18f, .15f, .11f, .14f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded };
        _arTrim = Tint("G_Stone", Stone, new Color(.74f, .71f, .66f));
        // the colosseum is built from unit boxes scaled per MultiMesh instance: world triplanar keeps the stone texture at its real size
        foreach (var m in new[] { _arStone, _arBase, _arTrim }) { m.Uv1Triplanar = true; m.Uv1WorldTriplanar = true; }
        _arVoid = new StandardMaterial3D { AlbedoColor = new Color(.025f, .02f, .02f), Roughness = 1f };   // arch openings and niches in deep shadow
        _arBanner.VertexColorUseAsAlbedo = true; _arBanner.AlbedoColor = new Color(.62f, .1f, .12f);   // banner colour per MultiMesh instance (crimson / black)
        _arIron = Tint("G_Metal_Iron", Metal, new Color(.42f, .4f, .38f));
        _arCrowd = new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .95f };
        _arFace = new StandardMaterial3D { AlbedoColor = new Color(.01f, .01f, .012f), Roughness = 1f };
    }
    static StandardMaterial3D _arTrim = null!, _arVoid = null!, _arIron = null!, _arCrowd = null!, _arFace = null!;

    /// <summary>One MultiMesh (one draw call) for every repeated arena piece.</summary>
    static MultiMeshInstance3D ArMulti(Node3D g, Mesh mesh, Material mat, List<Transform3D> xf, List<Color>? cols = null, bool shadow = true)
    {
        var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = cols != null, Mesh = mesh };
        mm.InstanceCount = xf.Count;
        for (int i = 0; i < xf.Count; i++) { mm.SetInstanceTransform(i, xf[i]); if (cols != null) mm.SetInstanceColor(i, cols[i]); }
        var mi = new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = mat, CastShadow = shadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off };
        g.AddChild(mi); return mi;
    }

    /// <summary>A seated, hooded spectator (robe, shoulders, hood with its point falling back, knees): faces -Z. The face is a separate dark mesh.</summary>
    static ArrayMesh HoodedFigure()
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        void Add(Mesh m, Basis b, Vector3 p) => st.AppendFrom(m, 0, new Transform3D(b, p));
        Add(new CylinderMesh { TopRadius = .15f, BottomRadius = .27f, Height = .78f, RadialSegments = 8, Rings = 1 }, Basis.Identity, new Vector3(0, .39f, .02f));
        Add(new SphereMesh { Radius = .22f, Height = .3f, RadialSegments = 8, Rings = 4 }, Basis.FromScale(new Vector3(1.15f, 1f, .8f)), new Vector3(0, .74f, .02f));
        Add(new SphereMesh { Radius = .145f, Height = .34f, RadialSegments = 8, Rings = 5 }, Basis.Identity, new Vector3(0, .99f, .01f));
        Add(new CylinderMesh { TopRadius = .005f, BottomRadius = .1f, Height = .22f, RadialSegments = 6, Rings = 1 }, new Basis(Vector3.Right, 1.0f), new Vector3(0, 1.08f, .12f));
        Add(new BoxMesh { Size = new Vector3(.38f, .17f, .36f) }, Basis.Identity, new Vector3(0, .44f, -.2f));
        Add(new CylinderMesh { TopRadius = .2f, BottomRadius = .26f, Height = .45f, RadialSegments = 8, Rings = 1 }, Basis.Identity, new Vector3(0, .2f, -.34f));   // robe over the shins
        return st.Commit();
    }
    static void Arena(Node3D over)
    {
        var ap = World.GetPoi("arena")!; float H = World.GroundAt(ap.X, ap.Z);
        bool blk = Structures.On;   // F-19: the colosseum is blocks (StructGen.Arena); banners, iron, braziers, crowd and the portcullis stay
        if (blk) { int oy = Structures.Floor(H); Structures.Place(StructGen.Arena(), Mathf.FloorToInt(ap.X), oy, Mathf.FloorToInt(ap.Z)); H = oy; }
        var g = new Node3D { Position = new Vector3(ap.X, H, ap.Z) }; over.AddChild(g);
        ArenaMats();
        if (!blk) g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 22.6f, BottomRadius = 22.6f, Height = .1f, RadialSegments = 40 }, MaterialOverride = _arSand, Position = new Vector3(0, .02f, 0) });
        { // worn central circle and dark stains (flat transparent discs, no shadows)
            void Disc(float x, float z, float r, Material m, float y) => g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = .01f, RadialSegments = 24 }, MaterialOverride = m, Position = new Vector3(x, y, z), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
            Disc(0, 0, 11f, _arWorn, .075f); Disc(0, 0, 6.5f, _arWorn, .08f);
            var sr = World.Rng(777u); float F(float a, float b) => a + sr() * (b - a);
            for (int i = 0; i < 9; i++) { float a = F(0, Tau), d = F(3f, 19f); Disc(Mathf.Cos(a) * d, Mathf.Sin(a) * d, F(.8f, 2.6f), _arStain, .09f + i * .002f); }
        }
        // ---- gothic colosseum: podium wall with lancet niches, four stepped stone tiers with seat lips, a two-storey arcaded outer
        // wall with a crenellated parapet, pylons with crimson banners and braziers. Every repeated piece is one MultiMesh of a
        // scaled unit box (a draw call each); collisions are box shapes on a single StaticBody.
        const int SEG = 44; float gateA = Mathf.Pi; (float r, float h)[] tiers = { (25.6f, 3.4f), (27.8f, 5.2f), (30f, 7f), (32.2f, 9f) };
        var unit = new BoxMesh { Size = Vector3.One };
        var walls = new StaticBody3D { Name = "ArenaWalls", CollisionLayer = 1 }; g.AddChild(walls);
        void Shape(Vector3 size, Transform3D t) { if (!blk) walls.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size }, Transform = t }); }
        Basis Seg(float a) => new Basis(Vector3.Up, -a + Mathf.Pi / 2);   // local X along the ring, +Z outward
        Vector3 At(float a, float r, float y) => new(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        Transform3D Xf(float a, float r, float y, Vector3 size, float tang = 0f, float roll = 0f)
        {
            var b = Seg(a); var o = At(a, r, y) + b.X * tang;
            return new Transform3D(b * new Basis(Vector3.Back, roll) * Basis.FromScale(size), o);
        }
        bool NearGate(float a, float w) => Mathf.Abs(Util.AngDiff(a, gateA)) < w;
        var stoneL = new List<Transform3D>(); var baseL = new List<Transform3D>(); var trim = new List<Transform3D>();
        var voids = new List<Transform3D>(); var iron = new List<Transform3D>(); var banners = new List<Transform3D>(); var bannerC = new List<Color>();
        Color crimson = new(1f, 1f, 1f), black = new Color(.16f, .14f, .15f).SrgbToLinear();   // instance colours are linear (no sRGB step)
        // podium wall round the sand: dark brick, stone coping, a lancet niche (slot + diamond head) in every other bay, banners between
        for (int i = 0; i < SEG; i++)
        {
            float a = i / (float)SEG * Tau; if (NearGate(a, .12f)) continue; float ch = Tau * 23.6f / SEG + .3f;
            var sz = new Vector3(ch, 3f, 1.4f); stoneL.Add(Xf(a, 23.6f, 1.2f, sz)); Shape(sz, new Transform3D(Seg(a), At(a, 23.6f, 1.2f)));
            trim.Add(Xf(a, 23.75f, 2.84f, new Vector3(ch + .1f, .28f, 1.9f)));
            if (i % 2 == 0)
            {
                voids.Add(Xf(a, 22.86f, 1.05f, new Vector3(1.0f, 1.7f, .1f)));
                voids.Add(Xf(a, 22.86f, 1.9f, new Vector3(.71f, .71f, .1f), 0, Mathf.Pi / 4));
            }
            else
            {
                banners.Add(Xf(a, 22.84f, 1.62f, new Vector3(.9f, 2.2f, .05f))); bannerC.Add(i % 4 == 1 ? crimson : black);
                iron.Add(Xf(a, 22.8f, 2.74f, new Vector3(1.1f, .08f, .08f)));
            }
        }
        // tiers: stepped stone seating, a pale seat lip along each front edge
        foreach (var t in tiers) for (int i = 0; i < SEG; i++)
        {
            float a = i / (float)SEG * Tau; if (NearGate(a, .17f)) continue; float ch = Tau * t.r / SEG + .35f;
            var sz = new Vector3(ch, t.h, 2.3f); (t.r < 26f ? baseL : stoneL).Add(Xf(a, t.r, t.h / 2 - .3f, sz));
            Shape(sz, new Transform3D(Seg(a), At(a, t.r, t.h / 2 - .3f)));
            trim.Add(Xf(a, t.r - .98f, t.h - .22f, new Vector3(ch, .18f, .42f)));
        }
        // outer wall: solid ring, two storeys of pointed arches on the outside (pilasters, dark lancets, hood mouldings, string
        // course, cornice), two merlons per bay on top
        for (int i = 0; i < SEG; i++)
        {
            float a = i / (float)SEG * Tau; if (NearGate(a, .17f)) continue; float ch = Tau * 34f / SEG + .3f;
            var sz = new Vector3(ch, 12.6f, 1.6f); stoneL.Add(Xf(a, 34f, 6f, sz)); Shape(sz, new Transform3D(Seg(a), At(a, 34f, 6f)));
            baseL.Add(Xf(a, 34.95f, 6f, new Vector3(.8f, 12.6f, .5f), ch / 2));   // pilaster between bays
            foreach (float y0 in new[] { 1.0f, 6.0f })
            {
                voids.Add(Xf(a, 34.83f, y0 + 1.3f, new Vector3(1.9f, 2.6f, .08f)));
                voids.Add(Xf(a, 34.83f, y0 + 2.6f, new Vector3(1.34f, 1.34f, .08f), 0, Mathf.Pi / 4));
                trim.Add(Xf(a, 34.86f, y0 + 3.15f, new Vector3(1.0f, .22f, .14f), -.48f, .78f));
                trim.Add(Xf(a, 34.86f, y0 + 3.15f, new Vector3(1.0f, .22f, .14f), .48f, -.78f));
            }
            trim.Add(Xf(a, 34.95f, 5.4f, new Vector3(ch, .35f, .45f))); trim.Add(Xf(a, 34.95f, 12.1f, new Vector3(ch, .4f, .5f)));
            foreach (float s in new[] { -.25f, .25f }) stoneL.Add(Xf(a, 34f, 12.9f, new Vector3(1.05f, 1.2f, 1.5f), s * ch));
        }
        // pylons on the podium: banners facing the sand; every other one carries a brazier, the rest a stone spire
        var spires = new List<Transform3D>(); var bowls = new List<Transform3D>(); var stands = new List<Transform3D>();
        for (int i = 0; i < 10; i++)
        {
            float a = (i + .5f) / 10f * Tau; if (NearGate(a, .3f)) continue;
            stoneL.Add(Xf(a, 24.6f, 3.9f, new Vector3(1.2f, 8.5f, 1.2f))); trim.Add(Xf(a, 24.6f, 8.2f, new Vector3(1.45f, .25f, 1.45f)));
            banners.Add(Xf(a, 23.96f, 5.9f, new Vector3(1.0f, 3.6f, .06f))); bannerC.Add(crimson);
            iron.Add(Xf(a, 23.9f, 7.75f, new Vector3(1.25f, .09f, .09f)));
            if (blk) continue;   // block pylons carry campfire blocks (their own light)
            if (i % 2 == 0) { bowls.Add(new Transform3D(Basis.Identity, At(a, 24.6f, 8.55f))); Fire(g, At(a, 24.6f, 8.95f), 1.8f, 14f); }
            else spires.Add(new Transform3D(Seg(a) * new Basis(Vector3.Up, Mathf.Pi / 4), At(a, 24.6f, 9.05f)));
        }
        // braziers on the first tier so the bowl reads at night
        for (int i = 0; i < 8; i++)
        {
            float a = (i + .5f) / 8f * Tau; if (NearGate(a, .3f)) continue;
            var bp = At(a, 25.2f, 3.1f);
            stands.Add(new Transform3D(Basis.Identity, bp + new Vector3(0, .35f, 0))); bowls.Add(new Transform3D(Basis.Identity, bp + new Vector3(0, .85f, 0)));
            Fire(g, bp + new Vector3(0, 1.15f, 0), 1.5f, 10f);
        }
        if (!blk) { Solid(g, new Vector3(12, 1.6f, 7), new Vector3(-28, 6.2f, 0), Stone); Solid(g, new Vector3(12, 6, 1.2f), new Vector3(-27.5f, 2.7f, 3f), Stone); Solid(g, new Vector3(12, 6, 1.2f), new Vector3(-27.5f, 2.7f, -3f), Stone); }
        _portcullis = new Node3D { Position = new Vector3(blk ? -23.5f : -24, 5.2f, 0) }; g.AddChild(_portcullis);
        for (int i = -2; i <= 2; i++) Box(_portcullis, new Vector3(.16f, 5f, .16f), new Vector3(0, 2.5f, i * .95f), Metal);
        for (int j = 1; j <= 4; j++) Box(_portcullis, new Vector3(.12f, .12f, 4.6f), new Vector3(0, j * 1.1f, 0), Metal);
        if (Kit.Available) foreach (var (k, lx, lz, yw) in new[] { ("weaponStand", -35f, 6.5f, Mathf.Pi / 2), ("dummy", -36f, -6.5f, Mathf.Pi / 2), ("dummy", -38f, -8f, 1.2f) })
            Kit.Add(g, Kit.Str("props", k), new Vector3(lx, blk ? -.02f : World.GroundAt(ap.X + lx, ap.Z + lz) - H - .02f, lz), yw, 1f, true);
        g.AddChild(new ArenaExit { Position = new Vector3(-22.2f, 0, 0) });   // walk back to the gate and press E to leave a run
        GateBody = new StaticBody3D { Position = new Vector3(-24, 2.5f, 0), CollisionLayer = 1 }; GateBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.5f, 5f, 5f) }, Disabled = true }); g.AddChild(GateBody);
        // gatehouse: two square towers with merlons flank the passage; a pointed arch and a black-and-crimson banner over the gate
        foreach (float s in new[] { -1f, 1f })
        {
            var tsz = new Vector3(4f, 15f, 3.4f); var tp = new Vector3(-25.6f, 7.2f, s * 4.7f);
            stoneL.Add(new Transform3D(Basis.FromScale(tsz), tp)); Shape(tsz, new Transform3D(Basis.Identity, tp));
            trim.Add(new Transform3D(Basis.FromScale(new Vector3(4.3f, .35f, 3.7f)), new Vector3(-25.6f, 14.6f, s * 4.7f)));
            foreach (var (mx, mz) in new[] { (-1.4f, -1.1f), (1.4f, -1.1f), (-1.4f, 1.1f), (1.4f, 1.1f) })
                stoneL.Add(new Transform3D(Basis.FromScale(new Vector3(.9f, 1.1f, .9f)), new Vector3(-25.6f + mx, 15.3f, s * 4.7f + mz)));
            voids.Add(new Transform3D(Basis.FromScale(new Vector3(.08f, 1.8f, .7f)), new Vector3(-23.58f, 10.5f, s * 4.7f)));   // arrow slits
            voids.Add(new Transform3D(new Basis(Vector3.Right, Mathf.Pi / 4) * Basis.FromScale(new Vector3(.08f, .5f, .5f)), new Vector3(-23.58f, 11.4f, s * 4.7f)));
            // the arch: two slanted ribs meeting over the passage (in the YZ plane of the gate face)
            trim.Add(new Transform3D(new Basis(Vector3.Right, s * .72f) * Basis.FromScale(new Vector3(.7f, .45f, 3.3f)), new Vector3(-22.75f, 5.6f, s * 1.25f)));
            Fire(g, new Vector3(-22.6f, 4.4f, s * 3.4f), 1f, 8f);   // gate torches
        }
        var gw = new Vector3(1.2f, 7.4f, 6f); stoneL.Add(new Transform3D(Basis.FromScale(gw), new Vector3(-23.2f, 9.1f, 0))); Shape(gw, new Transform3D(Basis.Identity, new Vector3(-23.2f, 9.1f, 0)));
        float gx = blk ? -.42f : 0f;   // the block lintel's face is at x -23
        banners.Add(new Transform3D(Basis.FromScale(new Vector3(.06f, 3.2f, 2.0f)), new Vector3(-22.55f + gx, 9.8f, 0))); bannerC.Add(black);
        banners.Add(new Transform3D(Basis.FromScale(new Vector3(.07f, 2.4f, 1.2f)), new Vector3(-22.48f + gx, 10.0f, 0))); bannerC.Add(crimson);
        iron.Add(new Transform3D(Basis.FromScale(new Vector3(.1f, .1f, 2.4f)), new Vector3(-22.5f + gx, 11.45f, 0)));
        // royal box across from the gate: a platform on the tiers under a crimson canopy on iron posts
        {
            float a0 = 0f;
            stoneL.Add(Xf(a0, 28.9f, 6.75f, new Vector3(6f, .3f, 4.4f))); trim.Add(Xf(a0, 26.75f, 7.05f, new Vector3(6f, .9f, .3f)));
            foreach (var (tx, rr) in new[] { (-2.8f, 26.9f), (2.8f, 26.9f), (-2.8f, 30.9f), (2.8f, 30.9f) }) iron.Add(Xf(a0, rr, 8.6f, new Vector3(.14f, 3.6f, .14f), tx));
            banners.Add(Xf(a0, 28.9f, 10.45f, new Vector3(6.2f, .14f, 4.6f))); bannerC.Add(crimson);
            banners.Add(Xf(a0, 30.93f, 8.8f, new Vector3(5.2f, 3.2f, .06f))); bannerC.Add(black);
            banners.Add(Xf(a0, 26.7f, 9.95f, new Vector3(6.2f, .9f, .06f))); bannerC.Add(crimson);   // valance
        }
        // crowd: seated hooded figures in muted cloth facing the sand (one MultiMesh + one for the shadowed faces); gaps where the
        // hash says so. R() is drawn exactly as the old crowd did, so every later seeded placement in the world stays the same.
        var crowd = new List<Transform3D>(); var crowdC = new List<Color>(); var faces = new List<Transform3D>();
        var robes = new[] { new Color(.44f, .34f, .25f), new Color(.32f, .3f, .29f), new Color(.55f, .5f, .4f), new Color(.52f, .15f, .12f), new Color(.34f, .37f, .29f), new Color(.42f, .36f, .31f), new Color(.2f, .19f, .21f) };
        for (int ti = 0; ti < tiers.Length; ti++)
        {
            float rr = tiers[ti].r - .2f; int n = 26 + ti * 5;
            for (int i = 0; i < n; i++)
            {
                float a = (i + R(0, .6f)) / n * Tau; if (NearGate(a, .22f)) continue;
                uint hsh = (uint)(i * 73856093 ^ ti * 19349663) * 2654435761u; float hf = (hsh >> 8 & 1023) / 1023f;
                if (hf < .18f || (ti >= 1 && Mathf.Abs(Util.AngDiff(a, 0f)) < .12f)) continue;   // empty seats; the royal box stays clear
                float sc = 1.08f + .14f * ((hsh >> 18 & 255) / 255f);
                float sy = blk ? Mathf.Round(tiers[ti].h - .3f) : tiers[ti].h - .3f;   // block tier tops are whole metres
                var p = At(a, rr, sy); var look = new Vector3(-p.X, 0, -p.Z).Normalized();
                var b = Basis.LookingAt(look, Vector3.Up) * new Basis(Vector3.Up, (hf - .5f) * .6f) * Basis.FromScale(Vector3.One * sc);
                crowd.Add(new Transform3D(b, p)); crowdC.Add(robes[(int)(hsh >> 4) % robes.Length]);
                faces.Add(new Transform3D(b, p));
                if ((hsh >> 12 & 7) == 0) { var p2 = At(a + .55f / rr, rr, sy); crowd.Add(new Transform3D(b, p2)); crowdC.Add(robes[(int)(hsh >> 7) % robes.Length]); faces.Add(new Transform3D(b, p2)); }   // pairs
            }
        }
        var faceMesh = new SphereMesh { Radius = .1f, Height = .22f, RadialSegments = 8, Rings = 4 };
        var faceShift = Transform3D.Identity.Translated(new Vector3(0, .97f, -.085f)) * new Transform3D(Basis.FromScale(new Vector3(.95f, 1f, .45f)), Vector3.Zero);
        for (int i = 0; i < faces.Count; i++) faces[i] = faces[i] * faceShift;
        for (int i = 0; i < crowdC.Count; i++) crowdC[i] = crowdC[i].SrgbToLinear();   // instance colours are linear: the robes stay dark and muted
        ArMulti(g, HoodedFigure(), _arCrowd, crowd, crowdC);
        ArMulti(g, faceMesh, _arFace, faces, null, false);
        // a few real (hooded) people in the royal box: the character model, idle
        if (Avatar.Available && !Net.Dedicated)
        {
            var hood = new Item { Name = "Hood", Slot = "head" }; var robe = new Item { Name = "Robe", Slot = "armor" }; var crown = new Item { Name = "Crown", Slot = "head" };
            var boots = new Item { Name = "Boots", Slot = "feet" }; var surcoat = new Item { Name = "Surcoat", Slot = "armor" };
            foreach (var (tx, lord, seed) in new[] { (-1.6f, false, "arena_a"), (0f, true, "arena_lord"), (1.6f, false, "arena_b") })
            {
                var av = new Avatar { Name = "Spectator" }; g.AddChild(av);
                var p = At(0f, 28.4f, blk ? 7f : 6.9f) + Seg(0f).X * tx; av.Position = p; av.Basis = Basis.LookingAt(new Vector3(-1, 0, 0), Vector3.Up);
                av.Build(new Appearance { Female = seed == "arena_b", Skin = new Color(.86f, .7f, .58f), Hair = new Color(.12f, .09f, .07f), Height = lord ? 1.02f : 1f, Bulk = 1f }, seed);
                Item? Eq(string sl) => sl switch { "head" => lord ? crown : hood, "armor" => lord ? surcoat : robe, "feet" => boots, _ => null };
                av.Dress(Eq, "none", lord ? new Color(.45f, .1f, .1f) : new Color(.2f, .17f, .15f), lord ? new Color(.4f, .12f, .1f) : new Color(.22f, .19f, .17f));
                av.Play("idle");
            }
        }
        if (!blk)
        {
            ArMulti(g, unit, _arStone, stoneL); ArMulti(g, unit, _arBase, baseL); ArMulti(g, unit, _arTrim, trim);
            ArMulti(g, unit, _arVoid, voids, null, false);
            ArMulti(g, new CylinderMesh { TopRadius = .02f, BottomRadius = .75f, Height = 1.5f, RadialSegments = 4, Rings = 1 }, _arTrim, spires);
        }
        ArMulti(g, unit, _arIron, iron); ArMulti(g, unit, _arBanner, banners, bannerC);
        ArMulti(g, new CylinderMesh { TopRadius = .45f, BottomRadius = .2f, Height = .32f, RadialSegments = 10, Rings = 1 }, _arIron, bowls, null, false);
        ArMulti(g, new CylinderMesh { TopRadius = .08f, BottomRadius = .14f, Height = .7f, RadialSegments = 8, Rings = 1 }, _arIron, stands, null, false);
        World.Lamp(g, new Vector3(0, 12, 0), new Color(1f, .8f, .55f), 40f, 1.2f);
        SetGate(true);
    }
    public static void SetGate(bool open)
    {
        if (GateBody != null) GateBody.GetChild<CollisionShape3D>(0).Disabled = open;
        if (_portcullis != null) _portcullis.CreateTween().TweenProperty(_portcullis, "position:y", open ? 5.2f : 0.2f, 0.8f);
    }

    // ---------------------------------------------------------------- dungeon mouths
    static ShaderMaterial PortalMat() => new ShaderMaterial { Shader = PortalShader ??= new Shader { Code = @"
shader_type spatial;
render_mode unshaded, cull_disabled;
uniform vec3 c1 : source_color = vec3(0.35, 0.85, 1.0);
uniform vec3 c2 : source_color = vec3(0.02, 0.02, 0.08);
void fragment(){
  vec2 uv = UV * 2.0 - 1.0; uv.x *= 0.62; float r = length(uv); float a = atan(uv.y, uv.x);
  float sw = sin(a * 3.0 + r * 11.0 - TIME * 2.2) * 0.5 + 0.5;
  float sw2 = sin(a * 5.0 - r * 7.0 + TIME * 1.3) * 0.5 + 0.5;
  float n = (sw * 0.7 + sw2 * 0.3) * smoothstep(1.05, 0.15, r);
  vec3 col = mix(c2, c1, pow(n, 2.2) * 0.85 + smoothstep(0.45, 0.0, r) * 0.25);
  col += c1 * smoothstep(0.75, 1.0, r) * 0.35 * (0.6 + 0.4 * sin(TIME * 3.0));
  ALBEDO = col;
}" } };
    /// <summary>F-19: a dungeon mouth from blocks (StructGen.Mouth) turned to the nearest quarter of the old facing; the portal, light and door spot stay.</summary>
    static void BlockEntrance(Node3D over, Poi p, float yaw)
    {
        int rot = Structures.RotOfYaw(yaw), ox = Mathf.FloorToInt(p.Door.X), oz = Mathf.FloorToInt(p.Door.Z), oy = Fl(ox, oz);
        byte arch = p.Dun == "depths" ? Bk("dark_stone") : p.Dun == "barrow" ? Bk("mossy_bricks") : Bk("stone_bricks");
        Structures.Place(StructGen.Mouth(arch, (uint)Math.Abs(ox * 31 + oz * 17)), ox, oy, oz, rot);
        var g = new Node3D { Position = new Vector3(ox + .5f, oy, oz + .5f), Rotation = new Vector3(0, rot * Mathf.Pi / 2, 0) }; over.AddChild(g);
        g.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(3f, 5f) }, MaterialOverride = PortalMat(), Position = new Vector3(0, 2.5f, -4.4f) });
        World.Lamp(g, new Vector3(0, 3f, 0.5f), new Color(.5f, .88f, 1f), 14f, 1.2f, -1, false);
        g.AddChild(new Door { Key = p.Dun!, Title = p.Name, Position = new Vector3(0, 1f, 3.2f) });
    }
    static void Entrance(Node3D over, Poi p)
    {
        float yaw = Mathf.Atan2(-p.Door.X, -p.Door.Z); var rg = World.Rng((uint)(Math.Abs(p.X * 31 + p.Z * 17)) + 1u); float rr(float a, float b) => a + rg() * (b - a);
        if (Structures.On) { BlockEntrance(over, p, yaw); return; }
        var g = Group(over, p.Door.X, p.Door.Z, yaw);
        for (int i = 0; i < 26; i++)
        {
            float a = (i / 26f - .5f) * Mathf.Pi * .95f, rad = 6 + rr(0, 3.4f), s = 2.2f + rr(0, 3.4f), hgt = rr(0, 1);
            var m = new MeshInstance3D { Mesh = Gfx.Boulder(i), MaterialOverride = Rock, Position = new Vector3(Mathf.Sin(a) * rad * 1.25f, s * .45f + hgt * 3.2f, -3.2f - Mathf.Cos(a) * rad * .8f), Scale = new Vector3(s * (.8f + rr(0, .5f)), s * (.7f + hgt * 1.1f), s * (.8f + rr(0, .5f))), Rotation = new Vector3(rr(0, .5f), rr(0, Tau), rr(0, .5f)) };
            g.AddChild(m);
        }
        var cliff = new StaticBody3D { CollisionLayer = 1 }; cliff.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(14, 10, 5) }, Position = new Vector3(0, 5, -6.5f) }); g.AddChild(cliff);
        foreach (var s in new[] { -1f, 1f })
        {
            for (int k = 0; k < 5; k++) Box(g, new Vector3(1.6f, 1.3f, 1.9f), new Vector3(s * 2.65f, .65f + k * 1.3f, 0), Stone);
            Box(g, new Vector3(2.1f, .5f, 2.4f), new Vector3(s * 2.65f, .25f, 0), Stone); Box(g, new Vector3(2.0f, .35f, 2.2f), new Vector3(s * 2.65f, 6.9f, 0), Stone);
        }
        for (int k = 0; k < 7; k++) { float a = Mathf.Pi * (k + .5f) / 7f; var m = Box(g, new Vector3(1.2f, 1.0f, 1.9f), new Vector3(Mathf.Cos(a) * 2.6f, 6.5f + Mathf.Sin(a) * 1.9f, 0), Stone); m.Rotation = new Vector3(0, 0, a - Mathf.Pi / 2); }
        Box(g, new Vector3(.9f, 1.1f, 2.1f), new Vector3(0, 8.55f, 0), Stone);
        var portalMat = new ShaderMaterial { Shader = PortalShader ??= new Shader { Code = @"
shader_type spatial;
render_mode unshaded, cull_disabled;
uniform vec3 c1 : source_color = vec3(0.35, 0.85, 1.0);
uniform vec3 c2 : source_color = vec3(0.02, 0.02, 0.08);
void fragment(){
  vec2 uv = UV * 2.0 - 1.0; uv.x *= 0.62; float r = length(uv); float a = atan(uv.y, uv.x);
  float sw = sin(a * 3.0 + r * 11.0 - TIME * 2.2) * 0.5 + 0.5;
  float sw2 = sin(a * 5.0 - r * 7.0 + TIME * 1.3) * 0.5 + 0.5;
  float n = (sw * 0.7 + sw2 * 0.3) * smoothstep(1.05, 0.15, r);
  vec3 col = mix(c2, c1, pow(n, 2.2) * 0.85 + smoothstep(0.45, 0.0, r) * 0.25);
  col += c1 * smoothstep(0.75, 1.0, r) * 0.35 * (0.6 + 0.4 * sin(TIME * 3.0));
  ALBEDO = col;
}" } };
        g.AddChild(new MeshInstance3D { Mesh = new QuadMesh { Size = new Vector2(3.9f, 6.4f) }, MaterialOverride = portalMat, Position = new Vector3(0, 3.2f, -.3f) });
        for (int k = 0; k < 4; k++) Box(g, new Vector3(5.4f - k * .4f, .26f, 1.6f), new Vector3(0, .13f + k * .01f, 2.1f + (3 - k) * .55f), Stone);
        foreach (var xx in new[] { -3.9f, 3.9f }) { Cyl(g, .07f, 2.6f, new Vector3(xx, 1.3f, 1.5f), Dark); Fire(g, new Vector3(xx, 2.9f, 1.5f), 1.7f, 16f); }
        World.Lamp(g, new Vector3(0, 3f, 0.5f), new Color(.5f, .88f, 1f), 14f, 1.2f, -1, false);
        // interaction spot just in front of the arch
        var door = new Door { Key = p.Dun!, Title = p.Name, Position = new Vector3(0, 1f, 3.2f) }; g.AddChild(door);
        // keep the entrance walkable: collide only with the pillars
        foreach (var s in new[] { -1f, 1f }) { var b = new StaticBody3D { Position = new Vector3(s * 2.65f, 3.5f, 0), CollisionLayer = 1 }; b.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.8f, 7f, 2f) } }); g.AddChild(b); }
    }

    // ---------------------------------------------------------------- dragon lair, stones, obelisks
    static void Lair(Node3D over)
    {
        var p = World.GetPoi("drake")!; var g = Group(over, p.X, p.Z); var bone = World.Mat(new Color(.85f, .82f, .74f), new Color(.5f, .45f, .55f));
        for (int i = 0; i < 9; i++) { float a = i / 9f * Tau, rr = 11 + (i % 2) * 2; g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = .9f, Height = 7 + (i % 3) * 1.5f, RadialSegments = 6 }, MaterialOverride = bone, Position = new Vector3(Mathf.Cos(a) * rr, 3f, Mathf.Sin(a) * rr), Rotation = new Vector3(R(-.25f, .25f), 0, R(-.25f, .25f)) }); }
        for (int i = 0; i < 7; i++) { float a = R(0, Tau), rr = R(3, 8); g.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = 1f, Height = 1.4f, RadialSegments = 6, Rings = 3 }, MaterialOverride = Gold, Position = new Vector3(Mathf.Cos(a) * rr, .3f, Mathf.Sin(a) * rr), Scale = new Vector3(R(1, 2.2f), R(.8f, 1.2f), R(1, 2.2f)) }); }
        World.Lamp(g, new Vector3(0, 6, 0), new Color(1f, .4f, .15f), 30f, 1.5f, -1, true);
    }

    static void Stones(Node3D over)
    {
        var p = World.GetPoi("stones")!; var g = Group(over, p.X, p.Z);
        // weathered menhirs: noise-displaced, faceted, leaning, with a lintel across two of them
        float lintelY = 99;
        for (int i = 0; i < 9; i++)
        {
            float a = i / 9f * Tau, rr = 6.2f, h = R(3.6f, 5.4f); var s = Solid(g, new Vector3(1.5f, h, .9f), new Vector3(Mathf.Cos(a) * rr, h / 2 - .2f, Mathf.Sin(a) * rr), Rock, -a + Mathf.Pi / 2);
            s.GetChild<MeshInstance3D>(0).Mesh = Gfx.Lumpy(new BoxMesh { Size = new Vector3(1.5f, h, .9f), SubdivideWidth = 2, SubdivideHeight = 5, SubdivideDepth = 1 }, .26f, .9f, (uint)i + 3);
            if (i < 2) lintelY = Mathf.Min(lintelY, h - .2f); else s.Rotation += new Vector3(R(-.06f, .06f), 0, R(-.07f, .07f));
        }
        { float m = Tau / 18f; var mid = new Vector3(Mathf.Cos(m) * 6.2f * Mathf.Cos(m), lintelY + .3f, Mathf.Sin(m) * 6.2f * Mathf.Cos(m));
          g.AddChild(new MeshInstance3D { Mesh = Gfx.Lumpy(new BoxMesh { Size = new Vector3(5.4f, .8f, 1f), SubdivideWidth = 4 }, .1f, 1.3f, 77), MaterialOverride = Rock, Position = mid, Rotation = new Vector3(0, -(m + Mathf.Pi / 2), 0) }); }
        Cyl(g, 1.8f, .5f, new Vector3(0, .2f, 0), Rock, true);
        var rg = new RealmGate { Only = 1 }; g.AddChild(rg);
        var orb = new Spinner { Axis = Vector3.Up, Speed = 1f, Position = new Vector3(0, 1.8f, 0) }; rg.AddChild(orb);
        orb.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .55f, Height = 1.1f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.7f, .4f, 1f), EmissionEnabled = true, Emission = new Color(.7f, .4f, 1f), EmissionEnergyMultiplier = 2.5f } });
        World.Lamp(g, new Vector3(0, 2f, 0), new Color(.7f, .4f, 1f), 20f, 1.4f, 1, false);
    }

    static void Obelisks(Node3D over)
    {
        var gate = new RealmGate { Only = 1 }; over.AddChild(gate);
        var dark = Gfx.Tex(World.Mat(new Color(.2f, .15f, .3f)), "rock", 3f, true, 1.4f); var glow = new StandardMaterial3D { AlbedoColor = new Color(.7f, .4f, 1f), EmissionEnabled = true, Emission = new Color(.7f, .4f, 1f), EmissionEnergyMultiplier = 2f };
        int n = 0, tries = 0;
        while (n < 22 && tries++ < 3000)
        {
            float x = R(-World.Half + 20, World.Half - 20), z = R(-World.Half + 20, World.Half - 20); if (!World.FreeSpot(x, z, 6) || World.GroundAt(x, z) < 1.5f) continue;
            var ob = new Obelisk { Position = World.OnGround(x, z), Rotation = new Vector3(0, R(0, Tau), 0) }; gate.AddChild(ob);
            float h = R(4, 8);
            // faceted rift-stone with a glowing rune seam up one face
            var body = new StaticBody3D { CollisionLayer = 1 }; body.AddChild(new MeshInstance3D { Mesh = Gfx.Lumpy(new BoxMesh { Size = new Vector3(1.1f, h, 1.1f), SubdivideHeight = 5, SubdivideWidth = 1, SubdivideDepth = 1 }, .12f, 1.4f, (uint)n + 40), MaterialOverride = dark, Position = new Vector3(0, h / 2, 0) });
            body.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.12f, h * .7f, .04f) }, MaterialOverride = glow, Position = new Vector3(0, h * .45f, -.58f) }); body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(1.1f, h, 1.1f) }, Position = new Vector3(0, h / 2, 0) }); ob.AddChild(body);
            ob.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(.9f, 1.4f, .9f) }, MaterialOverride = glow, Position = new Vector3(0, h + .9f, 0) });
            if (n % 3 == 0) { var l = new OmniLight3D { LightColor = new Color(.7f, .4f, 1f), OmniRange = 20, LightEnergy = 1.1f, Position = new Vector3(0, h + 1, 0) }; ob.AddChild(l); }
            n++;
        }
    }

    // ---------------------------------------------------------------- chests
    static void Chests(Node3D over)
    {
        int n = 0, tries = 0;
        while (n < 14 && tries++ < 3000)
        {
            float x = R(-World.Half + 30, World.Half - 30), z = R(-World.Half + 30, World.Half - 30), d = new Vector2(x, z).Length();
            if (d < 40 || !World.FreeSpot(x, z, 4) || World.GroundAt(x, z) < 1.5f) continue;
            var c = new Chest { Id = "chest" + n, Level = Game.LevelAt(x, z, 0), Bonus = 1f, Position = World.OnGround(x, z), Rotation = new Vector3(0, R(0, Tau), 0) }; over.AddChild(c); n++;
        }
    }

    // ---------------------------------------------------------------- NPCs
    static void Npcs(Node3D over)
    {
        if (Net.Dedicated) return;   // townsfolk are local to each player's game (talk, shops, quests): the online server needs none
        using var doc = JsonUtil.Load("res://data/quests.json");
        foreach (var n in doc.RootElement.GetProperty("npcs").EnumerateArray())
        {
            var lines = new List<string>(); if (n.TryGetProperty("lines", out var l)) foreach (var s in l.EnumerateArray()) lines.Add(s.GetString() ?? "");
            var qids = new List<string>(); if (n.TryGetProperty("quests", out var q)) foreach (var s in q.EnumerateArray()) qids.Add(s.GetString() ?? "");
            float x = JsonUtil.F(n, "x"), z = JsonUtil.F(n, "z");
            var npc = new Npc { Id = JsonUtil.S(n, "id"), NpcName = JsonUtil.S(n, "name"), Role = JsonUtil.S(n, "role", "talk"), Lines = lines.ToArray(), QuestIds = qids.ToArray(), Position = World.OnGround(x, z) };
            over.AddChild(npc); NpcSchedule.Bind(npc);
        }
        NpcDb.Load(); over.AddChild(new NpcSchedule { Name = "NpcSchedule" });   // F-27: the ~100 people of data/npcs.json and their days
    }

    // ---------------------------------------------------------------- camps and enemies
    static int _spawned;
    static Enemy SpawnOW(Node3D over, string type, float x, float z, int lvl, int pack = 0, bool leader = false, string prefix = "", bool anyRealm = false)
    {
        var e = new Enemy { Type = type, Level = lvl, PackId = pack, AnyRealm = anyRealm, Position = World.OnGround(x, z, 0.3f) };
        if (leader) { e.Leader = true; e.Prefix = prefix == "" ? "" : prefix + " "; e.TierHp = Zones.LeaderHp; e.TierDmg = Zones.LeaderDmg; }
        over.AddChild(e);
        // online server: building hundreds of monsters leaves a lot of temporary model data; collect it as we go so memory never spikes
        if (Net.Dedicated && ++_spawned % 40 == 0) { System.GC.Collect(); System.GC.WaitForPendingFinalizers(); }
        return e;
    }

    static void Spawns(Node3D over)
    {
        // monster packs per level zone (data/zones.json): every zone holds `density` packs (plus Shadow-realm packs on the home island),
        // each a leader and its members in a loose ring around a camp point. Seeded per zone, so every machine places the same monsters
        // in the same order (multiplayer: World.NetEnemies indices match the host's).
        Zones.Load(); _packIds = 0; int total = 0;
        // pack monsters are records (Zones.Slots), made into nodes only while a hero is near their camp (Zones.StreamTick)
        Zones.ResetCamps(); over.AddChild(new CampStreamer { Name = "CampStreamer" });
        var camps = new List<Vector2>(); float sp2 = Zones.PackSpacing * Zones.PackSpacing;
        var clear = new List<(float x, float z, float r)>();   // keep Oakhaven, the arena, dungeon doors, the stones and the lands' towns calm
        foreach (var p in World.Pois)
        {
            if (p.Icon is "village" or "arena" or "city" or "castle" or "stilts") clear.Add((p.X, p.Z, p.R + 22));
            else if (p.Dun != null || p.Icon is "stones" or "dragon") clear.Add((p.X, p.Z, p.R + 10));
        }
        foreach (var L in Lands.All) foreach (var p in L.Places) if (p.Kind is "city" or "village" or "castle" or "stilts") clear.Add((p.X, p.Z, p.R + 45));
        bool Ok(float x, float z)
        {
            foreach (var c in clear) if ((x - c.x) * (x - c.x) + (z - c.z) * (z - c.z) < c.r * c.r) return false;
            foreach (var c in camps) if (c.DistanceSquaredTo(new Vector2(x, z)) < sp2) return false;
            return true;
        }
        foreach (var zn in Zones.All)
        {
            var rg = World.Rng(Zones.Seed(zn.Id)); float Rr(float a, float b) => a + rg() * (b - a);
            bool OnLand(float x, float z)
            {
                if (Zones.At(x, z) != zn || World.GroundAt(x, z) < 1.8f || !Ok(x, z)) return false;
                bool home = Mathf.Abs(x) < World.Half - 20 && Mathf.Abs(z) < World.Half - 20;
                return zn.Home ? home && World.FreeSpot(x, z, 6) : !home;
            }
            for (int realm = 0; realm < (zn.Home ? 2 : 1); realm++)
            {
                var pool = realm == 0 ? zn.Packs : zn.ShadowPacks; int want = realm == 0 ? zn.Density : zn.ShadowDensity;
                if (pool.Length == 0) continue;
                int placed = 0, tries = 0;
                while (placed < want && tries++ < want * 80 && total < Zones.MaxEnemies)
                {
                    float a = Rr(0, Tau), d = zn.R * Mathf.Sqrt(rg()), x = zn.X + Mathf.Cos(a) * d, z = zn.Z + Mathf.Sin(a) * d;
                    if (!OnLand(x, z)) continue;
                    var id = pool[(int)(rg() * pool.Length) % pool.Length]; placed++;
                    if (!Zones.Packs.TryGetValue(id, out var pk)) { GD.PushWarning($"zones.json: zone {zn.Id} names unknown pack '{id}'"); continue; }
                    camps.Add(new Vector2(x, z));
                    total += SpawnPack(over, pk, zn, x, z, realm, rg);
                }
            }
            // a world boss holds a free spot in its zone (the dragon keeps its spire, below)
            if (zn.Boss != "" && zn.Boss != "dragon")
                for (int t = 0; t < 300; t++)
                {
                    float a = Rr(0, Tau), d = zn.R * .6f * Mathf.Sqrt(rg()), x = zn.X + Mathf.Cos(a) * d, z = zn.Z + Mathf.Sin(a) * d;
                    if (!OnLand(x, z)) continue;
                    camps.Add(new Vector2(x, z)); SpawnOW(over, zn.Boss, x, z, zn.MaxLvl + zn.BossLvl, 0, false, "", !zn.Home); break;
                }
        }
        // the Whispering Stones keep their circle of restless dead (one pack)
        var sp = World.GetPoi("stones")!; string[] pack = { "wraith", "wraith", "wraith", "skeleton", "skeleton", "hound", "skeleton" }; int sid = ++_packIds, sl = Game.LevelAt(sp.X, sp.Z, 0);
        for (int i = 0; i < 7; i++) { float a = i / 7f * Tau; SpawnOW(over, pack[i], sp.X + Mathf.Cos(a) * 13, sp.Z + Mathf.Sin(a) * 13, sl + (i % 2), sid); }
        var dr = World.GetPoi("drake")!; var dz = Zones.At(dr.X, dr.Z); int dl = dz != null && dz.Boss == "dragon" ? dz.MaxLvl + dz.BossLvl : 10;
        var dg = SpawnOW(over, "dragon", dr.X + 2, dr.Z + 2, dl); dg.NoLeash = true;
    }

    static int _packIds;
    /// <summary>One pack at a camp point: the leader near the middle (a level or two up, prefixed name, tougher), members in a loose ring. Returns how many monsters it placed.</summary>
    static int SpawnPack(Node3D over, PackDef pk, ZoneDef zn, float x, float z, int realm, Func<float> rg)
    {
        float Rr(float a, float b) => a + rg() * (b - a);
        int add = realm == 1 && zn.Home ? Zones.ShadowAdd : 0, pid = ++_packIds;
        int lvl = Mathf.Clamp(Game.LevelAt(x, z, realm) + (rg() < .3f ? 1 : 0) - (rg() < .2f ? 1 : 0), zn.MinLvl + add, zn.MaxLvl + add);
        bool any = !zn.Home;
        if (pk.Camp)
        {
            Campfire(over, x, z);
            if (zn.Home) { Breakables.Add("barrel", x - 2.4f, z - 2.6f, x); Breakables.Add("crate", x - 1.3f, z - 3.1f, z); Breakables.Add("sack", x + 2.2f, z - 2.4f, x + z); }   // A-36: breakable camp stores
        }
        var types = new List<string>();
        foreach (var m in pk.Members) { int n = Math.Min(m.max, m.min + (int)(rg() * (m.max - m.min + 1))); for (int i = 0; i < n; i++) types.Add(m.type); }
        int count = 0; var camp = Zones.NewCamp(x, z);
        void Slot(string type, float sx, float sz, int sl, bool leader, string prefix) =>
            Zones.AddSlot(camp, new CampSlot { Type = type, Level = sl, PackId = pid, Leader = leader, Prefix = prefix, AnyRealm = any, Pos = World.OnGround(sx, sz, 0.3f) });
        if (pk.Leader != "")
        {
            float la = Rr(0, Tau), lr = pk.Camp ? 2.2f : Rr(0, 1.5f);
            int ll = lvl + Zones.LeaderLvlMin + (int)(rg() * (Zones.LeaderLvlMax - Zones.LeaderLvlMin + 1));
            Slot(pk.Leader, x + Mathf.Cos(la) * lr, z + Mathf.Sin(la) * lr, ll, true, pk.Prefix); count++;
        }
        for (int i = 0; i < types.Count; i++)
        {
            float a = i / (float)types.Count * Tau + Rr(0, .8f), rr = Rr(2.8f, Mathf.Max(3.2f, pk.Spread));
            Slot(types[i], x + Mathf.Cos(a) * rr, z + Mathf.Sin(a) * rr, Math.Min(lvl + (rg() < .25f ? 1 : 0), zn.MaxLvl + add), false, ""); count++;
        }
        return count;
    }

}
