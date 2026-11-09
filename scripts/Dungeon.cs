using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Procedural dungeons: three themes, descending depth, boss in the farthest room.</summary>
public static class Dungeon
{
    public static bool Active;
    public static string Key = ""; public static int Depth = 1;
    public static Node3D? Root; static Enemy? _boss; static int _bossRoom; static List<Room> _rooms = new(); static float _baseLvl; static Vector3 _ret;
    static JsonDocument? _doc;
    const float Tile = 4f, WallH = 6f; const int GW = 40, GH = 40;
    static readonly Vector3 Origin = new Vector3(3000, 0, 3000);
    public class Room { public int X, Z, W, H, Cx, Cz; }

    static JsonElement Theme(string key)
    {
        _doc ??= JsonUtil.Load("res://data/dungeons.json");
        return _doc.RootElement.GetProperty("themes").GetProperty(key);
    }

    public static void Enter(string key, int depth)
    {
        var w = World.I; if (w == null || Player.Instance == null) return;
        var poi = World.GetPoi(key);
        if (!Active) { World.ForceRealm(0); if (Player.Instance.Mounted) Player.Instance.ToggleMount(true); if (poi != null) { var dir = new Vector2(-poi.Door.X, -poi.Door.Z).Normalized(); _ret = World.OnGround(poi.Door.X + dir.X * 7f, poi.Door.Z + dir.Y * 7f, 0.6f); } else _ret = new Vector3(0, 3, 9); }
        else Teardown();
        Key = key; Depth = depth; Active = true;
        Generate(key, depth, poi?.Lvl ?? 3);
        w.SetOverworldActive(false);
        var th = Theme(key);
        w.SetDungeonLook(true, JsonUtil.Hex(th.GetProperty("torch").GetInt32()).Darkened(0.55f).Lerp(new Color(.35f, .38f, .45f), .35f));
        var start = Origin + new Vector3((_rooms[0].Cx + .5f) * Tile, 1f, (_rooms[0].Cz + .5f) * Tile + 3f);
        Player.Instance.Teleport(start, 0f);
        Ui.Banner(th.GetProperty("name").GetString() ?? "Dungeon", "depth " + depth); Ui.Flash(Colors.Black);
        Game.D.Deepest = Math.Max(Game.D.Deepest, depth - 1);
        Game.Save();
    }

    public static void Leave(bool died)
    {
        if (!Active) return;
        Teardown(); Active = false;
        var w = World.I; w?.SetDungeonLook(false); w?.SetOverworldActive(true);
        if (Player.Instance != null) { if (died) Player.Instance.Revive(_ret); else Player.Instance.Teleport(_ret, 0f); }
        if (!died) { Ui.Flash(Colors.White); Ui.Note("You emerge into daylight", new Color(.8f, .75f, .65f)); }
        Game.Save();
    }

    static void Teardown()
    {
        if (Root != null && GodotObject.IsInstanceValid(Root)) Root.QueueFree();
        Root = null; _boss = null; Enemy.CurrentBoss = null;
        foreach (var n in Game.Root!.GetTree().GetNodesInGroup("projectiles")) n.QueueFree();
    }

    static Func<int, int, int> Rng(int seed) { var r = new Random(seed); return (a, b) => r.Next(a, b + 1); }

    static void Generate(string key, int depth, int poiLvl)
    {
        var th = Theme(key); int seed = Math.Abs(key.GetHashCode() * 31 + depth * 977 + (int)(Game.PlayTime));
        var ri = Rng(seed); var rf = new Random(seed + 1); float rnd() => (float)rf.NextDouble();
        var grid = new bool[GW * GH]; _rooms = new();
        for (int t = 0; t < 60 && _rooms.Count < 9; t++)
        {
            int w = ri(5, 9), h = ri(5, 9), x = ri(2, GW - w - 3), z = ri(2, GH - h - 3); bool ok = true;
            foreach (var r in _rooms) if (x < r.X + r.W + 2 && x + w + 2 > r.X && z < r.Z + r.H + 2 && z + h + 2 > r.Z) { ok = false; break; }
            if (!ok) continue; _rooms.Add(new Room { X = x, Z = z, W = w, H = h, Cx = x + w / 2, Cz = z + h / 2 });
        }
        if (_rooms.Count < 3) { _rooms.Clear(); _rooms.Add(new Room { X = 3, Z = 3, W = 7, H = 7, Cx = 6, Cz = 6 }); _rooms.Add(new Room { X = 20, Z = 5, W = 7, H = 7, Cx = 23, Cz = 8 }); _rooms.Add(new Room { X = 20, Z = 24, W = 8, H = 8, Cx = 24, Cz = 28 }); }
        foreach (var r in _rooms) for (int z = r.Z; z < r.Z + r.H; z++) for (int x = r.X; x < r.X + r.W; x++) grid[z * GW + x] = true;
        void Carve(int x, int z) { if (x >= 1 && z >= 1 && x < GW - 1 && z < GH - 1) grid[z * GW + x] = true; }
        for (int i = 1; i < _rooms.Count; i++)
        {
            var a = _rooms[i - 1]; var b = _rooms[i]; int x = a.Cx, z = a.Cz;
            while (x != b.Cx) { Carve(x, z); x += Math.Sign(b.Cx - x); }
            while (z != b.Cz) { Carve(x, z); z += Math.Sign(b.Cz - z); }
            Carve(x, z);
        }
        int bi = 0; float bd = -1; for (int i = 0; i < _rooms.Count; i++) { float d = new Vector2(_rooms[i].Cx - _rooms[0].Cx, _rooms[i].Cz - _rooms[0].Cz).Length(); if (d > bd) { bd = d; bi = i; } }
        _bossRoom = bi;

        Root = new Node3D { Name = "Dungeon", Position = Origin }; Game.Root!.AddChild(Root);
        var cF = JsonUtil.Hex(th.GetProperty("floor").GetInt32()); var cW = JsonUtil.Hex(th.GetProperty("wall").GetInt32());
        // textured surfaces (scripts/Gfx.cs): per-instance colour multiplies a procedural stone/rock/cobble texture with a normal map
        string S(string k, string d) => th.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? d : d;
        StandardMaterial3D TexMat(string kind, float tile, float bump) => Gfx.Tex(new StandardMaterial3D { VertexColorUseAsAlbedo = true, Roughness = .95f }, kind, tile, true, bump);
        var mat = TexMat(S("floorTex", "cobble"), JsonUtil.F(th, "floorTile", 3.5f), 1.5f);
        var wallMat = TexMat(S("wallTex", "stone"), JsonUtil.F(th, "wallTile", 3.2f), 1.6f);
        var ceilMat = TexMat(S("ceilTex", "rock"), 4f, 1.2f);
        var floors = new List<(int, int)>(); var walls = new List<(int, int)>();
        for (int z = 0; z < GH; z++) for (int x = 0; x < GW; x++)
        {
            if (grid[z * GW + x]) { floors.Add((x, z)); continue; }
            bool adj = false; for (int dz = -1; dz <= 1 && !adj; dz++) for (int dx = -1; dx <= 1; dx++) { int nx = x + dx, nz = z + dz; if (nx >= 0 && nz >= 0 && nx < GW && nz < GH && grid[nz * GW + nx]) { adj = true; break; } }
            if (adj) walls.Add((x, z));
        }
        MultiMeshInstance3D Inst(int n, Vector3 size, Material m) => new MultiMeshInstance3D { Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = new BoxMesh { Size = size }, InstanceCount = n }, MaterialOverride = m };
        var fl = Inst(floors.Count, new Vector3(Tile, .6f, Tile), mat); var ce = Inst(floors.Count, new Vector3(Tile, .6f, Tile), ceilMat); var wl = Inst(walls.Count, new Vector3(Tile, WallH + .6f, Tile), wallMat);
        for (int i = 0; i < floors.Count; i++)
        {
            var (x, z) = floors[i]; float v = .85f + rnd() * .3f;
            fl.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3((x + .5f) * Tile, -.3f, (z + .5f) * Tile))); fl.Multimesh.SetInstanceColor(i, cF * v);
            ce.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3((x + .5f) * Tile, WallH + .3f, (z + .5f) * Tile))); ce.Multimesh.SetInstanceColor(i, cW * .55f);
        }
        for (int i = 0; i < walls.Count; i++)
        {
            var (x, z) = walls[i]; wl.Multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, new Vector3((x + .5f) * Tile, (WallH + .6f) / 2 - .3f, (z + .5f) * Tile))); wl.Multimesh.SetInstanceColor(i, cW * (.8f + rnd() * .35f));
        }
        Root.AddChild(fl); Root.AddChild(ce); Root.AddChild(wl);
        // collision: floor slab, ceiling slab, merged wall runs
        var body = new StaticBody3D { CollisionLayer = 1 }; Root.AddChild(body);
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(GW * Tile, 1f, GH * Tile) }, Position = new Vector3(GW * Tile / 2, -.5f, GH * Tile / 2) });
        body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(GW * Tile, 1f, GH * Tile) }, Position = new Vector3(GW * Tile / 2, WallH + .5f, GH * Tile / 2) });
        var wallSet = new HashSet<(int, int)>(walls);
        for (int z = 0; z < GH; z++)
        {
            int x = 0; while (x < GW)
            {
                if (!wallSet.Contains((x, z))) { x++; continue; }
                int x0 = x; while (x < GW && wallSet.Contains((x, z))) x++;
                body.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3((x - x0) * Tile, WallH + 1f, Tile) }, Position = new Vector3((x0 + x) * Tile / 2, (WallH + 1f) / 2, (z + .5f) * Tile) });
            }
        }
        // sealed border so nothing walks off the grid
        // braziers + decor
        var stone = Gfx.Tex(DMat(cW.Lightened(.25f)), "stone", 2.5f); var flame = JsonUtil.Hex(th.GetProperty("flame").GetInt32());
        Dress(th, grid, wallSet, cF, cW, flame, seed);
        for (int ri_ = 0; ri_ < _rooms.Count; ri_++)
        {
            var r = _rooms[ri_]; var cs = new[] { (r.X + .8f, r.Z + .8f), (r.X + r.W - .8f, r.Z + .8f), (r.X + .8f, r.Z + r.H - .8f), (r.X + r.W - .8f, r.Z + r.H - .8f) };
            for (int k = 0; k < 4; k++)
            {
                if ((k + ri_) % 2 != 0) continue; float x = cs[k].Item1 * Tile, z = cs[k].Item2 * Tile;
                var g = new StaticBody3D { Position = new Vector3(x, 0, z), CollisionLayer = 1 }; Root.AddChild(g);
                g.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(.9f, 1.1f, .9f) }, MaterialOverride = stone, Position = new Vector3(0, .55f, 0) });
                g.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(.9f, 1.1f, .9f) }, Position = new Vector3(0, .55f, 0) });
                g.AddChild(new Torch { Position = new Vector3(0, 1.5f, 0), Size = 2f, Range = 24f, Col = flame, Light = ((k + ri_) % 4 == 0) });
            }
            var rr = new Random(ri_ * 77 + depth * 13 + 5);
            if (r.W >= 6 && r.H >= 6) foreach (var c in new[] { (.28, .28), (.72, .28), (.28, .72), (.72, .72) })
            {
                if (rr.NextDouble() < .45) continue; float px = (float)(r.X + r.W * c.Item1) * Tile, pz = (float)(r.Z + r.H * c.Item2) * Tile;
                var pb = new StaticBody3D { Position = new Vector3(px, 0, pz), CollisionLayer = 1 }; Root.AddChild(pb);
                pb.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .5f, BottomRadius = .55f, Height = WallH, RadialSegments = 12 }, MaterialOverride = stone, Position = new Vector3(0, WallH / 2, 0) });
                pb.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(1.4f, .45f, 1.4f) }, MaterialOverride = stone, Position = new Vector3(0, .22f, 0) });
                pb.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .85f, BottomRadius = .55f, Height = .5f, RadialSegments = 12 }, MaterialOverride = stone, Position = new Vector3(0, WallH - .45f, 0) });
                pb.AddChild(new CollisionShape3D { Shape = new CylinderShape3D { Radius = .6f, Height = WallH }, Position = new Vector3(0, WallH / 2, 0) });
            }
        }
        // exit pad
        var er = _rooms[0]; MakePad(new Vector3((er.Cx + .5f) * Tile, 0, (er.Cz + .5f) * Tile), false, new Color(.5f, .88f, 1f));
        // enemies and chests
        _baseLvl = poiLvl + depth - 1; var mobs = new List<string>(); foreach (var m in th.GetProperty("mobs").EnumerateArray()) mobs.Add(m.GetString() ?? "skeleton");
        string bossType = th.GetProperty("boss").GetString() ?? "golem";
        for (int i = 1; i < _rooms.Count; i++)
        {
            var r = _rooms[i]; float cx = (r.Cx + .5f) * Tile, cz = (r.Cz + .5f) * Tile;
            if (i == bi)
            {
                _boss = Spawn(bossType, cx, cz, (int)_baseLvl + 1);
                if (bossType != "golem") for (int k = 0; k < 2; k++) { var mt = mobs.FindAll(m => m != "brute"); Spawn(mt[ri(0, mt.Count - 1)], cx + (k == 1 ? 6 : -6), cz + 4, (int)_baseLvl); }
                continue;
            }
            int n = ri(2, 4) + (depth > 2 ? 1 : 0);
            for (int k = 0; k < n; k++) { float a = k / (float)n * Mathf.Tau, rr2 = ri(2, 5) * 1.1f; Spawn(mobs[ri(0, mobs.Count - 1)], cx + Mathf.Cos(a) * rr2, cz + Mathf.Sin(a) * rr2, (int)_baseLvl + (rnd() < .3f ? 1 : 0)); }
            if (rnd() < .55f) MakeChest((r.X + r.W - 1.2f) * Tile, (r.Z + r.H - 1.2f) * Tile, depth > 1 ? 2 : 1, (int)_baseLvl);
        }
    }

    /// <summary>
    /// Architectural and floor detail, per theme (data/dungeons.json): plinths and cornices along every wall, pilasters, iron sconces with torches,
    /// banners, stone ribs or wooden beams across rooms, glowing ember cracks, and floor clutter (bones, rubble, urns, coffins, mushrooms, crates, crystals).
    /// Static pieces are merged per material, so all of it costs a few dozen draw calls.
    /// </summary>
    static void Dress(JsonElement th, bool[] grid, HashSet<(int, int)> wallSet, Color cF, Color cW, Color flame, int seed)
    {
        var rnd = new Random(seed + 7); float R() => (float)rnd.NextDouble();
        bool Floor(int x, int z) => x >= 0 && z >= 0 && x < GW && z < GH && grid[z * GW + x];
        string S(string k) => th.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        bool B(string k) => th.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
        var deco = new Node3D { Name = "Decor" }; Root!.AddChild(deco);
        void Put(Mesh m, Material mt, Vector3 p, Vector3? rot = null, Vector3? scl = null) => deco.AddChild(new MeshInstance3D { Mesh = m, MaterialOverride = mt, Position = p, Rotation = rot ?? Vector3.Zero, Scale = scl ?? Vector3.One });
        var trim = Gfx.Tex(DMat(cW.Darkened(.15f)), "stone", 1.6f, true, 1.4f);
        var mossHex = S("moss"); var plinthMat = mossHex != "" ? Gfx.Tex(DMat(cW.Darkened(.2f).Lerp(Color.FromHtml(mossHex), .45f)), "rock", 1.5f, true, 1.2f) : trim;
        var iron = DMat(new Color(.18f, .17f, .17f), null, .5f, .7f); var wood = Gfx.Tex(DMat(new Color(.35f, .24f, .14f)), "planks", 1.4f);
        // ---- wall faces that look into a room or corridor
        var faces = new List<(Vector3 p, Vector3 n, float yaw, int h)>();
        foreach (var (x, z) in wallSet)
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                if (Floor(x + dx, z + dz)) faces.Add((new Vector3((x + .5f + dx * .5f) * Tile, 0, (z + .5f + dz * .5f) * Tile), new Vector3(dx, 0, dz), Util.YawTo(dx, dz), Math.Abs(x * 73856093 ^ z * 19349663 ^ (dx + 2) * 83492791 ^ (dz + 2) * 297)));
        int torches = 0;
        foreach (var f in faces)
        {
            var rot = new Vector3(0, f.yaw, 0);
            Put(new BoxMesh { Size = new Vector3(Tile, .5f, .35f) }, plinthMat, f.p + f.n * .17f + Vector3.Up * .25f, rot);
            Put(new BoxMesh { Size = new Vector3(Tile, .32f, .4f) }, trim, f.p + f.n * .2f + Vector3.Up * (WallH - .3f), rot);
            if (f.h % 3 == 0)
            {
                Put(new BoxMesh { Size = new Vector3(.62f, WallH - .6f, .3f) }, trim, f.p + f.n * .15f + Vector3.Up * (WallH / 2), rot);
                Put(new BoxMesh { Size = new Vector3(.82f, .25f, .45f) }, trim, f.p + f.n * .22f + Vector3.Up * (WallH - .58f), rot);
            }
            else if (f.h % 5 == 1 && torches < 46)
            {
                // iron sconce: wall plate, arm, cup, flame (only some cast light, to keep it fast)
                torches++; var at = f.p + f.n * .5f + Vector3.Up * 2.7f;
                Put(new BoxMesh { Size = new Vector3(.22f, .4f, .05f) }, iron, f.p + f.n * .03f + Vector3.Up * 2.55f, rot);
                Put(new BoxMesh { Size = new Vector3(.06f, .06f, .5f) }, iron, f.p + f.n * .28f + Vector3.Up * 2.5f, rot);
                Put(new CylinderMesh { TopRadius = .14f, BottomRadius = .06f, Height = .16f, RadialSegments = 8 }, iron, at - Vector3.Up * .12f);
                Root.AddChild(new Torch { Position = at, Size = 1.1f, Range = 13f, Col = flame, Light = torches % 2 == 0 });
            }
            else if (S("banner") != "" && f.h % 7 == 2)
            {
                var cloth = Gfx.Tex(DMat(Color.FromHtml(S("banner"))), "cloth", .6f);
                Put(new BoxMesh { Size = new Vector3(1.1f, 2.3f, .03f) }, cloth, f.p + f.n * .08f + Vector3.Up * 3.5f, rot);
                Put(new PrismMesh { Size = new Vector3(1.1f, .35f, .03f) }, cloth, f.p + f.n * .08f + Vector3.Up * 2.18f, rot + new Vector3(0, 0, Mathf.Pi));
                Put(new CylinderMesh { TopRadius = .03f, BottomRadius = .03f, Height = 1.3f, RadialSegments = 6 }, DMat(new Color(.8f, .65f, .3f), null, .35f, .8f), f.p + f.n * .12f + Vector3.Up * 4.65f, rot + new Vector3(0, 0, Mathf.Pi / 2));
            }
        }
        // ---- ribs or beams across each room
        foreach (var r in _rooms)
        {
            bool alongX = r.W >= r.H; int len = alongX ? r.W : r.H, span = alongX ? r.H : r.W;
            for (int k = 1; k < len; k += 2)
            {
                float a = (alongX ? r.X : r.Z) + k, mid = (alongX ? r.Z : r.X) + span / 2f;
                var pos = alongX ? new Vector3(a * Tile, WallH - .35f, mid * Tile) : new Vector3(mid * Tile, WallH - .35f, a * Tile);
                var size = alongX ? new Vector3(.55f, .55f, span * Tile) : new Vector3(span * Tile, .55f, .55f);
                if (B("arches")) Put(new BoxMesh { Size = size }, trim, pos);
                if (B("beams"))
                {
                    Put(new BoxMesh { Size = size * new Vector3(alongX ? .75f : 1, .75f, alongX ? 1 : .75f) }, wood, pos);
                    foreach (float sgn in new[] { -1f, 1f }) { var post = alongX ? new Vector3(a * Tile, WallH / 2, (mid + sgn * (span / 2f - .25f)) * Tile) : new Vector3((mid + sgn * (span / 2f - .25f)) * Tile, WallH / 2, a * Tile); Put(new BoxMesh { Size = new Vector3(.4f, WallH, .4f) }, wood, post); }
                }
            }
        }
        // ---- floor: ember cracks and clutter
        var props = new List<string>(); if (th.TryGetProperty("props", out var pj)) foreach (var p in pj.EnumerateArray()) props.Add(p.GetString() ?? "");
        var bone = DMat(new Color(.86f, .82f, .72f)); var dirt = Gfx.Tex(DMat(cF.Lightened(.15f)), "rock", 2f);
        var ember = new StandardMaterial3D { AlbedoColor = new Color(1f, .45f, .1f), EmissionEnabled = true, Emission = new Color(1f, .35f, .05f), EmissionEnergyMultiplier = 3f };
        var clay = Gfx.Tex(DMat(new Color(.55f, .34f, .22f)), "noise", .8f); var shroom = new StandardMaterial3D { AlbedoColor = new Color(.5f, .85f, .7f), EmissionEnabled = true, Emission = new Color(.3f, .9f, .7f), EmissionEnergyMultiplier = 1.2f };
        var crys = new StandardMaterial3D { AlbedoColor = new Color(1f, .4f, .25f), EmissionEnabled = true, Emission = new Color(1f, .3f, .15f), EmissionEnergyMultiplier = 2f, Roughness = .2f };
        if (B("embers"))
            for (int z = 0; z < GH; z++) for (int x = 0; x < GW; x++)
                if (Floor(x, z) && R() < .3f) for (int k = 0; k < 3; k++) Put(new BoxMesh { Size = new Vector3(.9f + R() * 1.6f, .03f, .07f + R() * .06f) }, ember, new Vector3((x + .2f + R() * .6f) * Tile, .01f, (z + .2f + R() * .6f) * Tile), new Vector3(0, R() * Mathf.Tau, 0));
        foreach (var r in _rooms)
        {
            int n = 6 + (int)(R() * 6);
            for (int i = 0; i < n && props.Count > 0; i++)
            {
                // hug the walls: clutter collects at the edges of rooms
                bool edgeX = R() < .5f; float u = R(), side = R() < .5f ? .12f : .88f;
                float fx = r.X + (edgeX ? side : .1f + u * .8f) * r.W, fz = r.Z + (edgeX ? .1f + u * .8f : side) * r.H; var p = new Vector3(fx * Tile, 0, fz * Tile); float yaw = R() * Mathf.Tau;
                switch (props[(int)(R() * props.Count) % props.Count])
                {
                    case "bones":
                        Put(new SphereMesh { Radius = .14f, Height = .26f, RadialSegments = 8, Rings = 4 }, bone, p + Vector3.Up * .12f);
                        for (int k = 0; k < 3; k++) Put(new CapsuleMesh { Radius = .04f, Height = .5f, RadialSegments = 6, Rings = 1 }, bone, p + new Vector3(R() - .5f, .04f, R() - .5f), new Vector3(Mathf.Pi / 2, R() * Mathf.Tau, 0)); break;
                    case "rubble":
                        for (int k = 0; k < 4; k++) { float s = .2f + R() * .45f; Put(Gfx.Boulder(k), dirt, p + new Vector3(R() * 1.4f - .7f, s * .3f, R() * 1.4f - .7f), new Vector3(R(), R() * 6, R()), new Vector3(s, s * .7f, s)); } break;
                    case "urn":
                        Put(new CylinderMesh { TopRadius = .2f, BottomRadius = .17f, Height = .55f, RadialSegments = 12 }, clay, p + Vector3.Up * .28f);
                        Put(new SphereMesh { Radius = .26f, Height = .4f, RadialSegments = 12, Rings = 5 }, clay, p + Vector3.Up * .3f);
                        Put(new CylinderMesh { TopRadius = .14f, BottomRadius = .12f, Height = .12f, RadialSegments = 12 }, clay, p + Vector3.Up * .6f); break;
                    case "coffin":
                        Put(new BoxMesh { Size = new Vector3(.8f, .45f, 2f) }, wood, p + Vector3.Up * .23f, new Vector3(0, yaw, 0));
                        Put(new BoxMesh { Size = new Vector3(.9f, .08f, 2.1f) }, wood, p + Vector3.Up * .5f, new Vector3(.05f, yaw, .08f)); break;
                    case "mushroom":
                        for (int k = 0; k < 4; k++) { var q = p + new Vector3(R() - .5f, 0, R() - .5f); float s = .5f + R(); Put(new CylinderMesh { TopRadius = .03f * s, BottomRadius = .05f * s, Height = .3f * s, RadialSegments = 6 }, bone, q + Vector3.Up * .15f * s); Put(new SphereMesh { Radius = .14f * s, Height = .14f * s, RadialSegments = 8, Rings = 3, IsHemisphere = true }, shroom, q + Vector3.Up * .3f * s); } break;
                    case "crate":
                        Put(new BoxMesh { Size = new Vector3(.9f, .9f, .9f) }, wood, p + Vector3.Up * .45f, new Vector3(0, yaw, 0));
                        if (R() < .5f) Put(new BoxMesh { Size = new Vector3(.6f, .6f, .6f) }, wood, p + new Vector3(.2f, 1.2f, .1f), new Vector3(0, yaw + .4f, 0)); break;
                    case "crystal":
                        for (int k = 0; k < 4; k++) { float s = .4f + R() * .8f; Put(new CylinderMesh { TopRadius = 0, BottomRadius = .12f * s, Height = .9f * s, RadialSegments = 5 }, crys, p + new Vector3(R() * .6f - .3f, .4f * s, R() * .6f - .3f), new Vector3(R() * .6f - .3f, R() * 6, R() * .6f - .3f)); } break;
                }
            }
        }
        Gfx.MergeChildren(deco);
    }

    /// <summary>Dungeon material: not registered for the realm colour shift (dungeons are always in the Light), so it does not pile up in World's list.</summary>
    static StandardMaterial3D DMat(Color c, Color? _ = null, float rough = .9f, float metal = 0f) => new() { AlbedoColor = c, Roughness = rough, Metallic = metal };

    static Enemy Spawn(string type, float x, float z, int lvl)
    {
        var e = new Enemy { Type = type, Level = Math.Max(1, lvl), FromDungeon = true, NoLeash = true, AnyRealm = true, Position = new Vector3(x, 0.4f, z) };
        Root!.AddChild(e); return e;
    }
    static void MakeChest(float x, float z, float bonus, int lvl) => Root!.AddChild(new Chest { Id = "dc" + GD.Randi(), Level = lvl, Bonus = bonus, Position = new Vector3(x, 0, z) });

    static Pad MakePad(Vector3 pos, bool down, Color col)
    {
        var p = new Pad { Down = down, Position = pos }; Root!.AddChild(p);
        p.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = 1.4f, BottomRadius = 1.4f, Height = .12f }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(col, .6f), Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded }, Position = new Vector3(0, .06f, 0) });
        p.AddChild(new OmniLight3D { LightColor = col, OmniRange = 10, LightEnergy = 1.3f, Position = new Vector3(0, 1.2f, 0) });
        return p;
    }

    public static void OnBossDown(Enemy e)
    {
        if (!Active || e != _boss) return;
        Game.D.Deepest = Math.Max(Game.D.Deepest, Depth);
        var b = _rooms[_bossRoom]; float bx = (b.Cx + .5f) * Tile, bz = (b.Cz + .5f) * Tile;
        MakePad(new Vector3(bx, 0, bz + 4f), true, new Color(1f, .75f, .38f));
        MakeChest(bx - 5, bz - 4, 2, (int)_baseLvl + 1); MakeChest(bx + 5, bz - 4, 2, (int)_baseLvl + 1);
        Ui.Note("A way down has opened. Descend for better loot and tougher foes.", new Color(1f, .75f, .38f));
        Game.Save();
    }
}

/// <summary>Glowing floor pad: leave dungeon (up) or descend (down).</summary>
public partial class Pad : Node3D, IInteractable
{
    public bool Down;
    public string Prompt => Down ? "Descend deeper" : "Leave dungeon";
    public bool CanInteract => true;
    public override void _Ready() { AddToGroup("interact"); }
    public void Interact() { if (Down) Dungeon.Enter(Dungeon.Key, Dungeon.Depth + 1); else Dungeon.Leave(false); }
}
