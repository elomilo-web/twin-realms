using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Real-shape melee hits (Dark Souls style): a swept weapon segment (blade grip to tip, or a creature's bite sphere) against the
/// target's body capsule (feet-up segment with a radius). Numbers: data/tuning.json game.hitbox.
/// </summary>
public static class Hitbox
{
    /// <summary>Closest points between segments p1-q1 and p2-q2 (Ericson). Returns the squared distance; c1 / c2 = the points.</summary>
    public static float SegSeg(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2; float a = d1.Dot(d1), e = d2.Dot(d2), f = d2.Dot(r), s, t;
        const float Eps = 1e-8f;
        if (a <= Eps && e <= Eps) { c1 = p1; c2 = p2; return (c1 - c2).LengthSquared(); }
        if (a <= Eps) { s = 0; t = Mathf.Clamp(f / e, 0, 1); }
        else
        {
            float c = d1.Dot(r);
            if (e <= Eps) { t = 0; s = Mathf.Clamp(-c / a, 0, 1); }
            else
            {
                float b = d1.Dot(d2), den = a * e - b * b;
                s = den != 0 ? Mathf.Clamp((b * f - c * e) / den, 0, 1) : 0;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Mathf.Clamp(-c / a, 0, 1); } else if (t > 1) { t = 1; s = Mathf.Clamp((b - c) / a, 0, 1); }
            }
        }
        c1 = p1 + d1 * s; c2 = p2 + d2 * t; return (c1 - c2).LengthSquared();
    }

    /// <summary>A body: a vertical capsule from feet (y) to the top, with its radius (the top and bottom are rounded inside the height).</summary>
    public readonly record struct Body(Vector3 Feet, float Height, float Radius)
    {
        public Vector3 A => Feet + Vector3.Up * Mathf.Min(Radius, Height * .5f);
        public Vector3 B => Feet + Vector3.Up * Mathf.Max(Height - Radius, Height * .5f);
    }

    /// <summary>
    /// Sweeps a weapon segment from last frame (a0-b0) to this frame (a1-b1), in steps no longer than hitbox.sweepStep (so a fast swing cannot
    /// pass through a body between two frames), against a body. rad = the weapon's thickness. Contact = the point on the body's surface.
    /// </summary>
    public static bool Sweep(Vector3 a0, Vector3 b0, Vector3 a1, Vector3 b1, float rad, Body body, out Vector3 contact)
    {
        float move = Mathf.Max(a0.DistanceTo(a1), b0.DistanceTo(b1));
        int n = Mathf.Clamp(Mathf.CeilToInt(move / Mathf.Max(.02f, Tune.F("hitbox.sweepStep", .12f))), 1, (int)Tune.F("hitbox.sweepMaxSteps", 10));
        float reach = rad + body.Radius;
        for (int i = 1; i <= n; i++)
        {
            float k = i / (float)n; var a = a0.Lerp(a1, k); var b = b0.Lerp(b1, k);
            if (SegSeg(a, b, body.A, body.B, out var cw, out var cb) <= reach * reach)
            {
                var o = cw - cb; contact = cb + (o.LengthSquared() > 1e-6f ? o.Normalized() * body.Radius : Vector3.Zero); return true;
            }
        }
        contact = default; return false;
    }

    /// <summary>A sphere moving from c0 to c1 (radius r) against a body.</summary>
    public static bool SweepSphere(Vector3 c0, Vector3 c1, float r, Body body, out Vector3 contact) => Sweep(c0, c0, c1, c1, r, body, out contact);

    /// <summary>The world blocks a blade: the first world hit (layer 1) from grip toward tip, or null. Excludes the given bodies.</summary>
    public static Vector3? WallCut(Node3D ctx, Vector3 grip, Vector3 tip, out Vector3 normal, Rid exclude)
    {
        normal = Vector3.Up; if (!ctx.IsInsideTree() || grip.DistanceSquaredTo(tip) < 1e-4f) return null;
        var q = PhysicsRayQueryParameters3D.Create(grip, tip, 1); q.Exclude = new Godot.Collections.Array<Rid> { exclude };
        var hit = ctx.GetWorld3D().DirectSpaceState.IntersectRay(q);
        if (hit.Count == 0) return null; normal = (Vector3)hit["normal"]; return (Vector3)hit["position"];
    }
}

/// <summary>
/// /hitbox: draws active hitboxes (weapon sweeps, bite spheres: red) and hurtboxes (body capsules: green hero, yellow foes) as thin
/// lines while on (one ImmediateMesh redrawn every frame). The sweep code calls Seg / Sphere; capsules are drawn from the scene each frame.
/// </summary>
public partial class HitboxDebug : MeshInstance3D
{
    public static bool On;
    static HitboxDebug? _i;
    readonly struct Line { public readonly Vector3 A, B; public readonly Color C; public readonly ulong Until; public Line(Vector3 a, Vector3 b, Color c, ulong u) { A = a; B = b; C = c; Until = u; } }
    static readonly List<Line> _lines = new();
    ImmediateMesh _m = new();

    public static void Register()
    {
        Commands.Register("hitbox", "hitbox [on|off]: draws melee hitboxes (red = active weapon / bite sweeps) and hurtboxes (green = hero, yellow = monsters) as lines", a =>
        {
            On = a.Length > 0 ? a[0] is "on" or "1" : !On; _lines.Clear(); if (On) Ensure();
            return "Hitbox view " + (On ? "on" : "off");
        });
    }
    static void Ensure()
    {
        if (_i != null && IsInstanceValid(_i)) return;
        var scene = (Engine.GetMainLoop() as SceneTree)?.CurrentScene; if (scene == null) return;
        _i = new HitboxDebug { Name = "HitboxDebug", CastShadow = ShadowCastingSetting.Off };
        _i.MaterialOverride = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true, NoDepthTest = true };
        _i.Mesh = _i._m; scene.AddChild(_i);
    }
    /// <summary>A line kept for life seconds (sweeps are drawn at physics rate, frames between them keep them).</summary>
    public static void Seg(Vector3 a, Vector3 b, Color c, float life = .25f)
    {
        if (!On) return; Ensure(); if (_lines.Count > 4000) _lines.RemoveRange(0, 1000);
        _lines.Add(new Line(a, b, c, Time.GetTicksMsec() + (ulong)(life * 1000)));
    }
    public static void Sphere(Vector3 c, float r, Color col, float life = .25f)
    {
        if (!On) return;
        for (int ax = 0; ax < 3; ax++) for (int i = 0; i < 12; i++)
            {
                float a0 = i * Mathf.Tau / 12, a1 = (i + 1) * Mathf.Tau / 12;
                Vector3 P(float a) => ax == 0 ? new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)) : ax == 1 ? new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) : new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                Seg(c + P(a0) * r, c + P(a1) * r, col, life);
            }
    }
    static void Capsule(ImmediateMesh m, Hitbox.Body b, Color col)
    {
        for (int ring = 0; ring < 2; ring++)
        {
            var c = ring == 0 ? b.A : b.B;
            for (int i = 0; i < 10; i++)
            {
                float a0 = i * Mathf.Tau / 10, a1 = (i + 1) * Mathf.Tau / 10;
                V(m, c + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * b.Radius, col); V(m, c + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * b.Radius, col);
            }
        }
        for (int i = 0; i < 4; i++) { var o = new Vector3(Mathf.Cos(i * Mathf.Pi / 2), 0, Mathf.Sin(i * Mathf.Pi / 2)) * b.Radius; V(m, b.A + o, col); V(m, b.B + o, col); }
        V(m, b.Feet, col); V(m, b.Feet + Vector3.Up * b.Height, col);
    }
    static void V(ImmediateMesh m, Vector3 p, Color c) { m.SurfaceSetColor(c); m.SurfaceAddVertex(p); }

    public override void _Process(double delta)
    {
        _m.ClearSurfaces();
        if (!On) { _lines.Clear(); return; }
        ulong now = Time.GetTicksMsec(); _lines.RemoveAll(l => l.Until < now);
        var p = Player.Instance;
        _m.SurfaceBegin(Mesh.PrimitiveType.Lines);
        int n = 0;
        foreach (var l in _lines) { V(_m, l.A, l.C); V(_m, l.B, l.C); n++; }
        if (p != null)
        {
            Capsule(_m, p.HurtBody(), new Color(.3f, 1f, .4f));
            foreach (var node in GetTree().GetNodesInGroup("enemies"))
                if (node is Enemy e && !e.Dead && e.Visible && e.GlobalPosition.DistanceSquaredTo(p.GlobalPosition) < 30 * 30) { Capsule(_m, e.HurtBody(), new Color(1f, .9f, .3f)); n++; }
        }
        V(_m, Vector3.Zero, Colors.Black); V(_m, Vector3.Zero, Colors.Black);   // never an empty surface
        _m.SurfaceEnd();
    }
}
