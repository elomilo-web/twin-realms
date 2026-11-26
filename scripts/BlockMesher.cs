using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>A chunk's finished mesh: one surface per material, plus the faces for its collision shape. Built off the main thread.</summary>
public sealed class BlockMeshData
{
    public sealed class Surf { public readonly List<Vector3> V = new(), N = new(); public readonly List<Color> C = new(); public readonly List<int> I = new(); }
    public readonly Dictionary<int, Surf> Surfs = new();   // key = material slot (BlockMesher.Slot)
    public Vector3[] Faces = Array.Empty<Vector3>();
    public int Version;
    public readonly List<Transform3D> Tufts = new(); public readonly List<Color> TuftCols = new(); public readonly List<float> TuftKeep = new();   // grass tufts on top faces (keep = 0..1 hash, compared with Game.Set.Grass)
}

/// <summary>
/// Builds chunk meshes: a face is drawn only where a block meets air or a see-through neighbour of another kind (hidden faces culled);
/// each corner gets ambient occlusion from the three blocks around it, faces get a fixed light by direction. Fluids get their own
/// surfaces with the top lowered by flow level; they have no collision. Unknown neighbours (chunks not made yet) count as solid.
/// </summary>
public static class BlockMesher
{
    // face order: +Y, -Y, +X, -X, +Z, -Z ; each with the unit-cube origin p, axes u and v (u x v = normal, so p, p+u, p+u+v, p+v run anticlockwise seen from outside)
    static readonly Vector3I[] Nrm = { new(0, 1, 0), new(0, -1, 0), new(1, 0, 0), new(-1, 0, 0), new(0, 0, 1), new(0, 0, -1) };
    static readonly Vector3I[] P0 = { new(0, 1, 0), new(0, 0, 0), new(1, 0, 0), new(0, 0, 0), new(0, 0, 1), new(0, 0, 0) };
    static readonly Vector3I[] U = { new(0, 0, 1), new(1, 0, 0), new(0, 1, 0), new(0, 0, 1), new(1, 0, 0), new(0, 1, 0) };
    static readonly Vector3I[] Vv = { new(1, 0, 0), new(0, 0, 1), new(0, 0, 1), new(0, 1, 0), new(0, 1, 0), new(1, 0, 0) };
    static readonly float[] Shade = { 1f, .58f, .82f, .82f, .9f, .9f };
    static readonly float[] Ao = { .38f, .58f, .8f, 1f };   // stronger creases

    // clockwise seen from outside = Godot's front face
    static void Tri(BlockMeshData.Surf s, int b0) { s.I.Add(b0); s.I.Add(b0 + 2); s.I.Add(b0 + 1); s.I.Add(b0); s.I.Add(b0 + 3); s.I.Add(b0 + 2); }
    static void Face6(List<Vector3> f, Vector3 a, Vector3 b, Vector3 c, Vector3 d) { f.Add(a); f.Add(c); f.Add(b); f.Add(a); f.Add(d); f.Add(c); }

    /// <summary>Material slot of a block face: id * 3 + (0 top, 1 side, 2 bottom). Fluids: their own id * 3.</summary>
    public static int Slot(byte id, int face, byte meta)
    {
        int f = face == 0 ? 0 : face == 1 ? 2 : 1;
        int axis = meta & 3;   // logs lying along x or z show their end grain on those faces
        if (axis == 1) f = face is 2 or 3 ? 0 : 1; else if (axis == 2) f = face is 4 or 5 ? 0 : 1;
        return id * 3 + f;
    }

    public static BlockMeshData Build(BlockArea a, BlockChunk c)
    {
        var d = new BlockMeshData { Version = c.Version }; var faces = new List<Vector3>();
        int S = c.S, x0 = c.Cx * S, z0 = c.Cz * S, yTop = Math.Min(c.Top + 1, c.MinY + c.H);
        var defs = Blocks.Defs; var lips = new Dictionary<byte, Color?>();
        byte At(int x, int y, int z)
        {
            if (y < c.MinY) return Blocks.Bedrock; if (y >= c.MinY + c.H) return 0;
            int lx = x - x0, lz = z - z0;
            if (lx >= 0 && lz >= 0 && lx < S && lz < S) return c.Id[c.Index(lx, y, lz)];
            byte b = a.Get(x, y, z); return b == 255 ? Blocks.Bedrock : b;   // a neighbour not made yet: treat as solid rock (no face)
        }
        byte MetaAt(int x, int y, int z) { int lx = x - x0, lz = z - z0; return lx >= 0 && lz >= 0 && lx < S && lz < S && y >= c.MinY && y < c.MinY + c.H ? c.Meta[c.Index(lx, y, lz)] : a.GetMeta(x, y, z); }
        bool Opaque(int x, int y, int z) { var b = At(x, y, z); return b < defs.Count && defs[b].Opaque; }
        BlockMeshData.Surf Surf(int slot) { if (!d.Surfs.TryGetValue(slot, out var s)) d.Surfs[slot] = s = new BlockMeshData.Surf(); return s; }

        for (int y = c.MinY; y < yTop; y++)
            for (int lz = 0; lz < S; lz++)
                for (int lx = 0; lx < S; lx++)
                {
                    byte id = c.Id[c.Index(lx, y, lz)]; if (id == 0 || id >= defs.Count) continue;
                    var def = defs[id]; int x = x0 + lx, z = z0 + lz;
                    if (def.IsFluid) { Fluid(def, id, x, y, z); continue; }
                    byte meta = c.Meta[c.Index(lx, y, lz)];
                    // per-block hash: brightness +-9% and a slight warm/cool hue shift, same on every face of the block
                    uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791); h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
                    float r1 = (h & 255) / 255f, r2 = ((h >> 8) & 255) / 255f, r3 = ((h >> 16) & 255) / 255f;
                    float bright = 1f + (r1 - .5f) * .18f;
                    var jit = new Color(bright * (1f + (r2 - .5f) * .10f), bright, bright * (1f + (r3 - .5f) * .10f));
                    if (def.Shape == "post")
                    {
                        // a torch: a thin stick (side material) with a flame block on top (top material, glowing), no collision
                        Box(id, new Vector3(x + .45f, y, z + .45f), new Vector3(.1f, .55f, .1f), jit.G, false, 1);
                        Box(id, new Vector3(x + .43f, y + .53f, z + .43f), new Vector3(.14f, .07f, .14f), jit.G * .8f, false, 2);   // charred wrap
                        Box(id, new Vector3(x + .435f, y + .6f, z + .435f), new Vector3(.13f, .16f, .13f), 1.4f, false, 0);
                        Box(id, new Vector3(x + .465f, y + .76f, z + .465f), new Vector3(.07f, .07f, .07f), 1.4f, false, 0);
                        continue;
                    }
                    if (def.Shape is "stairs" or "slab" or "fence")
                    {
                        float l0 = jit.G;
                        if (def.Shape == "slab")
                        {
                            float sy = (meta & 4) != 0 ? .5f : 0f;   // meta & 4 = the upper half of the cell
                            Box(id, new Vector3(x, y + sy, z), new Vector3(1, .5f, 1), l0, true);
                        }
                        else if (def.Shape == "stairs")
                        {
                            // a half-height step plus the upper half on the side it rises towards (meta & 3: 0 +X, 1 -X, 2 +Z, 3 -Z)
                            Box(id, new Vector3(x, y, z), new Vector3(1, .5f, 1), l0, true);
                            var (mn, sz) = (meta & 3) switch { 0 => (new Vector3(.5f, .5f, 0), new Vector3(.5f, .5f, 1)), 1 => (new Vector3(0, .5f, 0), new Vector3(.5f, .5f, 1)),
                                                              2 => (new Vector3(0, .5f, .5f), new Vector3(1, .5f, .5f)), _ => (new Vector3(0, .5f, 0), new Vector3(1, .5f, .5f)) };
                            Box(id, new Vector3(x, y, z) + mn, sz, l0, true);
                        }
                        else
                        {
                            // fence: a post with two rails to each neighbouring fence or solid block
                            Box(id, new Vector3(x + .375f, y, z + .375f), new Vector3(.25f, 1f, .25f), l0, true);
                            for (int f = 2; f < 6; f++)
                            {
                                var n = Nrm[f]; byte nb = At(x + n.X, y, z + n.Z);
                                if (nb == 0 || nb >= defs.Count || !(defs[nb].Shape == "fence" || defs[nb].Opaque)) continue;
                                for (int r = 0; r < 2; r++)
                                {
                                    float ry = r == 0 ? .3f : .7f;
                                    Vector3 mn = n.X > 0 ? new(.625f, ry, .44f) : n.X < 0 ? new(0, ry, .44f) : n.Z > 0 ? new(.44f, ry, .625f) : new(.44f, ry, 0);
                                    Vector3 sz = n.X != 0 ? new(.375f, .12f, .12f) : new(.12f, .12f, .375f);
                                    Box(id, new Vector3(x, y, z) + mn, sz, l0 * .95f, true);
                                }
                            }
                        }
                        continue;
                    }
                    if (def.Shape is "ladder" or "door")
                    {
                        // thin boxes against one side of the cell; meta & 3 = which side the wall/panel is on (0 +X, 1 -X, 2 +Z, 3 -Z), meta & 4 = upper half of a door
                        int dir = meta & 3; float l0 = jit.G;
                        void Part(float d0, float d1, float t0, float t1, float y0, float y1, bool coll)
                        {
                            Vector3 mn, mx;
                            switch (dir)
                            {
                                case 0: mn = new(1 - d1, y0, t0); mx = new(1 - d0, y1, t1); break;
                                case 1: mn = new(d0, y0, t0); mx = new(d1, y1, t1); break;
                                case 2: mn = new(t0, y0, 1 - d1); mx = new(t1, y1, 1 - d0); break;
                                default: mn = new(t0, y0, d0); mx = new(t1, y1, d1); break;
                            }
                            Box(id, new Vector3(x, y, z) + mn, mx - mn, l0, coll);
                        }
                        if (def.Shape == "ladder")
                        {
                            Part(0f, .08f, .1f, .2f, 0f, 1f, false); Part(0f, .08f, .8f, .9f, 0f, 1f, false);
                            for (int r = 0; r < 4; r++) { float ry = .08f + r * .25f; Part(.02f, .1f, .2f, .8f, ry, ry + .07f, false); }
                        }
                        else
                        {
                            if (def.Key.EndsWith("_open")) dir = dir switch { 0 => 2, 2 => 1, 1 => 3, _ => 0 };   // swung 90 degrees about the corner
                            Part(0f, .12f, 0f, 1f, 0f, 1f, def.Solid);
                        }
                        continue;
                    }
                    var lip = Lip(id);
                    if (def.Tufts > 0f && At(x, y + 1, z) == 0)
                    {
                        var tc = lip ?? new Color(.5f, .8f, .35f); uint th = h;
                        float Tf() { th = th * 1664525u + 1013904223u; return (th >> 8 & 0xffff) / 65535f; }
                        int cnt = Mathf.Max(1, Mathf.RoundToInt(def.Tufts * (1 + (int)(r1 * 1.99f))));   // GrassMesh is a multi-blade clump: 1-2 per block is plenty
                        for (int t = 0; t < cnt; t++)
                        {
                            float sc = .6f + Tf() * .5f;
                            var basis = Basis.FromEuler(new Vector3(0, Tf() * Mathf.Tau, 0)).Scaled(new Vector3(sc, sc * (.85f + Tf() * .3f), sc));
                            d.Tufts.Add(new Transform3D(basis, new Vector3(x + .1f + Tf() * .8f, y + 1f - .02f, z + .1f + Tf() * .8f)));
                            float v = .85f + Tf() * .3f; d.TuftCols.Add(new Color(tc.R * v, tc.G * v, tc.B * v)); d.TuftKeep.Add(Tf());
                        }
                    }
                    for (int f = 0; f < 6; f++)
                    {
                        var n = Nrm[f]; byte nb = At(x + n.X, y + n.Y, z + n.Z);
                        if (nb != 0 && nb < defs.Count) { var nd = defs[nb]; if (nd.Opaque || (nb == id && !nd.IsFluid)) continue; }
                        var s = Surf(Slot(id, f, meta)); int b0 = s.V.Count;
                        var pos = new Vector3[4]; var lc = new Color[4]; bool[] hi = new bool[4];
                        for (int k = 0; k < 4; k++)
                        {
                            int su = k == 1 || k == 2 ? 1 : -1, sv = k >= 2 ? 1 : -1;
                            var u = U[f]; var v = Vv[f];
                            pos[k] = new Vector3(x + P0[f].X + (su > 0 ? u.X : 0) + (sv > 0 ? v.X : 0), y + P0[f].Y + (su > 0 ? u.Y : 0) + (sv > 0 ? v.Y : 0), z + P0[f].Z + (su > 0 ? u.Z : 0) + (sv > 0 ? v.Z : 0));
                            hi[k] = f >= 2 && (u.Y != 0 ? su > 0 : sv > 0);   // top edge of a side face
                            int bx = x + n.X, by = y + n.Y, bz = z + n.Z;
                            bool s1 = Opaque(bx + su * u.X, by + su * u.Y, bz + su * u.Z), s2 = Opaque(bx + sv * v.X, by + sv * v.Y, bz + sv * v.Z);
                            bool cc = Opaque(bx + su * u.X + sv * v.X, by + su * u.Y + sv * v.Y, bz + su * u.Z + sv * v.Z);
                            int ao = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cc ? 1 : 0));
                            float l = Shade[f] * Ao[ao];
                            lc[k] = new Color(l * jit.R, l * jit.G, l * jit.B);
                        }
                        if (lip is Color lp && f >= 2)
                        {
                            // side of a grass-like block: dark earthy body fading darker toward the foot, plus a thin lip band under the top edge
                            const float band = .2f;
                            var mid = new Vector3[4]; var midLc = new Color[4];
                            for (int k = 0; k < 4; k++)
                            {
                                int o = U[f].Y != 0 ? (k == 0 ? 1 : k == 1 ? 0 : k == 2 ? 3 : 2) : (k == 0 ? 3 : k == 3 ? 0 : k == 1 ? 2 : 1);   // same horizontal spot, other height
                                mid[k] = hi[k] ? pos[k].Lerp(pos[o], band) : pos[o].Lerp(pos[k], band);
                                midLc[k] = hi[k] ? lc[k].Lerp(lc[o], band) : lc[o].Lerp(lc[k], band);
                            }
                            var nrm = new Vector3(n.X, n.Y, n.Z);
                            for (int k = 0; k < 4; k++)   // body: from just under the lip down to the foot, darker toward the bottom
                            {
                                var bc = hi[k] ? midLc[k] : lc[k]; float fall = hi[k] ? 1f : .72f;
                                s.V.Add(hi[k] ? mid[k] : pos[k]); s.N.Add(nrm); s.C.Add(new Color(bc.R * .86f * fall, bc.G * .82f * fall, bc.B * .8f * fall));
                            }
                            Tri(s, b0); if (def.Solid) Face6(faces, pos[0], pos[1], pos[2], pos[3]);
                            int b1 = s.V.Count;
                            for (int k = 0; k < 4; k++)   // lip band: tinted green-ish at the top edge
                            {
                                var lcol = hi[k] ? lc[k] : midLc[k];
                                s.V.Add(hi[k] ? pos[k] : mid[k]); s.N.Add(nrm); s.C.Add(new Color(lcol.R * lp.R, lcol.G * lp.G, lcol.B * lp.B));
                            }
                            Tri(s, b1);
                            continue;
                        }
                        for (int k = 0; k < 4; k++) { s.V.Add(pos[k]); s.N.Add(new Vector3(n.X, n.Y, n.Z)); s.C.Add(lc[k]); }
                        Tri(s, b0);
                        if (def.Solid) Face6(faces, pos[0], pos[1], pos[2], pos[3]);
                    }
                }
        d.Faces = faces.ToArray();
        return d;

        // an axis-aligned box (min corner + size) as 6 faces; coll adds it to the collision faces; face >= 0 = every face uses that material (0 top, 1 side, 2 bottom)
        void Box(byte id, Vector3 bas, Vector3 size, float lit, bool coll, int face = -1)
        {
            for (int f = 0; f < 6; f++)
            {
                var ps = Surf(face >= 0 ? id * 3 + face : Slot(id, f, 0)); int pb = ps.V.Count; float l = Shade[f] * lit; var q = new Vector3[4];
                for (int k = 0; k < 4; k++)
                {
                    int su = k == 1 || k == 2 ? 1 : 0, sv = k >= 2 ? 1 : 0; var u = U[f]; var v = Vv[f];
                    var o = new Vector3(P0[f].X + su * u.X + sv * v.X, P0[f].Y + su * u.Y + sv * v.Y, P0[f].Z + su * u.Z + sv * v.Z);
                    q[k] = bas + o * size; ps.V.Add(q[k]); ps.N.Add(new Vector3(Nrm[f].X, Nrm[f].Y, Nrm[f].Z)); ps.C.Add(new Color(l, l, l));
                }
                Tri(ps, pb);
                if (coll) Face6(faces, q[0], q[1], q[2], q[3]);
            }
        }

        // lip tint of a block's side material ("lip" colour in data/blocks.json), cached per mesh build
        Color? Lip(byte id)
        {
            if (lips.TryGetValue(id, out var have)) return have;
            Color? r = null; var m = defs[id].Mat;
            if (m.ValueKind == System.Text.Json.JsonValueKind.Object && m.TryGetProperty("side", out var sd) && sd.ValueKind == System.Text.Json.JsonValueKind.Object && sd.TryGetProperty("lip", out var lv) && lv.ValueKind == System.Text.Json.JsonValueKind.String)
                r = Color.FromHtml(lv.GetString() ?? "#ffffff");
            lips[id] = r; return r;
        }

        void Fluid(BlockDef def, byte id, int x, int y, int z)
        {
            int lvl = MetaAt(x, y, z) & 7; bool covered = At(x, y + 1, z) == id;
            float top = covered ? 1f : Mathf.Max(.15f, .88f - lvl * .1f);
            var s = Surf(id * 3);
            void Quad(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 a3, Vector3 nn, float l, bool side = false)
            {
                // COLOR.b = 0 marks a vertical face (the water shader turns it into falling water); the sea plane has no colours (white)
                int b0 = s.V.Count; s.V.Add(a0); s.V.Add(a1); s.V.Add(a2); s.V.Add(a3); for (int k = 0; k < 4; k++) { s.N.Add(nn); s.C.Add(new Color(l, l, side ? 0f : l)); }
                s.I.Add(b0); s.I.Add(b0 + 2); s.I.Add(b0 + 1); s.I.Add(b0); s.I.Add(b0 + 3); s.I.Add(b0 + 2);
            }
            if (!covered) { byte up = At(x, y + 1, z); if (up == 0 || !defs[up].Opaque) Quad(new(x, y + top, z), new(x, y + top, z + 1), new(x + 1, y + top, z + 1), new(x + 1, y + top, z), Vector3.Up, 1f); }
            for (int f = 2; f < 6; f++)
            {
                var n = Nrm[f]; byte nb = At(x + n.X, y, z + n.Z);
                if (nb == id || (nb != 0 && nb < defs.Count && defs[nb].Opaque)) continue;
                var u = U[f]; var v = Vv[f]; var p = new Vector3(x + P0[f].X, y, z + P0[f].Z);
                // side faces: v is "up" for +X/-X? use the axis that is vertical
                Vector3 uu = new(u.X, u.Y, u.Z), vv = new(v.X, v.Y, v.Z);
                if (uu.Y > 0) uu *= top; else vv *= top;
                Quad(p, p + uu, p + uu + vv, p + vv, new Vector3(n.X, 0, n.Z), .85f, true);
            }
            byte dn = At(x, y - 1, z); if (dn == 0) Quad(new(x, y, z), new(x + 1, y, z), new(x + 1, y, z + 1), new(x, y, z + 1), Vector3.Down, .6f);
        }
    }
}
