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
    static readonly float[] Ao = { .5f, .66f, .83f, 1f };

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
        var defs = Blocks.Defs;
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
                    for (int f = 0; f < 6; f++)
                    {
                        var n = Nrm[f]; byte nb = At(x + n.X, y + n.Y, z + n.Z);
                        if (nb != 0 && nb < defs.Count) { var nd = defs[nb]; if (nd.Opaque || (nb == id && !nd.IsFluid)) continue; }
                        var s = Surf(Slot(id, f, meta)); int b0 = s.V.Count;
                        for (int k = 0; k < 4; k++)
                        {
                            int su = k == 1 || k == 2 ? 1 : -1, sv = k >= 2 ? 1 : -1;
                            var u = U[f]; var v = Vv[f];
                            var corner = new Vector3(x + P0[f].X + (su > 0 ? u.X : 0) + (sv > 0 ? v.X : 0), y + P0[f].Y + (su > 0 ? u.Y : 0) + (sv > 0 ? v.Y : 0), z + P0[f].Z + (su > 0 ? u.Z : 0) + (sv > 0 ? v.Z : 0));
                            int bx = x + n.X, by = y + n.Y, bz = z + n.Z;
                            bool s1 = Opaque(bx + su * u.X, by + su * u.Y, bz + su * u.Z), s2 = Opaque(bx + sv * v.X, by + sv * v.Y, bz + sv * v.Z);
                            bool cc = Opaque(bx + su * u.X + sv * v.X, by + su * u.Y + sv * v.Y, bz + su * u.Z + sv * v.Z);
                            int ao = s1 && s2 ? 0 : 3 - ((s1 ? 1 : 0) + (s2 ? 1 : 0) + (cc ? 1 : 0));
                            float l = Shade[f] * Ao[ao];
                            s.V.Add(corner); s.N.Add(new Vector3(n.X, n.Y, n.Z)); s.C.Add(new Color(l, l, l));
                        }
                        // clockwise seen from outside = Godot's front face
                        s.I.Add(b0); s.I.Add(b0 + 2); s.I.Add(b0 + 1); s.I.Add(b0); s.I.Add(b0 + 3); s.I.Add(b0 + 2);
                        if (def.Solid) { faces.Add(s.V[b0]); faces.Add(s.V[b0 + 2]); faces.Add(s.V[b0 + 1]); faces.Add(s.V[b0]); faces.Add(s.V[b0 + 3]); faces.Add(s.V[b0 + 2]); }
                    }
                }
        d.Faces = faces.ToArray();
        return d;

        void Fluid(BlockDef def, byte id, int x, int y, int z)
        {
            int lvl = MetaAt(x, y, z) & 7; bool covered = At(x, y + 1, z) == id;
            float top = covered ? 1f : Mathf.Max(.15f, .88f - lvl * .1f);
            var s = Surf(id * 3);
            void Quad(Vector3 a0, Vector3 a1, Vector3 a2, Vector3 a3, Vector3 nn, float l)
            {
                int b0 = s.V.Count; s.V.Add(a0); s.V.Add(a1); s.V.Add(a2); s.V.Add(a3); for (int k = 0; k < 4; k++) { s.N.Add(nn); s.C.Add(new Color(l, l, l)); }
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
                Quad(p, p + uu, p + uu + vv, p + vv, new Vector3(n.X, 0, n.Z), .85f);
            }
            byte dn = At(x, y - 1, z); if (dn == 0) Quad(new(x, y, z), new(x + 1, y, z), new(x + 1, y, z + 1), new(x, y, z + 1), Vector3.Down, .6f);
        }
    }
}
