using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Shared procedural graphics: tileable textures generated in code (plaster, stone blocks, planks, shingles, bark, rock, cobbles),
/// matching normal maps, and mesh builders (gable roof with thickness and overhang, lumpy faceted rocks/foliage, jagged pine tiers).
/// Everything is made at startup; nothing is loaded from disk.
/// </summary>
public static class Gfx
{
    const int S = 256;
    static readonly Dictionary<string, (ImageTexture alb, ImageTexture nrm)> _tex = new();

    // ---------------------------------------------------------------- tileable noise
    static float H(int x, int y, uint seed)
    {
        unchecked { uint h = (uint)x * 374761393u + (uint)y * 668265263u + seed * 2246822519u; h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xffffff) / 16777215f; }
    }
    static int Wrap(int i, int p) => ((i % p) + p) % p;
    static float VN(float x, float y, int per, uint seed)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y); float fx = x - xi, fy = y - yi; fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        int x0 = Wrap(xi, per), x1 = Wrap(xi + 1, per), y0 = Wrap(yi, per), y1 = Wrap(yi + 1, per);
        return Mathf.Lerp(Mathf.Lerp(H(x0, y0, seed), H(x1, y0, seed), fx), Mathf.Lerp(H(x0, y1, seed), H(x1, y1, seed), fx), fy);
    }
    /// <summary>Fractal noise over a 0..1 tile with base period per (tiles seamlessly).</summary>
    static float Fbm(float u, float v, int per, uint seed, int oct = 4)
    {
        float s = 0, a = .5f, n = 0; int p = per;
        for (int i = 0; i < oct; i++) { s += a * VN(u * p, v * p, p, seed + (uint)i * 31u); n += a; a *= .5f; p *= 2; }
        return s / n;
    }

    // ---------------------------------------------------------------- texture kinds
    /// <summary>Returns (albedo brightness 0..1, height 0..1) for pixel (x,y).</summary>
    static (float a, float h) Sample(string kind, int x, int y, Ctx c)
    {
        float u = x / (float)S, v = y / (float)S;
        switch (kind)
        {
            case "plaster":
            {
                float n = Fbm(u, v, 6, 11), f = Fbm(u, v, 32, 12, 2), sp = H(x, y, 13) > .985f ? -.12f : 0;
                return (.84f + .16f * n + (f - .5f) * .08f + sp, n * .6f + f * .4f);
            }
            case "stone":
            {
                // irregular block courses with mortar and bevelled edges
                const int rows = 5; float rh = S / (float)rows; int r = (int)(y / rh); float ry = y - r * rh;
                var b = c.Rows[r]; int k = 0; while (k < b.Count - 1 && Wrap(x - c.Off[r], S) >= b[k + 1]) k++;
                float xl = Wrap(x - c.Off[r], S) - b[k], xr = b[k + 1] - Wrap(x - c.Off[r], S);
                float edge = MathF.Min(MathF.Min(xl, xr), MathF.Min(ry, rh - ry));
                float n = Fbm(u, v, 8, 21), bv = H(r, k, 22);
                if (edge < 2.2f) return (.5f + n * .1f, .05f);
                float bev = Mathf.Clamp((edge - 2.2f) / 6f, 0, 1);
                return ((.74f + .2f * bv) * (.88f + .2f * n), .35f + .45f * bev + n * .2f);
            }
            case "planks":
            {
                const int planks = 4; float ph = S / (float)planks; int p = (int)(y / ph); float py = y - p * ph;
                float g = MathF.Sin((v * 40f + Fbm(u, v, 4, 31) * 6f + p * 1.7f) * Mathf.Tau * .5f) * .5f + .5f, n = Fbm(u, v * .25f + p * .25f, 16, 32, 3);
                float pv = H(p, 0, 33), knot = Fbm(u, v, 3, 34) > .74f ? -.15f : 0;
                if (py < 2f) return (.45f, 0f);
                float endJoint = Wrap(x + (int)(pv * S), S) < 2 ? -.35f : 0;
                return ((.78f + .14f * pv) * (.86f + .1f * g + .08f * n) + knot + endJoint, .6f + .2f * g + .2f * n + endJoint);
            }
            case "grain":
            {
                float g = MathF.Sin((v * 24f + Fbm(u, v, 4, 41) * 5f) * Mathf.Tau * .5f) * .5f + .5f, n = Fbm(u * .2f, v, 8, 42, 3);
                return (.8f + .1f * g + .1f * n, .5f + .3f * g + .2f * n);
            }
            case "shingle":
            {
                // rows run along u; each row is shaded at the top where the row above overlaps it
                const int rows = 8; float rh = S / (float)rows; int r = (int)(v * rows); float ry = (y - r * rh) / rh;
                float sw = S / 8f; float sx = x + (r % 2) * sw * .5f; int k = (int)(sx / sw); float kx = sx - k * sw;
                float tone = .8f + .2f * H(Wrap(k, 8), r, 51), n = Fbm(u, v, 16, 52, 3);
                float gap = kx < 1.6f ? -.3f : 0, lip = ry > .9f ? -.25f : 0;
                float shade = Mathf.Lerp(.72f, 1f, Mathf.Clamp(ry * 2.2f, 0, 1));
                return (tone * shade * (.9f + .1f * n) + gap + lip, ry * .8f + n * .2f + gap);
            }
            case "bark":
            {
                float n = Fbm(u, v * .15f, 12, 61), f = Fbm(u, v, 24, 62, 2);
                float ridge = 1f - MathF.Abs(n - .5f) * 2f;
                return (.66f + .3f * ridge + (f - .5f) * .1f, ridge * .8f + f * .2f);
            }
            case "rock":
            {
                float n = Fbm(u, v, 4, 71, 5), crack = 1f - Mathf.Clamp(MathF.Abs(Fbm(u, v, 3, 72) - .5f) / .012f, 0, 1), lich = Fbm(u, v, 10, 73, 3);
                return (.8f + .26f * n - crack * .14f - (lich > .66f ? .06f : 0), n - crack * .25f);
            }
            case "cobble":
            {
                // periodic voronoi: rounded stones with dark joints
                const int G = 7; float px = u * G, py = v * G; int cx = (int)px, cy = (int)py; float f1 = 9, f2 = 9; int id = 0;
                for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++)
                {
                    int gx = cx + i, gy = cy + j; int wx = Wrap(gx, G), wy = Wrap(gy, G);
                    float ox = gx + .15f + .7f * H(wx, wy, 81), oy = gy + .15f + .7f * H(wx, wy, 82);
                    float d = MathF.Sqrt((px - ox) * (px - ox) + (py - oy) * (py - oy));
                    if (d < f1) { f2 = f1; f1 = d; id = wx * 31 + wy; } else if (d < f2) f2 = d;
                }
                float e = f2 - f1, n = Fbm(u, v, 16, 83, 3);
                if (e < .06f) return (.45f, 0);
                float dome = Mathf.Clamp(e * 3f, 0, 1);
                return ((.72f + .25f * H(id, 0, 84)) * (.9f + .15f * n), .3f + .6f * dome + .1f * n);
            }
            case "cloth":
            {
                float w = ((x / 2 + y / 2) % 2 == 0) ? 1f : .9f, n = Fbm(u, v, 8, 91, 3);
                return (.86f + .1f * n * w, .5f * w + .5f * n);
            }
            default: // "noise": soft speckled dirt/sand
            {
                float n = Fbm(u, v, 8, 101), f = H(x, y, 102);
                return (.86f + .12f * n + (f - .5f) * .06f, n);
            }
        }
    }

    class Ctx { public List<int>[] Rows = new List<int>[5]; public int[] Off = new int[5]; }

    /// <summary>Albedo (grey, multiplies the material colour) and normal map for a texture kind, generated once.</summary>
    public static (ImageTexture alb, ImageTexture nrm) Get(string kind)
    {
        if (_tex.TryGetValue(kind, out var t)) return t;
        var c = new Ctx();
        for (int r = 0; r < 5; r++)
        {
            var l = new List<int> { 0 }; int x = 0, k = 0;
            while (x < S) { x += 40 + (int)(H(r, k++, 23) * 60); l.Add(Math.Min(x, S)); }
            if (S - l[^2] < 30 && l.Count > 2) l.RemoveAt(l.Count - 2);
            c.Rows[r] = l; c.Off[r] = (int)(H(r, 99, 24) * S);
        }
        var alb = Image.CreateEmpty(S, S, false, Image.Format.Rgb8); var hgt = Image.CreateEmpty(S, S, false, Image.Format.Rgb8);
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++)
        {
            var (a, h) = Sample(kind, x, y, c); a = Mathf.Clamp(a, 0, 1); h = Mathf.Clamp(h, 0, 1);
            alb.SetPixel(x, y, new Color(a, a, a)); hgt.SetPixel(x, y, new Color(h, h, h));
        }
        hgt.BumpMapToNormalMap(kind == "plaster" ? 2f : kind == "noise" ? 1.5f : 4f);
        alb.GenerateMipmaps(); hgt.GenerateMipmaps();
        t = (ImageTexture.CreateFromImage(alb), ImageTexture.CreateFromImage(hgt)); _tex[kind] = t; return t;
    }

    /// <summary>Puts a generated texture on a material. tileM = metres covered by one texture tile. Triplanar uses world space, so any mesh works.</summary>
    public static StandardMaterial3D Tex(StandardMaterial3D m, string kind, float tileM, bool triplanar = true, float bump = 1f)
    {
        var (a, n) = Get(kind);
        m.AlbedoTexture = a; m.NormalEnabled = true; m.NormalTexture = n; m.NormalScale = bump;
        m.TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic;
        float s = 1f / tileM; m.Uv1Scale = new Vector3(s, s, s);
        if (triplanar) { m.Uv1Triplanar = true; m.Uv1WorldTriplanar = true; m.Uv1TriplanarSharpness = 4f; }
        return m;
    }

    // ---------------------------------------------------------------- mesh building
    /// <summary>Adds a quad with Godot's clockwise front-face winding, facing the given normal.</summary>
    static void Quad(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
    {
        Tri(st, a, b, c, n, ua, ub, uc); Tri(st, a, c, d, n, ua, uc, ud);
    }
    static void Tri(SurfaceTool st, Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc)
    {
        if ((b - a).Cross(c - a).Dot(n) > 0) { (b, c) = (c, b); (ub, uc) = (uc, ub); }
        st.SetNormal(n); st.SetUV(ua); st.AddVertex(a); st.SetNormal(n); st.SetUV(ub); st.AddVertex(b); st.SetNormal(n); st.SetUV(uc); st.AddVertex(c);
    }

    /// <summary>
    /// Gable roof with real thickness and overhang. Ridge runs along Z. w = wall width (X), d = wall depth (Z), rh = ridge height above the wall top.
    /// Surface 0: the two roof slabs (UV in metres: u along the ridge, v down the slope). Surface 1: the two gable triangles (wall material).
    /// </summary>
    public static ArrayMesh GableRoof(float w, float d, float rh, float ovX = .7f, float ovZ = .6f, float thick = .22f)
    {
        float half = w / 2, s = rh / half, D = d / 2 + ovZ, ex = half + ovX, ey = -ovX * s;
        float slopeLen = MathF.Sqrt(ex * ex + (rh - ey) * (rh - ey));
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float side in new[] { -1f, 1f })
        {
            var up = new Vector3(side * rh, half, 0).Normalized(); var t = up * thick;
            Vector3 e0 = new(side * ex, ey, -D), e1 = new(side * ex, ey, D), r0 = new(0, rh, -D), r1 = new(0, rh, D);
            // top (shingles), underside (soffit), eave fascia, gable-end edges
            Quad(st, r0 + t, r1 + t, e1 + t, e0 + t, up, new(0, 0), new(2 * D, 0), new(2 * D, slopeLen), new(0, slopeLen));
            Quad(st, r0, e0, e1, r1, -up, new(0, 0), new(0, slopeLen), new(2 * D, slopeLen), new(2 * D, 0));
            var fasc = new Vector3(side, -.25f, 0).Normalized();
            Quad(st, e0, e0 + t, e1 + t, e1, fasc, new(0, 0), new(0, thick), new(2 * D, thick), new(2 * D, 0));
            foreach (float zs in new[] { -1f, 1f })
            {
                var a = zs < 0 ? r0 : r1; var b = zs < 0 ? e0 : e1;
                Quad(st, a, b, b + t, a + t, new Vector3(0, 0, zs), new(0, 0), new(slopeLen, 0), new(slopeLen, thick), new(0, thick));
            }
        }
        // ridge seam: close the small notch where the two slabs meet on top
        var mesh = st.Commit();
        var g = new SurfaceTool(); g.Begin(Mesh.PrimitiveType.Triangles);
        foreach (float zs in new[] { -1f, 1f })
        {
            float z = zs * (d / 2 + .02f);
            Tri(g, new Vector3(-half - .02f, -.02f, z), new Vector3(half + .02f, -.02f, z), new Vector3(0, rh, z), new Vector3(0, 0, zs), Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        g.Commit(mesh);
        return mesh;
    }

    static float N3(Vector3 p, float f, uint seed)
    {
        float x = p.X * f + seed * .37f, y = p.Y * f, z = p.Z * f - seed * .19f;
        return (World.Fbm(x + y * .71f, z - y * .53f, 3) + World.Fbm(z + 13.1f + y * .4f, x - 7.7f, 3)) * .5f;
    }

    /// <summary>Displaces a mesh along its normals with 3D-ish noise and returns it flat shaded (faceted low-poly look).</summary>
    public static ArrayMesh Lumpy(Mesh src, float amp, float freq, uint seed = 1, float squashY = 1f)
    {
        var arr = src is PrimitiveMesh pm ? pm.GetMeshArrays() : src.SurfaceGetArrays(0);
        var v = arr[(int)Mesh.ArrayType.Vertex].AsVector3Array();
        var idx = arr[(int)Mesh.ArrayType.Index].AsInt32Array();
        var outV = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++)
        {
            // radial push (not along per-face normals) so duplicated corner vertices move together and no cracks open
            var dir = v[i].LengthSquared() > 1e-6f ? v[i].Normalized() : Vector3.Up;
            var p = v[i] + dir * (N3(v[i], freq, seed) - .5f) * 2f * amp; p.Y *= squashY; outV[i] = p;
        }
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        if (idx.Length == 0) { idx = new int[v.Length]; for (int i = 0; i < idx.Length; i++) idx[i] = i; }
        for (int i = 0; i + 2 < idx.Length; i += 3)
        {
            Vector3 a = outV[idx[i]], b = outV[idx[i + 1]], c = outV[idx[i + 2]];
            var fn = (b - a).Cross(c - a); if (fn.LengthSquared() < 1e-10f) continue; fn = fn.Normalized();
            if (fn.Dot((a + b + c) / 3f) < 0) fn = -fn;                     // convex shapes around the origin: face outward
            Tri(st, a, b, c, fn, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        return st.Commit();
    }

    /// <summary>Sword blade along -Z from the guard: diamond cross-section, fuller-less, tapering to a point.</summary>
    public static ArrayMesh Blade(float len, float w, float t)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3 E(int k, float z, float s) => k switch { 0 => new Vector3(-w / 2 * s, 0, z), 1 => new Vector3(0, t / 2 * s, z), 2 => new Vector3(w / 2 * s, 0, z), _ => new Vector3(0, -t / 2 * s, z) };
        var tip = new Vector3(0, 0, -len - w * 1.3f);
        for (int k = 0; k < 4; k++)
        {
            int k2 = (k + 1) % 4; Vector3 a0 = E(k, 0, 1), a1 = E(k2, 0, 1), b0 = E(k, -len, .82f), b1 = E(k2, -len, .82f);
            var mid = (a0 + a1 + b0 + b1) / 4; var n = (a1 - a0).Cross(b0 - a0).Normalized(); if (n.Dot(new Vector3(mid.X, mid.Y, 0)) < 0) n = -n;
            Quad(st, a0, a1, b1, b0, n, Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            var nt = (b1 - b0).Cross(tip - b0).Normalized(); var mt = (b0 + b1 + tip) / 3; if (nt.Dot(new Vector3(mt.X, mt.Y, 0)) < 0) nt = -nt;
            Tri(st, b0, b1, tip, nt, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        return st.Commit();
    }

    /// <summary>Convex 2D outline (u = Z, v = Y) extruded along X by depth. Used for shields and plates.</summary>
    public static ArrayMesh ExtrudeX(Vector2[] o, float depth)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles); float h = depth / 2;
        Vector3 V(Vector2 p, float x) => new(x, p.Y, p.X);
        for (int i = 1; i + 1 < o.Length; i++) { Tri(st, V(o[0], -h), V(o[i], -h), V(o[i + 1], -h), Vector3.Left, Vector2.Zero, Vector2.Zero, Vector2.Zero); Tri(st, V(o[0], h), V(o[i], h), V(o[i + 1], h), Vector3.Right, Vector2.Zero, Vector2.Zero, Vector2.Zero); }
        var c = Vector2.Zero; foreach (var p in o) c += p; c /= o.Length;
        for (int i = 0; i < o.Length; i++)
        {
            var a = o[i]; var b = o[(i + 1) % o.Length]; var e = b - a; var n2 = new Vector2(e.Y, -e.X).Normalized(); if (n2.Dot((a + b) / 2 - c) < 0) n2 = -n2;
            Quad(st, V(a, -h), V(b, -h), V(b, h), V(a, h), new Vector3(0, n2.Y, n2.X), Vector2.Zero, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
        return st.Commit();
    }

    /// <summary>
    /// Merges the direct MeshInstance3D children of a node into one mesh per material (fewer draw calls). Children that
    /// are other nodes (pivots, lights) are left alone, so animated limbs keep working: call it on each pivot separately.
    /// </summary>
    public static void MergeChildren(Node3D parent)
    {
        var groups = new Dictionary<Material, SurfaceTool>(); var drop = new List<MeshInstance3D>();
        foreach (var c in parent.GetChildren())
        {
            if (c is not MeshInstance3D mi || mi.Mesh == null || mi.MaterialOverride == null || mi.Mesh.GetSurfaceCount() != 1) continue;
            if (!groups.TryGetValue(mi.MaterialOverride, out var st)) { groups[mi.MaterialOverride] = st = new SurfaceTool(); }
            st.AppendFrom(mi.Mesh, 0, mi.Transform); drop.Add(mi);
        }
        if (drop.Count < 2) return;
        foreach (var mi in drop) { parent.RemoveChild(mi); mi.QueueFree(); }
        foreach (var kv in groups) parent.AddChild(new MeshInstance3D { Mesh = kv.Value.Commit(), MaterialOverride = kv.Key });
    }

    static readonly Dictionary<int, ArrayMesh> _boulders = new();
    /// <summary>Faceted noise boulder of radius ~1 (a few cached variants).</summary>
    public static ArrayMesh Boulder(int variant)
    {
        variant = Math.Abs(variant) % 4;
        if (!_boulders.TryGetValue(variant, out var m)) _boulders[variant] = m = Lumpy(new SphereMesh { Radius = 1f, Height = 2f, RadialSegments = 10, Rings = 6 }, .28f, 1.4f, (uint)variant * 7 + 5, .85f);
        return m;
    }

    /// <summary>One pine tier: a cone whose lower rim is jagged and droops, with a shallow underside so it is not hollow from below.</summary>
    public static void PineTier(SurfaceTool st, float y0, float r, float h, int seg, uint seed)
    {
        var apex = new Vector3(0, y0 + h, 0); var under = new Vector3(0, y0 + h * .22f, 0);
        var rim = new Vector3[seg];
        for (int i = 0; i < seg; i++)
        {
            float a = i / (float)seg * Mathf.Tau + H(i, 1, seed) * .3f, rr = r * (i % 2 == 0 ? 1f : .78f) * (.9f + .2f * H(i, 2, seed));
            rim[i] = new Vector3(MathF.Cos(a) * rr, y0 - (i % 2 == 0 ? h * .12f : 0) + H(i, 3, seed) * h * .06f, MathF.Sin(a) * rr);
        }
        for (int i = 0; i < seg; i++)
        {
            var a = rim[i]; var b = rim[(i + 1) % seg];
            var n = (b - a).Cross(apex - a).Normalized(); if (n.Dot(a + b - 2 * new Vector3(0, y0, 0)) < 0) n = -n;
            Tri(st, apex, a, b, n, Vector2.Zero, Vector2.Zero, Vector2.Zero);
            var nu = (b - a).Cross(under - a).Normalized(); if (nu.Y > 0) nu = -nu;
            Tri(st, under, a, b, nu, Vector2.Zero, Vector2.Zero, Vector2.Zero);
        }
    }

    public static ArrayMesh Pine(uint seed)
    {
        var st = new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        PineTier(st, 1.7f, 2.3f, 2.6f, 10, seed); PineTier(st, 3.1f, 1.85f, 2.4f, 9, seed + 1); PineTier(st, 4.5f, 1.4f, 2.2f, 8, seed + 2); PineTier(st, 5.9f, .9f, 2.1f, 7, seed + 3);
        return st.Commit();
    }
}
