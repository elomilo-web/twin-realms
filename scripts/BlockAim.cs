using System;
using Godot;

namespace TwinRealms;

/// <summary>
/// The one block-aiming function, shared by the HUD reticle, the outline, the ghost, digging, placing, the crack overlay, door and chest
/// use and the pickaxe swing (scripts/PlayerBlocks.cs asks once per drawn frame and everything reads that answer).
/// - The reticle is one screen point, <see cref="Reticle"/> (the centre of the visible screen); the HUD draws it there.
/// - The ray is the camera ray through exactly that point (Camera3D.ProjectRayOrigin/Normal: the over-the-shoulder HOffset/VOffset of
///   CameraRig and the window stretch are included), stepped through the block grid cell by cell (DDA; water and lava do not stop it).
/// - Blocks that do not fill their cell (slabs, stairs, fences, ladders, doors, torches, chests) are hit only where they are drawn: the ray
///   passes through the empty part of their cell to whatever is behind (they used to count as full cubes).
/// - Reach is measured from the hero's eye to the hit point, not from the camera; blocks more than a metre behind the hero along the view
///   (between the camera and the hero) are skipped, and so is a block the camera itself is inside.
/// The answer: the block cell, the normal of the face the ray entered (the place cell is cell + normal) and the hit point.
/// </summary>
public static class BlockAim
{
    /// <summary>Where the reticle is on screen (viewport canvas coordinates): the centre of the visible rect. The HUD draws it here and the aim ray starts here.</summary>
    public static Vector2 Reticle(Viewport vp) { var r = vp.GetVisibleRect(); return r.Position + r.Size / 2f; }

    /// <summary>The ray through the reticle: origin and unit direction, or null without a camera.</summary>
    public static (Vector3 o, Vector3 d)? Ray(Viewport vp)
    {
        var cam = vp.GetCamera3D(); if (cam == null) return null;
        var mid = Reticle(vp);
        return (cam.ProjectRayOrigin(mid), cam.ProjectRayNormal(mid).Normalized());
    }

    /// <summary>
    /// The block under the reticle within reach of the eye: the block cell, the normal of the face the ray entered (the place cell is b + n)
    /// and the point where the ray met it. Null when there is nothing within reach.
    /// </summary>
    public static (Vector3I b, Vector3I n, Vector3 hit)? Target(Viewport vp, Vector3 eye, float reach)
    {
        if (Ray(vp) is not { } r) return null;
        return Cast(r.o, r.d, eye, reach);
    }

    /// <summary>The same for any ray (origin, unit direction): the first block it meets whose hit point is within reach of the eye.</summary>
    public static (Vector3I b, Vector3I n, Vector3 hit)? Cast(Vector3 o, Vector3 dir, Vector3 eye, float reach)
    {
        var a = BlockWorld.I?.Area; if (a == null) return null;
        // the ray leaves the reach sphere around the eye at tMax (none: the reticle points away from everything within reach)
        var oe = eye - o; float b = oe.Dot(dir), disc = b * b - (oe.LengthSquared() - reach * reach);
        if (disc < 0) return null;
        float tMax = b + Mathf.Sqrt(disc); if (tMax <= 0) return null;
        var cell = new Vector3I(Mathf.FloorToInt(o.X), Mathf.FloorToInt(o.Y), Mathf.FloorToInt(o.Z));
        var step = new Vector3I(Math.Sign(dir.X), Math.Sign(dir.Y), Math.Sign(dir.Z));
        static float T(float p, float d, int c, int s) => s == 0 ? float.PositiveInfinity : ((s > 0 ? c + 1 : c) - p) / d;
        float tx = T(o.X, dir.X, cell.X, step.X), ty = T(o.Y, dir.Y, cell.Y, step.Y), tz = T(o.Z, dir.Z, cell.Z, step.Z);
        float dx = step.X == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.X), dy = step.Y == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.Y), dz = step.Z == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.Z);
        var n = Vector3I.Zero; float t = 0; Span<Aabb> boxes = stackalloc Aabb[6];
        for (int i = 0; i < 256 && t <= tMax; i++)
        {
            byte id = a.Get(cell.X, cell.Y, cell.Z);
            if (id != 0 && id != 255)
            {
                var def = Blocks.Def(id);
                if (!def.IsFluid)
                {
                    float th = -1; var hn = n;
                    if (def.Shape == "")
                    {
                        if (i > 0) th = t;   // a full cube: the face the ray stepped in through (i == 0: the camera is inside this block, look past it)
                    }
                    else
                    {
                        // a partial block: the nearest of its drawn boxes the ray enters from outside
                        int cnt = Boxes(a, def, cell.X, cell.Y, cell.Z, a.GetMeta(cell.X, cell.Y, cell.Z), boxes);
                        var c0 = new Vector3(cell.X, cell.Y, cell.Z);
                        for (int k = 0; k < cnt; k++)
                            if (RayBox(o, dir, c0 + boxes[k].Position, c0 + boxes[k].End, out float tb, out var nb) && (th < 0 || tb < th)) { th = tb; hn = nb; }
                    }
                    if (th >= 0 && th <= tMax + .001f)
                    {
                        var hit = o + dir * th;
                        if ((hit - eye).Dot(dir) >= -1f)   // not behind the hero (a wall at the camera's back)
                            return (hit - eye).Length() <= reach + .01f ? (cell, hn, hit) : null;
                    }
                }
            }
            if (tx < ty && tx < tz) { t = tx; tx += dx; cell.X += step.X; n = new Vector3I(-step.X, 0, 0); }
            else if (ty < tz) { t = ty; ty += dy; cell.Y += step.Y; n = new Vector3I(0, -step.Y, 0); }
            else { t = tz; tz += dz; cell.Z += step.Z; n = new Vector3I(0, 0, -step.Z); }
        }
        return null;
    }

    /// <summary>Where a ray enters an axis-aligned box from outside: the distance along it and the face's normal. False when it misses or starts inside.</summary>
    static bool RayBox(Vector3 o, Vector3 d, Vector3 mn, Vector3 mx, out float t, out Vector3I n)
    {
        t = 0; n = Vector3I.Zero; float near = float.NegativeInfinity, far = float.PositiveInfinity; int axis = -1;
        for (int k = 0; k < 3; k++)
        {
            float ok = o[k], dk = d[k];
            if (Mathf.Abs(dk) < 1e-7f) { if (ok < mn[k] || ok > mx[k]) return false; continue; }
            float t1 = (mn[k] - ok) / dk, t2 = (mx[k] - ok) / dk; if (t1 > t2) (t1, t2) = (t2, t1);
            if (t1 > near) { near = t1; axis = k; }
            if (t2 < far) far = t2;
            if (near > far) return false;
        }
        if (axis < 0 || near < 0 || far < 0) return false;
        t = near; int s = d[axis] > 0 ? -1 : 1;
        n = axis == 0 ? new Vector3I(s, 0, 0) : axis == 1 ? new Vector3I(0, s, 0) : new Vector3I(0, 0, s);
        return true;
    }

    /// <summary>
    /// The boxes a partial block is drawn with, in its own cell (0..1), as BlockMesher builds them: slab, stairs (two boxes), fence (post plus
    /// a rail box to each joined neighbour), ladder and door (a thin panel on one side), torch (a padded box round the stick), chest.
    /// </summary>
    static int Boxes(BlockArea a, BlockDef def, int x, int y, int z, byte meta, Span<Aabb> o)
    {
        static Aabb Part(int dir, float d0, float d1, float t0, float t1)   // a panel d0..d1 deep on wall side dir (0 +X, 1 -X, 2 +Z, 3 -Z), t0..t1 along it
        {
            Vector3 mn, mx;
            switch (dir)
            {
                case 0: mn = new(1 - d1, 0, t0); mx = new(1 - d0, 1, t1); break;
                case 1: mn = new(d0, 0, t0); mx = new(d1, 1, t1); break;
                case 2: mn = new(t0, 0, 1 - d1); mx = new(t1, 1, 1 - d0); break;
                default: mn = new(t0, 0, d0); mx = new(t1, 1, d1); break;
            }
            return new Aabb(mn, mx - mn);
        }
        switch (def.Shape)
        {
            case "slab":
                o[0] = new Aabb(new Vector3(0, (meta & 4) != 0 ? .5f : 0f, 0), new Vector3(1, .5f, 1)); return 1;
            case "stairs":
            {
                bool inv = (meta & 4) != 0; float ky = inv ? 0f : .5f;
                o[0] = new Aabb(new Vector3(0, inv ? .5f : 0f, 0), new Vector3(1, .5f, 1));
                o[1] = (meta & 3) switch
                {
                    0 => new Aabb(new Vector3(.5f, ky, 0), new Vector3(.5f, .5f, 1)), 1 => new Aabb(new Vector3(0, ky, 0), new Vector3(.5f, .5f, 1)),
                    2 => new Aabb(new Vector3(0, ky, .5f), new Vector3(1, .5f, .5f)), _ => new Aabb(new Vector3(0, ky, 0), new Vector3(1, .5f, .5f)),
                };
                return 2;
            }
            case "fence":
            {
                int c = 0; o[c++] = new Aabb(new Vector3(.375f, 0, .375f), new Vector3(.25f, 1f, .25f));
                bool Joins(int nx, int nz) { byte nb = a.Get(nx, y, nz); if (nb == 0 || nb == 255) return false; var nd = Blocks.Def(nb); return nd.Shape == "fence" || nd.Opaque; }
                if (Joins(x + 1, z)) o[c++] = new Aabb(new Vector3(.625f, .3f, .44f), new Vector3(.375f, .52f, .12f));
                if (Joins(x - 1, z)) o[c++] = new Aabb(new Vector3(0, .3f, .44f), new Vector3(.375f, .52f, .12f));
                if (Joins(x, z + 1)) o[c++] = new Aabb(new Vector3(.44f, .3f, .625f), new Vector3(.12f, .52f, .375f));
                if (Joins(x, z - 1)) o[c++] = new Aabb(new Vector3(.44f, .3f, 0), new Vector3(.12f, .52f, .375f));
                return c;
            }
            case "ladder":
                o[0] = Part(meta & 3, 0f, .1f, .1f, .9f); return 1;
            case "door":
            {
                int dir = meta & 3; if (def.Key.EndsWith("_open")) dir = dir switch { 0 => 2, 2 => 1, 1 => 3, _ => 0 };   // swung 90 degrees about the corner
                o[0] = Part(dir, 0f, .12f, 0f, 1f); return 1;
            }
            case "post":
            {
                // a torch: the box round its foot and its flame, padded so the thin stick is not fiddly to point at
                var (foot, tb) = BlockMesher.TorchPose(x, y, z, meta); var c0 = new Vector3(x, y, z);
                Vector3 p0 = foot - c0, p1 = p0 + tb * new Vector3(0, .83f, 0); const float pad = .13f;
                var mn = new Vector3(Mathf.Min(p0.X, p1.X) - pad, Mathf.Min(p0.Y, p1.Y), Mathf.Min(p0.Z, p1.Z) - pad).Clamp(Vector3.Zero, Vector3.One);
                var mx = new Vector3(Mathf.Max(p0.X, p1.X) + pad, Mathf.Max(p0.Y, p1.Y) + .04f, Mathf.Max(p0.Z, p1.Z) + pad).Clamp(Vector3.Zero, Vector3.One);
                o[0] = new Aabb(mn, mx - mn); return 1;
            }
            case "chest":
                o[0] = new Aabb(new Vector3(.05f, 0, .05f), new Vector3(.9f, .86f, .9f)); return 1;
            default:
                o[0] = new Aabb(Vector3.Zero, Vector3.One); return 1;   // an unknown shape: the whole cell
        }
    }
}
