using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Vector icons drawn in code with CanvasItem primitives: spells, potions, weapon and gear types, materials.
/// Icons.Draw(canvas, id, centre, size). Unknown ids draw a neutral diamond.
/// </summary>
public static class Icons
{
    static Vector2[] P(Vector2 c, float s, params float[] xy) { var a = new Vector2[xy.Length / 2]; for (int i = 0; i < a.Length; i++) a[i] = c + new Vector2(xy[i * 2], xy[i * 2 + 1]) * s; return a; }
    static void Poly(CanvasItem ci, Vector2[] pts, Color fill, Color? line = null, float w = 1.5f)
    {
        ci.DrawColoredPolygon(pts, fill);
        if (line != null) { var l = new Vector2[pts.Length + 1]; pts.CopyTo(l, 0); l[^1] = pts[0]; ci.DrawPolyline(l, line.Value, w, true); }
    }
    static void Glow(CanvasItem ci, Vector2 c, float r, Color col) { for (int i = 4; i >= 1; i--) ci.DrawCircle(c, r * (0.55f + i * 0.14f), col with { A = col.A * 0.12f }); }

    public static void Draw(CanvasItem ci, string id, Vector2 c, float size, float alpha = 1f)
    {
        float s = size / 2f; var ol = new Color(0, 0, 0, .7f * alpha);
        Color A(Color k) => k with { A = k.A * alpha };
        switch (id)
        {
            // ---------------- spells
            case "fire":
                Glow(ci, c + new Vector2(0, s * .15f), s * .8f, A(new Color(1f, .45f, .1f, 1)));
                Poly(ci, P(c, s, 0, -.95f, .3f, -.45f, .55f, -.1f, .6f, .35f, .35f, .75f, 0, .85f, -.35f, .75f, -.6f, .35f, -.55f, -.05f, -.3f, -.3f, -.15f, -.6f), A(new Color(1f, .42f, .08f)), ol);
                Poly(ci, P(c, s, 0, -.45f, .25f, -.05f, .3f, .35f, 0, .62f, -.3f, .35f, -.22f, 0), A(new Color(1f, .78f, .2f)));
                Poly(ci, P(c, s, 0, .05f, .14f, .3f, 0, .5f, -.14f, .3f), A(new Color(1f, .97f, .75f)));
                break;
            case "nova":
                Glow(ci, c, s * .85f, A(new Color(.4f, .85f, 1f)));
                for (int k = 0; k < 6; k++)
                {
                    float a = k * Mathf.Pi / 3f; var d = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var n = new Vector2(-d.Y, d.X);
                    ci.DrawLine(c, c + d * s * .85f, A(new Color(.85f, .97f, 1f)), Mathf.Max(2f, s * .13f), true);
                    foreach (float t in new[] { .45f, .68f }) { var b = c + d * s * t; ci.DrawLine(b, b + (d + n) * s * .2f, A(new Color(.7f, .92f, 1f)), Mathf.Max(1.5f, s * .08f), true); ci.DrawLine(b, b + (d - n) * s * .2f, A(new Color(.7f, .92f, 1f)), Mathf.Max(1.5f, s * .08f), true); }
                }
                ci.DrawCircle(c, s * .14f, A(Colors.White));
                break;
            case "heal":
                Glow(ci, c, s * .85f, A(new Color(.55f, 1f, .5f)));
                ci.DrawArc(c, s * .72f, 0, Mathf.Tau, 32, A(new Color(1f, .9f, .5f)), Mathf.Max(1.5f, s * .08f), true);
                Poly(ci, P(c, s, -.18f, -.6f, .18f, -.6f, .18f, -.18f, .6f, -.18f, .6f, .18f, .18f, .18f, .18f, .6f, -.18f, .6f, -.18f, .18f, -.6f, .18f, -.6f, -.18f, -.18f, -.18f), A(new Color(.5f, 1f, .45f)), A(new Color(.1f, .35f, .1f)));
                Poly(ci, P(c, s, -.08f, -.5f, .08f, -.5f, .08f, .5f, -.08f, .5f), A(new Color(.9f, 1f, .85f, .6f)));
                break;
            case "zap":
                Glow(ci, c, s * .8f, A(new Color(.65f, .55f, 1f)));
                Poly(ci, P(c, s, .2f, -.95f, -.45f, .08f, -.05f, .08f, -.25f, .95f, .5f, -.15f, .08f, -.15f, .35f, -.95f), A(new Color(1f, .95f, .45f)), A(new Color(.35f, .2f, .7f)), 2f);
                Poly(ci, P(c, s, .14f, -.7f, -.25f, -.02f, .02f, -.02f), A(Colors.White with { A = .8f }));
                break;
            // ---------------- potions
            case "hp": case "mp":
            {
                var liq = id == "hp" ? new Color(.92f, .15f, .2f) : new Color(.2f, .5f, 1f); var glass = new Color(.8f, .9f, 1f, .35f);
                Glow(ci, c + new Vector2(0, s * .25f), s * .6f, A(liq));
                ci.DrawCircle(c + new Vector2(0, s * .25f), s * .55f, A(glass)); ci.DrawCircle(c + new Vector2(0, s * .28f), s * .48f, A(liq));
                ci.DrawRect(new Rect2(c + new Vector2(-s * .16f, -s * .55f), new Vector2(s * .32f, s * .4f)), A(glass));
                ci.DrawRect(new Rect2(c + new Vector2(-s * .2f, -s * .8f), new Vector2(s * .4f, s * .26f)), A(new Color(.6f, .42f, .25f)));
                ci.DrawCircle(c + new Vector2(-s * .2f, s * .12f), s * .12f, A(new Color(1, 1, 1, .7f)));
                ci.DrawArc(c + new Vector2(0, s * .25f), s * .55f, 0, Mathf.Tau, 28, ol, 1.5f, true);
                break;
            }
            // ---------------- weapons and gear
            case "sword":
                Poly(ci, P(c, s, .62f, -.78f, .78f, -.62f, -.2f, .36f, -.36f, .2f), A(new Color(.85f, .9f, .98f)), ol);
                ci.DrawLine(c + new Vector2(.6f, -.6f) * s, c + new Vector2(-.24f, .24f) * s, A(new Color(1, 1, 1, .6f)), 1f, true);
                ci.DrawLine(c + new Vector2(-.5f, .1f) * s, c + new Vector2(-.1f, .5f) * s, A(new Color(.85f, .65f, .25f)), Mathf.Max(2f, s * .14f), true);
                ci.DrawLine(c + new Vector2(-.32f, .32f) * s, c + new Vector2(-.62f, .62f) * s, A(new Color(.45f, .28f, .15f)), Mathf.Max(2f, s * .12f), true);
                ci.DrawCircle(c + new Vector2(-.68f, .68f) * s, s * .09f, A(new Color(.85f, .65f, .25f)));
                break;
            case "greatsword":
                Poly(ci, P(c, s, .72f, -.88f, .88f, -.72f, -.15f, .42f, -.42f, .15f), A(new Color(.85f, .9f, .98f)), ol);
                ci.DrawLine(c + new Vector2(-.55f, .05f) * s, c + new Vector2(-.05f, .55f) * s, A(new Color(.85f, .65f, .25f)), Mathf.Max(2.5f, s * .16f), true);
                ci.DrawLine(c + new Vector2(-.3f, .3f) * s, c + new Vector2(-.75f, .75f) * s, A(new Color(.45f, .28f, .15f)), Mathf.Max(2.5f, s * .14f), true);
                break;
            case "axe":
                ci.DrawLine(c + new Vector2(-.6f, .7f) * s, c + new Vector2(.35f, -.6f) * s, A(new Color(.5f, .33f, .18f)), Mathf.Max(2.5f, s * .13f), true);
                Poly(ci, P(c, s, .1f, -.55f, .55f, -.85f, .8f, -.35f, .55f, .05f, .3f, -.2f), A(new Color(.8f, .84f, .9f)), ol);
                break;
            case "mace":
                ci.DrawLine(c + new Vector2(-.6f, .7f) * s, c + new Vector2(.25f, -.3f) * s, A(new Color(.35f, .35f, .38f)), Mathf.Max(2.5f, s * .12f), true);
                ci.DrawCircle(c + new Vector2(.35f, -.42f) * s, s * .3f, A(new Color(.75f, .78f, .84f)));
                for (int k = 0; k < 6; k++) { float a = k * Mathf.Tau / 6; var o = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); Poly(ci, new[] { c + new Vector2(.35f, -.42f) * s + o * s * .26f + new Vector2(-o.Y, o.X) * s * .08f, c + new Vector2(.35f, -.42f) * s + o * s * .45f, c + new Vector2(.35f, -.42f) * s + o * s * .26f - new Vector2(-o.Y, o.X) * s * .08f }, A(new Color(.75f, .78f, .84f))); }
                break;
            case "dagger":
                Poly(ci, P(c, s, .5f, -.6f, .62f, -.5f, .0f, .15f, -.12f, .03f), A(new Color(.88f, .92f, .98f)), ol);
                ci.DrawLine(c + new Vector2(-.25f, -.1f) * s, c + new Vector2(.1f, .25f) * s, A(new Color(.85f, .65f, .25f)), Mathf.Max(2f, s * .1f), true);
                ci.DrawLine(c + new Vector2(-.06f, .1f) * s, c + new Vector2(-.42f, .46f) * s, A(new Color(.3f, .2f, .12f)), Mathf.Max(2f, s * .12f), true);
                break;
            case "spear":
                ci.DrawLine(c + new Vector2(-.8f, .8f) * s, c + new Vector2(.4f, -.4f) * s, A(new Color(.55f, .38f, .2f)), Mathf.Max(2f, s * .09f), true);
                Poly(ci, P(c, s, .85f, -.85f, .55f, -.25f, .4f, -.4f, .25f, -.55f), A(new Color(.85f, .9f, .98f)), ol);
                break;
            case "head":
                Poly(ci, P(c, s, -.62f, .3f, -.6f, -.25f, -.35f, -.65f, 0, -.75f, .35f, -.65f, .6f, -.25f, .62f, .3f, .2f, .3f, .2f, .65f, -.2f, .65f, -.2f, .3f), A(new Color(.7f, .73f, .78f)), ol, 2f);
                ci.DrawLine(c + new Vector2(-.45f, -.05f) * s, c + new Vector2(.45f, -.05f) * s, A(new Color(.1f, .1f, .12f)), Mathf.Max(2f, s * .1f));
                break;
            case "hands":
                Poly(ci, P(c, s, -.4f, .8f, -.45f, -.1f, -.6f, -.45f, -.4f, -.55f, -.25f, -.25f, -.2f, -.8f, 0, -.85f, .05f, -.4f, .12f, -.85f, .3f, -.8f, .28f, -.35f, .4f, -.7f, .55f, -.6f, .5f, .1f, .45f, .8f), A(new Color(.62f, .44f, .26f)), ol, 1.5f);
                ci.DrawLine(c + new Vector2(-.42f, .45f) * s, c + new Vector2(.47f, .45f) * s, A(new Color(.85f, .7f, .35f)), Mathf.Max(2f, s * .1f));
                break;
            case "feet":
                Poly(ci, P(c, s, -.35f, -.85f, .2f, -.85f, .2f, .3f, .8f, .45f, .85f, .8f, -.4f, .8f), A(new Color(.5f, .34f, .2f)), ol, 1.5f);
                ci.DrawLine(c + new Vector2(-.35f, -.55f) * s, c + new Vector2(.2f, -.55f) * s, A(new Color(.85f, .7f, .35f)), Mathf.Max(2f, s * .1f));
                break;
            case "bow":
                ci.DrawArc(c + new Vector2(-.35f, .35f) * s, s * 1.05f, -Mathf.Pi * .5f + .15f, -.15f, 20, A(new Color(.6f, .4f, .2f)), Mathf.Max(2.5f, s * .14f), true);
                ci.DrawLine(c + new Vector2(-.33f, -.68f) * s, c + new Vector2(.68f, .33f) * s, A(new Color(.95f, .95f, .9f)), 1.2f, true);
                ci.DrawLine(c + new Vector2(-.6f, .6f) * s, c + new Vector2(.55f, -.55f) * s, A(new Color(.75f, .6f, .4f)), Mathf.Max(1.5f, s * .06f), true);
                Poly(ci, P(c, s, .62f, -.62f, .4f, -.52f, .52f, -.4f), A(new Color(.85f, .9f, .95f)));
                break;
            case "wand":
                ci.DrawLine(c + new Vector2(-.6f, .6f) * s, c + new Vector2(.3f, -.3f) * s, A(new Color(.5f, .32f, .2f)), Mathf.Max(2f, s * .12f), true);
                Glow(ci, c + new Vector2(.42f, -.42f) * s, s * .35f, A(new Color(.7f, .5f, 1f)));
                Poly(ci, P(c + new Vector2(.42f, -.42f) * s, s * .3f, 0, -1, .3f, -.3f, 1, 0, .3f, .3f, 0, 1, -.3f, .3f, -1, 0, -.3f, -.3f), A(new Color(1f, .9f, 1f)));
                break;
            case "staff":
                ci.DrawLine(c + new Vector2(-.5f, .9f) * s, c + new Vector2(.25f, -.5f) * s, A(new Color(.45f, .3f, .18f)), Mathf.Max(2.5f, s * .13f), true);
                Glow(ci, c + new Vector2(.35f, -.65f) * s, s * .4f, A(new Color(.35f, .8f, 1f)));
                ci.DrawCircle(c + new Vector2(.35f, -.65f) * s, s * .22f, A(new Color(.55f, .9f, 1f)));
                ci.DrawCircle(c + new Vector2(.3f, -.7f) * s, s * .08f, A(Colors.White));
                break;
            case "shield":
                Poly(ci, P(c, s, -.7f, -.75f, .7f, -.75f, .65f, .1f, 0, .9f, -.65f, .1f), A(new Color(.55f, .38f, .22f)), ol, 2f);
                Poly(ci, P(c, s, -.52f, -.6f, .52f, -.6f, .48f, .05f, 0, .68f, -.48f, .05f), A(new Color(.25f, .45f, .75f)));
                ci.DrawLine(c + new Vector2(0, -.6f) * s, c + new Vector2(0, .68f) * s, A(new Color(.85f, .75f, .4f)), Mathf.Max(2f, s * .1f));
                ci.DrawLine(c + new Vector2(-.5f, -.2f) * s, c + new Vector2(.5f, -.2f) * s, A(new Color(.85f, .75f, .4f)), Mathf.Max(2f, s * .1f));
                break;
            case "armor":
                Poly(ci, P(c, s, -.35f, -.8f, -.12f, -.62f, .12f, -.62f, .35f, -.8f, .82f, -.5f, .62f, -.1f, .5f, -.2f, .5f, .8f, -.5f, .8f, -.5f, -.2f, -.62f, -.1f, -.82f, -.5f), A(new Color(.62f, .66f, .72f)), ol, 2f);
                ci.DrawLine(c + new Vector2(0, -.55f) * s, c + new Vector2(0, .75f) * s, A(new Color(.4f, .43f, .5f)), 1.5f);
                ci.DrawLine(c + new Vector2(-.45f, .3f) * s, c + new Vector2(.45f, .3f) * s, A(new Color(.45f, .3f, .18f)), Mathf.Max(2f, s * .12f));
                break;
            case "trinket":
                ci.DrawArc(c + new Vector2(0, -.25f) * s, s * .5f, Mathf.Pi * .1f, Mathf.Pi * .9f, 14, A(new Color(.9f, .75f, .35f)), Mathf.Max(1.5f, s * .07f), true);
                Glow(ci, c + new Vector2(0, .35f) * s, s * .38f, A(new Color(.4f, 1f, .7f)));
                Poly(ci, P(c + new Vector2(0, .35f) * s, s * .38f, 0, -1, .8f, 0, 0, 1, -.8f, 0), A(new Color(.35f, .95f, .7f)), A(new Color(.9f, .75f, .35f)), 2f);
                break;
            // ---------------- materials
            case "herb": case "Sunleaf": case "Moonpetal":
            {
                var leaf = id == "Moonpetal" ? new Color(.7f, .5f, 1f) : new Color(.45f, .8f, .3f);
                ci.DrawLine(c + new Vector2(0, .85f) * s, c + new Vector2(0, -.4f) * s, A(new Color(.3f, .55f, .2f)), 2f, true);
                foreach (float sx in new[] { -1f, 1f }) Poly(ci, P(c, s, 0, .1f, sx * .4f, -.25f, sx * .7f, -.1f, sx * .45f, .2f), A(leaf), ol, 1f);
                Poly(ci, P(c, s, 0, -.85f, .25f, -.45f, 0, -.2f, -.25f, -.45f), A(leaf.Lightened(.2f)), ol, 1f);
                break;
            }
            case "Redcap Mushroom": case "mushroom":
                ci.DrawRect(new Rect2(c + new Vector2(-.15f, -.1f) * s, new Vector2(.3f, .9f) * s), A(new Color(.95f, .92f, .85f)));
                Poly(ci, P(c, s, -.8f, .05f, -.6f, -.45f, 0, -.75f, .6f, -.45f, .8f, .05f), A(new Color(.85f, .2f, .15f)), ol);
                foreach (var dpt in new[] { new Vector2(-.35f, -.25f), new Vector2(.2f, -.45f), new Vector2(.45f, -.15f) }) ci.DrawCircle(c + dpt * s, s * .09f, A(Colors.White));
                break;
            case "Wildberry": case "berry":
                foreach (var dpt in new[] { new Vector2(-.3f, .2f), new Vector2(.3f, .25f), new Vector2(0, -.2f) }) { ci.DrawCircle(c + dpt * s, s * .33f, A(new Color(.75f, .1f, .3f))); ci.DrawCircle(c + (dpt + new Vector2(-.1f, -.1f)) * s, s * .09f, A(new Color(1, .8f, .9f))); }
                Poly(ci, P(c, s, 0, -.55f, .35f, -.85f, .1f, -.5f), A(new Color(.35f, .65f, .25f)));
                break;
            case "Iron Ore": case "ore":
                Poly(ci, P(c, s, -.75f, .4f, -.5f, -.35f, .05f, -.7f, .7f, -.3f, .78f, .45f, .1f, .75f), A(new Color(.45f, .43f, .42f)), ol);
                foreach (var dpt in new[] { new Vector2(-.25f, -.1f), new Vector2(.3f, .1f), new Vector2(0, .4f) }) ci.DrawCircle(c + dpt * s, s * .12f, A(new Color(.85f, .5f, .25f)));
                break;
            case "Rift Crystal": case "crystal":
                Glow(ci, c, s * .8f, A(new Color(.4f, .85f, 1f)));
                Poly(ci, P(c, s, 0, -.95f, .35f, -.3f, .2f, .8f, -.2f, .8f, -.35f, -.3f), A(new Color(.45f, .9f, 1f)), A(new Color(.1f, .35f, .55f)));
                Poly(ci, P(c, s, 0, -.95f, .35f, -.3f, 0, -.1f), A(new Color(.85f, 1f, 1f, .8f)));
                break;
            case "Oak Log": case "Log": case "log":
                Poly(ci, P(c, s, -.85f, -.2f, .55f, -.55f, .75f, .15f, -.65f, .5f), A(new Color(.5f, .33f, .18f)), ol);
                ci.DrawCircle(c + new Vector2(.65f, -.2f) * s, s * .36f, A(new Color(.82f, .66f, .42f))); ci.DrawArc(c + new Vector2(.65f, -.2f) * s, s * .2f, 0, Mathf.Tau, 16, A(new Color(.6f, .45f, .25f)), 1.2f);
                break;
            case "Hide": case "hide":
                Poly(ci, P(c, s, -.6f, -.7f, -.2f, -.5f, .2f, -.5f, .6f, -.7f, .7f, -.1f, .5f, .3f, .55f, .8f, 0, .6f, -.55f, .8f, -.5f, .3f, -.7f, -.1f), A(new Color(.65f, .48f, .3f)), ol);
                break;
            case "Raw Meat": case "meat":
                ci.DrawCircle(c + new Vector2(-.1f, .05f) * s, s * .6f, A(new Color(.82f, .3f, .3f))); ci.DrawCircle(c + new Vector2(-.15f, 0) * s, s * .35f, A(new Color(.95f, .55f, .55f)));
                ci.DrawLine(c + new Vector2(.35f, -.35f) * s, c + new Vector2(.75f, -.75f) * s, A(new Color(.95f, .92f, .85f)), Mathf.Max(2.5f, s * .16f), true);
                break;
            default:
                Poly(ci, P(c, s, 0, -.7f, .6f, 0, 0, .7f, -.6f, 0), A(UiTheme.Accent), ol);
                break;
        }
    }

    /// <summary>Gear slot/weapon type to icon id.</summary>
    public static string ForItem(string slot, string wt) => slot switch
    {
        "weapon" => wt is "bow" or "wand" or "staff" or "greatsword" or "axe" or "mace" or "dagger" or "spear" ? wt : "sword",
        "shield" => "shield", "armor" => "armor", "trinket" => "trinket", _ => slot,
    };

    /// <summary>Rarity frame: a coloured border with a soft inner glow, brighter for rarer items.</summary>
    public static void Frame(CanvasItem ci, Rect2 r, Color rarity, int tier)
    {
        ci.DrawRect(r, new Color(0.02f, 0.05f, 0.1f, .75f));
        if (tier > 0) for (int i = 0; i < 3; i++) ci.DrawRect(r.Grow(-1 - i * 2), rarity with { A = .08f + .05f * tier }, false, 2f);
        ci.DrawRect(r, rarity with { A = .55f + .1f * tier }, false, tier >= 3 ? 2f : 1f);
    }
}
