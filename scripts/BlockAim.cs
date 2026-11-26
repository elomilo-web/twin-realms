using System;
using Godot;

namespace TwinRealms;

/// <summary>
/// The one block-aiming ray, shared by digging, placing, the crack overlay, the outline and the ghost (scripts/PlayerBlocks.cs).
/// It is the camera ray through the centre of the visible screen, where the HUD draws the reticle (Camera3D.ProjectRayOrigin/Normal, so the
/// over-the-shoulder HOffset/VOffset of CameraRig are included), stepped through the block grid cell by cell (DDA; water and lava do not stop it).
/// Reach is measured from the hero's eye, not from the camera: a block the ray meets further than `reach` from the eye is not a target, and
/// blocks more than a metre behind the hero along the view (between the camera and the hero) are skipped.
/// </summary>
public static class BlockAim
{
    /// <summary>The ray through the reticle: origin and unit direction, or null without a camera.</summary>
    public static (Vector3 o, Vector3 d)? Ray(Viewport vp)
    {
        var cam = vp.GetCamera3D(); if (cam == null) return null;
        var mid = vp.GetVisibleRect().Size / 2f;
        return (cam.ProjectRayOrigin(mid), cam.ProjectRayNormal(mid).Normalized());
    }

    /// <summary>
    /// The block under the reticle within reach of the eye: the block cell, the normal of the face the ray entered (the place cell is b + n)
    /// and the point where the ray met it. Null when there is nothing within reach.
    /// </summary>
    public static (Vector3I b, Vector3I n, Vector3 hit)? Target(Viewport vp, Vector3 eye, float reach)
    {
        var a = BlockWorld.I?.Area; if (a == null || Ray(vp) is not { } r) return null;
        var (o, dir) = r;
        // the ray leaves the reach sphere around the eye at tMax (none: the reticle points away from everything within reach)
        var oe = eye - o; float b = oe.Dot(dir), disc = b * b - (oe.LengthSquared() - reach * reach);
        if (disc < 0) return null;
        float tMax = b + Mathf.Sqrt(disc); if (tMax <= 0) return null;
        var cell = new Vector3I(Mathf.FloorToInt(o.X), Mathf.FloorToInt(o.Y), Mathf.FloorToInt(o.Z));
        var step = new Vector3I(Math.Sign(dir.X), Math.Sign(dir.Y), Math.Sign(dir.Z));
        static float T(float p, float d, int c, int s) => s == 0 ? float.PositiveInfinity : ((s > 0 ? c + 1 : c) - p) / d;
        float tx = T(o.X, dir.X, cell.X, step.X), ty = T(o.Y, dir.Y, cell.Y, step.Y), tz = T(o.Z, dir.Z, cell.Z, step.Z);
        float dx = step.X == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.X), dy = step.Y == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.Y), dz = step.Z == 0 ? float.PositiveInfinity : Mathf.Abs(1 / dir.Z);
        var n = Vector3I.Zero; float t = 0;
        for (int i = 0; i < 256 && t <= tMax; i++)
        {
            byte id = a.Get(cell.X, cell.Y, cell.Z);
            if (id != 0 && id != 255 && !Blocks.Def(id).IsFluid)
            {
                var hit = o + dir * t;
                if ((hit - eye).Dot(dir) >= -1f)   // not behind the hero (a wall at the camera's back)
                    return (hit - eye).Length() <= reach + .01f ? (cell, n, hit) : null;
            }
            if (tx < ty && tx < tz) { t = tx; tx += dx; cell.X += step.X; n = new Vector3I(-step.X, 0, 0); }
            else if (ty < tz) { t = ty; ty += dy; cell.Y += step.Y; n = new Vector3I(0, -step.Y, 0); }
            else { t = tz; tz += dz; cell.Z += step.Z; n = new Vector3I(0, 0, -step.Z); }
        }
        return null;
    }
}
