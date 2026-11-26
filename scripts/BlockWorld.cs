using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// The block world (data/blocks.json, docs/DESIGN.md "Block world"). Around the player the ground is made of 1 m blocks in 16 x 16 chunks
/// (generated and meshed on worker threads, a few meshes put in the scene per frame); farther out the smooth heightfield is drawn as the
/// low-detail far ground. Where the blocks are ready, the smooth ground is cut away (a shader rectangle, global uniform vox_rect) and its
/// collision tiles are switched off, so the two never overlap. Dungeon floors are block areas of their own.
/// Changes are kept per area as a diff from the generated world and saved with the hero (PlayerStats.BlockDiffs).
/// The online server has no block world (it never draws and keeps no ground near players).
/// </summary>
public partial class BlockWorld : Node3D
{
    static int _tuftTotal, _tuftChunks;
    static ShaderMaterial? TuftMat;   // grass tufts on block tops (Flora.GrassMat copy with vox_cut = 0)
    public static BlockWorld? I;
    public static bool Enabled => I != null && !Net.Dedicated;
    public BlockArea? Area;                         // the area the player is in (null in a raid lair)
    BlockArea? _over;                               // the overworld, kept while in a dungeon so its changes are not lost
    Material?[] _slots = Array.Empty<Material?>();
    int _radius, _perFrame; float _tick;
    (int cx, int cz) _center = (int.MinValue, 0);
    int _cut = -1;                                  // chunks around the centre that are all shown (the cut-out square), -1 none
    readonly HashSet<BlockChunk> _dirty = new();
    /// <summary>A bot or a test can wait for the ground around a point to be ready.</summary>
    public bool ReadyAround(Vector3 p, int ring = 1)
    {
        if (Area == null) return false;
        int cx = Area.CFloor(Mathf.FloorToInt(p.X)), cz = Area.CFloor(Mathf.FloorToInt(p.Z));
        for (int dz = -ring; dz <= ring; dz++) for (int dx = -ring; dx <= ring; dx++)
            if (!Area.Chunks.TryGetValue(BlockArea.CKey(cx + dx, cz + dz), out var c) || c.State < 5) return false;
        return true;
    }

    // ---------------------------------------------------------------- the smooth ground the blocks replace
    /// <summary>Collision tiles of the smooth terrain (World.BuildTerrain, Lands.Terrain): switched off where blocks are ready.</summary>
    public static readonly List<(Rect2 r, CollisionShape3D s)> GroundTiles = new();
    public static void RegisterGroundTile(Rect2 r, CollisionShape3D s) => GroundTiles.Add((r, s));

    /// <summary>Global shader values the terrain, water and grass shaders read (must exist before those shaders compile).</summary>
    static bool _globals;
    public static void InitGlobals()
    {
        if (_globals) return; _globals = true;
        // (asking RenderingServer which globals exist is editor-only and slow; remember it ourselves)
        
            RenderingServer.GlobalShaderParameterAdd("vox_rect", RenderingServer.GlobalShaderParameterType.Vec4, new Vector4(1e9f, 1e9f, -1e9f, -1e9f));
    }

    public override void _Ready()
    {
        I = this; Name = "BlockWorld"; Blocks.Load(); Blocks.ResetLooks(); GroundTiles.RemoveAll(t => !IsInstanceValid(t.s));
        _radius = Blocks.Radius; _perFrame = (int)Blocks.F("world.meshPerFrame", 2);
        // every face material, made once on the main thread (the mesher on worker threads only looks them up)
        _slots = new Material?[Blocks.Defs.Count * 3];
        foreach (var d in Blocks.Defs)
        {
            if (d.Id == 0) continue;
            if (d.IsFluid) { _slots[d.Id * 3] = Blocks.Material(d, Blocks.Face.Side); continue; }
            _slots[d.Id * 3] = Blocks.Material(d, Blocks.Face.Top); _slots[d.Id * 3 + 1] = Blocks.Material(d, Blocks.Face.Side); _slots[d.Id * 3 + 2] = Blocks.Material(d, Blocks.Face.Bottom);
        }
        StatusReady();
    }
    public override void _ExitTree() { if (I == this) I = null; SetCut(null); }

    /// <summary>The overworld area (made on first use; the hero's saved changes laid over it).</summary>
    public BlockArea Overworld()
    {
        if (_over != null) return _over;
        _over = new BlockArea("overworld", new OverworldGen());
        if ((Game.D.BlockDiffs ??= new()).TryGetValue("overworld", out var s)) _over.Decode(s);
        return _over;
    }

    // ---------------------------------------------------------------- area switches (Dungeon.cs calls these)
    public void UseOverworld() { if (Area == _over && Area != null) return; Store(Game.D); DropArea(); Area = Overworld(); _center = (int.MinValue, 0); }
    public void UseDungeon(BlockArea a)
    {
        Store(Game.D); DropArea(); Area = a;
        if ((Game.D.BlockDiffs ??= new()).TryGetValue(a.Key, out var s)) a.Decode(s);
        // a dungeon floor is small: make it all now, so the player lands on solid blocks
        var g = a.Gen; var list = new List<BlockChunk>();
        int S = a.S, ocx = a.CFloor(Mathf.FloorToInt(Dungeon.Origin.X)), ocz = a.CFloor(Mathf.FloorToInt(Dungeon.Origin.Z));
        for (int cz = ocz - 2; cz <= ocz + 40; cz++) for (int cx = ocx - 2; cx <= ocx + 40; cx++)
        {
            if (!g.InBounds(cx, cz)) continue;
            var c = new BlockChunk(cx, cz, S, g.MinY, g.MaxY); g.Fill(c); a.ApplyDiff(c); c.State = 2; a.Chunks[BlockArea.CKey(cx, cz)] = c; list.Add(c);
        }
        foreach (var c in list) { var md = BlockMesher.Build(a, c); Show(a, c, md); }
        _center = (int.MinValue, 0);
    }
    /// <summary>A raid lair (no blocks) or leaving the game: drop the area's chunks.</summary>
    public void UseNone() { Store(Game.D); DropArea(); Area = null; }
    void DropArea()
    {
        if (Area == null) return;
        foreach (var c in Area.Chunks.Values) FreeNodes(c);
        Area.Chunks.Clear(); _dirty.Clear(); ClearLights(); ClearStatus(); FluidReset();
        SetCut(null); _cut = -1;
    }
    static void FreeNodes(BlockChunk c) { if (c.Node != null && IsInstanceValid(c.Node)) c.Node.QueueFree(); if (c.Body != null && IsInstanceValid(c.Body)) c.Body.QueueFree(); c.Node = null; c.Body = null; c.Shape = null; }

    // ---------------------------------------------------------------- saving
    /// <summary>Writes the areas' changes into the hero (Game.Save calls this). Not while visiting a friend's world: that is theirs.</summary>
    public static void Store(PlayerStats d)
    {
        var w = I; if (w == null || w._foreign) return;
        d.BlockDiffs ??= new();
        if (w._over != null) { if (w._over.Diff.Count > 0) d.BlockDiffs["overworld"] = w._over.Encode(); else d.BlockDiffs.Remove("overworld"); }
        if (w.Area != null && w.Area != w._over && w.Area.Diff.Count > 0)
        {
            d.BlockDiffs[w.Area.Key] = w.Area.Encode();
            // keep the last few dungeon floors only (a floor is new every visit, so old ones are never used again)
            var old = d.BlockDiffs.Keys.Where(k => k.StartsWith("dun:") && k != w.Area.Key).ToList();
            while (old.Count > 5) { d.BlockDiffs.Remove(old[0]); old.RemoveAt(0); }
        }
    }
    /// <summary>Tests: forget the loaded overworld and make it again from the hero's saved changes.</summary>
    public void ReloadOverworld() { bool cur = Area == _over; if (cur) DropArea(); _over = null; if (cur) { Area = Overworld(); _center = (int.MinValue, 0); } }

    // ---------------------------------------------------------------- streaming
    public override void _Process(double delta)
    {
        if (Net.Dedicated) return;
        if (!Structures.Ready) return;   // the world build is still stamping its block structures (F-19)
        if (Area == null && !Dungeon.Active && World.I != null) UseOverworld();
        var a = Area; var p = Player.Instance; if (a == null || p == null || !IsInstanceValid(p)) return;
        int S = a.S, pcx = a.CFloor(Mathf.FloorToInt(p.GlobalPosition.X)), pcz = a.CFloor(Mathf.FloorToInt(p.GlobalPosition.Z));
        bool overworld = a == _over;
        if (overworld && (pcx, pcz) != _center)
        {
            _center = (pcx, pcz);
            // make (radius + 1) so every meshed chunk has its neighbours; drop chunks beyond radius + 2
            for (int r = 0; r <= _radius + 1; r++)
                for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != r) continue;
                    int cx = pcx + dx, cz = pcz + dz; long k = BlockArea.CKey(cx, cz);
                    if (a.Chunks.ContainsKey(k) || !a.Gen.InBounds(cx, cz)) continue;
                    var c = new BlockChunk(cx, cz, S, a.Gen.MinY, a.Gen.MaxY); c.State = 1; a.Chunks[k] = c;
                    Task.Run(() => { try { a.Gen.Fill(c); a.ApplyDiff(c); c.State = 2; } catch (Exception e) { GD.PrintErr("[BLOCKS] chunk " + cx + "," + cz + ": " + e.Message); } });
                }
            foreach (var (k, c) in a.Chunks)
                if (Math.Max(Math.Abs(c.Cx - pcx), Math.Abs(c.Cz - pcz)) > _radius + 2 && c.State != 1 && c.State != 3) { FreeNodes(c); a.Chunks.TryRemove(k, out _); _dirty.Remove(c); }
            RecalcCut();
        }
        // mesh chunks within the radius once they and their neighbours are made
        int started = 0;
        foreach (var c in a.Chunks.Values)
        {
            if (started >= 6) break;
            if (c.State != 2 || (overworld && Math.Max(Math.Abs(c.Cx - pcx), Math.Abs(c.Cz - pcz)) > _radius)) continue;
            if (!NeighboursMade(a, c)) continue;
            c.State = 3; started++; var cc = c;
            Task.Run(() => { try { cc.Pending = BlockMesher.Build(a, cc); cc.State = 4; } catch (Exception e) { GD.PrintErr("[BLOCKS] mesh: " + e.Message); cc.State = 2; } });
        }
        // put finished meshes in the scene, a few per frame
        int shown = 0;
        foreach (var c in a.Chunks.Values)
        {
            if (shown >= _perFrame) break;
            if (c.State != 4 || c.Pending == null) continue;
            var md = c.Pending; c.Pending = null; Show(a, c, md); shown++;
            if (c.Version != md.Version) _dirty.Add(c);
        }
        if (shown > 0 && overworld) RecalcCut();
        // edited chunks: rebuild at once (a dug block should vanish this frame)
        if (_dirty.Count > 0)
        {
            int n = 0; foreach (var c in _dirty.ToList()) { if (n++ >= 4) break; _dirty.Remove(c); if (c.State < 5) continue; Show(a, c, BlockMesher.Build(a, c)); }
        }
        if (Blocks.HasLights) LightTick(a, p.GlobalPosition, (float)delta);
        _tick -= (float)delta;
        if (_tick <= 0) { _tick = Blocks.F("world.tick", .25f); if (!Net.IsClient) { FluidTick(a); FireTick(a, Blocks.F("world.tick", .25f)); } StatusTick(Blocks.F("world.tick", .25f)); FlushNet(a); }
        StatusFx();
    }
    static bool NeighboursMade(BlockArea a, BlockChunk c)
    {
        foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
        {
            if (!a.Gen.InBounds(c.Cx + dx, c.Cz + dz)) continue;
            if (!a.Chunks.TryGetValue(BlockArea.CKey(c.Cx + dx, c.Cz + dz), out var n) || n.State < 2) return false;
        }
        return true;
    }
    public void MarkDirty(BlockArea a, BlockChunk c) { if (a == Area && c.State >= 5) _dirty.Add(c); }

    /// <summary>Puts a chunk's mesh and collision in the scene (main thread).</summary>
    void Show(BlockArea a, BlockChunk c, BlockMeshData md)
    {
        var mesh = new ArrayMesh();
        foreach (var (slot, s) in md.Surfs)
        {
            if (s.I.Count == 0) continue;
            var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
            arr[(int)Mesh.ArrayType.Vertex] = s.V.ToArray(); arr[(int)Mesh.ArrayType.Normal] = s.N.ToArray(); arr[(int)Mesh.ArrayType.Color] = s.C.ToArray(); arr[(int)Mesh.ArrayType.Index] = s.I.ToArray();
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arr);
            var m = slot < _slots.Length ? _slots[slot] : null; if (m != null) mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, m);
        }
        if (c.Node == null || !IsInstanceValid(c.Node)) { c.Node = new MeshInstance3D { Name = $"C{c.Cx}_{c.Cz}" }; AddChild(c.Node); }
        c.Node.Mesh = mesh.GetSurfaceCount() > 0 ? mesh : null;
        if (c.Body == null || !IsInstanceValid(c.Body)) { c.Body = new StaticBody3D { CollisionLayer = 1 }; c.Shape = new CollisionShape3D(); c.Body.AddChild(c.Shape); AddChild(c.Body); c.Body.SetMeta("blocks", true); }
        foreach (var ch in c.Node.GetChildren()) if (ch is MultiMeshInstance3D) ch.QueueFree();
        // (TuftMat: a copy of Flora.GrassMat with vox_cut = 0, made on first use)
        if (md.Tufts.Count > 0 && Flora.GrassMesh != null && Flora.GrassMat != null && Game.Set.Grass > 0f)
        {
            float gs = Game.Set.Grass; var ts = new List<Transform3D>(); var tcs = new List<Color>();
            var cam = GetViewport().GetCamera3D();   // full density within ~2 chunks of the camera, half beyond, capped per chunk
            if (cam != null) { var cc = c.Node.ToGlobal(new Vector3((c.Cx + .5f) * c.S, cam.GlobalPosition.Y, (c.Cz + .5f) * c.S)); if (cc.DistanceTo(cam.GlobalPosition) > c.S * 2.5f) gs *= .5f; }
            for (int i = 0; i < md.Tufts.Count && ts.Count < 600; i++) if (md.TuftKeep[i] < gs) { ts.Add(md.Tufts[i]); tcs.Add(md.TuftCols[i]); }
            _tuftTotal += ts.Count; if (++_tuftChunks % 40 == 0) GD.Print($"[tufts] {_tuftTotal} instances built over {_tuftChunks} chunk builds");
            if (ts.Count > 0)
            {
                var mm = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = Flora.GrassMesh, InstanceCount = ts.Count };
                for (int i = 0; i < ts.Count; i++) { mm.SetInstanceTransform(i, ts[i]); mm.SetInstanceColor(i, tcs[i]); }
                if (TuftMat == null) { TuftMat = (ShaderMaterial)Flora.GrassMat.Duplicate(); TuftMat.SetShaderParameter("vox_cut", 0f); }   // the field grass hides itself inside the block square; tufts must not
                c.Node.AddChild(new MultiMeshInstance3D { Multimesh = mm, MaterialOverride = TuftMat, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = 45f, VisibilityRangeEndMargin = 6f });
            }
        }
        c.Shape!.Shape = md.Faces.Length > 0 ? new ConcavePolygonShape3D { Data = md.Faces } : null;
        c.MeshedVersion = md.Version; c.State = 5;
        ScanLights(c);
    }

    // ---------------------------------------------------------------- light blocks (data "light": torch): real OmniLight3Ds, the nearest few
    readonly Dictionary<long, List<Vector3I>> _lightBlocks = new();     // chunk key -> light block cells, made when the chunk is meshed
    readonly Dictionary<Vector3I, Torch> _lightNodes = new();
    float _lightT; const int MaxLights = 24;
    void ScanLights(BlockChunk c)
    {
        long key = BlockArea.CKey(c.Cx, c.Cz); _lightBlocks.Remove(key);
        if (!Blocks.HasLights) return;
        List<Vector3I>? list = null; int S = c.S, x0 = c.Cx * S, z0 = c.Cz * S, top = Math.Min(c.Top + 1, c.MinY + c.H);
        for (int y = c.MinY; y < top; y++)
            for (int lz = 0; lz < S; lz++) for (int lx = 0; lx < S; lx++)
            {
                byte id = c.Id[c.Index(lx, y, lz)]; if (id == 0) continue;
                if (Blocks.Def(id).LightRange > 0) (list ??= new()).Add(new Vector3I(x0 + lx, y, z0 + lz));
            }
        if (list != null) _lightBlocks[key] = list;
    }
    void LightTick(BlockArea a, Vector3 pp, float dt)
    {
        _lightT -= dt; if (_lightT > 0) return; _lightT = .3f;
        var want = new List<(Vector3I p, float d)>();
        foreach (var (k, list) in _lightBlocks.ToList())
        {
            if (!a.Chunks.TryGetValue(k, out var ch) || ch.State < 5) { _lightBlocks.Remove(k); continue; }
            foreach (var p in list) { if (Blocks.Def(a.Get(p.X, p.Y, p.Z)).LightRange <= 0) continue; want.Add((p, pp.DistanceSquaredTo(new Vector3(p.X + .5f, p.Y, p.Z + .5f)))); }
        }
        want.Sort((m, n) => m.d.CompareTo(n.d));
        if (want.Count > MaxLights) want.RemoveRange(MaxLights, want.Count - MaxLights);
        var keep = new HashSet<Vector3I>(want.Select(w => w.p));
        foreach (var p in _lightNodes.Keys.ToList())
            if (!keep.Contains(p)) { if (IsInstanceValid(_lightNodes[p])) _lightNodes[p].QueueFree(); _lightNodes.Remove(p); }
        // each light flickers in energy and colour (Torch); only the nearest few also carry the small flame and smoke particles
        int flames = (int)Blocks.F("world.torchFlames", 8);
        for (int i = 0; i < want.Count; i++)
        {
            var p = want[i].p; bool fl = i < flames;
            if (_lightNodes.TryGetValue(p, out var have)) { if (IsInstanceValid(have)) have.SetFlames(fl); continue; }
            var d = Blocks.Def(a.Get(p.X, p.Y, p.Z));
            var pos = d.Shape == "post" ? BlockMesher.TorchFlame(p.X, p.Y, p.Z, a.GetMeta(p.X, p.Y, p.Z)) - new Vector3(0, .04f, 0) : new Vector3(p.X + .5f, p.Y + .62f, p.Z + .5f);
            var t = new Torch { Col = d.LightCol, Range = d.LightRange, Energy = d.LightEnergy, Size = .5f, Position = pos, HueFlicker = true, Flames = fl };
            AddChild(t); _lightNodes[p] = t;
        }
    }
    void ClearLights()
    {
        foreach (var t in _lightNodes.Values) if (IsInstanceValid(t)) t.QueueFree();
        _lightNodes.Clear(); _lightBlocks.Clear();
    }

    // ---------------------------------------------------------------- the cut-out square
    void RecalcCut()
    {
        var a = Area; if (a == null || a != _over) { SetCut(null); return; }
        int r = -1;
        for (int k = 0; k <= _radius; k++)
        {
            bool all = true;
            for (int dz = -k; dz <= k && all; dz++) for (int dx = -k; dx <= k; dx++)
            {
                if (Math.Max(Math.Abs(dx), Math.Abs(dz)) != k) continue;
                int cx = _center.cx + dx, cz = _center.cz + dz;
                if (!a.Gen.InBounds(cx, cz)) continue;
                if (!a.Chunks.TryGetValue(BlockArea.CKey(cx, cz), out var c) || c.State < 5) { all = false; break; }
            }
            if (!all) break; r = k;
        }
        if (r == _cut) return; _cut = r;
        if (r < 0) { SetCut(null); return; }
        int S = a.S; float x0 = (_center.cx - r) * S, z0 = (_center.cz - r) * S, x1 = (_center.cx + r + 1) * S, z1 = (_center.cz + r + 1) * S;
        SetCut(new Rect2(x0, z0, x1 - x0, z1 - z0));
    }
    /// <summary>The square where the smooth ground is hidden and its collision off (null: none).</summary>
    public static Rect2? Cut { get; private set; }
    static void SetCut(Rect2? r)
    {
        Cut = r;
        var v = r is { } q ? new Vector4(q.Position.X, q.Position.Y, q.End.X, q.End.Y) : new Vector4(1e9f, 1e9f, -1e9f, -1e9f);
        if (_globals) RenderingServer.GlobalShaderParameterSet("vox_rect", v);
        foreach (var (tr, s) in GroundTiles)
            if (IsInstanceValid(s)) s.SetDeferred(CollisionShape3D.PropertyName.Disabled, r is { } q2 && q2.Encloses(tr));
    }

    // ---------------------------------------------------------------- questions the rest of the game asks
    static (int, int, int) B(Vector3 p) => (Mathf.FloorToInt(p.X), Mathf.FloorToInt(p.Y), Mathf.FloorToInt(p.Z));
    /// <summary>The block at a world point (0 air; 255 not loaded or no block world).</summary>
    public static byte At(Vector3 p) { var a = I?.Area; if (a == null) return 255; var (x, y, z) = B(p); return a.Get(x, y, z); }
    /// <summary>
    /// Standing height on the loaded overworld blocks in column (x, z) near block row y: the first free row above the solid ground there
    /// (scans up to 4 rows up out of the ground, or 4 rows down to it). Null when the column is not made yet, not the overworld, or no ground is near.
    /// </summary>
    public static int? StandTop(int x, int z, int y)
    {
        var w = I; var a = w?.Area; if (a == null || a != w!._over) return null;
        static bool Solid(byte b) { if (b == 0 || b == 255) return false; var d = Blocks.Def(b); return d.Solid && !d.IsFluid; }
        byte b0 = a.Get(x, y, z); if (b0 == 255) return null;
        if (Solid(b0)) { for (int k = 1; k <= 4; k++) if (!Solid(a.Get(x, y + k, z))) return y + k; return null; }
        for (int k = 1; k <= 4; k++) { byte b = a.Get(x, y - k, z); if (b == 255) return null; if (Solid(b)) return y - k + 1; }
        return null;
    }
    /// <summary>True when the blocks are drawn at (x, z) (inside the cut-out square of the overworld).</summary>
    public static bool Drawn(float x, float z) => Cut is { } r && r.HasPoint(new Vector2(x, z));
    /// <summary>"water", "lava" or "" at a point.</summary>
    public static string FluidAt(Vector3 p) { var b = At(p); return b == 255 || b == 0 ? "" : Blocks.Def(b).Fluid; }
    /// <summary>Ice, packed ice or a frozen block under the feet.</summary>
    public static bool SlipperyAt(Vector3 feet)
    {
        var a = I?.Area; if (a == null) return false; var (x, y, z) = B(feet + Vector3.Down * .15f);
        byte b = a.Get(x, y, z); if (b == 255 || b == 0) return false;
        return Blocks.Def(b).Slippery || (I!._frozen.ContainsKey(BlockArea.BKey(x, y, z)));
    }
}
