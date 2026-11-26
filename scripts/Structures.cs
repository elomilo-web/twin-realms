using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace TwinRealms;

/// <summary>
/// A block blueprint (F-19): cells relative to an origin (local y 0 = the first layer above the ground), ground pads (columns levelled
/// to the origin's floor: a top block, a foundation fill down to solid ground, air cleared above) and posts (a block run down from a
/// cell until it meets solid ground: stilts, bridge piers, wall footings). Blueprints merge into bigger ones (a town = one blueprint).
/// </summary>
public sealed class Blueprint
{
    public struct Pad { public short Dy; public byte Top, Fill, Clear; }   // Top 255 = the column's natural top block
    public readonly Dictionary<long, ushort> Cells = new();               // key K(x, y, z) -> id | meta << 8 (id 0 = carve air)
    public readonly Dictionary<long, Pad> Pads = new();                   // key K(x, 0, z)
    public readonly List<(int x, int y, int z, byte id)> Posts = new();

    public static long K(int x, int y, int z) => ((long)(x + 8192) << 28) | ((long)(z + 8192) << 14) | (long)(y + 8192);
    public static (int x, int y, int z) U(long k) => ((int)(k >> 28) - 8192, (int)(k & 16383) - 8192, (int)((k >> 14) & 16383) - 8192);

    public void Set(int x, int y, int z, byte id, byte meta = 0) => Cells[K(x, y, z)] = (ushort)(id | meta << 8);
    public byte Get(int x, int y, int z) => Cells.TryGetValue(K(x, y, z), out var v) ? (byte)(v & 255) : (byte)0;
    public bool Has(int x, int y, int z) => Cells.ContainsKey(K(x, y, z));
    public void Remove(int x, int y, int z) => Cells.Remove(K(x, y, z));
    public void Box(int x0, int y0, int z0, int x1, int y1, int z1, byte id, byte meta = 0)
    {
        if (x0 > x1) (x0, x1) = (x1, x0); if (y0 > y1) (y0, y1) = (y1, y0); if (z0 > z1) (z0, z1) = (z1, z0);
        for (int y = y0; y <= y1; y++) for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) Set(x, y, z, id, meta);
    }
    public void PadBox(int x0, int z0, int x1, int z1, byte top, byte fill, int clear, int dy = 0)
    {
        if (x0 > x1) (x0, x1) = (x1, x0); if (z0 > z1) (z0, z1) = (z1, z0);
        for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) Pads[K(x, 0, z)] = new Pad { Dy = (short)dy, Top = top, Fill = fill, Clear = (byte)Math.Clamp(clear, 0, 255) };
    }
    public void PadAt(int x, int z, byte top, byte fill, int clear, int dy = 0) => Pads[K(x, 0, z)] = new Pad { Dy = (short)dy, Top = top, Fill = fill, Clear = (byte)Math.Clamp(clear, 0, 255) };
    public void Post(int x, int y, int z, byte id) => Posts.Add((x, y, z, id));

    /// <summary>Copies another blueprint in, moved by (dx, dy, dz) and turned by rot quarter turns. pads = false leaves its pads out (stilts).</summary>
    public void Merge(Blueprint o, int dx, int dy, int dz, int rot = 0, bool pads = true)
    {
        if (pads) foreach (var (k, p) in o.Pads) { var (x, _, z) = U(k); var (rx, rz) = Structures.Rot(x, z, rot); var q = p; q.Dy = (short)(p.Dy + dy); Pads[K(rx + dx, 0, rz + dz)] = q; }
        foreach (var (k, v) in o.Cells) { var (x, y, z) = U(k); var (rx, rz) = Structures.Rot(x, z, rot); byte id = (byte)(v & 255); Set(rx + dx, y + dy, rz + dz, id, Structures.RotMeta(id, (byte)(v >> 8), rot)); }
        foreach (var (x, y, z, id) in o.Posts) { var (rx, rz) = Structures.Rot(x, z, rot); Posts.Add((rx + dx, y + dy, rz + dz, id)); }
    }
}

/// <summary>
/// F-19: every town, castle, bridge, shrine, camp and dungeon mouth is built from blocks. Builder/Lands/Wayshrine stamp blueprints here
/// while the world is built (always the same order and seeds, so every machine and every reload makes the same world); the overworld
/// generator lays them into each chunk it makes (OverworldGen.Fill -> Apply), and player edits are laid over that as before.
/// Beyond the block radius each 128 m tile of structures is one merged far mesh (outer faces only, vertex colours, one draw call) that
/// hides itself inside the block square (global vox_rect, like the far terrain), so near blocks and far meshes never draw twice.
/// data/blocks.json "structures.blockStructures" false = the old mesh buildings (the online server always keeps those: it has no blocks).
/// </summary>
public static class Structures
{
    public static bool On { get; private set; }
    /// <summary>All structures are stamped (BlockWorld waits for this before it makes overworld chunks).</summary>
    public static bool Ready { get; private set; } = true;
    struct Cell { public byte Lx, Lz; public short Y; public byte Id, Meta; }
    struct PadCol { public byte Lx, Lz; public short Floor; public byte Top, Fill, Clear; }
    struct PostCol { public byte Lx, Lz; public short Y; public byte Id; }
    sealed class Job { public readonly List<PadCol> Pads = new(); public readonly List<Cell> Cells = new(); public readonly List<PostCol> Posts = new(); }
    static readonly Dictionary<long, Job> _jobs = new();
    static readonly Dictionary<long, Dictionary<long, ushort>> _far = new();   // far tile -> world cells (BlockArea.BKey)
    const int FarTile = 128;
    public static int CellCount { get; private set; }
    static ulong _t0;

    /// <summary>A new world is being built: forget the old stamps.</summary>
    public static void Begin()
    {
        Blocks.Load();
        On = Blocks.F("structures.blockStructures", 1f) > .5f && !Net.Dedicated;
        _jobs.Clear(); _far.Clear(); _occ.Clear(); CellCount = 0; Ready = !On; _t0 = Time.GetTicksMsec();
        InitKinds();
    }

    // ---------------------------------------------------------------- rotation (quarter turns about +Y: local +Z faces +Z, +X, -Z, -X for rot 0..3)
    public static (int x, int z) Rot(int x, int z, int r) => (r & 3) switch { 0 => (x, z), 1 => (z, -x), 2 => (-x, -z), _ => (-z, x) };
    static readonly (int x, int z)[] Dirs = { (1, 0), (-1, 0), (0, 1), (0, -1) };   // meta sides 0 +X, 1 -X, 2 +Z, 3 -Z
    public static int RotDir(int d, int r) { var (x, z) = Rot(Dirs[d & 3].x, Dirs[d & 3].z, r); for (int i = 0; i < 4; i++) if (Dirs[i] == (x, z)) return i; return d; }
    /// <summary>The quarter turn nearest a yaw (Godot: yaw 0 faces +Z... the local +Z side of a blueprint ends up facing yaw).</summary>
    public static int RotOfYaw(float yaw) => ((int)Mathf.Round(yaw / (Mathf.Pi / 2)) % 4 + 4) % 4;
    /// <summary>The quarter turn that makes a blueprint's front (+Z) face the world direction (dx, dz).</summary>
    public static int RotFacing(float dx, float dz) => Math.Abs(dx) > Math.Abs(dz) ? (dx > 0 ? 1 : 3) : (dz > 0 ? 0 : 2);
    static byte[] _kind = Array.Empty<byte>();   // 0 none, 1 side in meta & 3, 2 torch (1 + side in meta & 7), 3 log axis
    static void InitKinds()
    {
        _kind = new byte[Blocks.Defs.Count];
        foreach (var d in Blocks.Defs)
            _kind[d.Id] = d.Shape is "stairs" or "door" or "ladder" or "chest" ? (byte)1 : d.Shape == "post" ? (byte)2 : d.Key == "log" ? (byte)3 : (byte)0;
    }
    public static byte RotMeta(byte id, byte meta, int r)
    {
        if ((r & 3) == 0 || id >= _kind.Length) return meta;
        switch (_kind[id])
        {
            case 1: return (byte)((meta & ~3) | RotDir(meta & 3, r));
            case 2: { int m = meta & 7; return m is >= 1 and <= 4 ? (byte)((meta & ~7) | (1 + RotDir(m - 1, r))) : meta; }
            case 3: { int a = meta & 3; return (r & 1) == 1 && a != 0 ? (byte)((meta & ~3) | (a == 1 ? 2 : 1)) : meta; }
        }
        return meta;
    }

    // ---------------------------------------------------------------- stamping
    static int CF(int v) => v >= 0 ? v / Blocks.Chunk : -((-v + Blocks.Chunk - 1) / Blocks.Chunk);
    static Job J(int x, int z) { long k = BlockArea.CKey(CF(x), CF(z)); if (!_jobs.TryGetValue(k, out var j)) _jobs[k] = j = new Job(); return j; }
    static byte L(int v) => (byte)(v - CF(v) * Blocks.Chunk);
    static Dictionary<long, ushort> Far(int x, int z)
    {
        long k = ((long)Mathf.FloorToInt(x / (float)FarTile) << 32) ^ (uint)Mathf.FloorToInt(z / (float)FarTile);
        if (!_far.TryGetValue(k, out var d)) _far[k] = d = new Dictionary<long, ushort>(); return d;
    }

    /// <summary>Lays a blueprint into the world: its origin at block (ox, oy, oz) (oy = the floor surface), turned rot quarter turns.</summary>
    public static void Place(Blueprint bp, int ox, int oy, int oz, int rot = 0)
    {
        if (!On) return;
        foreach (var (k, p) in bp.Pads)
        {
            var (x, _, z) = Blueprint.U(k); var (rx, rz) = Rot(x, z, rot); int wx = ox + rx, wz = oz + rz;
            J(wx, wz).Pads.Add(new PadCol { Lx = L(wx), Lz = L(wz), Floor = (short)(oy + p.Dy), Top = p.Top, Fill = p.Fill, Clear = p.Clear });
        }
        foreach (var (k, v) in bp.Cells)
        {
            var (x, y, z) = Blueprint.U(k); var (rx, rz) = Rot(x, z, rot); int wx = ox + rx, wz = oz + rz, wy = oy + y;
            if (wy < Blocks.MinY + 1 || wy >= Blocks.MaxY) continue;
            byte id = (byte)(v & 255), meta = RotMeta(id, (byte)(v >> 8), rot);
            J(wx, wz).Cells.Add(new Cell { Lx = L(wx), Lz = L(wz), Y = (short)wy, Id = id, Meta = meta });
            if (id != 0) _occ.Add(((long)wx << 32) ^ (uint)wz);
            var f = Far(wx, wz); long bk = BlockArea.BKey(wx, wy, wz);
            if (id == 0) f.Remove(bk); else f[bk] = (ushort)(id | meta << 8);
            CellCount++;
        }
        foreach (var (x, y, z, id) in bp.Posts)
        {
            var (rx, rz) = Rot(x, z, rot); int wx = ox + rx, wz = oz + rz, wy = oy + y;
            J(wx, wz).Posts.Add(new PostCol { Lx = L(wx), Lz = L(wz), Y = (short)wy, Id = id });
            // far view: down to the smooth ground (or the sea bed)
            int gy = Mathf.FloorToInt(World.GroundAt(wx + .5f, wz + .5f) + .5f); var f = Far(wx, wz);
            for (int yy = wy; yy >= Math.Max(gy, wy - 40); yy--) { long bk = BlockArea.BKey(wx, yy, wz); if (!f.ContainsKey(bk)) f[bk] = id; }
        }
    }

    static readonly HashSet<long> _occ = new();
    /// <summary>No structure block stands in the square of half size r round column (x, z) (so shrines are not built into houses).</summary>
    public static bool Free(int x, int z, int r)
    {
        for (int dz = -r; dz <= r; dz++) for (int dx = -r; dx <= r; dx++) if (_occ.Contains(((long)(x + dx) << 32) ^ (uint)(z + dz))) return false;
        return true;
    }
    /// <summary>The floor level (block surface) for a smooth ground height.</summary>
    public static int Floor(float h) => Mathf.FloorToInt(h + .5f);

    /// <summary>Lays this chunk's share of the structures over freshly generated terrain (worker thread; the tables are read-only by then).</summary>
    public static void Apply(BlockChunk c, OverworldGen gen)
    {
        if (!On || !_jobs.TryGetValue(BlockArea.CKey(c.Cx, c.Cz), out var j)) return;
        int maxY = c.MinY + c.H, S = c.S;
        foreach (var p in j.Pads)
        {
            int f = Math.Clamp((int)p.Floor, c.MinY + 3, maxY - 2);
            byte top = p.Top;
            if (top == 255) { var col = gen.Column(c.Cx * S + p.Lx, c.Cz * S + p.Lz); top = col.water > col.top ? col.underId : col.topId; if (top == Blocks.Water) top = Blocks.Dirt; }
            for (int y = f - 1; y > c.MinY + 1; y--)
            {
                int i = c.Index(p.Lx, y, p.Lz); byte cur = c.Id[i];
                if (y == f - 1) { c.Id[i] = top; c.Meta[i] = 0; continue; }
                var d = Blocks.Def(cur); bool soft = cur == 0 || d.IsFluid || !d.Solid;
                if (!soft && y < f - 2) break;
                c.Id[i] = p.Fill; c.Meta[i] = 0;
            }
            for (int y = f; y < Math.Min(f + p.Clear, maxY); y++) { int i = c.Index(p.Lx, y, p.Lz); c.Id[i] = 0; c.Meta[i] = 0; }
            if (f > c.Top) c.Top = f;
        }
        foreach (var e in j.Cells)
        {
            if (!c.Has(e.Y)) continue;
            int i = c.Index(e.Lx, e.Y, e.Lz); c.Id[i] = e.Id; c.Meta[i] = e.Meta;
            if (e.Id != 0 && e.Y + 1 > c.Top) c.Top = e.Y + 1;
        }
        foreach (var p in j.Posts)
            for (int y = Math.Min((int)p.Y, maxY - 1); y > c.MinY + 1; y--)
            {
                int i = c.Index(p.Lx, y, p.Lz); byte cur = c.Id[i]; var d = Blocks.Def(cur);
                if (y < p.Y && cur != 0 && d.Solid && !d.IsFluid) break;
                c.Id[i] = p.Id; c.Meta[i] = 0; if (y + 1 > c.Top) c.Top = y + 1;
            }
    }

    // ---------------------------------------------------------------- finishing: the far meshes
    static ShaderMaterial? _farMat;
    const string FarShader = @"
shader_type spatial;
render_mode cull_back, diffuse_burley;
global uniform vec4 vox_rect;
varying vec3 wpos;
void vertex() { wpos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }
void fragment() {
  if (wpos.x > vox_rect.x && wpos.z > vox_rect.y && wpos.x < vox_rect.z && wpos.z < vox_rect.w) discard;
  ALBEDO = COLOR.rgb; ROUGHNESS = 0.95;
}";
    /// <summary>All structures are stamped: build the far meshes (worker threads) and let the block world start.</summary>
    public static void Finish(Node3D over)
    {
        if (!On) { Ready = true; return; }
        BlockWorld.InitGlobals();
        _farMat ??= new ShaderMaterial { Shader = new Shader { Code = FarShader } };
        // per block: top and side colour (data tint, darkened a little for the textures it lacks far away), linear
        var top = new Color[Blocks.Defs.Count]; var side = new Color[Blocks.Defs.Count];
        foreach (var d in Blocks.Defs)
        {
            Color Of(string k)
            {
                var m = d.Mat; if (m.ValueKind != System.Text.Json.JsonValueKind.Object) return new Color(.5f, .5f, .5f);
                if (m.TryGetProperty(k, out var s) || m.TryGetProperty("side", out s)) m = s;
                var c = Color.FromHtml(JsonUtil.S(m, "color", "#808080")); float g = JsonUtil.F(m, "glow", 0f);
                return (g > 0 ? c : c * .74f).SrgbToLinear();
            }
            top[d.Id] = Of("top"); side[d.Id] = Of("side");
        }
        var node = new StructureFar { Name = "StructureFar" }; over.AddChild(node);
        float range = Blocks.F("structures.farRange", 900f);
        var tiles = new List<Dictionary<long, ushort>>(_far.Values);
        foreach (var t in tiles) { var tt = t; Task.Run(() => { try { var a = FarArrays(tt, top, side); if (a != null) node.Q.Enqueue((a, range)); } catch (Exception e) { GD.PrintErr("[STRUCT] far mesh: " + e.Message); } }); }
        GD.Print($"[STRUCT] {CellCount} blocks in {_jobs.Count} chunks, {tiles.Count} far tiles ({(Time.GetTicksMsec() - _t0) / 1000f:0.00} s since Lands.Build began, all world building included)");
        Ready = true;
        // a world rebuilt while overworld chunks were already made (quit to title and back): make them again with the new structures
        if (BlockWorld.I?.Area != null && BlockWorld.I.Area.Chunks.Count > 0 && BlockWorld.I.Area.Key == "overworld") BlockWorld.I.ReloadOverworld();
    }

    static readonly Vector3I[] Nrm = { new(0, 1, 0), new(0, -1, 0), new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
    static readonly float[] Shade = { 1f, .5f, .8f, .8f, .9f, .9f };
    static Godot.Collections.Array? FarArrays(Dictionary<long, ushort> cells, Color[] top, Color[] side)
    {
        var V = new List<Vector3>(); var N = new List<Vector3>(); var C = new List<Color>(); var I = new List<int>();
        var defs = Blocks.Defs;
        bool Full(long k) => cells.TryGetValue(k, out var v) && (v & 255) < defs.Count && defs[v & 255].Opaque && defs[v & 255].Shape == "";
        foreach (var (k, v) in cells)
        {
            byte id = (byte)(v & 255); if (id == 0 || id >= defs.Count) continue; var d = defs[id];
            if (d.Shape is "post" or "door" or "ladder" || d.IsFluid) continue;
            var (x, y, z) = BlockArea.Unkey(k);
            Vector3 mn = new(x, y, z), mx = new(x + 1, y + 1, z + 1);
            if (d.Shape == "slab") { if (((v >> 8) & 4) != 0) mn.Y += .5f; else mx.Y -= .5f; }
            else if (d.Shape == "fence") { mn.X += .375f; mn.Z += .375f; mx.X -= .375f; mx.Z -= .375f; }
            bool full = d.Shape is "" or "stairs" or "chest";
            for (int f = 0; f < 6; f++)
            {
                var n = Nrm[f];
                if (full && d.Opaque && Full(BlockArea.BKey(x + n.X, y + n.Y, z + n.Z))) continue;
                if (d.Shape == "" && Full(BlockArea.BKey(x + n.X, y + n.Y, z + n.Z))) continue;
                var col = (f == 0 ? top[id] : side[id]) * Shade[f]; col.A = 1;
                Quad(V, N, C, I, mn, mx, f, col);
            }
        }
        if (I.Count == 0) return null;
        var arr = new Godot.Collections.Array(); arr.Resize((int)Mesh.ArrayType.Max);
        arr[(int)Mesh.ArrayType.Vertex] = V.ToArray(); arr[(int)Mesh.ArrayType.Normal] = N.ToArray(); arr[(int)Mesh.ArrayType.Color] = C.ToArray(); arr[(int)Mesh.ArrayType.Index] = I.ToArray();
        return arr;
    }
    static void Quad(List<Vector3> V, List<Vector3> N, List<Color> C, List<int> I, Vector3 a, Vector3 b, int f, Color col)
    {
        Vector3 p0, p1, p2, p3;   // anticlockwise seen from outside
        switch (f)
        {
            case 0: p0 = new(a.X, b.Y, a.Z); p1 = new(a.X, b.Y, b.Z); p2 = new(b.X, b.Y, b.Z); p3 = new(b.X, b.Y, a.Z); break;
            case 1: p0 = new(a.X, a.Y, a.Z); p1 = new(b.X, a.Y, a.Z); p2 = new(b.X, a.Y, b.Z); p3 = new(a.X, a.Y, b.Z); break;
            case 2: p0 = new(b.X, a.Y, a.Z); p1 = new(b.X, b.Y, a.Z); p2 = new(b.X, b.Y, b.Z); p3 = new(b.X, a.Y, b.Z); break;
            case 3: p0 = new(a.X, a.Y, a.Z); p1 = new(a.X, a.Y, b.Z); p2 = new(a.X, b.Y, b.Z); p3 = new(a.X, b.Y, a.Z); break;
            case 4: p0 = new(a.X, a.Y, b.Z); p1 = new(b.X, a.Y, b.Z); p2 = new(b.X, b.Y, b.Z); p3 = new(a.X, b.Y, b.Z); break;
            default: p0 = new(a.X, a.Y, a.Z); p1 = new(a.X, b.Y, a.Z); p2 = new(b.X, b.Y, a.Z); p3 = new(b.X, a.Y, a.Z); break;
        }
        int b0 = V.Count; var n = (Vector3)Nrm[f];
        V.Add(p0); V.Add(p1); V.Add(p2); V.Add(p3); for (int i = 0; i < 4; i++) { N.Add(n); C.Add(col); }
        // Godot's front faces wind clockwise: p0, p2, p1 and p0, p3, p2
        I.Add(b0); I.Add(b0 + 2); I.Add(b0 + 1); I.Add(b0); I.Add(b0 + 3); I.Add(b0 + 2);
    }

    public static Material? FarMat => _farMat;
}

/// <summary>Puts the finished structure far meshes in the scene, a few per frame.</summary>
public partial class StructureFar : Node3D
{
    public readonly ConcurrentQueue<(Godot.Collections.Array a, float range)> Q = new();
    public int Shown;
    public override void _Process(double delta)
    {
        for (int n = 0; n < 3 && Q.TryDequeue(out var it); n++)
        {
            var m = new ArrayMesh(); m.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, it.a); m.SurfaceSetMaterial(0, Structures.FarMat);
            AddChild(new MeshInstance3D { Mesh = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, VisibilityRangeEnd = it.range, VisibilityRangeEndMargin = 40f });
            Shown++;
        }
    }
}
