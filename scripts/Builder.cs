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
        Wall = World.Mat(new Color(.79f, .71f, .56f), new Color(.29f, .26f, .35f)); Roof = World.Mat(new Color(.55f, .23f, .16f), new Color(.1f, .08f, .15f)); RoofB = World.Mat(new Color(.33f, .38f, .44f), new Color(.1f, .09f, .18f));
        Wood = World.Mat(new Color(.42f, .29f, .17f), new Color(.17f, .14f, .22f)); Stone = World.Mat(new Color(.55f, .54f, .52f), new Color(.23f, .2f, .31f), 1f);
        Dark = World.Mat(new Color(.23f, .16f, .1f), new Color(.1f, .07f, .14f)); Cloth = World.Mat(new Color(.69f, .23f, .23f), new Color(.29f, .11f, .36f)); Hay = World.Mat(new Color(.79f, .66f, .29f), new Color(.29f, .23f, .35f));
        Sand = World.Mat(new Color(.84f, .75f, .53f), new Color(.23f, .18f, .28f), 1f); Rock = World.Mat(new Color(.54f, .54f, .52f), new Color(.23f, .19f, .28f), 1f); Gold = World.Mat(new Color(.88f, .69f, .24f), new Color(.54f, .35f, .66f), .4f, .8f);
        Metal = World.Mat(new Color(.48f, .5f, .53f), new Color(.29f, .27f, .38f), .4f, .7f); Crim = World.Mat(new Color(.6f, .12f, .16f), new Color(.23f, .08f, .35f)); Beam = World.Mat(new Color(.29f, .21f, .14f), new Color(.1f, .08f, .13f));
        // procedural surface detail (scripts/Gfx.cs): plaster, stone courses, planks, wood grain, shingles, rock, sand, cloth
        Gfx.Tex(Wall, "plaster", 2.5f, true, .6f); Gfx.Tex(Stone, "stone", 2.6f); Gfx.Tex(Wood, "planks", 1.6f); Gfx.Tex(Beam, "grain", 1.2f); Gfx.Tex(Dark, "grain", 1.2f);
        Gfx.Tex(Roof, "shingle", 2.4f, false, 1.4f); Gfx.Tex(RoofB, "shingle", 2.4f, false, 1.4f); Gfx.Tex(Rock, "rock", 4f, true, 1.2f); Gfx.Tex(Sand, "noise", 3f);
        Gfx.Tex(Cloth, "cloth", .8f); Gfx.Tex(Hay, "grain", .8f); Gfx.Tex(Crim, "cloth", .8f);
        Post = World.Mat(new Color(.2f, .15f, .1f), new Color(.1f, .07f, .14f)); Gfx.Tex(Post, "grain", 1.2f);
        Post.DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha; Post.DistanceFadeMinDistance = 1.5f; Post.DistanceFadeMaxDistance = 4.5f;
        Village(over); Arena(over); foreach (var p in World.Pois) if (p.Dun != null) Entrance(over, p);
        Lair(over); Stones(over); Obelisks(over); Chests(over); Npcs(over); Spawns(over);
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
    static void House(Node parent, float x, float z, float w, float d, float h, float rot)
    {
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
        foreach (var a in hs) House(over, a.x, a.z, a.w, a.d, a.h, a.r);
        // plaza cobbles
        var cob = World.Mat(new Color(.62f, .59f, .55f), new Color(.2f, .17f, .25f), 1f); Gfx.Tex(cob, "cobble", 3.2f, true, 1.6f);
        over.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 9f, BottomRadius = 9f, Height = .06f, RadialSegments = 32 }, MaterialOverride = cob, Position = World.OnGround(0, 0, .03f) });
        // well
        var well = Group(over, 0, 0); Cyl(well, 1.5f, .9f, new Vector3(0, .45f, 0), Stone, true); Cyl(well, 1.2f, .05f, new Vector3(0, .86f, 0), World.Mat(new Color(.04f, .1f, .16f)));
        Box(well, new Vector3(.2f, 2.4f, .2f), new Vector3(-1.2f, 1.5f, 0), Wood); Box(well, new Vector3(.2f, 2.4f, .2f), new Vector3(1.2f, 1.5f, 0), Wood); Box(well, new Vector3(3.2f, .25f, 1.8f), new Vector3(0, 2.7f, 0), Roof);
        Campfire(over, 7, 5);
        // the hero's home: the cottage north of the plaza (at -7, 19, door facing the plaza); storage chest beside the door
        over.AddChild(new StashChest { Position = World.OnGround(-4.6f, 15.3f), Rotation = new Vector3(0, Mathf.Pi, 0) });
        over.AddChild(new PartyChestNode { Position = World.OnGround(-2.1f, 15.1f), Rotation = new Vector3(0, Mathf.Pi, 0) });
        var st = Group(over, -9, 8); foreach (var s in new[] { (-1.8f, -1f), (1.8f, -1f), (-1.8f, 1f), (1.8f, 1f) }) Box(st, new Vector3(.2f, 2.8f, .2f), new Vector3(s.Item1, 1.4f, s.Item2), Wood);
        Box(st, new Vector3(4.4f, .15f, 3f), new Vector3(0, 2.85f, 0), Cloth); Solid(st, new Vector3(3.6f, .9f, .9f), new Vector3(0, .5f, .3f), Wood); Box(st, new Vector3(3.6f, .1f, .9f), new Vector3(0, .97f, .3f), Cloth);
        var fg = Group(over, 24, 2); Solid(fg, new Vector3(2.4f, 1.3f, 2f), new Vector3(0, .65f, 0), Stone); Box(fg, new Vector3(1.1f, .6f, .6f), new Vector3(2, .8f, 0), Metal); Box(fg, new Vector3(.5f, 3f, .5f), new Vector3(-.6f, 2.4f, -.5f), Stone); Fire(fg, new Vector3(.2f, 1.45f, 0), 1.4f, 16f);
        var wm = Group(over, 26, -18); var tower = new StaticBody3D { CollisionLayer = 1 }; tower.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.7f, BottomRadius = 2.7f, Height = 9f, RadialSegments = 10 }, MaterialOverride = Wall, Position = new Vector3(0, 4.5f, 0) });
        tower.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = 2.4f, Height = 9f }, Position = new Vector3(0, 4.5f, 0) }); wm.AddChild(tower);
        wm.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 3.1f, Height = 3f, RadialSegments = 10 }, MaterialOverride = Roof, Position = new Vector3(0, 10.5f, 0) });
        var hub = new Spinner { Axis = Vector3.Forward, Speed = .45f, Position = new Vector3(0, 8f, 2.4f) }; wm.AddChild(hub);
        for (int i = 0; i < 4; i++) { var arm = new Node3D { Rotation = new Vector3(0, 0, i * P / 2) }; arm.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.5f, 6f, .12f) }, MaterialOverride = Wood, Position = new Vector3(0, 3f, 0) }); hub.AddChild(arm); }
        foreach (var p in new[] { (-11f, 0f), (11, 0), (0, -12), (3.5f, 13), (-22, -2), (22, 12), (30, 10), (-30, 6) }) Lamp(over, p.Item1, p.Item2);
        foreach (var p in new[] { (-20f, -14f), (21, -22), (-22, 16), (15, 25) }) { Crate(over, p.Item1, p.Item2, 1.1f); Crate(over, p.Item1 + 1.3f, p.Item2 + .4f, .8f); Barrel(over, p.Item1 - 1, p.Item2 + 1); }
        for (int i = 0; i < 4; i++) { var hb = Group(over, -24 + i * 1.8f, 20 + (i % 2) * .8f, 0, .4f); hb.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .9f, BottomRadius = .9f, Height = .8f, RadialSegments = 8 }, MaterialOverride = Hay }); }
        for (int i = 0; i < 10; i++) foreach (var zz in new[] { -4.2f, 4.2f }) Box(over, new Vector3(.15f, 1.1f, .15f), World.OnGround(30 + i * 1.6f, 10 + zz, .5f), Wood);
    }
    static void Lamp(Node parent, float x, float z)
    {
        // post fades out (dithered) when the camera is right next to it, so lamps never hide the hero
        var g = Group(parent, x, z); Box(g, new Vector3(.16f, 3.0f, .16f), new Vector3(0, 1.5f, 0), Post); Box(g, new Vector3(.34f, .3f, .34f), new Vector3(0, .15f, 0), Post);
        Box(g, new Vector3(.5f, .07f, .07f), new Vector3(.18f, 2.95f, 0), Post);
        g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .12f, BottomRadius = .2f, Height = .42f, RadialSegments = 6 }, MaterialOverride = World.LampMat, Position = new Vector3(.38f, 2.68f, 0) });
        g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .02f, BottomRadius = .26f, Height = .18f, RadialSegments = 6 }, MaterialOverride = Post, Position = new Vector3(.38f, 2.97f, 0) });
        World.Lamp(g, new Vector3(.38f, 2.6f, 0), new Color(1f, .75f, .4f), 14f, 1.6f);
    }
    static void Campfire(Node parent, float x, float z)
    {
        var g = Group(parent, x, z); for (int i = 0; i < 6; i++) { float a = i / 6f * Tau; g.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .22f, Height = .3f, RadialSegments = 6, Rings = 3 }, MaterialOverride = Stone, Position = new Vector3(Mathf.Cos(a) * .6f, .08f, Mathf.Sin(a) * .6f) }); }
        for (int i = 0; i < 3; i++) g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .08f, BottomRadius = .08f, Height = .9f }, MaterialOverride = Wood, Position = new Vector3(0, .25f, 0), Rotation = new Vector3(0, i * 2.1f, 1.2f) });
        Fire(g, new Vector3(0, .5f, 0), 1.2f, 14f);
    }
    static void Crate(Node parent, float x, float z, float s) { Solid(parent, new Vector3(s, s, s), World.OnGround(x, z, s / 2), Wood, R(0, 3)); }
    static void Barrel(Node parent, float x, float z) { var b = new StaticBody3D { Position = World.OnGround(x, z, .47f), CollisionLayer = 1 }; b.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .45f, BottomRadius = .45f, Height = .95f, RadialSegments = 10 }, MaterialOverride = Wood }); b.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .45f, Height = .95f } }); parent.AddChild(b); }

    // ---------------------------------------------------------------- Arena
    public static StaticBody3D? GateBody; static Node3D? _portcullis;
    public static Vector3 ArenaPos => new Vector3(World.GetPoi("arena")!.X, 0, World.GetPoi("arena")!.Z);
    public static float ArenaH => World.GroundAt(ArenaPos.X, ArenaPos.Z);
    static void Arena(Node3D over)
    {
        var ap = World.GetPoi("arena")!; float H = World.GroundAt(ap.X, ap.Z);
        var g = new Node3D { Position = new Vector3(ap.X, H, ap.Z) }; over.AddChild(g);
        g.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 22.6f, BottomRadius = 22.6f, Height = .1f, RadialSegments = 40 }, MaterialOverride = Sand, Position = new Vector3(0, .02f, 0) });
        const int SEG = 44; float gateA = Mathf.Pi; (float r, float h)[] tiers = { (25.6f, 3.4f), (27.8f, 5.2f), (30f, 7f), (32.2f, 9f) };
        foreach (var t in tiers) for (int i = 0; i < SEG; i++)
        {
            float a = i / (float)SEG * Tau; if (Mathf.Abs(Util.AngDiff(a, gateA)) < .17f) continue;
            Solid(g, new Vector3(Tau * t.r / SEG + .35f, t.h, 2.3f), new Vector3(Mathf.Cos(a) * t.r, t.h / 2 - .3f, Mathf.Sin(a) * t.r), Stone, -a + Mathf.Pi / 2);
        }
        for (int i = 0; i < SEG; i++) { float a = i / (float)SEG * Tau; if (Mathf.Abs(Util.AngDiff(a, gateA)) < .12f) continue; Solid(g, new Vector3(Tau * 23.6f / SEG + .3f, 3f, 1.4f), new Vector3(Mathf.Cos(a) * 23.6f, 1.2f, Mathf.Sin(a) * 23.6f), Crim, -a + Mathf.Pi / 2); }
        for (int i = 0; i < 10; i++)
        {
            float a = (i + .5f) / 10f * Tau; if (Mathf.Abs(Util.AngDiff(a, gateA)) < .3f) continue;
            Box(g, new Vector3(1.2f, 8.5f, 1.2f), new Vector3(Mathf.Cos(a) * 24.6f, 3.9f, Mathf.Sin(a) * 24.6f), Stone);
            Box(g, new Vector3(1.1f, 3.4f, .08f), new Vector3(Mathf.Cos(a) * 23.8f, 6.2f, Mathf.Sin(a) * 23.8f), Crim, -a + Mathf.Pi / 2);
            if (i % 2 == 0) Fire(g, new Vector3(Mathf.Cos(a) * 23.6f, 8.9f, Mathf.Sin(a) * 23.6f), 1.8f, 24f);
        }
        Solid(g, new Vector3(12, 1.6f, 7), new Vector3(-28, 6.2f, 0), Stone); Solid(g, new Vector3(12, 6, 1.2f), new Vector3(-27.5f, 2.7f, 3f), Stone); Solid(g, new Vector3(12, 6, 1.2f), new Vector3(-27.5f, 2.7f, -3f), Stone);
        _portcullis = new Node3D { Position = new Vector3(-24, 5.2f, 0) }; g.AddChild(_portcullis);
        for (int i = -2; i <= 2; i++) Box(_portcullis, new Vector3(.16f, 5f, .16f), new Vector3(0, 2.5f, i * .95f), Metal);
        for (int j = 1; j <= 4; j++) Box(_portcullis, new Vector3(.12f, .12f, 4.6f), new Vector3(0, j * 1.1f, 0), Metal);
        GateBody = new StaticBody3D { Position = new Vector3(-24, 2.5f, 0), CollisionLayer = 1 }; GateBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.5f, 5f, 5f) }, Disabled = true }); g.AddChild(GateBody);
        // crowd (static)
        var cm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new CapsuleMesh { Radius = .28f, Height = 1.0f, RadialSegments = 6, Rings = 2 } };
        var tf = new List<Transform3D>(); var tc = new[] { new Color(.69f, .23f, .23f), new Color(.23f, .35f, .69f), new Color(.29f, .54f, .29f), new Color(.79f, .64f, .23f), new Color(.54f, .29f, .69f), new Color(.88f, .88f, .88f) };
        for (int ti = 0; ti < tiers.Length; ti++) { float rr = tiers[ti].r + .25f; int n = 26 + ti * 5; for (int i = 0; i < n; i++) { float a = (i + R(0, .6f)) / n * Tau; if (Mathf.Abs(Util.AngDiff(a, gateA)) < .22f) continue; tf.Add(new Transform3D(Basis.Identity, new Vector3(Mathf.Cos(a) * rr, tiers[ti].h + .35f, Mathf.Sin(a) * rr))); } }
        cm.InstanceCount = tf.Count; for (int i = 0; i < tf.Count; i++) { cm.SetInstanceTransform(i, tf[i]); cm.SetInstanceColor(i, tc[i % tc.Length]); }
        g.AddChild(new MultiMeshInstance3D { Multimesh = cm, MaterialOverride = new StandardMaterial3D { VertexColorUseAsAlbedo = true } });
        World.Lamp(g, new Vector3(0, 12, 0), new Color(1f, .8f, .55f), 40f, 1.2f);
        SetGate(true);
    }
    public static void SetGate(bool open)
    {
        if (GateBody != null) GateBody.GetChild<CollisionShape3D>(0).Disabled = open;
        if (_portcullis != null) _portcullis.CreateTween().TweenProperty(_portcullis, "position:y", open ? 5.2f : 0.2f, 0.8f);
    }

    // ---------------------------------------------------------------- dungeon mouths
    static void Entrance(Node3D over, Poi p)
    {
        float yaw = Mathf.Atan2(-p.Door.X, -p.Door.Z); var rg = World.Rng((uint)(Math.Abs(p.X * 31 + p.Z * 17)) + 1u); float rr(float a, float b) => a + rg() * (b - a);
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
        using var doc = JsonUtil.Load("res://data/quests.json");
        foreach (var n in doc.RootElement.GetProperty("npcs").EnumerateArray())
        {
            var lines = new List<string>(); if (n.TryGetProperty("lines", out var l)) foreach (var s in l.EnumerateArray()) lines.Add(s.GetString() ?? "");
            var qids = new List<string>(); if (n.TryGetProperty("quests", out var q)) foreach (var s in q.EnumerateArray()) qids.Add(s.GetString() ?? "");
            float x = JsonUtil.F(n, "x"), z = JsonUtil.F(n, "z");
            over.AddChild(new Npc { Id = JsonUtil.S(n, "id"), NpcName = JsonUtil.S(n, "name"), Role = JsonUtil.S(n, "role", "talk"), Lines = lines.ToArray(), QuestIds = qids.ToArray(), Position = World.OnGround(x, z) });
        }
    }

    // ---------------------------------------------------------------- camps and enemies
    static Enemy SpawnOW(Node3D over, string type, float x, float z, int lvl) { var e = new Enemy { Type = type, Level = lvl, Position = World.OnGround(x, z, 0.3f) }; over.AddChild(e); return e; }

    static void Spawns(Node3D over)
    {
        string[][] k0 = { new[] { "bandit", "bandit", "bandit", "archer" }, new[] { "goblin", "goblin", "goblin", "goblin" }, new[] { "wolf", "wolf", "wolf", "wolf" }, new[] { "brute", "goblin", "goblin" }, new[] { "bandit", "archer", "archer", "brute" } };
        string[][] k1 = { new[] { "skeleton", "skeleton", "skeleton", "wraith" }, new[] { "hound", "hound", "hound" }, new[] { "wraith", "wraith", "wraith" }, new[] { "revenant", "skeleton", "skeleton" }, new[] { "skeleton", "wraith", "hound", "hound" } };
        int[] cnt = { 0, 0 }, need = { 15, 13 }; int tries = 0; var camps = new List<(float x, float z, int realm, string[] kind)>();
        while ((cnt[0] < need[0] || cnt[1] < need[1]) && tries++ < 5000)
        {
            float x = R(-World.Half + 36, World.Half - 36), z = R(-World.Half + 36, World.Half - 36), d = new Vector2(x, z).Length();
            if (d < 62 || !World.FreeSpot(x, z, 8) || World.GroundAt(x, z) < 1.8f) continue;
            bool bad = false; foreach (var c in camps) if (new Vector2(c.x - x, c.z - z).Length() < 48) { bad = true; break; } if (bad) continue;
            int r = cnt[0] <= cnt[1] * 1.15f ? 0 : 1; if (cnt[r] >= need[r]) continue;
            var kinds = r == 1 ? k1 : k0; var kind = kinds[Math.Min(kinds.Length - 1, ((int)(d / 70f + R(0, 2.2f))) % kinds.Length)];
            camps.Add((x, z, r, kind)); cnt[r]++;
        }
        foreach (var c in camps)
        {
            int lvl = Game.LevelAt(c.x, c.z, c.realm);
            if (c.realm == 0) { Campfire(over, c.x, c.z); var tent = Group(over, c.x + 3, c.z + 2, R(0, Tau)); tent.AddChild(new MeshInstance3D { Mesh = new PrismMesh { Size = new Vector3(3, 2.2f, 3.4f) }, MaterialOverride = Cloth, Position = new Vector3(0, 1.1f, 0) }); }
            for (int i = 0; i < c.kind.Length; i++) { float a = i / (float)c.kind.Length * Tau + R(0, 1), rr = R(2.5f, 6f); SpawnOW(over, c.kind[i], c.x + Mathf.Cos(a) * rr, c.z + Mathf.Sin(a) * rr, lvl + (i == 0 && c.kind.Length < 4 ? 1 : 0)); }
        }
        for (int i = 0; i < 5; i++) SpawnOW(over, "wolf", -54 + R(-6, 6), 30 + R(-6, 6), 1 + (i % 2));
        var sp = World.GetPoi("stones")!; string[] pack = { "wraith", "wraith", "wraith", "skeleton", "skeleton", "hound", "skeleton" };
        for (int i = 0; i < 7; i++) { float a = i / 7f * Tau; SpawnOW(over, pack[i], sp.X + Mathf.Cos(a) * 13, sp.Z + Mathf.Sin(a) * 13, 2 + (i % 2)); }
        for (int i = 0; i < 3; i++) SpawnOW(over, "bandit", 48 + R(-4, 4), -46 + R(-4, 4), 2);
        var dr = World.GetPoi("drake")!; var dg = SpawnOW(over, "dragon", dr.X + 2, dr.Z + 2, 10); dg.NoLeash = true;
    }
}
