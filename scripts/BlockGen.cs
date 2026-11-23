using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>
/// The overworld as blocks, made from the same heightfield, ground types and water as the smooth world, so hills, roads, rivers, lakes
/// and coasts sit where they always were. A column's top block is at round(ground height): the top of the block is within half a metre
/// of the old surface. Layers: the ground type's top block, a few blocks of subsoil, then stone with ores, bedrock at the bottom.
/// Water fills up to round(water height) as source blocks (sea, lakes, rivers); under water the top is sand (gravel when deep).
/// </summary>
public sealed class OverworldGen : IBlockGen
{
    public int MinY => Blocks.MinY;
    public int MaxY => Blocks.MaxY;
    readonly Dictionary<string, byte> _top = new(), _under = new();
    readonly byte _underwater, _deep; readonly float _deepAt; readonly int _dirt, _bed;
    readonly List<(byte id, int below, float chance)> _ores = new();
    readonly float _extent;

    public OverworldGen()
    {
        Blocks.Load(); var r = Blocks.Root;
        var s = r.GetProperty("surface");
        foreach (var p in s.GetProperty("top").EnumerateObject()) _top[p.Name] = Blocks.Id(p.Value.GetString() ?? "grass");
        foreach (var p in s.GetProperty("under").EnumerateObject()) _under[p.Name] = Blocks.Id(p.Value.GetString() ?? "dirt");
        _underwater = Blocks.Id(JsonUtil.S(s, "underwater", "sand")); _deep = Blocks.Id(JsonUtil.S(s, "deepWater", "gravel")); _deepAt = JsonUtil.F(s, "deepAt", 4);
        _dirt = (int)Blocks.F("world.dirtDepth", 3); _bed = (int)Blocks.F("world.bedrock", 1);
        foreach (var p in r.GetProperty("ores").EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.Object) _ores.Add((Blocks.Id(p.Name), (int)JsonUtil.F(p.Value, "below", 6), JsonUtil.F(p.Value, "chance", .005f)));
        _extent = Lands.Extent;
    }

    public bool InBounds(int cx, int cz) { float e = _extent + 16; return Math.Abs(cx * Blocks.Chunk) < e && Math.Abs(cz * Blocks.Chunk) < e; }
    public bool Protected(int x, int y, int z) => false;
    public bool KeepClear(int x, int y, int z) => false;

    static uint Hash(int x, int y, int z) { unchecked { uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791); h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h; } }

    /// <summary>Ground height, top block and water height of one column (also used by tests).</summary>
    public (int top, byte topId, byte underId, int water) Column(int x, int z)
    {
        float fx = x + .5f, fz = z + .5f, h = World.GroundAt(fx, fz);
        int top = Mathf.FloorToInt(h + .5f);
        float wl = Lands.WaterAt(fx, fz); int water = Mathf.FloorToInt(wl + .5f);
        string kind = Math.Abs(fx) < World.Half && Math.Abs(fz) < World.Half && fx * fx + fz * fz < 9.2f * 9.2f ? "plaza" : World.SurfaceAt(fx, fz, h);
        byte t = _top.TryGetValue(kind, out var tt) ? tt : _top.GetValueOrDefault("grass");
        string tk = Blocks.Def(t).Key; byte u = _under.TryGetValue(tk, out var uu) ? uu : Blocks.Dirt;
        if (water > top) { t = water - top >= _deepAt ? _deep : _underwater; u = t; }
        return (top, t, u, water);
    }

    public void Fill(BlockChunk c)
    {
        int S = c.S; byte stone = Blocks.Stone, bed = Blocks.Bedrock, waterId = Blocks.Water;
        for (int lz = 0; lz < S; lz++)
            for (int lx = 0; lx < S; lx++)
            {
                int x = c.Cx * S + lx, z = c.Cz * S + lz;
                var (top, t, u, water) = Column(x, z);
                top = Math.Clamp(top, MinY + 2, MaxY - 2); water = Math.Min(water, MaxY - 2);
                for (int y = MinY; y < top; y++)
                {
                    byte id;
                    if (y < MinY + _bed) id = bed;
                    else if (y == top - 1) id = t;
                    else if (y >= top - 1 - _dirt) id = u;
                    else
                    {
                        id = stone; int depth = top - 1 - y; uint h = Hash(x, y, z); float r = (h & 0xffffff) / 16777216f;
                        foreach (var (oid, below, ch) in _ores) if (depth >= below && r < ch) { id = oid; break; }
                    }
                    c.Id[c.Index(lx, y, lz)] = id;
                }
                for (int y = top; y < water; y++) c.Id[c.Index(lx, y, lz)] = waterId;   // sources (meta 0)
                int hi = Math.Max(top, water); if (hi > c.Top) c.Top = hi;
            }
    }
}

/// <summary>
/// A dungeon floor as blocks: the room grid from Dungeon.Generate, 4 blocks per tile. Bedrock below and above and at the grid's edge
/// (nothing leaves the floor), a floor layer and walls and ceiling in the theme's blocks (data/dungeons.json "blocks"). The way from the
/// entry room to the boss room, the entry room and the boss room are protected: their floor never breaks and they stay clear of placed
/// blocks and flowing lava, so every floor can be finished. Ashen Depths has lava pools in room corners.
/// </summary>
public sealed class DungeonGen : IBlockGen
{
    public int MinY => -3;
    public int MaxY { get; }
    readonly bool[] _grid, _keep; readonly bool[]? _lava; readonly int _gw, _gh, _tile, _wallH, _ox, _oz;
    readonly byte _floor, _wall, _ceil, _bed;
    public DungeonGen(bool[] grid, bool[] keep, bool[]? lava, int gw, int gh, float tile, float wallH, Vector3 origin, string floor, string wall, string ceil)
    {
        _grid = grid; _keep = keep; _lava = lava; _gw = gw; _gh = gh; _tile = Mathf.RoundToInt(tile); _wallH = Mathf.RoundToInt(wallH);
        _ox = Mathf.RoundToInt(origin.X); _oz = Mathf.RoundToInt(origin.Z); MaxY = _wallH + 3;
        _floor = Blocks.Id(floor); _wall = Blocks.Id(wall); _ceil = Blocks.Id(ceil); _bed = Blocks.Bedrock;
        if (_floor == 0) _floor = Blocks.Id("cobblestone"); if (_wall == 0) _wall = Blocks.Id("dark_stone"); if (_ceil == 0) _ceil = _wall;
    }
    (int tx, int tz) Tile(int x, int z) => ((int)Math.Floor((x - _ox) / (float)_tile), (int)Math.Floor((z - _oz) / (float)_tile));
    bool Cell(bool[] a, int tx, int tz) => tx >= 0 && tz >= 0 && tx < _gw && tz < _gh && a[tz * _gw + tx];
    public bool InBounds(int cx, int cz)
    {
        int S = Blocks.Chunk; int x0 = cx * S, z0 = cz * S;
        return x0 + S > _ox - S && z0 + S > _oz - S && x0 < _ox + _gw * _tile + S && z0 < _oz + _gh * _tile + S;
    }
    public bool Protected(int x, int y, int z) { var (tx, tz) = Tile(x, z); return y < 0 && Cell(_keep, tx, tz); }
    public bool KeepClear(int x, int y, int z) { var (tx, tz) = Tile(x, z); return y >= 0 && y < _wallH && Cell(_keep, tx, tz); }

    public void Fill(BlockChunk c)
    {
        int S = c.S;
        for (int lz = 0; lz < S; lz++)
            for (int lx = 0; lx < S; lx++)
            {
                int x = c.Cx * S + lx, z = c.Cz * S + lz; var (tx, tz) = Tile(x, z);
                bool inside = tx >= 1 && tz >= 1 && tx < _gw - 1 && tz < _gh - 1, floor = Cell(_grid, tx, tz);
                for (int y = MinY; y < MaxY; y++)
                {
                    byte id;
                    if (!inside && !floor) id = _bed;                      // the edge of the floor: nothing digs out of the grid
                    else if (y < -1 || y > _wallH) id = _bed;
                    else if (y == -1) id = _lava != null && Cell(_lava, tx, tz) ? Blocks.Lava : _floor;
                    else if (y == _wallH) id = _ceil;
                    else id = floor ? (byte)0 : _wall;
                    c.Id[c.Index(lx, y, lz)] = id;
                }
                c.Top = MaxY;
            }
    }

    /// <summary>Tiles kept clear: the entry room, the boss room and the shortest way between them (4-way steps over floor tiles).</summary>
    public static bool[] KeepTiles(bool[] grid, int gw, int gh, IList<Dungeon.Room> rooms, int boss)
    {
        var keep = new bool[gw * gh];
        void Room(Dungeon.Room r) { for (int z = r.Z; z < r.Z + r.H; z++) for (int x = r.X; x < r.X + r.W; x++) if (x >= 0 && z >= 0 && x < gw && z < gh) keep[z * gw + x] = true; }
        if (rooms.Count == 0) return keep;
        Room(rooms[0]); if (boss >= 0 && boss < rooms.Count) Room(rooms[boss]);
        var a = rooms[0]; var b = rooms[Math.Clamp(boss, 0, rooms.Count - 1)];
        var prev = new int[gw * gh]; Array.Fill(prev, -1); var q = new Queue<int>(); int s0 = a.Cz * gw + a.Cx, goal = b.Cz * gw + b.Cx;
        q.Enqueue(s0); prev[s0] = s0;
        while (q.Count > 0)
        {
            int i = q.Dequeue(); if (i == goal) break; int x = i % gw, z = i / gw;
            foreach (var (dx, dz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, nz = z + dz; if (nx < 0 || nz < 0 || nx >= gw || nz >= gh) continue; int n = nz * gw + nx;
                if (!grid[n] || prev[n] >= 0) continue; prev[n] = i; q.Enqueue(n);
            }
        }
        if (prev[goal] >= 0) for (int i = goal; ; i = prev[i]) { keep[i] = true; if (i == s0) break; }
        return keep;
    }
}
