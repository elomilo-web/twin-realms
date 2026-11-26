using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;

namespace TwinRealms;

/// <summary>Signed distance (normalised icon space: -1..1, y down; negative inside).</summary>
public delegate float Sdf(Vector2 p);

/// <summary>A tiny software painter: anti-aliased SDF shapes, gradients and glows into an RGBA float buffer (works headless).</summary>
public sealed class IconPainter
{
    public readonly int N; readonly float[] _c;
    /// <summary>While set, everything painted is clipped to this shape (the badge interior).</summary>
    public Sdf? Clip;
    public IconPainter(int n) { N = n; _c = new float[n * n * 4]; }
    float Px => 2f / N;
    Vector2 At(int x, int y) => new((x + .5f) / N * 2 - 1, (y + .5f) / N * 2 - 1);
    float Cov(float d) => Math.Clamp(.5f - d / Px, 0, 1);
    void Over(int i, Color c, float a)
    {
        a = Math.Clamp(a * c.A, 0, 1); if (a <= 0) return;
        float da = _c[i + 3], oa = a + da * (1 - a); if (oa <= 0) return;
        _c[i] = (c.R * a + _c[i] * da * (1 - a)) / oa; _c[i + 1] = (c.G * a + _c[i + 1] * da * (1 - a)) / oa; _c[i + 2] = (c.B * a + _c[i + 2] * da * (1 - a)) / oa; _c[i + 3] = oa;
    }
    public void Paint(Sdf s, Func<Vector2, Color> col)
    {
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            var p = At(x, y); float a = Cov(s(p)); if (a <= 0) continue;
            if (Clip != null) { a *= Cov(Clip(p)); if (a <= 0) continue; }
            Over((y * N + x) * 4, col(p), a);
        }
    }
    public void Fill(Sdf s, Color c) => Paint(s, _ => c);
    public void Lin(Sdf s, Color a, Color b, Vector2 p0, Vector2 p1) { var dir = p1 - p0; float l = Math.Max(1e-5f, dir.LengthSquared()); Paint(s, p => a.Lerp(b, Math.Clamp((p - p0).Dot(dir) / l, 0, 1))); }
    public void Rad(Sdf s, Color cin, Color cout, Vector2 c, float r) => Paint(s, p => cin.Lerp(cout, Math.Clamp((p - c).Length() / r, 0, 1)));
    public void Stroke(Sdf s, float w, Color c) => Fill(p => MathF.Abs(s(p)) - w, c);
    /// <summary>Soft halo around a shape (exponential falloff outside it).</summary>
    public void Glow(Sdf s, Color c, float spread)
    {
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++)
        {
            var p = At(x, y); float d = s(p); if (d > spread * 6) continue;
            float a = d <= 0 ? 1 : MathF.Exp(-d / spread);
            if (Clip != null) a *= Cov(Clip(p));
            Over((y * N + x) * 4, c, a);
        }
    }
    public Image ToImage()
    {
        var img = Image.CreateEmpty(N, N, false, Image.Format.Rgba8);
        for (int y = 0; y < N; y++) for (int x = 0; x < N; x++) { int i = (y * N + x) * 4; img.SetPixel(x, y, new Color(_c[i], _c[i + 1], _c[i + 2], _c[i + 3])); }
        return img;
    }
}

/// <summary>SDF primitives and combinators for the icon painter.</summary>
public static class S
{
    public static Vector2 V(float x, float y) => new(x, y);
    public static Sdf Circle(Vector2 c, float r) => p => (p - c).Length() - r;
    public static Sdf Ellipse(Vector2 c, Vector2 r) { float m = Math.Min(r.X, r.Y); return p => (((p - c) / r).Length() - 1) * m; }
    public static Sdf Ring(Vector2 c, float r, float w) => p => MathF.Abs((p - c).Length() - r) - w;
    public static Sdf Box(Vector2 c, Vector2 h, float round = 0, float rot = 0) => p =>
    {
        var q = (p - c).Rotated(-rot); q = new Vector2(MathF.Abs(q.X), MathF.Abs(q.Y)) - h + new Vector2(round, round);
        return new Vector2(Math.Max(q.X, 0), Math.Max(q.Y, 0)).Length() + Math.Min(Math.Max(q.X, q.Y), 0) - round;
    };
    public static Sdf Seg(Vector2 a, Vector2 b, float w) => p =>
    {
        var pa = p - a; var ba = b - a; float h = Math.Clamp(pa.Dot(ba) / Math.Max(1e-6f, ba.Dot(ba)), 0, 1);
        return (pa - ba * h).Length() - w;
    };
    /// <summary>A capsule that thins from wa at a to wb at b.</summary>
    public static Sdf Taper(Vector2 a, Vector2 b, float wa, float wb) => p =>
    {
        var pa = p - a; var ba = b - a; float h = Math.Clamp(pa.Dot(ba) / Math.Max(1e-6f, ba.Dot(ba)), 0, 1);
        return (pa - ba * h).Length() - (wa + (wb - wa) * h);
    };
    public static Sdf Poly(params Vector2[] v) => p =>
    {
        float d = (p - v[0]).Dot(p - v[0]), s = 1;
        for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
        {
            var e = v[j] - v[i]; var w = p - v[i];
            var b = w - e * Math.Clamp(w.Dot(e) / Math.Max(1e-9f, e.Dot(e)), 0, 1); d = Math.Min(d, b.Dot(b));
            bool c1 = p.Y >= v[i].Y, c2 = p.Y < v[j].Y, c3 = e.X * w.Y > e.Y * w.X;
            if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
        }
        return s * MathF.Sqrt(d);
    };
    public static Sdf P(params float[] xy) { var v = new Vector2[xy.Length / 2]; for (int i = 0; i < v.Length; i++) v[i] = new Vector2(xy[i * 2], xy[i * 2 + 1]); return Poly(v); }
    /// <summary>Ring arc from angle a0 to a1 (radians, 0 = +x, clockwise on screen), round caps.</summary>
    public static Sdf Arc(Vector2 c, float r, float w, float a0, float a1) => p =>
    {
        var q = p - c; float a = MathF.Atan2(q.Y, q.X); float span = a1 - a0; float t = a - a0;
        t -= MathF.Floor(t / MathF.Tau) * MathF.Tau;
        if (t <= span) return MathF.Abs(q.Length() - r) - w;
        var e0 = c + new Vector2(MathF.Cos(a0), MathF.Sin(a0)) * r; var e1 = c + new Vector2(MathF.Cos(a1), MathF.Sin(a1)) * r;
        return Math.Min((p - e0).Length(), (p - e1).Length()) - w;
    };
    public static Sdf Star(Vector2 c, float ro, float ri, int n, float rot = 0)
    {
        var v = new Vector2[n * 2];
        for (int i = 0; i < n * 2; i++) { float a = rot - MathF.PI / 2 + i * MathF.PI / n; v[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * (i % 2 == 0 ? ro : ri); }
        return Poly(v);
    }
    public static Sdf Ngon(Vector2 c, float r, int n, float rot = 0)
    {
        var v = new Vector2[n]; for (int i = 0; i < n; i++) { float a = rot - MathF.PI / 2 + i * MathF.Tau / n; v[i] = c + new Vector2(MathF.Cos(a), MathF.Sin(a)) * r; }
        return Poly(v);
    }
    public static Sdf U(params Sdf[] f) => p => { float d = float.MaxValue; foreach (var g in f) d = Math.Min(d, g(p)); return d; };
    public static Sdf Sub(Sdf a, Sdf b) => p => Math.Max(a(p), -b(p));
    public static Sdf I(Sdf a, Sdf b) => p => Math.Max(a(p), b(p));
    public static Sdf Grow(Sdf a, float r) => p => a(p) - r;
    /// <summary>Moves, scales and rotates a shape drawn around the origin.</summary>
    public static Sdf X(Sdf f, Vector2 off, float scale = 1, float rot = 0) => p => f((p - off).Rotated(-rot) / scale) * scale;
    public static Sdf Polyline(float w, params Vector2[] pts) { var l = new List<Sdf>(); for (int i = 0; i + 1 < pts.Length; i++) l.Add(Seg(pts[i], pts[i + 1], w)); return U(l.ToArray()); }
    public static Sdf Mirror(Sdf f) => p => f(new Vector2(MathF.Abs(p.X), p.Y));
}

/// <summary>
/// Skill icons in the anime fantasy style: a rounded badge with an element gradient, a faint rune circle, a glowing symbol drawn
/// for that one skill, an element motif and an element-coloured rim. <c>--gen-icons</c> (Main.cs) writes every skill of
/// data/skills.json to res://assets/icons/skills/&lt;id&gt;.png and checks no two pictures are the same. The same symbols build the
/// player's own icons (Spellcraft icon editor, <see cref="SpellIcon"/>).
/// </summary>
public static class IconArt
{
    public const string Dir = "res://assets/icons/skills/";
    public const int Size = 128;

    public record struct Pal(Color Bg, Color Dark, Color Light, Color Rim, Color Sym, Color Glow);
    public static Pal PalOf(string type) => type switch
    {
        "fire" => new(new Color(.78f, .25f, .08f), new Color(.16f, .03f, .03f), new Color(1f, .93f, .6f), new Color(1f, .6f, .25f), new Color(1f, .55f, .15f), new Color(1f, .5f, .1f, .55f)),
        "frost" => new(new Color(.2f, .55f, .85f), new Color(.02f, .07f, .18f), new Color(.92f, .99f, 1f), new Color(.6f, .9f, 1f), new Color(.6f, .88f, 1f), new Color(.5f, .85f, 1f, .55f)),
        "lightning" => new(new Color(.45f, .32f, .85f), new Color(.06f, .03f, .17f), new Color(1f, .98f, .7f), new Color(.8f, .66f, 1f), new Color(1f, .9f, .4f), new Color(.85f, .7f, 1f, .6f)),
        "poison" => new(new Color(.28f, .62f, .2f), new Color(.02f, .1f, .04f), new Color(.88f, 1f, .62f), new Color(.6f, .95f, .35f), new Color(.55f, .95f, .3f), new Color(.5f, 1f, .3f, .5f)),
        "shadow" => new(new Color(.45f, .18f, .62f), new Color(.06f, .01f, .1f), new Color(.95f, .8f, 1f), new Color(.75f, .45f, 1f), new Color(.8f, .5f, 1f), new Color(.7f, .35f, 1f, .55f)),
        "holy" => new(new Color(.85f, .65f, .25f), new Color(.18f, .1f, .03f), new Color(1f, 1f, .88f), new Color(1f, .87f, .45f), new Color(1f, .92f, .6f), new Color(1f, .9f, .5f, .6f)),
        "blood" => new(new Color(.7f, .1f, .12f), new Color(.14f, .01f, .02f), new Color(1f, .75f, .72f), new Color(.95f, .35f, .35f), new Color(.95f, .2f, .22f), new Color(1f, .25f, .25f, .5f)),
        "arcane" => new(new Color(.25f, .35f, .85f), new Color(.03f, .04f, .16f), new Color(.88f, .92f, 1f), new Color(.55f, .7f, 1f), new Color(.6f, .75f, 1f), new Color(.5f, .6f, 1f, .55f)),
        _ => new(new Color(.5f, .47f, .44f), new Color(.08f, .07f, .07f), new Color(1f, .98f, .92f), new Color(.88f, .74f, .48f), new Color(.86f, .88f, .92f), new Color(1f, .9f, .7f, .35f)),
    };

    static readonly Sdf Badge = S.Box(Vector2.Zero, new Vector2(.9f, .9f), .26f);

    // ------------------------------------------------------------------------------------------------------------- symbols
    // drawn around the origin, about .6 across the badge; up is -y
    public static Sdf Flame(float s = 1) => S.X(S.P(0, -.95f, .3f, -.5f, .56f, -.12f, .6f, .32f, .38f, .72f, 0, .85f, -.38f, .72f, -.6f, .32f, -.52f, -.08f, -.3f, -.32f, -.18f, -.62f, -.05f, -.4f), Vector2.Zero, .62f * s);
    public static Sdf FlameCore(float s = 1) => S.X(S.P(0, -.4f, .28f, .05f, .3f, .4f, 0, .66f, -.3f, .4f, -.22f, .02f), Vector2.Zero, .62f * s);
    public static Sdf Snowflake(float s = 1)
    {
        var l = new List<Sdf>();
        for (int k = 0; k < 6; k++)
        {
            var d = new Vector2(MathF.Cos(k * MathF.PI / 3 - MathF.PI / 2), MathF.Sin(k * MathF.PI / 3 - MathF.PI / 2)); var n = new Vector2(-d.Y, d.X);
            l.Add(S.Seg(Vector2.Zero, d * .62f, .05f));
            foreach (float t in new[] { .3f, .48f }) { var b = d * t; l.Add(S.Seg(b, b + (d + n) * .14f, .035f)); l.Add(S.Seg(b, b + (d - n) * .14f, .035f)); }
        }
        return S.X(S.U(l.ToArray()), Vector2.Zero, s);
    }
    public static Sdf Bolt(float s = 1) => S.X(S.P(.18f, -.95f, -.45f, .1f, -.04f, .1f, -.25f, .95f, .5f, -.16f, .08f, -.16f, .35f, -.95f), Vector2.Zero, .62f * s);
    public static Sdf Drop(float s = 1) => S.X(S.U(S.Circle(new Vector2(0, .16f), .36f), S.P(0, -.62f, .31f, 0, -.31f, 0)), Vector2.Zero, s);
    public static Sdf Skull(float s = 1)
    {
        var head = S.U(S.Circle(new Vector2(0, -.1f), .44f), S.Box(new Vector2(0, .3f), new Vector2(.25f, .17f), .06f));
        var holes = S.U(S.Circle(new Vector2(-.17f, -.06f), .12f), S.Circle(new Vector2(.17f, -.06f), .12f), S.P(0, .08f, .06f, .18f, -.06f, .18f),
            S.Box(new Vector2(-.09f, .4f), new Vector2(.022f, .08f)), S.Box(new Vector2(.09f, .4f), new Vector2(.022f, .08f)), S.Box(new Vector2(0, .4f), new Vector2(.022f, .08f)));
        return S.X(S.Sub(head, holes), Vector2.Zero, s);
    }
    public static Sdf Moon(float s = 1) => S.X(S.Sub(S.Circle(Vector2.Zero, .5f), S.Circle(new Vector2(.24f, -.14f), .42f)), Vector2.Zero, s);
    public static Sdf Eye(float s = 1)
    {
        var almond = S.I(S.Circle(new Vector2(0, .5f), .8f), S.Circle(new Vector2(0, -.5f), .8f));
        return S.X(S.U(S.Sub(almond, S.Circle(Vector2.Zero, .24f)), S.Circle(Vector2.Zero, .13f)), Vector2.Zero, s);
    }
    public static Sdf Sun(float s = 1)
    {
        var l = new List<Sdf> { S.Circle(Vector2.Zero, .27f) };
        for (int k = 0; k < 8; k++) { var d = new Vector2(MathF.Cos(k * MathF.PI / 4), MathF.Sin(k * MathF.PI / 4)); l.Add(S.Taper(d * .36f, d * (k % 2 == 0 ? .64f : .52f), .06f, .015f)); }
        return S.X(S.U(l.ToArray()), Vector2.Zero, s);
    }
    public static Sdf Cross(float s = 1) => S.X(S.U(S.Box(new Vector2(0, .05f), new Vector2(.11f, .6f), .03f), S.Box(new Vector2(0, -.2f), new Vector2(.42f, .11f), .03f)), Vector2.Zero, s);
    public static Sdf Plus(float s = 1) => S.X(S.U(S.Box(Vector2.Zero, new Vector2(.15f, .5f), .06f), S.Box(Vector2.Zero, new Vector2(.5f, .15f), .06f)), Vector2.Zero, s);
    public static Sdf Leaf(float s = 1, float rot = .785f) => S.X(S.Sub(S.I(S.Circle(new Vector2(-.32f, 0), .55f), S.Circle(new Vector2(.32f, 0), .55f)), S.Seg(new Vector2(0, -.36f), new Vector2(0, .4f), .02f)), Vector2.Zero, s, rot);
    public static Sdf ArrowUp(float s = 1) => S.X(S.P(0, -.62f, .42f, -.15f, .16f, -.15f, .16f, .56f, -.16f, .56f, -.16f, -.15f, -.42f, -.15f), Vector2.Zero, s);
    public static Sdf Sword(float s = 1, float rot = 0, float wide = 1, float len = 1) => S.X(S.U(
        S.P(0, -.85f * len, .09f * wide, -.68f * len, .09f * wide, .3f, -.09f * wide, .3f, -.09f * wide, -.68f * len),
        S.Box(new Vector2(0, .35f), new Vector2(.28f * MathF.Sqrt(wide), .05f), .03f), S.Box(new Vector2(0, .55f), new Vector2(.05f, .16f)), S.Circle(new Vector2(0, .75f), .08f)), Vector2.Zero, s, rot);
    public static Sdf Dagger(float s = 1, float rot = 0) => S.X(S.U(S.P(0, -.62f, .1f, -.42f, .08f, .15f, -.08f, .15f, -.1f, -.42f),
        S.Box(new Vector2(0, .2f), new Vector2(.2f, .045f), .02f), S.Box(new Vector2(0, .36f), new Vector2(.045f, .12f)), S.Circle(new Vector2(0, .52f), .065f)), Vector2.Zero, s, rot);
    public static Sdf Axe(float s = 1, float rot = 0) => S.X(S.U(S.Seg(new Vector2(0, .82f), new Vector2(0, -.75f), .055f),
        S.P(.04f, -.72f, .3f, -.88f, .55f, -.62f, .6f, -.3f, .52f, .02f, .3f, -.1f, .04f, -.22f)), Vector2.Zero, s, rot);
    public static Sdf GreatAxe(float s = 1, float rot = 0) => S.X(S.U(S.Seg(new Vector2(0, .85f), new Vector2(0, -.8f), .06f),
        S.P(.04f, -.7f, .45f, -.92f, .62f, -.45f, .45f, .05f, .04f, -.15f), S.P(-.04f, -.62f, -.3f, -.7f, -.38f, -.42f, -.3f, -.18f, -.04f, -.24f)), Vector2.Zero, s, rot);
    public static Sdf Hammer(float s = 1, float rot = 0) => S.X(S.U(S.Seg(new Vector2(0, .82f), new Vector2(0, -.45f), .055f), S.Box(new Vector2(0, -.55f), new Vector2(.36f, .18f), .05f)), Vector2.Zero, s, rot);
    public static Sdf Mace(float s = 1, float rot = 0)
    {
        var l = new List<Sdf> { S.Seg(new Vector2(0, .82f), new Vector2(0, -.3f), .055f), S.Circle(new Vector2(0, -.48f), .25f) };
        for (int k = 0; k < 8; k++) { var d = new Vector2(MathF.Cos(k * MathF.PI / 4), MathF.Sin(k * MathF.PI / 4)); l.Add(S.Taper(new Vector2(0, -.48f) + d * .2f, new Vector2(0, -.48f) + d * .38f, .06f, .005f)); }
        return S.X(S.U(l.ToArray()), Vector2.Zero, s, rot);
    }
    public static Sdf Spear(float s = 1, float rot = 0) => S.X(S.U(S.Seg(new Vector2(0, .9f), new Vector2(0, -.5f), .04f), S.P(0, -.95f, .13f, -.6f, 0, -.45f, -.13f, -.6f)), Vector2.Zero, s, rot);
    public static Sdf ShieldShape(float s = 1) => S.X(S.P(-.52f, -.58f, 0, -.66f, .52f, -.58f, .48f, .06f, 0, .68f, -.48f, .06f), Vector2.Zero, s);
    public static Sdf Arrow(float s = 1, float rot = 0) => S.X(S.U(S.Seg(new Vector2(0, .62f), new Vector2(0, -.42f), .03f), S.P(0, -.7f, .13f, -.4f, -.13f, -.4f),
        S.P(0, .42f, .14f, .6f, .14f, .74f, 0, .58f), S.P(0, .42f, -.14f, .6f, -.14f, .74f, 0, .58f)), Vector2.Zero, s, rot);
    public static Sdf Bow(float s = 1, float rot = 0) => S.X(S.U(S.Arc(new Vector2(-.55f, 0), .85f, .055f, -.85f, .85f),
        S.Seg(new Vector2(-.55f + .85f * MathF.Cos(.85f), -.85f * MathF.Sin(.85f)), new Vector2(-.55f + .85f * MathF.Cos(.85f), .85f * MathF.Sin(.85f)), .012f)), Vector2.Zero, s, rot);
    public static Sdf Star5(float s = 1) => S.Star(Vector2.Zero, .55f * s, .23f * s, 5);
    public static Sdf Spark4(float s = 1) => S.Star(Vector2.Zero, .55f * s, .13f * s, 4);
    public static Sdf Heart(float s = 1) => S.X(S.U(S.Circle(new Vector2(-.22f, -.15f), .27f), S.Circle(new Vector2(.22f, -.15f), .27f), S.P(-.46f, -.05f, .46f, -.05f, 0, .55f)), Vector2.Zero, s);
    public static Sdf Hourglass(float s = 1) => S.X(S.U(S.P(-.36f, -.5f, .36f, -.5f, .04f, 0, -.04f, 0), S.P(-.04f, 0, .04f, 0, .36f, .5f, -.36f, .5f),
        S.Box(new Vector2(0, -.58f), new Vector2(.46f, .06f), .03f), S.Box(new Vector2(0, .58f), new Vector2(.46f, .06f), .03f)), Vector2.Zero, s);
    public static Sdf Clock(float s = 1) => S.X(S.U(S.Ring(Vector2.Zero, .5f, .06f), S.Seg(Vector2.Zero, new Vector2(0, -.36f), .045f), S.Seg(Vector2.Zero, new Vector2(.26f, .1f), .045f), S.Circle(Vector2.Zero, .07f)), Vector2.Zero, s);
    public static Sdf Ankh(float s = 1) => S.X(S.U(S.Ring(new Vector2(0, -.38f), .2f, .07f), S.Taper(new Vector2(0, -.12f), new Vector2(0, .68f), .07f, .1f), S.Seg(new Vector2(-.38f, .02f), new Vector2(.38f, .02f), .07f)), Vector2.Zero, s);
    public static Sdf Note(float s = 1) => S.X(S.U(S.Ellipse(new Vector2(-.14f, .42f), new Vector2(.2f, .14f)), S.Seg(new Vector2(.04f, .4f), new Vector2(.04f, -.55f), .045f),
        S.P(.04f, -.6f, .38f, -.38f, .34f, -.18f, .04f, -.35f)), Vector2.Zero, s);
    public static Sdf Feather(float s = 1, float rot = .6f) => S.X(S.U(S.I(S.Circle(new Vector2(-.5f, 0), .62f), S.Circle(new Vector2(.5f, 0), .62f)), S.Seg(new Vector2(0, .3f), new Vector2(0, .78f), .03f)), Vector2.Zero, s, rot);
    public static Sdf Spiral(float s = 1) => S.X(S.U(S.Arc(Vector2.Zero, .16f, .05f, 0, 4.2f), S.Arc(new Vector2(.04f, 0), .33f, .05f, 1.2f, 5.6f), S.Arc(new Vector2(-.04f, .02f), .52f, .05f, 2.6f, 6.9f)), Vector2.Zero, s);
    public static Sdf Orb(float s = 1) => S.Circle(Vector2.Zero, .4f * s);
    public static Sdf Gem(float s = 1) => S.X(S.P(0, -.6f, .4f, -.15f, 0, .6f, -.4f, -.15f), Vector2.Zero, s);
    public static Sdf Fist(float s = 1) => S.X(S.Sub(S.U(S.Box(new Vector2(0, -.05f), new Vector2(.38f, .32f), .14f), S.Box(new Vector2(-.36f, .05f), new Vector2(.14f, .2f), .1f), S.Box(new Vector2(.05f, .45f), new Vector2(.28f, .16f), .05f)),
        S.U(S.Box(new Vector2(-.12f, -.15f), new Vector2(.012f, .2f)), S.Box(new Vector2(.1f, -.15f), new Vector2(.012f, .2f)), S.Box(new Vector2(.05f, .27f), new Vector2(.3f, .015f)))), Vector2.Zero, s);
    public static Sdf Ghost(float s = 1) => S.X(S.Sub(S.U(S.Circle(new Vector2(0, -.15f), .38f), S.Box(new Vector2(0, .2f), new Vector2(.38f, .35f))),
        S.U(S.Circle(new Vector2(-.25f, .62f), .13f), S.Circle(new Vector2(.25f, .62f), .13f), S.Circle(new Vector2(0, .55f), .1f), S.Circle(new Vector2(-.14f, -.15f), .08f), S.Circle(new Vector2(.14f, -.15f), .08f))), Vector2.Zero, s);
    public static Sdf Wing(float s = 1) => S.X(S.P(-.6f, .2f, -.2f, -.55f, .55f, -.65f, .3f, -.38f, .5f, -.3f, .25f, -.08f, .4f, .02f, .1f, .15f, .2f, .3f, -.3f, .35f), Vector2.Zero, s);
    public static Sdf Cloud(float s = 1) => S.X(S.U(S.Circle(new Vector2(-.3f, .05f), .24f), S.Circle(new Vector2(0, -.1f), .32f), S.Circle(new Vector2(.3f, .05f), .24f), S.Box(new Vector2(0, .14f), new Vector2(.5f, .15f), .14f)), Vector2.Zero, s);
    public static Sdf Footprint(float s = 1) => S.X(S.U(S.Ellipse(new Vector2(0, .15f), new Vector2(.22f, .38f)), S.Circle(new Vector2(-.2f, -.36f), .08f), S.Circle(new Vector2(-.04f, -.44f), .085f), S.Circle(new Vector2(.12f, -.42f), .08f), S.Circle(new Vector2(.24f, -.32f), .07f)), Vector2.Zero, s);
    public static Sdf Rock(float s = 1) => S.X(S.P(-.55f, .35f, -.42f, -.2f, -.05f, -.5f, .38f, -.36f, .58f, .1f, .35f, .5f, -.2f, .52f), Vector2.Zero, s);
    public static Sdf Tooth(Vector2 at, float w, float h, float rot) => S.X(S.P(-w, 0, w, 0, 0, -h), at, 1, rot);
    public static Sdf Chain(Vector2 a, Vector2 b, int n, float r)
    {
        var l = new List<Sdf>(); float ang = MathF.Atan2(b.Y - a.Y, b.X - a.X);
        for (int i = 0; i < n; i++) { var c = a + (b - a) * (i / (float)Math.Max(1, n - 1)); var link = S.Ellipse(Vector2.Zero, i % 2 == 0 ? new Vector2(r * 1.5f, r) : new Vector2(r * 1.5f, r * .45f)); l.Add(S.X(p => MathF.Abs(link(p)) - r * .28f, c, 1, ang)); }
        return S.U(l.ToArray());
    }

    // ------------------------------------------------------------------------------------------------------------- one skill
    sealed class Ctx
    {
        public IconPainter G = null!; public Pal E; public string Id = "";
        /// <summary>Glow, a dark outline, then the shape with a light-to-main gradient.</summary>
        public void Sym(Sdf s, Color? main = null, Color? light = null, float glow = 1f, float outline = .03f)
        {
            var m = main ?? E.Sym; var lt = light ?? E.Light;
            if (glow > 0) G.Glow(s, E.Glow with { A = E.Glow.A * glow }, .07f);
            if (outline > 0) G.Fill(S.Grow(s, outline), E.Dark with { A = .85f });
            G.Lin(s, lt, m, new Vector2(-.3f, -.6f), new Vector2(.3f, .6f));
        }
        public void Steel(Sdf s, float glow = .6f) => Sym(s, new Color(.7f, .74f, .82f), new Color(1f, 1f, 1f), glow);
        public void Gold(Sdf s, float glow = .6f) => Sym(s, new Color(.85f, .6f, .22f), new Color(1f, .95f, .7f), glow);
        public void Wood(Sdf s) => Sym(s, new Color(.45f, .28f, .14f), new Color(.75f, .55f, .32f), 0);
        public void Soft(Sdf s, Color c, float a = .35f) => G.Fill(s, c with { A = a });
        public void Lines(int n, Vector2 dir, Vector2 at, float spread, float len, Color c)
        {
            var nrm = new Vector2(-dir.Y, dir.X);
            for (int i = 0; i < n; i++) { var o = at + nrm * ((i - (n - 1) / 2f) * spread); G.Fill(S.Taper(o, o - dir * len * (1 - .2f * (i % 2)), .03f, .005f), c with { A = .7f }); }
        }
    }

    static Pal Tweak(Pal p, string id)
    {   // a few skills borrow a palette that reads better than their damage type
        return id switch { "bloodrite" or "bloodpact" => PalOf("blood"), "mirrorward" or "arcanesurge" or "chronofield" or "chronoblessing" => PalOf("arcane"), _ when Arcane(id) => PalOf("arcane"), _ => p };
    }
    /// <summary>The Chronomancer's time skills (F-34) read as arcane, not lightning.</summary>
    static bool Arcane(string id) => id is "chronobolt" or "rewind" or "accelerate" or "stasis" or "temporalrift";

    /// <summary>The element motif in the lower right corner (none for plain physical skills).</summary>
    static Sdf? Motif(string type) => type switch
    {
        "fire" => Flame(.42f), "frost" => Snowflake(.42f), "lightning" => Bolt(.42f), "poison" => Drop(.4f), "shadow" => Moon(.48f), "holy" => Sun(.45f),
        "blood" => Drop(.4f), "arcane" => Spark4(.5f), _ => null,
    };

    public static Image Render(string id, string type, int n = Size)
    {
        var g = new IconPainter(n); var e = Tweak(PalOf(type), id); var cx = new Ctx { G = g, E = e, Id = id };
        string motifType = id is "bloodrite" or "bloodpact" ? "blood" : id is "mirrorward" or "arcanesurge" or "chronofield" or "chronoblessing" || Arcane(id) ? "arcane" : type;
        Background(g, e, Seed(id));
        g.Clip = S.Grow(Badge, -.05f);
        if (!Symbol(cx, id)) GD.PushWarning($"[ICONS] no symbol drawing for {id}");
        if (Motif(motifType) is { } m)
        {
            var c = new Vector2(.56f, .56f);
            g.Fill(S.Circle(c, .25f), e.Dark with { A = .85f }); g.Stroke(S.Circle(c, .25f), .018f, e.Rim);
            g.Glow(S.X(m, c), e.Glow, .04f); g.Lin(S.X(m, c), e.Light, e.Sym, c - new Vector2(0, .2f), c + new Vector2(0, .2f));
        }
        g.Clip = null;
        Frame(g, e);
        return g.ToImage();
    }

    static uint Seed(string s) { uint h = 2166136261; foreach (char ch in s) { h ^= ch; h *= 16777619; } return h; }

    static void Background(IconPainter g, Pal e, uint seed)
    {
        g.Rad(Badge, e.Bg.Lerp(e.Light, .18f), e.Dark, new Vector2(0, -.15f), 1.25f);
        g.Clip = Badge;
        // soft diagonal light band and a faint rune circle with seeded rune marks
        g.Fill(S.Box(new Vector2(-.1f, -.1f), new Vector2(1.4f, .22f), 0, -.7f), e.Light with { A = .05f });
        g.Stroke(S.Circle(Vector2.Zero, .72f), .008f, e.Light with { A = .22f });
        g.Stroke(S.Circle(Vector2.Zero, .64f), .005f, e.Light with { A = .14f });
        var rng = new RandomNumberGenerator { Seed = seed };
        for (int k = 0; k < 10; k++)
        {
            float a = k * MathF.Tau / 10 + (seed % 7) * .05f; var d = new Vector2(MathF.Cos(a), MathF.Sin(a)); var nr = new Vector2(-d.Y, d.X); var c = d * .68f;
            Sdf mark = rng.RandiRange(0, 3) switch
            {
                0 => S.Seg(c - nr * .03f, c + nr * .03f, .008f),
                1 => S.U(S.Seg(c - d * .03f - nr * .025f, c + d * .03f, .007f), S.Seg(c - d * .03f + nr * .025f, c + d * .03f, .007f)),
                2 => S.U(S.Seg(c - d * .03f, c + d * .03f, .007f), S.Seg(c - nr * .025f, c + nr * .025f, .007f)),
                _ => S.Circle(c, .014f),
            };
            g.Fill(mark, e.Light with { A = .32f });
        }
        g.Clip = null;
    }

    static void Frame(IconPainter g, Pal e)
    {
        g.Lin(p => MathF.Abs(Badge(p) + .03f) - .03f, e.Rim.Lerp(Colors.White, .45f), e.Rim.Darkened(.5f), new Vector2(0, -.9f), new Vector2(0, .9f));
        g.Stroke(S.Grow(Badge, .065f), .006f, new Color(0, 0, 0, .5f));
        g.Stroke(Badge, .006f, new Color(0, 0, 0, .7f));
        g.Clip = S.Grow(Badge, .06f);
        g.Fill(S.Ellipse(new Vector2(-.1f, -.62f), new Vector2(.7f, .26f)), new Color(1, 1, 1, .08f));
        g.Clip = null;
        foreach (var c in new[] { new Vector2(-.8f, -.8f), new Vector2(.8f, -.8f), new Vector2(-.8f, .8f), new Vector2(.8f, .8f) })
            g.Fill(S.X(Gem(.07f), c), e.Rim.Lerp(Colors.White, .3f));
    }

    static Vector2 V(float x, float y) => new(x, y);
    static readonly float D45 = MathF.PI / 4;

    /// <summary>The symbol of one built-in skill. False when the id has no drawing.</summary>
    static bool Symbol(Ctx c, string id)
    {
        var e = c.E; var g = c.G;
        switch (id)
        {
            // ---- spells
            case "fire":   // orb with a flame trail
                c.Sym(S.Taper(V(.15f, -.15f), V(-.62f, .55f), .28f, .02f), e.Sym with { A = .9f }, glow: .6f);
                c.Sym(S.Circle(V(.18f, -.18f), .3f)); g.Fill(S.Circle(V(.1f, -.26f), .1f), new Color(1, 1, .9f, .9f)); break;
            case "nova":   // ring burst
                c.Sym(S.Ring(Vector2.Zero, .45f, .05f));
                for (int k = 0; k < 12; k++) { var d = new Vector2(MathF.Cos(k * MathF.Tau / 12), MathF.Sin(k * MathF.Tau / 12)); c.Sym(S.Taper(d * .52f, d * .74f, .05f, .005f), glow: .4f, outline: .015f); }
                c.Sym(Snowflake(.5f)); break;
            case "heal":   // plus cross with a leaf
                c.Sym(Plus(.95f), new Color(.55f, 1f, .5f), new Color(.95f, 1f, .9f)); c.Sym(S.X(Leaf(.5f), V(.32f, -.32f)), new Color(.35f, .85f, .3f)); break;
            case "zap":    // a bolt jumping between three nodes
            {
                var a = V(-.55f, .35f); var b = V(-.05f, -.35f); var d = V(.5f, .2f);
                c.Sym(S.Polyline(.04f, a, V(-.4f, .05f), V(-.25f, .1f), b, V(.1f, -.1f), V(.25f, .05f), d));
                foreach (var q in new[] { a, b, d }) c.Sym(S.Circle(q, .12f), e.Light); break;
            }
            case "shadowbolt":   // dark orb with a crescent inside and a trail
                c.Sym(S.Taper(V(-.1f, .1f), V(.62f, .6f), .3f, .02f), e.Sym with { A = .85f }, glow: .5f);
                c.Sym(S.Circle(V(-.15f, -.12f), .34f), e.Dark.Lerp(e.Sym, .4f), e.Sym); c.Sym(S.X(Moon(.5f), V(-.15f, -.12f)), e.Light); break;
            case "drain":   // blood drop pulled along a curving stream
                c.Sym(S.Arc(V(0, .05f), .5f, .04f, 3.3f, 5.9f), glow: .5f); c.Sym(S.Arc(V(0, .05f), .32f, .03f, 3.6f, 5.6f), glow: .3f);
                c.Sym(S.X(Drop(.55f), V(0, .2f)), new Color(.9f, .15f, .2f), new Color(1f, .7f, .7f)); break;
            case "hex":   // pentagram with a skull
            {
                var pts = Enumerable.Range(0, 5).Select(k => new Vector2(MathF.Cos(-MathF.PI / 2 + k * MathF.Tau / 5), MathF.Sin(-MathF.PI / 2 + k * MathF.Tau / 5)) * .58f).ToArray();
                c.Sym(S.U(S.Ring(Vector2.Zero, .58f, .025f), S.Polyline(.025f, pts[0], pts[2], pts[4], pts[1], pts[3], pts[0])), glow: .7f);
                c.Sym(Skull(.42f), e.Light); break;
            }
            case "meteor":   // a rock falling in fire
                c.Sym(S.Taper(V(-.15f, .2f), V(.65f, -.62f), .36f, .04f), new Color(1f, .45f, .1f), glow: .8f);
                c.Sym(S.X(Rock(.55f), V(-.2f, .25f)), new Color(.35f, .2f, .15f), new Color(.75f, .45f, .3f));
                g.Fill(S.X(S.Polyline(.02f, V(-.15f, -.1f), V(0, .05f), V(.1f, 0)), V(-.2f, .25f), .55f), new Color(1f, .8f, .3f)); break;
            case "icelance":   // a long ice shard
                c.Sym(S.P(.68f, -.68f, .1f, .02f, -.58f, .6f, -.02f, -.1f), e.Sym, e.Light);
                g.Fill(S.Seg(V(.6f, -.6f), V(-.5f, .5f), .012f), new Color(1, 1, 1, .8f)); break;
            case "thornvolley":   // three thorns in a fan
                for (int k = -1; k <= 1; k++) c.Sym(S.X(S.P(0, -.62f, .12f, .2f, 0, .35f, -.12f, .2f), V(k * .25f, .1f), 1, k * .4f), new Color(.45f, .75f, .25f));
                break;
            case "entangle":   // two vines wound round, with leaves
                c.Sym(S.Arc(V(-.2f, 0), .42f, .045f, -1.4f, 1.6f), new Color(.35f, .65f, .2f)); c.Sym(S.Arc(V(.2f, 0), .42f, .045f, 1.7f, 4.7f), new Color(.35f, .65f, .2f));
                foreach (var (q, r) in new[] { (V(.2f, -.42f), .4f), (V(-.2f, .42f), 3.5f), (V(.55f, .1f), 1.6f), (V(-.55f, -.1f), -1.6f) }) c.Sym(S.X(Leaf(.28f, 0), q, 1, r), new Color(.5f, .9f, .3f), outline: .015f);
                break;
            case "consecrate":   // holy ground circle with runes and a cross above
                c.Sym(p => MathF.Abs(((p - V(0, .35f)) / V(1, .35f)).Length() - .55f) * .35f - .02f, glow: .9f);
                c.Soft(S.Ellipse(V(0, .35f), V(.5f, .16f)), e.Glow, .45f); c.Sym(S.X(Cross(.6f), V(0, -.2f))); break;
            case "smite":   // a pillar of light striking down
                c.Sym(S.Taper(V(0, -.85f), V(0, .4f), .07f, .2f), e.Light, e.Light, 1.2f, 0);
                c.Sym(S.X(Sun(.55f), V(0, -.5f))); c.Soft(S.Ellipse(V(0, .45f), V(.5f, .12f)), e.Light, .6f);
                c.Lines(5, V(0, -1), V(0, .45f), .18f, .2f, e.Light); break;
            // ---- weapon arts
            case "cleave":   // an axe with a wide swing arc
                c.Soft(S.Arc(V(.1f, .3f), .62f, .07f, 3.6f, 5.6f), e.Light, .5f); c.Steel(S.X(Axe(.8f), V(.15f, .05f), 1, .5f)); break;
            case "whirlwind":   // spinning arcs round a level sword
                c.Sym(Spiral(1.1f), e.Light with { A = .8f }, glow: .4f); c.Steel(Sword(.78f, MathF.PI / 2)); break;
            case "leap":   // the jump arc and a hammer coming down on cracks
                c.Soft(S.Arc(V(-.1f, .4f), .55f, .03f, 3.4f, 5.4f), e.Light, .6f);
                c.Steel(S.X(Hammer(.6f), V(.3f, -.05f), 1, .5f)); c.Sym(S.Polyline(.025f, V(-.6f, .62f), V(-.3f, .55f), V(-.15f, .68f), V(.15f, .58f), V(.4f, .7f)), e.Light); break;
            case "lunge":   // a spear thrust with speed lines
                c.Lines(4, V(1, 0), V(-.15f, 0), .14f, .45f, e.Light); c.Steel(Spear(.9f, MathF.PI / 2)); break;
            case "shieldbash":   // shield with an impact star
                c.Sym(S.X(Star5(.55f), V(.4f, -.35f)), new Color(1f, .85f, .4f), new Color(1, 1, .9f)); c.Gold(S.X(ShieldShape(.85f), V(-.08f, .05f)));
                g.Fill(S.X(S.U(S.Box(Vector2.Zero, V(.04f, .45f)), S.Box(V(0, -.15f), V(.33f, .04f))), V(-.08f, .05f)), new Color(.55f, .1f, .1f)); break;
            case "phantomrush":   // after-images of a sword dash
                for (int k = 2; k >= 1; k--) g.Fill(S.X(Sword(.75f, D45), V(-.18f * k, .18f * k)), e.Light with { A = .18f * (3 - k) });
                c.Steel(S.X(Sword(.75f, D45), V(.12f, -.12f))); break;
            case "moonglaive":   // a crescent blade with motion arcs
                c.Soft(S.Arc(Vector2.Zero, .66f, .025f, 3.5f, 5.2f), e.Light, .5f); c.Steel(S.X(Moon(1.15f), V(-.05f, 0), 1, .5f)); break;
            case "frostchakram":   // a spiked ring with a snowflake
            {
                var l = new List<Sdf> { S.Ring(Vector2.Zero, .42f, .07f) };
                for (int k = 0; k < 6; k++) { float a = k * MathF.Tau / 6; l.Add(S.X(S.P(-.08f, 0, .08f, 0, .02f, -.22f), new Vector2(MathF.Cos(a), MathF.Sin(a)) * .47f, 1, a + MathF.PI / 2 + .4f)); }
                c.Sym(S.U(l.ToArray())); c.Sym(Snowflake(.42f), e.Light); break;
            }
            case "bladeorbit":   // three blades circling a core
                c.Soft(S.Ring(Vector2.Zero, .45f, .02f), e.Light, .5f); c.Gold(S.Circle(Vector2.Zero, .14f));
                for (int k = 0; k < 3; k++) { float a = k * MathF.Tau / 3 - MathF.PI / 2; c.Steel(S.X(Dagger(.5f), new Vector2(MathF.Cos(a), MathF.Sin(a)) * .45f, 1, a + MathF.PI)); }
                break;
            case "emberhalo":   // six flames circling
                c.Soft(S.Ring(Vector2.Zero, .45f, .03f), e.Glow, .6f);
                for (int k = 0; k < 6; k++) { float a = k * MathF.Tau / 6 - MathF.PI / 2; c.Sym(S.X(Flame(.36f), new Vector2(MathF.Cos(a), MathF.Sin(a)) * .45f, 1, a + MathF.PI / 2)); }
                c.Sym(S.Circle(Vector2.Zero, .12f), e.Light); break;
            case "arcsentry":   // a tripod tower with a charged orb
                c.Steel(S.U(S.Seg(V(0, -.1f), V(-.4f, .62f), .04f), S.Seg(V(0, -.1f), V(.4f, .62f), .04f), S.Seg(V(0, -.1f), V(0, .65f), .04f), S.Box(V(0, -.12f), V(.18f, .06f), .02f)), .3f);
                c.Sym(S.Circle(V(0, -.42f), .2f), e.Light); c.Sym(S.X(Bolt(.35f), V(-.45f, -.45f), 1, -.4f)); c.Sym(S.X(Bolt(.35f), V(.45f, -.45f), 1, .4f)); break;
            case "thorntotem":   // a carved totem crowned with thorns
                c.Wood(S.U(S.Box(V(0, .05f), V(.24f, .6f), .05f), S.Box(V(0, -.05f), V(.36f, .06f), .02f)));
                g.Fill(S.U(S.Circle(V(-.1f, -.3f), .06f), S.Circle(V(.1f, -.3f), .06f), S.Box(V(0, -.14f), V(.12f, .03f)), S.Circle(V(-.1f, .2f), .05f), S.Circle(V(.1f, .2f), .05f)), new Color(.6f, 1f, .4f));
                for (int k = -2; k <= 2; k++) c.Sym(S.X(S.P(-.06f, 0, .06f, 0, 0, -.25f), V(k * .1f, -.55f), 1, k * .3f), new Color(.45f, .8f, .25f), outline: .015f);
                break;
            case "sunlance":   // a beam from a small sun
                c.Sym(S.Taper(V(-.45f, -.45f), V(.7f, .7f), .12f, .05f), e.Light, e.Light, 1.2f, 0); c.Sym(S.X(Sun(.6f), V(-.45f, -.45f))); break;
            case "voidray":   // an eye firing a dark beam
                c.Sym(S.Taper(V(-.2f, 0), V(.8f, 0), .14f, .08f), e.Sym, e.Light, 1, .02f); c.Sym(S.X(Eye(.75f), V(-.32f, 0)), e.Sym, e.Light); break;
            case "singularity":   // a black hole with a bright accretion ring
                c.Sym(Spiral(1.1f), e.Sym with { A = .8f }, glow: .5f); c.Sym(S.Ring(Vector2.Zero, .26f, .05f), e.Light, e.Light, 1.4f);
                g.Fill(S.Circle(Vector2.Zero, .22f), new Color(0, 0, 0, 1)); break;
            case "maelstrom":   // a water whirl with droplets
                c.Sym(S.X(Spiral(1.05f), Vector2.Zero, 1, 2f));
                foreach (var q in new[] { V(-.5f, -.45f), V(.52f, -.35f), V(-.45f, .5f) }) c.Sym(S.X(Drop(.22f), q), e.Light, outline: .015f);
                break;
            case "deathmark":   // a reticle round a skull
                c.Sym(S.U(S.Ring(Vector2.Zero, .5f, .03f), S.Seg(V(0, -.7f), V(0, -.38f), .03f), S.Seg(V(0, .38f), V(0, .7f), .03f), S.Seg(V(-.7f, 0), V(-.38f, 0), .03f), S.Seg(V(.38f, 0), V(.7f, 0), .03f)), new Color(.9f, .3f, .35f));
                c.Sym(Skull(.55f), e.Light); break;
            case "brand":   // a glowing branded rune
                c.Soft(S.Circle(Vector2.Zero, .5f), e.Glow, .5f);
                c.Sym(S.U(S.Sub(Gem(1f), Gem(.7f)), S.Seg(V(0, -.4f), V(0, .4f), .04f), S.Seg(V(-.26f, -.1f), V(.26f, -.1f), .04f)), e.Light, e.Light, 1.2f); break;
            case "firewall":   // a row of flames on a burning line
                c.Sym(S.Box(V(0, .5f), V(.7f, .05f), .04f), e.Light);
                foreach (var (x, s) in new[] { (-.48f, .55f), (0f, .75f), (.48f, .55f) }) c.Sym(S.X(Flame(s), V(x, .45f - .5f * s)));
                break;
            case "icewall":   // a row of ice crystals
                foreach (var (x, h) in new[] { (-.48f, .5f), (-.16f, .75f), (.16f, .65f), (.48f, .45f) }) c.Sym(S.P(x, .55f - h * 1.25f, x + .14f, .55f - h * .9f, x + .12f, .55f, x - .12f, .55f, x - .14f, .55f - h * .9f));
                break;
            case "bramble":   // a thorny hedge
                c.Sym(S.U(S.Circle(V(-.3f, .3f), .3f), S.Circle(V(.1f, .2f), .36f), S.Circle(V(.45f, .35f), .25f), S.Box(V(0, .5f), V(.7f, .12f))), new Color(.25f, .5f, .15f), new Color(.55f, .85f, .3f));
                for (int k = 0; k < 7; k++) { float a = MathF.PI + k * MathF.PI / 6; var q = V(.05f, .32f) + new Vector2(MathF.Cos(a) * .6f, MathF.Sin(a) * .45f); c.Sym(S.X(S.P(-.05f, 0, .05f, 0, 0, -.18f), q, 1, a + MathF.PI / 2), new Color(.8f, .9f, .5f), outline: .012f); }
                break;
            case "blink":   // a faint start sparkle, a dashed arc, a bright end sparkle
                g.Fill(S.X(Spark4(.55f), V(-.45f, .35f)), e.Light with { A = .3f });
                for (int k = 0; k < 4; k++) { float a = 3.5f + k * .45f; c.Sym(S.Circle(V(0, .35f) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * .48f, .04f), e.Light, outline: 0); }
                c.Sym(S.X(Spark4(.8f), V(.4f, -.3f)), e.Light); break;
            case "thunderstep":   // a footprint struck by a bolt
                c.Sym(S.X(Footprint(.8f), V(-.15f, .1f), 1, -.3f), e.Sym.Lerp(e.Dark, .3f), e.Sym); c.Sym(S.X(Bolt(.6f), V(.35f, -.2f)), new Color(1f, .95f, .5f)); break;
            case "beartrap":   // steel jaws with teeth
            {
                c.Steel(S.U(S.Arc(V(0, .1f), .5f, .05f, 3.3f, 6.1f), S.Arc(V(0, -.1f), .5f, .05f, .2f, 2.95f)));
                for (int k = 0; k < 5; k++) { float a = 3.5f + k * .55f; c.Steel(Tooth(V(0, .1f) + new Vector2(MathF.Cos(a), MathF.Sin(a)) * .46f, .06f, .16f, a - MathF.PI / 2), 0); float b = .45f + k * .55f; c.Steel(Tooth(V(0, -.1f) + new Vector2(MathF.Cos(b), MathF.Sin(b)) * .46f, .06f, .16f, b - MathF.PI / 2), 0); }
                c.Steel(S.Circle(V(0, 0), .08f)); break;
            }
            case "runemine":   // a spiked mine with a rune star
            {
                var l = new List<Sdf> { S.Circle(Vector2.Zero, .38f) };
                for (int k = 0; k < 8; k++) { var d = new Vector2(MathF.Cos(k * D45), MathF.Sin(k * D45)); l.Add(S.Taper(d * .3f, d * .55f, .07f, .02f)); }
                c.Sym(S.U(l.ToArray()), new Color(.4f, .2f, .15f), new Color(.75f, .45f, .3f)); c.Sym(S.Star(Vector2.Zero, .28f, .11f, 6), e.Light, e.Light, 1.3f, .015f); break;
            }
            case "stoneskin":   // a shield of cracked stone plates
                c.Sym(ShieldShape(.95f), new Color(.45f, .42f, .38f), new Color(.8f, .76f, .68f), .4f);
                g.Fill(S.Polyline(.02f, V(-.3f, -.45f), V(-.05f, -.1f), V(-.3f, .2f), V(0, .55f)), new Color(.2f, .18f, .15f)); g.Fill(S.Polyline(.02f, V(-.05f, -.1f), V(.35f, -.25f)), new Color(.2f, .18f, .15f));
                g.Fill(S.Polyline(.02f, V(-.3f, .2f), V(.3f, .15f)), new Color(.2f, .18f, .15f)); break;
            case "mirrorward":   // a mirror diamond bouncing an arrow back
                c.Sym(Gem(1.05f), e.Sym, e.Light); g.Fill(S.Seg(V(-.18f, -.25f), V(.1f, .2f), .025f), new Color(1, 1, 1, .7f));
                c.Sym(S.Polyline(.03f, V(-.75f, -.1f), V(-.42f, 0), V(-.75f, .2f)), e.Light, outline: .015f); break;
            case "soulsiphon":   // a ghost drawn into a spiral
                c.Soft(Spiral(1.1f), e.Glow, .6f); c.Sym(S.X(Ghost(.75f), V(0, -.05f)), e.Light, new Color(1, 1, 1)); break;
            case "chainhook":   // a hook on a chain
                c.Steel(Chain(V(-.6f, -.6f), V(.0f, -.05f), 5, .1f), .3f);
                c.Steel(S.U(S.Seg(V(.05f, -.05f), V(.2f, .1f), .05f), S.Arc(V(.1f, .35f), .25f, .055f, -1.2f, 2.6f), S.X(S.P(-.06f, 0, .06f, 0, 0, -.18f), V(-.12f, .2f), 1, -.4f))); break;
            case "starfall":   // three stars with trails
                foreach (var (q, s) in new[] { (V(-.4f, .1f), .5f), (V(.1f, .45f), .65f), (V(.45f, -.2f), .45f) })
                { c.Soft(S.Taper(q, q + V(-.35f, -.55f) * s, .1f * s, .01f), e.Light, .5f); c.Sym(S.X(Star5(s), q)); }
                break;
            case "earthquake":   // the ground splits with rocks thrown up
                c.Sym(S.Box(V(0, .52f), V(.75f, .12f)), new Color(.45f, .32f, .2f), new Color(.7f, .55f, .35f), 0);
                c.Sym(S.Polyline(.035f, V(0, .65f), V(-.08f, .4f), V(.08f, .2f), V(-.05f, 0)), e.Dark.Lerp(new Color(1f, .6f, .2f), .7f), new Color(1f, .9f, .5f));
                foreach (var (q, s) in new[] { (V(-.4f, -.25f), .35f), (V(.35f, -.4f), .3f), (V(.1f, -.6f), .2f) }) c.Sym(S.X(Rock(s), q), new Color(.5f, .4f, .3f), new Color(.8f, .7f, .55f), .3f);
                break;
            case "cometdive":   // a sword diving down as a comet
                c.Sym(S.Taper(V(.1f, .35f), V(-.5f, -.75f), .3f, .03f), e.Sym, e.Light, 1);
                c.Steel(S.X(Sword(.7f, MathF.PI * .83f), V(.08f, .05f))); break;
            case "ricochet":   // an arrow bouncing off a wall
                c.Sym(S.Box(V(.62f, 0), V(.06f, .7f)), new Color(.5f, .45f, .4f), new Color(.75f, .7f, .65f), 0);
                c.Soft(S.Polyline(.02f, V(-.6f, -.55f), V(.52f, 0)), e.Light, .6f); c.Steel(S.X(Arrow(.6f), V(-.05f, .3f), 1, -2.03f)); c.Sym(S.X(Star5(.25f), V(.5f, 0)), new Color(1f, .9f, .5f), outline: .01f); break;
            case "bloodrite":   // a blood drop over a ritual ring
                c.Sym(S.Ring(Vector2.Zero, .55f, .03f)); for (int k = 0; k < 6; k++) { var d = new Vector2(MathF.Cos(k * MathF.Tau / 6), MathF.Sin(k * MathF.Tau / 6)); c.Sym(S.Circle(d * .55f, .06f), e.Light, outline: .012f); }
                c.Sym(Drop(.85f)); break;
            case "chronofield":   // a clock inside a field circle
                c.Soft(S.Circle(Vector2.Zero, .62f), e.Glow, .35f); c.Sym(S.Ring(Vector2.Zero, .62f, .02f), e.Light); c.Sym(Clock(1.05f), e.Light, e.Sym); break;
            case "executioner":   // a great axe and a skull
                c.Steel(S.X(GreatAxe(.85f), V(.1f, -.05f), 1, .35f)); c.Sym(S.X(Skull(.38f), V(-.4f, .42f)), new Color(.9f, .85f, .75f), new Color(1, 1, 1), .3f); break;
            // ---- enchants (auras)
            case "hymnvalor":   // a song note over a sword
                c.Steel(S.X(Sword(.6f, -.35f), V(.2f, .05f)), .3f); c.Sym(S.X(Note(.8f), V(-.2f, -.05f))); break;
            case "aegischorus":   // a shield under a song note
                c.Gold(S.X(ShieldShape(.75f), V(-.12f, .12f)), .4f); c.Sym(S.X(Note(.65f), V(.3f, -.3f)), e.Light); break;
            case "swiftwind":   // a feather in wind lines
                c.Lines(3, V(1, 0), V(.05f, .45f), .12f, .55f, e.Light); c.Sym(Feather(.85f), new Color(.85f, .9f, 1f), new Color(1, 1, 1)); break;
            case "renewal":   // a sprout with a water drop
                c.Sym(S.Seg(V(0, .62f), V(0, -.05f), .04f), new Color(.3f, .6f, .2f)); c.Sym(S.X(Leaf(.55f, 0), V(-.24f, -.1f), 1, -.8f), new Color(.45f, .9f, .35f));
                c.Sym(S.X(Leaf(.55f, 0), V(.24f, -.18f), 1, .8f), new Color(.45f, .9f, .35f)); c.Sym(S.X(Drop(.3f), V(0, -.55f)), new Color(.5f, .85f, 1f), new Color(1, 1, 1), outline: .015f); break;
            case "purify":   // a halo over a sparkle
                c.Sym(S.X(p => MathF.Abs(((p) / V(1, .35f)).Length() - .45f) * .35f - .02f, V(0, -.48f)), e.Light, e.Light, 1.2f, .01f); c.Sym(S.X(Spark4(.95f), V(0, .12f))); break;
            case "unbreakable":   // a clenched fist with an up-arrow aura
                c.Soft(S.X(ArrowUp(1.3f), V(0, -.05f)), e.Light, .2f); c.Sym(Fist(.95f), new Color(.85f, .65f, .45f), new Color(1f, .9f, .75f)); break;
            case "arcanesurge":   // a four-point star rising
                c.Sym(S.X(Spark4(1f), V(0, .05f))); c.Sym(S.X(ArrowUp(.35f), V(-.5f, -.4f)), e.Light, outline: .015f); c.Sym(S.X(ArrowUp(.35f), V(.5f, -.4f)), e.Light, outline: .015f); break;
            case "elementward":   // a shield quartered by the four elements
                c.Sym(ShieldShape(.95f), new Color(.3f, .35f, .5f), new Color(.6f, .65f, .8f), .5f);
                c.Sym(S.X(Flame(.3f), V(-.2f, -.28f)), new Color(1f, .5f, .15f), new Color(1, .9f, .6f), 0, .01f); c.Sym(S.X(Snowflake(.28f), V(.2f, -.28f)), new Color(.6f, .9f, 1f), new Color(1, 1, 1), 0, .01f);
                c.Sym(S.X(Bolt(.28f), V(-.18f, .15f)), new Color(1f, .9f, .4f), new Color(1, 1, .9f), 0, .01f); c.Sym(S.X(Drop(.25f), V(.18f, .15f)), new Color(.5f, .95f, .3f), new Color(.9f, 1, .8f), 0, .01f); break;
            case "bloodpact":   // a heart pierced by a dagger
                c.Sym(Heart(.95f)); c.Steel(Dagger(.85f, -D45), .2f); break;
            case "chronoblessing":   // an hourglass with an up-arrow aura
                c.Soft(S.X(ArrowUp(1.25f), V(0, -.05f)), e.Light, .2f); c.Gold(Hourglass(.85f)); g.Fill(S.U(S.P(-.18f, -.3f, .18f, -.3f, 0, -.05f), S.P(0, .25f, .22f, .4f, -.22f, .4f)), new Color(.6f, .8f, 1f)); break;
            // ---- warcries and others
            case "taunt":   // a roaring mouth with sound waves both ways
                c.Sym(S.Sub(S.Circle(V(0, 0), .32f), S.P(-.2f, .02f, .2f, .02f, 0, .26f)), new Color(.85f, .6f, .35f), new Color(1f, .9f, .7f));
                foreach (float r in new[] { .45f, .6f }) { c.Sym(S.Arc(Vector2.Zero, r, .03f, -.6f, .6f), e.Light, outline: .015f); c.Sym(S.Arc(Vector2.Zero, r, .03f, MathF.PI - .6f, MathF.PI + .6f), e.Light, outline: .015f); }
                break;
            case "resurrect":   // an ankh in light
                c.Soft(S.Circle(V(0, -.05f), .55f), e.Glow, .4f); c.Gold(Ankh(1.05f), 1.2f); break;
            case "warcry":   // a war horn blowing waves
                c.Sym(S.P(-.6f, .2f, -.1f, -.15f, .25f, -.35f, .35f, -.1f, .05f, .15f, -.5f, .35f), new Color(.75f, .55f, .3f), new Color(1f, .9f, .65f));
                foreach (float r in new[] { .22f, .38f }) c.Sym(S.Arc(V(.3f, -.22f), r, .03f, -1.1f, .6f), e.Light, outline: .015f);
                break;
            case "groundslam":   // a hammer down on a shockwave ring
                c.Soft(S.X(p => MathF.Abs((p / V(1, .3f)).Length() - .6f) * .3f - .02f, V(0, .5f)), e.Light, .8f);
                c.Steel(S.X(Hammer(.75f), V(0, -.05f), 1, MathF.PI)); break;
            case "shadowstep":   // a dagger over shadow footsteps
                foreach (var (q, a) in new[] { (V(-.5f, .45f), .1f), (V(-.2f, .15f), .1f) }) g.Fill(S.X(Footprint(.35f), q, 1, .8f), e.Sym with { A = .45f + a });
                c.Steel(S.X(Dagger(.8f), V(.2f, -.1f), 1, D45)); c.Sym(S.X(Moon(.4f), V(-.45f, -.45f)), e.Light, outline: .015f); break;
            case "poisonblade":   // a dagger dripping venom
                c.Steel(S.X(Dagger(.85f), V(.05f, -.1f), 1, MathF.PI * .85f));
                c.Sym(S.X(Drop(.3f), V(.25f, .52f)), e.Sym, e.Light, outline: .015f); c.Sym(S.X(Drop(.18f), V(.5f, .3f)), e.Sym, e.Light, outline: .01f); break;
            case "fan":   // five daggers in a fan
                for (int k = -2; k <= 2; k++) c.Steel(S.X(Dagger(.55f), V(k * .2f, .25f - MathF.Abs(k) * .06f), 1, k * .35f), .3f);
                break;
            case "arcanestrike":   // a sword wrapped in lightning
                c.Steel(Sword(.9f, D45)); c.Sym(S.Polyline(.025f, V(-.5f, .2f), V(-.25f, .05f), V(-.2f, -.2f), V(.05f, -.3f), V(.1f, -.55f), V(.3f, -.5f)), new Color(1f, .95f, .5f)); break;
            case "volley":   // three arrows side by side
                for (int k = -1; k <= 1; k++) c.Steel(S.X(Arrow(.75f), V(k * .2f - .02f, k * -.2f), 1, D45), .3f);
                break;
            case "rain":   // arrows falling from a cloud
                c.Sym(S.X(Cloud(.8f), V(0, -.48f)), new Color(.5f, .52f, .6f), new Color(.85f, .87f, .95f), .2f);
                foreach (var x in new[] { -.35f, 0f, .35f }) c.Steel(S.X(Arrow(.42f), V(x, .2f + MathF.Abs(x) * .3f), 1, MathF.PI), .2f);
                break;
            // ---- F-34 Monk
            case "palmstrike":   // an open palm with a chi burst in front of it
                c.Sym(S.Ring(V(-.05f, -.3f), .3f, .03f), new Color(1f, .85f, .5f), glow: .8f);
                c.Sym(S.U(S.Box(V(0, .18f), V(.26f, .26f), .1f), S.Box(V(-.18f, -.18f), V(.05f, .2f), .05f), S.Box(V(-.06f, -.24f), V(.05f, .24f), .05f), S.Box(V(.06f, -.22f), V(.05f, .22f), .05f), S.Box(V(.18f, -.16f), V(.05f, .18f), .05f), S.Box(V(.33f, .12f), V(.05f, .16f), .05f, -.6f)), new Color(.95f, .78f, .6f), new Color(1f, .95f, .85f)); break;
            case "cyclonekick":   // a foot in a whirl of three arcs
                foreach (int k in new[] { 0, 1, 2 }) c.Sym(S.Arc(Vector2.Zero, .5f - k * .1f, .035f, k * 2.1f, k * 2.1f + 2.4f), new Color(.85f, .9f, 1f), glow: .5f);
                c.Sym(S.X(Footprint(.55f), V(0, .02f), 1, .5f), new Color(.95f, .8f, .62f), new Color(1f, .95f, .85f)); break;
            case "chiwave":   // a fist inside a widening double ring
                c.Sym(S.Ring(Vector2.Zero, .6f, .04f), new Color(1f, .8f, .4f), glow: .9f); c.Sym(S.Ring(Vector2.Zero, .45f, .025f), new Color(1f, .9f, .6f), glow: .5f);
                c.Sym(Fist(.55f), new Color(.95f, .78f, .6f), new Color(1f, .95f, .85f)); break;
            case "cranekick":   // a crane's wing over a descending arrow
                c.Sym(S.X(Wing(.75f), V(-.05f, -.2f)), new Color(.95f, .95f, 1f), new Color(1f, 1f, 1f), .5f);
                c.Gold(S.X(ArrowUp(.5f), V(.2f, .3f), 1, MathF.PI)); break;
            case "innerpeace":   // a lotus: five petals over a calm line
                for (int k = -2; k <= 2; k++) c.Sym(S.X(Leaf(.42f, 0), V(k * .16f, -.05f + MathF.Abs(k) * .06f), 1, k * .42f), new Color(1f, .75f, .85f), new Color(1f, .95f, .97f), .5f);
                c.Sym(S.Box(V(0, .4f), V(.5f, .035f), .02f), new Color(.6f, .9f, 1f)); break;
            case "ironbody":   // an iron torso plate with a shield ring
                c.Sym(S.Ring(Vector2.Zero, .62f, .035f), new Color(.8f, .85f, .95f), glow: .6f);
                c.Steel(S.P(-.36f, -.42f, .36f, -.42f, .42f, -.05f, .2f, .5f, -.2f, .5f, -.42f, -.05f));
                g.Fill(S.Seg(V(0, -.36f), V(0, .42f), .02f), new Color(.3f, .32f, .38f, .9f)); g.Fill(S.Seg(V(-.3f, -.05f), V(.3f, -.05f), .02f), new Color(.3f, .32f, .38f, .9f)); break;
            // ---- F-34 Bard
            case "sonicriff":   // a note shooting sound waves
                c.Sym(S.X(Note(.65f), V(-.22f, .05f)));
                foreach (float r in new[] { .3f, .45f, .6f }) c.Sym(S.Arc(V(-.1f, 0), r, .03f, -.7f, .7f), glow: .4f, outline: .015f); break;
            case "discord":   // two crossed broken notes in a burst
                c.Sym(S.Star(Vector2.Zero, .66f, .3f, 7), e.Dark.Lerp(e.Sym, .5f), e.Sym, .6f);
                c.Sym(S.X(Note(.5f), V(-.12f, 0), 1, -.35f), e.Light); c.Sym(S.X(Note(.42f), V(.2f, .05f), 1, .4f), e.Light); break;
            case "lullaby":   // a crescent moon with a note and sleepy stars
                c.Sym(S.X(Moon(.95f), V(-.1f, 0)), new Color(.75f, .85f, 1f), new Color(1f, 1f, 1f));
                c.Sym(S.X(Note(.38f), V(.3f, -.2f)), e.Light); c.Sym(S.X(Spark4(.25f), V(.32f, .32f)), e.Light, outline: .01f); break;
            case "balladheroes":   // a lyre with a sword through its arms
                c.Gold(S.U(S.Arc(V(-.25f, -.05f), .3f, .045f, 1.6f, 4.7f), S.Arc(V(.25f, -.05f), .3f, .045f, -1.55f, 1.55f), S.Box(V(0, .3f), V(.3f, .05f), .02f)));
                foreach (float x in new[] { -.12f, 0f, .12f }) g.Fill(S.Seg(V(x, -.25f), V(x, .28f), .012f), new Color(1f, .97f, .8f, .9f));
                c.Steel(S.X(Sword(.55f), V(0, -.25f)), .3f); break;
            case "mendingverse":   // a heart with notes rising from it
                c.Sym(Heart(.75f), new Color(.95f, .45f, .55f), new Color(1f, .85f, .88f));
                c.Sym(S.X(Note(.3f), V(-.4f, -.45f)), e.Light, outline: .015f); c.Sym(S.X(Note(.25f), V(.42f, -.5f)), e.Light, outline: .015f); break;
            // ---- F-34 Shaman
            case "stormtotem":   // a carved totem pole struck by a bolt
                c.Wood(S.U(S.Box(V(0, .15f), V(.18f, .5f), .04f), S.Box(V(0, -.28f), V(.34f, .07f), .03f)));
                g.Fill(S.U(S.Circle(V(-.07f, .02f), .04f), S.Circle(V(.07f, .02f), .04f), S.Box(V(0, .2f), V(.08f, .025f))), new Color(.15f, .08f, .04f));
                c.Sym(S.X(Bolt(.5f), V(0, -.55f))); break;
            case "lavaburst":   // a molten rock with cracks of fire
                c.Sym(S.Taper(V(.1f, -.1f), V(-.6f, .55f), .3f, .02f), new Color(1f, .5f, .1f), glow: .7f);
                c.Sym(S.X(Rock(.6f), V(.12f, -.12f)), new Color(.3f, .15f, .1f), new Color(.6f, .3f, .2f));
                g.Fill(S.X(S.Polyline(.03f, V(-.3f, -.1f), V(0, .05f), V(.1f, -.2f), V(.3f, 0)), V(.12f, -.12f), .6f), new Color(1f, .75f, .25f)); break;
            case "earthshock":   // rock spikes bursting from a crack
                c.Sym(S.U(Tooth(V(-.3f, .35f), .14f, .55f, 0), Tooth(V(.05f, .35f), .18f, .8f, 0), Tooth(V(.36f, .35f), .12f, .45f, 0)), new Color(.55f, .45f, .35f), new Color(.85f, .75f, .6f), .3f);
                c.Sym(S.Box(V(0, .45f), V(.62f, .07f), .03f), new Color(.4f, .32f, .25f), new Color(.6f, .5f, .4f), 0); break;
            case "healingspring":   // a water drop over a pool with a leaf
                c.Sym(S.Ellipse(V(0, .42f), V(.55f, .12f)), new Color(.45f, .8f, 1f), new Color(.85f, .97f, 1f), .5f);
                c.Sym(S.X(Drop(.7f), V(0, -.1f)), new Color(.5f, .85f, 1f), new Color(.95f, 1f, 1f));
                c.Sym(S.X(Leaf(.35f), V(.35f, -.35f)), new Color(.4f, .85f, .35f)); break;
            case "windfury":   // three swirling wind gusts
                foreach (int k in new[] { -1, 0, 1 }) c.Sym(S.U(S.Seg(V(-.55f, k * .25f), V(.2f, k * .25f), .035f), S.Arc(V(.2f, k * .25f - .1f), .1f, .035f, -1.6f, 1.6f)), new Color(.8f, .95f, 1f), new Color(1f, 1f, 1f), .5f);
                break;
            // ---- F-34 Samurai
            case "iaido":   // a katana half drawn from its scabbard with a flash
                c.Sym(S.Seg(V(-.55f, .45f), V(.0f, -.1f), .07f), new Color(.2f, .15f, .2f), new Color(.45f, .35f, .45f), 0);
                c.Steel(S.Taper(V(-.05f, -.05f), V(.62f, -.6f), .05f, .015f));
                c.Sym(S.X(Spark4(.35f), V(.05f, -.12f)), new Color(1f, 1f, .8f), new Color(1f, 1f, 1f)); break;
            case "crescentslash":   // a big crescent arc of a cut
                c.Sym(S.Sub(S.Circle(Vector2.Zero, .62f), S.Circle(V(.18f, -.12f), .55f)), new Color(.85f, .9f, 1f), new Color(1f, 1f, 1f), .8f);
                c.Steel(S.X(Sword(.45f, 0, .7f, 1.2f), V(.25f, .1f), 1, .6f), .3f); break;
            case "windscythe":   // a flying sickle of wind
                c.Sym(S.Arc(V(.1f, .1f), .5f, .07f, 3.6f, 5.9f), new Color(.75f, 1f, .9f), new Color(1f, 1f, 1f), .8f);
                c.Lines(3, V(.7f, -.7f).Normalized(), V(-.2f, .3f), .14f, .35f, new Color(.85f, 1f, .95f)); break;
            case "kiai":   // a shouting mouth: a wedge with sound bars
                c.Sym(S.P(-.55f, -.1f, .0f, -.3f, .0f, .3f, -.55f, .1f), new Color(.9f, .2f, .2f), new Color(1f, .7f, .6f));
                foreach (float r in new[] { .2f, .38f, .56f }) c.Sym(S.Arc(V(-.1f, 0), r, .035f, -.8f, .8f), new Color(1f, .85f, .4f), glow: .5f, outline: .015f); break;
            case "thousandcuts":   // many slashes criss-crossing
                for (int k = 0; k < 6; k++) { float a = k * .52f + .2f; var d = new Vector2(MathF.Cos(a), MathF.Sin(a)); c.Sym(S.Taper(-d * .55f, d * .55f, .02f, .005f), new Color(.85f, .9f, 1f), new Color(1f, 1f, 1f), .4f, .012f); }
                c.Sym(S.Circle(Vector2.Zero, .1f), new Color(.9f, .2f, .25f)); break;
            // ---- F-34 Chronomancer
            case "chronobolt":   // a small clock flying with a trail
                c.Sym(S.Taper(V(.1f, -.1f), V(-.62f, .55f), .26f, .02f), e.Sym with { A = .85f }, glow: .6f);
                c.Sym(S.X(Clock(.6f), V(.15f, -.15f)), e.Light, e.Sym); break;
            case "rewind":   // an hourglass inside a counter-clockwise arrow
                c.Sym(S.U(S.Arc(Vector2.Zero, .55f, .045f, .6f, 5.6f), S.X(S.P(0, -.12f, .14f, .1f, -.14f, .1f), V(.47f, -.28f), 1, 2.2f)), glow: .6f);
                c.Gold(Hourglass(.55f)); break;
            case "accelerate":   // clock hands with speed lines
                c.Sym(S.X(Clock(.75f), V(.12f, 0)));
                c.Lines(3, V(1, 0), V(-.45f, 0), .2f, .3f, e.Light); break;
            case "stasis":   // a figure frozen inside a diamond of time
                c.Sym(S.Grow(S.P(0, -.66f, .55f, 0, 0, .66f, -.55f, 0), .02f), e.Dark.Lerp(e.Sym, .4f), e.Sym, .8f);
                c.Sym(S.X(Hourglass(.45f), Vector2.Zero), e.Light); break;
            case "temporalrift":   // a spiral rift with clock ticks around it
                c.Sym(Spiral(1f), glow: .8f);
                for (int k = 0; k < 12; k++) { var d = new Vector2(MathF.Cos(k * MathF.Tau / 12), MathF.Sin(k * MathF.Tau / 12)); g.Fill(S.Seg(d * .62f, d * (k % 3 == 0 ? .5f : .56f), .015f), e.Light with { A = .8f }); }
                break;
            default: return false;
        }
        return true;
    }

    // ------------------------------------------------------------------------------------------------------------- generator
    /// <summary>--gen-icons: every skill of data/skills.json to <see cref="Dir"/>, a contact sheet, and a duplicate check.</summary>
    public static void GenerateAll()
    {
        var dir = ProjectSettings.GlobalizePath(Dir); System.IO.Directory.CreateDirectory(dir);
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(ProjectSettings.GlobalizePath("res://data/skills.json")));
        var skills = doc.RootElement.GetProperty("skills").EnumerateObject().Select(p => (id: p.Name, type: p.Value.TryGetProperty("type", out var t) ? t.GetString() ?? "physical" : "physical")).ToList();
        var hashes = new Dictionary<string, string>(); int dup = 0, missing = 0; var imgs = new List<Image>();
        foreach (var (id, type) in skills)
        {
            var img = Render(id, type);
            if (!Has(id)) missing++;
            img.SavePng(dir + id + ".png"); imgs.Add(img);
            var h = Convert.ToHexString(System.Security.Cryptography.MD5.HashData(img.GetData()));
            if (hashes.TryGetValue(h, out var other)) { dup++; GD.PrintErr($"[ICONS] {id} is the same picture as {other}"); } else hashes[h] = id;
        }
        // contact sheet for review
        int cols = 10, rows = (imgs.Count + cols - 1) / cols; var sheet = Image.CreateEmpty(cols * Size, rows * Size, false, Image.Format.Rgba8); sheet.Fill(new Color(.12f, .12f, .14f));
        for (int i = 0; i < imgs.Count; i++) sheet.BlendRect(imgs[i], new Rect2I(0, 0, Size, Size), new Vector2I(i % cols * Size, i / cols * Size));
        sheet.SavePng(ProjectSettings.GlobalizePath("res://.devloop/icon_sheet.png"));
        GD.Print($"[ICONS] generated {skills.Count} skill icons in {Dir}; duplicates {dup}; without their own drawing {missing}");
    }
    static bool Has(string id) { var g = new Ctx { G = new IconPainter(4), E = PalOf("physical"), Id = id }; return Symbol(g, id); }

    // ------------------------------------------------------------------------------------------------------------- player icons
    /// <summary>Editor symbols (index = the number saved in an "ed:" icon).</summary>
    public static readonly (string name, Func<Sdf> f)[] EdSymbols =
    {
        ("Flame", () => Flame(1.1f)), ("Snowflake", () => Snowflake(1.05f)), ("Bolt", () => Bolt(1.1f)), ("Drop", () => Drop(1.1f)), ("Skull", () => Skull(1.1f)), ("Moon", () => Moon(1.2f)),
        ("Eye", () => Eye(1.1f)), ("Sun", () => Sun(1.15f)), ("Cross", () => Cross(1f)), ("Plus", () => Plus(1f)), ("Leaf", () => Leaf(1.1f)), ("Arrow up", () => ArrowUp(1f)),
        ("Sword", () => Sword(1f, D45)), ("Axe", () => Axe(.95f, .4f)), ("Hammer", () => Hammer(.95f, .4f)), ("Dagger", () => Dagger(1.2f, D45)), ("Spear", () => Spear(1f, D45)), ("Shield", () => ShieldShape(1.1f)),
        ("Bow", () => Bow(1f)), ("Arrow", () => Arrow(1f, D45)), ("Star", () => Star5(1.2f)), ("Spark", () => Spark4(1.3f)), ("Heart", () => Heart(1.1f)), ("Hourglass", () => Hourglass(1f)),
        ("Clock", () => Clock(1.1f)), ("Ankh", () => Ankh(1f)), ("Note", () => Note(1.1f)), ("Feather", () => Feather(1f)), ("Spiral", () => Spiral(1.15f)), ("Orb", () => Orb(1.3f)),
        ("Gem", () => Gem(1.2f)), ("Fist", () => Fist(1f)), ("Ghost", () => Ghost(1.1f)), ("Wing", () => Wing(1.1f)), ("Cloud", () => Cloud(1.1f)), ("Rock", () => Rock(1.1f)),
    };
    public static readonly string[] EdShapes = { "Rounded", "Circle", "Diamond", "Shield", "Hexagon" };
    public static readonly string[] EdBorders = { "None", "Thin", "Gold", "Double" };
    static Sdf EdShape(int i) => i switch
    {
        1 => S.Circle(Vector2.Zero, .92f), 2 => S.Grow(S.P(0, -.9f, .9f, 0, 0, .9f, -.9f, 0), .04f), 3 => S.P(-.85f, -.85f, .85f, -.85f, .8f, .15f, 0, .92f, -.8f, .15f),
        4 => S.Ngon(Vector2.Zero, .93f, 6, MathF.PI / 6), _ => Badge,
    };

    /// <summary>Renders an editor icon: background shape, symbol, background and symbol colours, border style.</summary>
    public static Image RenderEditor(int shape, int symbol, Color bg, Color sym, int border, int n = 64)
    {
        var g = new IconPainter(n); var sh = EdShape(Math.Clamp(shape, 0, EdShapes.Length - 1));
        g.Rad(sh, bg.Lerp(Colors.White, .25f), bg.Darkened(.75f), new Vector2(0, -.2f), 1.25f);
        g.Clip = sh; g.Stroke(S.Circle(Vector2.Zero, .68f), .008f, Colors.White with { A = .18f });
        var s = EdSymbols[Math.Clamp(symbol, 0, EdSymbols.Length - 1)].f();
        g.Glow(s, sym with { A = .5f }, .07f); g.Fill(S.Grow(s, .03f), bg.Darkened(.85f) with { A = .85f }); g.Lin(s, sym.Lerp(Colors.White, .6f), sym, new Vector2(-.3f, -.6f), new Vector2(.3f, .6f));
        g.Fill(S.Ellipse(new Vector2(-.1f, -.6f), new Vector2(.7f, .25f)), new Color(1, 1, 1, .07f));
        g.Clip = null;
        var rim = sym.Lerp(bg, .3f); var gold = new Color(.95f, .78f, .38f);
        switch (border)
        {
            case 1: g.Stroke(sh, .025f, rim); break;
            case 2: g.Lin(p => MathF.Abs(sh(p) + .03f) - .035f, gold.Lerp(Colors.White, .4f), gold.Darkened(.5f), new Vector2(0, -.9f), new Vector2(0, .9f)); break;
            case 3: g.Stroke(sh, .02f, rim); g.Stroke(S.Grow(sh, -.08f), .012f, rim.Lerp(Colors.White, .3f)); break;
        }
        return g.ToImage();
    }
}
