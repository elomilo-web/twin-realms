using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>Compact controller button badges drawn with primitives (no font glyphs): face buttons, D-pad cross, bumpers / triggers, and "LT+Up" combos.
/// Takes the names Inputs.PadName produces.</summary>
public static class PadGlyphs
{
    static Font F => ThemeDB.FallbackFont;
    static readonly Color Dark = new(.07f, .065f, .06f, .95f), Edge = new(.78f, .72f, .58f, .9f), Lit = new(1f, .82f, .3f);

    static bool IsFace(string n) => n is "A" or "B" or "X" or "Y" or "Cross" or "Circle" or "Square" or "Triangle";
    static bool IsDir(string n) => n is "Up" or "Down" or "Left" or "Right" or "D-pad Up" or "D-pad Down" or "D-pad Left" or "D-pad Right";

    static List<string> Parts(string name)
    {
        var l = new List<string>(); int i;
        while ((i = name.IndexOf('+')) > 0 && name.Length > 1) { l.Add(name[..i]); name = name[(i + 1)..]; }
        l.Add(name); return l;
    }
    static float PartW(string n, float h) => IsFace(n) || IsDir(n) ? h : Mathf.Max(h * 1.25f, F.GetStringSize(n, HorizontalAlignment.Left, -1, Mathf.Max(8, (int)(h * .62f))).X + h * .5f);

    /// <summary>Width the glyph needs at height h.</summary>
    public static float Width(string padName, float h)
    {
        float w = 0; var p = Parts(padName);
        for (int i = 0; i < p.Count; i++) w += PartW(p[i], h) + (i > 0 ? h * .12f : 0);
        return w;
    }

    /// <summary>Draws the glyph for a pad button name ("A", "LT+Up", "Cross", "R1"...) left-aligned in r, scaled to r's height.</summary>
    public static void Draw(CanvasItem ci, Rect2 r, string padName, string style)
    {
        float h = r.Size.Y, x = r.Position.X; bool ps = style == "playstation";
        var p = Parts(padName);
        for (int i = 0; i < p.Count; i++)
        {
            if (i > 0) x += h * .12f;
            float w = PartW(p[i], h); var pr = new Rect2(x, r.Position.Y, w, h);
            if (IsFace(p[i])) Face(ci, pr, p[i], ps);
            else if (IsDir(p[i])) Dpad(ci, pr, p[i]);
            else Cap(ci, pr, p[i]);
            x += w;
        }
    }

    static void Cap(CanvasItem ci, Rect2 r, string t)
    {
        var sb = new StyleBoxFlat { BgColor = Dark, BorderColor = Edge, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1 };
        int rad = (int)(r.Size.Y * .3f); sb.CornerRadiusTopLeft = sb.CornerRadiusTopRight = sb.CornerRadiusBottomLeft = sb.CornerRadiusBottomRight = rad;
        ci.DrawStyleBox(sb, r);
        int fs = Mathf.Max(8, (int)(r.Size.Y * .62f));
        ci.DrawString(F, new Vector2(r.Position.X, r.Position.Y + r.Size.Y * .5f + fs * .36f), t, HorizontalAlignment.Center, r.Size.X, fs, Colors.White);
    }

    static void Face(CanvasItem ci, Rect2 r, string n, bool ps)
    {
        var c = r.GetCenter(); float rad = Mathf.Min(r.Size.X, r.Size.Y) * .5f - .5f;
        string k = n switch { "A" or "Cross" => "A", "B" or "Circle" => "B", "X" or "Square" => "X", _ => "Y" };
        Color col = ps ? k switch { "A" => new Color("7aa7ff"), "B" => new Color("ff6a6a"), "X" => new Color("f08ad0"), _ => new Color("5be3a0") }
                       : k switch { "A" => new Color("5bd36b"), "B" => new Color("ee5a4f"), "X" => new Color("4d8dff"), _ => new Color("f2c230") };
        ci.DrawCircle(c, rad, Dark); ci.DrawArc(c, rad, 0, Mathf.Tau, 24, ps ? Edge : col, 1.5f, true);
        if (!ps) { int fs = Mathf.Max(8, (int)(rad * 1.25f)); ci.DrawString(F, new Vector2(r.Position.X, c.Y + fs * .36f), k, HorizontalAlignment.Center, r.Size.X, fs, col); return; }
        float s = rad * .5f, lw = Mathf.Max(1.4f, rad * .2f);
        switch (k)
        {
            case "A": ci.DrawLine(c + new Vector2(-s, -s), c + new Vector2(s, s), col, lw, true); ci.DrawLine(c + new Vector2(-s, s), c + new Vector2(s, -s), col, lw, true); break;
            case "B": ci.DrawArc(c, s * 1.1f, 0, Mathf.Tau, 20, col, lw, true); break;
            case "X": ci.DrawRect(new Rect2(c - new Vector2(s, s), new Vector2(s * 2, s * 2)), col, false, lw); break;
            default: ci.DrawPolyline(new[] { c + new Vector2(0, -s * 1.15f), c + new Vector2(s * 1.1f, s * .85f), c + new Vector2(-s * 1.1f, s * .85f), c + new Vector2(0, -s * 1.15f) }, col, lw, true); break;
        }
    }

    static void Dpad(CanvasItem ci, Rect2 r, string n)
    {
        var c = r.GetCenter(); float s = Mathf.Min(r.Size.X, r.Size.Y), a = s * .3f, l = s * .5f - .5f;
        string d = n.Replace("D-pad ", "");
        Color Arm(string which) => d == which ? Lit : new Color(.55f, .52f, .48f, .95f);
        ci.DrawRect(new Rect2(c.X - a / 2, c.Y - l, a, l * 2), new Color(.55f, .52f, .48f, .95f)); ci.DrawRect(new Rect2(c.X - l, c.Y - a / 2, l * 2, a), new Color(.55f, .52f, .48f, .95f));
        ci.DrawRect(new Rect2(c.X - a / 2, c.Y - l, a, l - a / 2), Arm("Up")); ci.DrawRect(new Rect2(c.X - a / 2, c.Y + a / 2, a, l - a / 2), Arm("Down"));
        ci.DrawRect(new Rect2(c.X - l, c.Y - a / 2, l - a / 2, a), Arm("Left")); ci.DrawRect(new Rect2(c.X + a / 2, c.Y - a / 2, l - a / 2, a), Arm("Right"));
        ci.DrawRect(new Rect2(c.X - a / 2, c.Y - l, a, l * 2), Dark, false, 1f); ci.DrawRect(new Rect2(c.X - l, c.Y - a / 2, l * 2, a), Dark, false, 1f);
    }
}
