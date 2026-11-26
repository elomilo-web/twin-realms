using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// F-19 block blueprints, made in code from a few numbers and a seed (scripts/Structures.cs stamps them into the world).
/// Convention: local y 0 is the first layer above the floor surface; a blueprint's front faces local +Z (rot turns it).
/// Meta sides: 0 +X, 1 -X, 2 +Z, 3 -Z (stairs rise towards that side, doors/ladders sit against it, chests face it).
/// </summary>
public static class StructGen
{
    static byte I(string k) => Blocks.Id(k);
    public static uint Hash(int a, int b, int c, uint s)
    {
        unchecked { uint h = (uint)(a * 73856093 ^ b * 19349663 ^ c * 83492791) ^ s * 2654435761u; h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15; return h; }
    }
    public static float H01(int a, int b, int c, uint s) => (Hash(a, b, c, s) & 0xffffff) / 16777216f;

    // ---------------------------------------------------------------- house styles per region
    public sealed class Style { public byte Wall, Upper, Base, Frame, Roof, Ridge, Floor, Door; public bool Flat; public bool FrameLog => Frame == Blocks.Id("log"); }
    public static Style StyleFor(string region, uint seed)
    {
        byte planks = I("planks"), log = I("log"), cob = I("cobblestone"), sb = I("stone_bricks"), pl = I("plaster");
        int v = (int)(seed % 3);
        switch (region)
        {
            case "sun": return new Style { Wall = I("sandstone"), Upper = I("sandstone"), Base = I("sandstone"), Frame = v == 1 ? log : I("sandstone"), Roof = I("sandstone_stairs"), Ridge = I("sandstone_slab"), Floor = planks, Door = I("door"), Flat = v != 2 };
            case "frost": return new Style { Wall = I("dark_planks"), Upper = v == 0 ? pl : I("dark_planks"), Base = sb, Frame = log, Roof = I("slate_stairs"), Ridge = I("slate_slab"), Floor = planks, Door = I("door_dark") };
            case "mire": return new Style { Wall = planks, Upper = planks, Base = v == 0 ? cob : planks, Frame = log, Roof = I("thatch_stairs"), Ridge = I("thatch"), Floor = planks, Door = I("door") };
            case "ember": return new Style { Wall = I("dark_stone"), Upper = v == 1 ? I("brick") : I("dark_stone"), Base = I("dark_stone"), Frame = I("dark_planks"), Roof = I("slate_stairs"), Ridge = I("slate_slab"), Floor = I("dark_planks"), Door = I("door_dark") };
            case "elder": return v switch
            {
                0 => new Style { Wall = pl, Upper = pl, Base = sb, Frame = log, Roof = I("roof_stairs"), Ridge = I("roof_slab"), Floor = planks, Door = I("door") },
                1 => new Style { Wall = sb, Upper = pl, Base = sb, Frame = log, Roof = I("slate_stairs"), Ridge = I("slate_slab"), Floor = planks, Door = I("door_dark") },
                _ => new Style { Wall = planks, Upper = pl, Base = cob, Frame = log, Roof = I("roof_stairs"), Ridge = I("roof_slab"), Floor = planks, Door = I("door") },
            };
            default: return v switch
            {
                0 => new Style { Wall = planks, Upper = planks, Base = cob, Frame = log, Roof = I("roof_stairs"), Ridge = I("roof_slab"), Floor = planks, Door = I("door") },
                1 => new Style { Wall = pl, Upper = pl, Base = sb, Frame = log, Roof = I("slate_stairs"), Ridge = I("slate_slab"), Floor = planks, Door = I("door_dark") },
                _ => new Style { Wall = sb, Upper = pl, Base = cob, Frame = log, Roof = I("roof_stairs"), Ridge = I("roof_slab"), Floor = planks, Door = I("door") },
            };
        }
    }

    // ---------------------------------------------------------------- house
    /// <summary>
    /// A house W x D (odd, at least 5) with 1-3 floors of 4 blocks: a plinth row, walls with log corners and beam rows, glass windows,
    /// a working door in the middle of the front (+Z), a torch by the door and inside, a ladder to the upper floors, a chest,
    /// a stair gable roof (ridge along the longer side) or a flat roof with a parapet, and a chimney. Ground levelled under it.
    /// </summary>
    public static Blueprint House(Style s, int W, int D, int floors, uint seed, bool pad = true)
    {
        var b = new Blueprint(); W = Math.Max(5, W | 1); D = Math.Max(5, D | 1); floors = Math.Clamp(floors, 1, 3);
        int hw = W / 2, hd = D / 2, fh = 4, H = floors * fh;
        bool ridgeX = W >= D; int halfU = ridgeX ? hw : hd, halfV = ridgeX ? hd : hw;
        int roofTop = s.Flat ? H + 1 : H + halfV + 1;
        byte glass = I("glass"), torch = I("torch"), air = 0;
        if (pad)
        {
            b.PadBox(-hw - 2, -hd - 2, hw + 2, hd + 3, 255, Blocks.Dirt, roofTop + 2);
            b.PadBox(-1, hd + 1, 1, hd + 3, I("path"), Blocks.Dirt, roofTop + 2);
            b.PadBox(-hw, -hd, hw, hd, s.Base, s.Base, roofTop + 2);
        }
        b.Box(-hw + 1, -1, -hd + 1, hw - 1, -1, hd - 1, s.Floor);
        b.Box(-hw + 1, 0, -hd + 1, hw - 1, H - 2, hd - 1, air);
        for (int y = 0; y < H; y++)
            for (int z = -hd; z <= hd; z++)
                for (int x = -hw; x <= hw; x++)
                {
                    bool ex = Math.Abs(x) == hw, ez = Math.Abs(z) == hd; if (!ex && !ez) continue;
                    bool corner = ex && ez, beam = (y + 1) % fh == 0;
                    byte id, meta = 0;
                    if (corner) id = s.Frame;
                    else if (beam) { id = s.Frame; if (s.FrameLog) meta = ez ? (byte)1 : (byte)2; }
                    else if (y == 0) id = s.Base;
                    else
                    {
                        id = y < fh ? s.Wall : s.Upper;
                        int t = ez ? x : z, half = ez ? hw : hd, at = Math.Abs(t);
                        if (y % fh is 1 or 2 && at >= 1 && at <= half - 1 && (half - at) % 2 == 1) id = glass;
                    }
                    b.Set(x, y, z, id, meta);
                }
        b.Set(0, 0, hd, s.Door, 2); b.Set(0, 1, hd, s.Door, 2 | 4);
        b.Set(1, 2, hd + 1, torch, 1 + 3);                       // lantern beside the door (on the front wall)
        for (int f = 0; f < floors; f++) b.Set(0, f * fh + 2, -hd + 1, torch, 1 + 3);   // and one inside on the back wall per floor
        if (seed % 2 == 0) b.Set(hw - 1, 0, -hd + 1, I("chest"), 2);
        for (int k = 1; k < floors; k++) b.Box(-hw + 1, k * fh - 1, -hd + 1, hw - 1, k * fh - 1, hd - 1, s.Floor);
        if (floors > 1) for (int y = 0; y <= (floors - 1) * fh - 1; y++) b.Set(-hw + 1, y, -hd + 1, I("ladder"), 1);
        if (s.Flat)
        {
            b.Box(-hw + 1, H - 1, -hd + 1, hw - 1, H - 1, hd - 1, s.Wall);
            for (int z = -hd; z <= hd; z++) for (int x = -hw; x <= hw; x++) if (Math.Abs(x) == hw || Math.Abs(z) == hd) b.Set(x, H, z, (x + z) % 2 == 0 ? s.Wall : s.Ridge);
            return b;
        }
        void UV(int u, int v, int y, byte id, byte meta = 0) { if (ridgeX) b.Set(u, y, v, id, meta); else b.Set(v, y, u, id, meta); }
        byte up = ridgeX ? (byte)2 : (byte)0, down = ridgeX ? (byte)3 : (byte)1;   // stairs rising towards +v / -v
        for (int k = 0; k <= halfV + 1; k++)
        {
            int y = H - 1 + k, v0 = -(halfV + 1) + k, v1 = halfV + 1 - k;
            if (v0 < v1)
            {
                for (int u = -halfU - 1; u <= halfU + 1; u++) { UV(u, v0, y, s.Roof, up); UV(u, v1, y, s.Roof, down); }
                if (k >= 1) for (int v = v0 + 1; v < v1; v++) { UV(-halfU, v, y, s.Upper); UV(halfU, v, y, s.Upper); for (int u = -halfU + 1; u < halfU; u++) UV(u, v, y, air); }
            }
            else { for (int u = -halfU - 1; u <= halfU + 1; u++) UV(u, v0, y, s.Ridge); break; }
        }
        if (seed % 3 != 0) for (int y = 0; y <= roofTop; y++) b.Set(hw - 1, y, -hd, I("cobblestone"));
        return b;
    }

    // ---------------------------------------------------------------- small pieces
    public static Blueprint Well(byte rim, byte roof)
    {
        var b = new Blueprint(); byte water = Blocks.Water, fence = I("fence");
        b.PadBox(-3, -3, 3, 3, I("cobblestone"), Blocks.Dirt, 6);
        for (int z = -2; z <= 2; z++) for (int x = -2; x <= 2; x++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) == 2) b.Set(x, 0, z, rim);
            else { b.Set(x, 0, z, 0); b.Set(x, -1, z, water); b.Set(x, -2, z, water); b.Set(x, -3, z, rim); }
            b.Set(x, 3, z, roof);
        }
        foreach (int sx in new[] { -2, 2 }) foreach (int sz in new[] { -2, 2 }) { b.Set(sx, 1, sz, fence); b.Set(sx, 2, sz, fence); }
        b.Set(0, 4, 0, roof);
        return b;
    }
    /// <summary>A market stall facing +Z: fence posts, a plank counter at the front, a cloth awning with a front lip, stock behind.</summary>
    public static Blueprint Stall(byte awning, bool lantern, uint seed)
    {
        var b = new Blueprint(); byte fence = I("fence"), planks = I("planks");
        b.PadBox(-3, -2, 3, 3, 255, Blocks.Dirt, 5);
        foreach (int sx in new[] { -2, 2 }) foreach (int sz in new[] { -1, 1 }) for (int y = 0; y < 3; y++) b.Set(sx, y, sz, fence);
        for (int x = -1; x <= 1; x++) b.Set(x, 0, 1, planks);
        b.Set(seed % 2 == 0 ? -1 : 1, 0, -1, I("log")); b.Set(0, 0, -1, I("chest"), 2);
        b.Box(-2, 3, -1, 2, 3, 1, awning);
        for (int x = -2; x <= 2; x++) b.Set(x, 2, 2, awning, 4);
        if (lantern) b.Set(2, 2, -2, I("torch"), 1 + 2);
        return b;
    }
    public static Blueprint LampPost()
    {
        var b = new Blueprint(); byte fence = I("fence");
        b.Set(0, 0, 0, I("cobblestone")); b.Set(0, 1, 0, fence); b.Set(0, 2, 0, fence); b.Set(0, 3, 0, I("torch"));
        return b;
    }
    public static Blueprint Campfire()
    {
        var b = new Blueprint(); byte slab = I("cobble_slab");
        b.PadBox(-2, -2, 2, 2, 255, Blocks.Dirt, 3);
        b.Set(0, 0, 0, I("campfire")); b.Set(1, 0, 0, slab); b.Set(-1, 0, 0, slab); b.Set(0, 0, 1, slab); b.Set(0, 0, -1, slab);
        return b;
    }
    /// <summary>An A-frame tent facing +Z, 3 wide, len deep (open front, closed back).</summary>
    public static Blueprint Tent(byte cloth, int len)
    {
        var b = new Blueprint(); int z0 = -len / 2, z1 = z0 + len - 1;
        b.PadBox(-2, z0 - 1, 2, z1 + 1, 255, Blocks.Dirt, 4);
        for (int z = z0; z <= z1; z++) { b.Set(-1, 0, z, cloth); b.Set(1, 0, z, cloth); b.Set(0, 1, z, cloth); b.Set(0, 0, z, 0); }
        b.Set(0, 0, z0, cloth);
        b.Set(-1, 1, z0, I("fence")); b.Set(1, 1, z0, I("fence"));
        return b;
    }
    /// <summary>A wayshrine: a stone-brick plinth, a column with a glowing rune stone and a slab cap.</summary>
    public static Blueprint Wayshrine()
    {
        var b = new Blueprint(); byte sb = I("stone_bricks"), slab = I("brick_slab");
        b.PadBox(-2, -2, 2, 2, sb, I("cobblestone"), 7);
        b.Box(-1, 0, -1, 1, 0, 1, sb);
        foreach (int sx in new[] { -1, 1 }) foreach (int sz in new[] { -1, 1 }) b.Set(sx, 1, sz, slab);
        b.Set(0, 1, 0, sb); b.Set(0, 2, 0, I("rune_stone")); b.Set(0, 3, 0, sb); b.Set(0, 4, 0, slab);
        return b;
    }

    // ---------------------------------------------------------------- towers, walls, keeps, ruins
    /// <summary>A square tower (size odd): hollow, a door at the front, arrow slits, a roof deck and crenellations.</summary>
    public static Blueprint Tower(int size, int h, byte wall, bool door = true)
    {
        var b = new Blueprint(); int r = Math.Max(2, size / 2); byte slit = I("dark_stone");
        for (int y = 0; y < h; y++)
            for (int z = -r; z <= r; z++) for (int x = -r; x <= r; x++)
            {
                bool edge = Math.Abs(x) == r || Math.Abs(z) == r;
                if (y == h - 1 || edge) b.Set(x, y, z, wall); else b.Set(x, y, z, 0);
                if (y == 0 && edge) b.Post(x, -1, z, wall);
            }
        for (int y = 4; y < h - 2; y += 4) { b.Set(0, y, r, slit); b.Set(0, y, -r, slit); b.Set(r, y, 0, slit); b.Set(-r, y, 0, slit); b.Set(0, y + 1, r, slit); b.Set(0, y + 1, -r, slit); b.Set(r, y + 1, 0, slit); b.Set(-r, y + 1, 0, slit); }
        for (int z = -r; z <= r; z++) for (int x = -r; x <= r; x++) if ((Math.Abs(x) == r || Math.Abs(z) == r) && ((x + z) & 1) == 0) b.Set(x, h, z, wall);
        if (door) { b.Set(0, 0, r, I("door_dark"), 2); b.Set(0, 1, r, I("door_dark"), 2 | 4); b.Set(0, 2, r - 1, I("torch"), 0); }
        if (h > 6) for (int y = 0; y < h - 1; y++) b.Set(-r + 1, y, -r + 1, I("ladder"), 1);
        b.Set(-r + 1, h - 1, -r + 1, I("ladder"), 1);
        return b;
    }
    /// <summary>A circular town wall of radius R round the origin, 2 thick, h high, crenellated, open where the gates are.</summary>
    public static void RingWall(Blueprint b, float R, List<float> gates, int h, byte wall, bool ruined, uint seed)
    {
        int Ri = (int)R + 3;
        for (int z = -Ri; z <= Ri; z++) for (int x = -Ri; x <= Ri; x++)
        {
            float d = Mathf.Sqrt(x * x + z * z); if (d < R - 1 || d >= R + 1) continue;
            float a = Mathf.Atan2(z, x); bool gate = gates.Exists(g => Mathf.Abs(Mathf.AngleDifference(g, a)) * R < 2.6f);
            int top = h; if (ruined) { top = (int)(h * (.2f + .8f * H01(x, 0, z, seed))); if (H01(x / 3, 1, z / 3, seed) < .3f) continue; }
            for (int y = 0; y < top; y++) b.Set(x, y, z, gate && y <= 4 ? (byte)0 : wall);
            if (!gate) b.Post(x, -1, z, wall);
            if (!ruined && d >= R && ((int)Mathf.Floor((a + Mathf.Pi) * R) & 1) == 0) b.Set(x, h, z, wall);
        }
    }
    /// <summary>Wears a blueprint down into a ruin: columns broken off at random heights, holes, moss and rubble.</summary>
    public static void Ruin(Blueprint b, uint seed, int maxH)
    {
        byte sb = I("stone_bricks"), moss = I("mossy_bricks"), cob = I("cobblestone");
        var keys = new List<long>(b.Cells.Keys);
        foreach (var k in keys)
        {
            var (x, y, z) = Blueprint.U(k); ushort v = b.Cells[k]; byte id = (byte)(v & 255); if (id == 0) continue;
            int cut = (int)(1 + H01(x, 0, z, seed) * maxH * .85f);
            if (y > cut || H01(x, y, z, seed + 1) < .1f) { b.Cells.Remove(k); continue; }
            if (Blocks.Def(id).Shape is "door" or "post" or "ladder") { b.Cells.Remove(k); continue; }
            float r = H01(x, y, z, seed + 2);
            if (id == sb) b.Cells[k] = r < .45f ? moss : r < .62f ? cob : sb;
        }
    }

    // ---------------------------------------------------------------- dungeon and cave mouths
    /// <summary>A stone arch in a rock mound with a short dark tunnel (front +Z), torch posts at the sides.</summary>
    public static Blueprint Mouth(byte arch, uint seed)
    {
        var b = new Blueprint(); byte stone = Blocks.Stone, cob = I("cobblestone"), moss = I("mossy_bricks"), dark = I("dark_stone"), fence = I("fence");
        b.PadBox(-5, 0, 5, 6, 255, Blocks.Dirt, 9);
        b.PadBox(-2, 1, 2, 5, cob, Blocks.Dirt, 9);
        for (int z = -9; z <= -1; z++) for (int y = 0; y <= 10; y++) for (int x = -8; x <= 8; x++)
        {
            float e = (x / 8.5f) * (x / 8.5f) + (y / 10.5f) * (y / 10.5f) + ((z + 5f) / 4.8f) * ((z + 5f) / 4.8f);
            if (e >= 1 || z > -2) continue;
            float r = H01(x, y, z, seed); b.Set(x, y, z, r < .22f ? cob : r < .34f ? moss : stone);
            if (y == 0) b.Post(x, -1, z, stone);
        }
        for (int z = -1; z <= 1; z++)
        {
            for (int y = 0; y <= 5; y++) foreach (int x in new[] { -3, -2, 2, 3 }) b.Set(x, y, z, arch);
            b.Set(-1, 5, z, arch); b.Set(1, 5, z, arch); b.Set(0, 5, z, 0);
            for (int x = -3; x <= 3; x++) b.Set(x, 6, z, arch);
            for (int x = -2; x <= 2; x++) b.Set(x, 7, z, arch);
        }
        b.Set(0, 7, 1, I("rune_stone"));
        for (int z = -5; z <= 1; z++) for (int y = 0; y <= 4; y++) for (int x = -1; x <= 1; x++) b.Set(x, y, z, z == -5 ? dark : (byte)0);
        for (int z = -5; z <= 1; z++) for (int x = -1; x <= 1; x++) b.Set(x, -1, z, cob);
        foreach (int sx in new[] { -4, 4 }) { b.Set(sx, 0, 2, fence); b.Set(sx, 1, 2, fence); b.Set(sx, 2, 2, I("torch")); }
        return b;
    }

    // ---------------------------------------------------------------- the arena colosseum
    /// <summary>
    /// The Crimson Arena as blocks round its centre: sand floor (r &lt; 23), a podium wall with dark niches, four stepped seating tiers with
    /// pale seat lips (tops at 3, 3, 5, 7, 9), a 12-high outer wall with dark arch recesses and merlons, stone pylons with braziers,
    /// and the west gatehouse (passage |z| &lt; 3 west of x -19, flanking towers, portcullis lintel at x -24).
    /// </summary>
    public static Blueprint Arena()
    {
        var b = new Blueprint(); byte sand = I("sand"), sb = I("stone_bricks"), br = I("brick"), ds = I("dark_stone"), lip = I("sandstone");
        float bay = Mathf.Tau / 44f;
        for (int z = -38; z <= 38; z++) for (int x = -38; x <= 38; x++)
        {
            float fx = x + .5f, fz = z + .5f, r = Mathf.Sqrt(fx * fx + fz * fz), a = Mathf.Atan2(fz, fx);
            bool gh = fx < -21 && fx > -38 && Mathf.Abs(fz) < 8f;   // gatehouse ground
            if (r > 35.6f && !gh) continue;
            b.PadAt(x, z, r < 23f ? sand : sb, sb, 17);
            if (r < 23f || r > 35.6f) continue;
            bool passage = fx < -19 && Mathf.Abs(fz) < 3f;
            int h; byte id = sb; float inner;
            if (r < 24.4f) { h = 3; inner = 23f; }
            else if (r < 26.7f) { h = 3; id = br; inner = 24.4f; }
            else if (r < 29f) { h = 5; inner = 26.7f; }
            else if (r < 31.2f) { h = 7; inner = 29f; }
            else if (r < 33.2f) { h = 9; inner = 31.2f; }
            else { h = 12; inner = 33.2f; }
            float bf = (a + Mathf.Pi) / bay; int bi = (int)bf; float frac = bf - bi;
            for (int y = 0; y < h; y++)
            {
                if (passage && y < 5) continue;
                byte c = id;
                if (h == 3 && inner == 23f && r < 23.8f && bi % 2 == 0 && y < 2 && Mathf.Abs(frac - .5f) < .3f) c = ds;   // podium niches
                if (h == 12 && r > 34.3f && Mathf.Abs(frac - .5f) < .24f && (y is >= 1 and <= 3 || y is >= 6 and <= 8)) c = ds;   // arch recesses
                if (h == 12 && r > 34.3f && Mathf.Abs(frac - .5f) < .12f && (y == 4 || y == 9)) c = ds;
                if (y == h - 1 && h < 12 && r - inner < 1f && inner > 23f) c = lip;
                b.Set(x, y, z, c);
            }
            if (h == 12 && r > 34f && !passage && (((int)Mathf.Floor((a + Mathf.Pi) * 34f / 1.3f)) & 1) == 0) b.Set(x, 12, z, sb);
        }
        // gatehouse: side walls, the roof over the outer passage, two towers, the wall over the portcullis
        for (int x = -38; x <= -22; x++) foreach (int z in new[] { 3, -4 }) for (int y = 0; y <= 6; y++) b.Set(x, y, z, sb);
        for (int x = -38; x <= -33; x++) for (int z = -3; z <= 2; z++) { b.Set(x, 5, z, sb); b.Set(x, 6, z, sb); }
        foreach (int z0 in new[] { 3, -7 })
            for (int x = -28; x <= -24; x++) for (int z = z0; z <= z0 + 3; z++)
            {
                for (int y = 0; y <= 14; y++) b.Set(x, y, z, y is 9 or 10 && z == z0 + 1 && x == -24 ? ds : sb);
                if ((x == -28 || x == -24) && (z == z0 || z == z0 + 3)) b.Set(x, 15, z, sb);
            }
        for (int y = 5; y <= 12; y++) for (int z = -3; z <= 2; z++) b.Set(-24, y, z, sb);
        // pylons on the podium (braziers on every other one; none at the gate)
        for (int i = 0; i < 10; i++)
        {
            float a = (i + .5f) / 10f * Mathf.Tau; if (Mathf.Abs(Mathf.AngleDifference(a, Mathf.Pi)) < .3f) continue;
            int px = Mathf.FloorToInt(Mathf.Cos(a) * 24.6f), pz = Mathf.FloorToInt(Mathf.Sin(a) * 24.6f);
            for (int y = 3; y <= 8; y++) b.Set(px, y, pz, sb);
            b.Set(px, 9, pz, i % 2 == 0 ? I("campfire") : I("brick_slab"));
        }
        // the royal box across from the gate: a stone floor on the third tier
        for (int z = -3; z <= 2; z++) for (int x = 27; x <= 30; x++) b.Set(x, 6, z, sb);
        return b;
    }

    // ---------------------------------------------------------------- bridges (world coordinates: place at origin 0, 0, 0)
    /// <summary>A plank road bridge over a river from a to b, deck top at deckTop, log rails with fences, log piers down to the bed.</summary>
    public static Blueprint RiverBridge(Vector2 a, Vector2 bEnd, int deckTop)
    {
        var b = new Blueprint(); byte planks = I("planks"), log = I("log"), fence = I("fence");
        var dir = (bEnd - a).Normalized(); var side = new Vector2(-dir.Y, dir.X); float len = (bEnd - a).Length();
        var box = new Rect2(a, Vector2.Zero).Expand(bEnd).Grow(4f); int y = deckTop - 1;
        byte axis = Mathf.Abs(dir.X) > Mathf.Abs(dir.Y) ? (byte)1 : (byte)2;
        for (int z = Mathf.FloorToInt(box.Position.Y); z <= Mathf.CeilToInt(box.End.Y); z++)
            for (int x = Mathf.FloorToInt(box.Position.X); x <= Mathf.CeilToInt(box.End.X); x++)
            {
                var q = new Vector2(x + .5f, z + .5f) - a; float al = q.Dot(dir), ac = Mathf.Abs(q.Dot(side));
                if (al < -.5f || al > len + .5f || ac > 3.1f) continue;
                for (int yy = y + 1; yy <= y + 3; yy++) b.Set(x, yy, z, 0);
                if (ac <= 2.3f) b.Set(x, y, z, planks);
                else { b.Set(x, y, z, log, axis); b.Set(x, y + 1, z, fence); if (((int)al) % 5 == 2) b.Post(x, y - 1, z, log); }
            }
        return b;
    }
    /// <summary>A long stone sea bridge along an arch P(t) (t 0..1): 6 m deck in half-block steps, parapets, piers, lamp posts.</summary>
    public static Blueprint SeaBridge(Vector2 sA, Vector2 sB, Func<float, Vector3> P)
    {
        var b = new Blueprint(); byte sb = I("stone_bricks"), slab = I("brick_slab"), fence = I("fence");
        var dir = (sB - sA).Normalized(); var side = new Vector2(-dir.Y, dir.X); float len = (sB - sA).Length();
        var box = new Rect2(sA, Vector2.Zero).Expand(sB).Grow(5f);
        for (int z = Mathf.FloorToInt(box.Position.Y); z <= Mathf.CeilToInt(box.End.Y); z++)
            for (int x = Mathf.FloorToInt(box.Position.X); x <= Mathf.CeilToInt(box.End.X); x++)
            {
                var q = new Vector2(x + .5f, z + .5f) - sA; float al = q.Dot(dir), sc = q.Dot(side), ac = Mathf.Abs(sc);
                if (al < -.5f || al > len + .5f || ac > 3.9f) continue;
                float topF = P(Mathf.Clamp(al / len, 0, 1)).Y; int t2 = Mathf.RoundToInt(topF * 2); int yb = Mathf.FloorToInt(t2 / 2f); bool half = (t2 & 1) != 0;
                if (ac <= 3f)
                {
                    if (half) { b.Set(x, yb, z, slab); b.Set(x, yb - 1, z, sb); } else { b.Set(x, yb - 1, z, sb); b.Set(x, yb - 2, z, sb); }
                    for (int yy = half ? yb + 1 : yb; yy <= yb + 3; yy++) b.Set(x, yy, z, 0);
                    if (((int)al) % 30 < 4 && ac <= 2.5f && World.GroundAt(x + .5f, z + .5f) < yb - 4) b.Post(x, yb - 3, z, sb);
                }
                else
                {
                    int rt = half ? yb + 1 : yb;
                    for (int yy = yb - 2; yy <= rt; yy++) b.Set(x, yy, z, sb);
                    if (((int)al) % 40 == 20 && (((int)al / 40) % 2 == 0) == (sc > 0)) { b.Set(x, rt + 1, z, fence); b.Set(x, rt + 2, z, I("torch")); }
                }
            }
        return b;
    }
}
