using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Godot;

namespace TwinRealms;

/// <summary>
/// A 16 x 16 column of blocks from Blocks.MinY up to Blocks.MaxY. Ids and meta are flat arrays (index = (y * S + z) * S + x, y counted from MinY).
/// Meta: fluids keep their flow level in the low 3 bits (0 = source, 1-7 = flowing farther away); logs keep their axis (0 y, 1 x, 2 z).
/// </summary>
public sealed class BlockChunk
{
    public readonly int Cx, Cz, S, MinY, H;
    public readonly byte[] Id, Meta;
    public volatile int State;          // 0 new, 1 generating, 2 generated, 3 meshing, 4 mesh waiting for the scene, 5 shown
    public int Version, MeshedVersion = -1;
    public int Top;                     // highest y with anything in it (+1), so meshing skips the empty sky
    public MeshInstance3D? Node; public StaticBody3D? Body; public CollisionShape3D? Shape;
    public BlockMeshData? Pending;      // a finished mesh waiting to be put in the scene (main thread)
    public BlockChunk(int cx, int cz, int s, int minY, int maxY) { Cx = cx; Cz = cz; S = s; MinY = minY; H = maxY - minY; Id = new byte[s * s * H]; Meta = new byte[s * s * H]; Top = minY; }
    public int Index(int lx, int y, int lz) => ((y - MinY) * S + lz) * S + lx;
    public bool Has(int y) => y >= MinY && y < MinY + H;
}

/// <summary>Fills a chunk with the area's generated blocks and answers which blocks may never change.</summary>
public interface IBlockGen
{
    int MinY { get; }
    int MaxY { get; }
    void Fill(BlockChunk c);
    /// <summary>Nothing breaks this block (dungeon floors on the way to the boss, exit pads).</summary>
    bool Protected(int x, int y, int z);
    /// <summary>Kept clear for walking: no placed blocks, no flowing water or lava (the path to the boss room).</summary>
    bool KeepClear(int x, int y, int z);
    /// <summary>Chunks outside these bounds are never made (dungeons are small; the overworld streams).</summary>
    bool InBounds(int cx, int cz);
}

/// <summary>
/// One block world: the overworld or one dungeon floor. Chunks are made from the generator around the player and dropped when far;
/// every change is kept in <see cref="Diff"/> (block key -> id | meta << 8), laid over freshly made chunks and saved with the hero.
/// </summary>
public sealed class BlockArea
{
    public readonly string Key; public readonly IBlockGen Gen; public readonly int S;
    public readonly ConcurrentDictionary<long, BlockChunk> Chunks = new();
    public readonly Dictionary<long, ushort> Diff = new();
    readonly Dictionary<long, List<long>> _diffByChunk = new();
    readonly object _diffLock = new();
    public BlockArea(string key, IBlockGen gen) { Key = key; Gen = gen; S = Blocks.Chunk; }

    // ---------------------------------------------------------------- keys
    public static long BKey(int x, int y, int z) => ((long)(x + (1 << 20)) << 32) | ((long)(z + (1 << 20)) << 10) | (long)(y + 512);
    public static (int x, int y, int z) Unkey(long k) => ((int)(k >> 32) - (1 << 20), (int)(k & 1023) - 512, (int)((k >> 10) & 0x3fffff) - (1 << 20));
    public static long CKey(int cx, int cz) => ((long)cx << 32) ^ (uint)cz;
    public int CFloor(int v) => v >= 0 ? v / S : -((-v + S - 1) / S);
    public long ChunkKeyOf(int x, int z) => CKey(CFloor(x), CFloor(z));

    public BlockChunk? ChunkAt(int x, int z) => Chunks.TryGetValue(ChunkKeyOf(x, z), out var c) && c.State >= 2 ? c : null;

    // ---------------------------------------------------------------- reading
    /// <summary>The block at a spot; 255 when its chunk is not made yet (callers treat that as "unknown, leave it").</summary>
    public byte Get(int x, int y, int z)
    {
        if (y < Gen.MinY) return Blocks.Bedrock; if (y >= Gen.MaxY) return 0;
        var c = ChunkAt(x, z); if (c == null) return 255;
        return c.Id[c.Index(x - c.Cx * S, y, z - c.Cz * S)];
    }
    public byte GetMeta(int x, int y, int z)
    {
        if (y < Gen.MinY || y >= Gen.MaxY) return 0;
        var c = ChunkAt(x, z); if (c == null) return 0;
        return c.Meta[c.Index(x - c.Cx * S, y, z - c.Cz * S)];
    }

    // ---------------------------------------------------------------- writing
    /// <summary>Changes a block, records it for saving and marks the chunk (and a neighbour on its edge) for a new mesh. False if its chunk is not loaded.</summary>
    public bool Set(int x, int y, int z, byte id, byte meta = 0)
    {
        if (y < Gen.MinY || y >= Gen.MaxY) return false;
        var c = ChunkAt(x, z); if (c == null) { Record(x, y, z, id, meta); return false; }
        int lx = x - c.Cx * S, lz = z - c.Cz * S, i = c.Index(lx, y, lz);
        if (c.Id[i] == id && c.Meta[i] == meta) return true;
        c.Id[i] = id; c.Meta[i] = meta; c.Version++;
        if (id != 0 && y + 1 > c.Top) c.Top = y + 1;
        Record(x, y, z, id, meta);
        BlockWorld.I?.MarkDirty(this, c);
        if (lx == 0) Dirty(x - 1, z); else if (lx == S - 1) Dirty(x + 1, z);
        if (lz == 0) Dirty(x, z - 1); else if (lz == S - 1) Dirty(x, z + 1);
        return true;
    }
    void Dirty(int x, int z) { var n = ChunkAt(x, z); if (n != null) BlockWorld.I?.MarkDirty(this, n); }
    void Record(int x, int y, int z, byte id, byte meta)
    {
        long k = BKey(x, y, z);
        lock (_diffLock)
        {
            if (!Diff.ContainsKey(k)) { long ck = ChunkKeyOf(x, z); if (!_diffByChunk.TryGetValue(ck, out var l)) _diffByChunk[ck] = l = new List<long>(); l.Add(k); }
            Diff[k] = (ushort)(id | meta << 8);
        }
    }

    /// <summary>Lays the saved changes over a freshly generated chunk (worker thread).</summary>
    public void ApplyDiff(BlockChunk c)
    {
        List<long>? keys;
        lock (_diffLock) { if (!_diffByChunk.TryGetValue(CKey(c.Cx, c.Cz), out var l)) return; keys = new List<long>(l); }
        foreach (var k in keys)
        {
            ushort v; lock (_diffLock) { if (!Diff.TryGetValue(k, out v)) continue; }
            var (x, y, z) = Unkey(k); if (!c.Has(y)) continue;
            int i = c.Index(x - c.Cx * S, y, z - c.Cz * S); c.Id[i] = (byte)(v & 255); c.Meta[i] = (byte)(v >> 8);
            if ((v & 255) != 0 && y + 1 > c.Top) c.Top = y + 1;
        }
    }

    // ---------------------------------------------------------------- saving (base64 of deflated x,y,z,id,meta records)
    public string Encode()
    {
        using var ms = new MemoryStream();
        using (var z = new DeflateStream(ms, CompressionLevel.Optimal, true))
        using (var w = new BinaryWriter(z))
        {
            lock (_diffLock) { w.Write(Diff.Count); foreach (var (k, v) in Diff) { var (x, y, zz) = Unkey(k); w.Write(x); w.Write((short)y); w.Write(zz); w.Write(v); } }
        }
        return Convert.ToBase64String(ms.ToArray());
    }
    public void Decode(string s)
    {
        if (string.IsNullOrEmpty(s)) return;
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(s));
            using var z = new DeflateStream(ms, CompressionMode.Decompress);
            using var r = new BinaryReader(z);
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) { int x = r.ReadInt32(), y = r.ReadInt16(), zz = r.ReadInt32(); ushort v = r.ReadUInt16(); Record(x, y, zz, (byte)(v & 255), (byte)(v >> 8)); }
        }
        catch (Exception e) { GD.PushWarning("[BLOCKS] saved changes for " + Key + " unreadable: " + e.Message); }
    }
    /// <summary>A list of changes as bytes for the network: x, y, z, id, meta per block.</summary>
    public static byte[] Pack(List<(int x, int y, int z, byte id, byte meta)> l)
    {
        var b = new byte[l.Count * 12];
        for (int i = 0; i < l.Count; i++)
        {
            var (x, y, z, id, m) = l[i]; int o = i * 12;
            BitConverter.TryWriteBytes(new Span<byte>(b, o, 4), x); BitConverter.TryWriteBytes(new Span<byte>(b, o + 4, 2), (short)y);
            BitConverter.TryWriteBytes(new Span<byte>(b, o + 6, 4), z); b[o + 10] = id; b[o + 11] = m;
        }
        return b;
    }
    public static List<(int x, int y, int z, byte id, byte meta)> Unpack(byte[] b)
    {
        var l = new List<(int, int, int, byte, byte)>(b.Length / 12);
        for (int o = 0; o + 12 <= b.Length; o += 12) l.Add((BitConverter.ToInt32(b, o), BitConverter.ToInt16(b, o + 4), BitConverter.ToInt32(b, o + 6), b[o + 10], b[o + 11]));
        return l;
    }
    public List<(int x, int y, int z, byte id, byte meta)> AllChanges()
    {
        var l = new List<(int, int, int, byte, byte)>();
        lock (_diffLock) foreach (var (k, v) in Diff) { var (x, y, z) = Unkey(k); l.Add((x, y, z, (byte)(v & 255), (byte)(v >> 8))); }
        return l;
    }
    public void ClearDiff() { lock (_diffLock) { Diff.Clear(); _diffByChunk.Clear(); } }
}
