using Godot;

namespace TwinRealms;

/// <summary>Menu colours and sizes for the sci-fi "system window" look. Loaded from data/ui_theme.json.</summary>
public static class UiTheme
{
    public static Color Panel = new("071a33dd"), PanelHeader = new("0c2b52ee"), Button = new("0b2a4ccc"), ButtonHover = new("1a5a8ccc"), ButtonPressed = new("5fe1ff");
    public static Color Accent = new("45d6ff"), AccentSoft = new("45d6ff66"), Glow = new("2fb8ff55"), Title = new("bff4ff"), Text = new("e6f5ff"), Dim = new("7fa6c8");
    public static Color Highlight = new("ffd36a"), Backdrop = new("020812a0"), Grid = new("45d6ff10");
    public static int Corner = 3, GlowSize = 14; public static float OpenTime = 0.16f;

    static UiTheme()
    {
        using var doc = JsonUtil.Load("res://data/ui_theme.json"); var r = doc.RootElement;
        Color C(string k, Color d) { var s = JsonUtil.S(r, k); return s == "" ? d : Color.FromHtml(s); }
        Panel = C("panel", Panel); PanelHeader = C("panelHeader", PanelHeader); Button = C("button", Button); ButtonHover = C("buttonHover", ButtonHover); ButtonPressed = C("buttonPressed", ButtonPressed);
        Accent = C("accent", Accent); AccentSoft = C("accentSoft", AccentSoft); Glow = C("glow", Glow); Title = C("title", Title); Text = C("text", Text); Dim = C("dim", Dim);
        Highlight = C("highlight", Highlight); Backdrop = C("backdrop", Backdrop); Grid = C("grid", Grid);
        Corner = (int)JsonUtil.F(r, "corner", Corner); GlowSize = (int)JsonUtil.F(r, "glowSize", GlowSize); OpenTime = JsonUtil.F(r, "openTime", OpenTime);
    }

    public static StyleBoxFlat Box(Color bg, Color border, int borderW = 1, int padX = 14, int padY = 6, int glow = 0)
    {
        var s = new StyleBoxFlat { BgColor = bg, BorderColor = border, ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY };
        s.SetBorderWidthAll(borderW); s.SetCornerRadiusAll(Corner);
        if (glow > 0) { s.ShadowColor = Glow; s.ShadowSize = glow; }
        return s;
    }
}

/// <summary>Faint tech grid over the dimmed game behind an open menu.</summary>
public partial class TechBackdrop : Control
{
    public override void _Ready() { MouseFilter = MouseFilterEnum.Ignore; }
    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), UiTheme.Backdrop);
        for (float x = 0; x < Size.X; x += 48) DrawLine(new Vector2(x, 0), new Vector2(x, Size.Y), UiTheme.Grid, 1f);
        for (float y = 0; y < Size.Y; y += 48) DrawLine(new Vector2(0, y), new Vector2(Size.X, y), UiTheme.Grid, 1f);
    }
}

/// <summary>Menu panel that draws its own decoration on top of the glass box (and under its children):
/// bright corner brackets, a header band and rule with a fading tail, a diamond marker and tick marks.</summary>
public partial class TechPanel : PanelContainer
{
    public float HeaderY = 52f;
    public override void _Draw()
    {
        var a = UiTheme.Accent; var s = Size; float L = Mathf.Min(26f, s.X * .1f), w = 2.5f;
        // corner brackets
        void Corner(Vector2 p, Vector2 dx, Vector2 dy) { DrawLine(p, p + dx * L, a, w); DrawLine(p, p + dy * L, a, w); }
        Corner(new Vector2(0, 0), Vector2.Right, Vector2.Down); Corner(new Vector2(s.X, 0), Vector2.Left, Vector2.Down);
        Corner(new Vector2(0, s.Y), Vector2.Right, Vector2.Up); Corner(new Vector2(s.X, s.Y), Vector2.Left, Vector2.Up);
        // header band and rule: solid on the left, fading out to the right
        DrawRect(new Rect2(1, 1, s.X - 2, HeaderY - 1), UiTheme.PanelHeader with { A = UiTheme.PanelHeader.A * 0.55f });
        int seg = 24; for (int i = 0; i < seg; i++) { float t0 = i / (float)seg, t1 = (i + 1) / (float)seg; DrawLine(new Vector2(18 + (s.X - 36) * t0, HeaderY), new Vector2(18 + (s.X - 36) * t1, HeaderY), a with { A = 0.9f * (1 - t0) + 0.08f }, 1.5f); }
        // diamond marker left of the title
        var c = new Vector2(10, HeaderY / 2 + 2); DrawColoredPolygon(new[] { c + new Vector2(0, -5), c + new Vector2(4, 0), c + new Vector2(0, 5), c + new Vector2(-4, 0) }, a);
        // tick marks along the bottom edge
        for (int i = 0; i < 6; i++) DrawLine(new Vector2(s.X - 30 - i * 7, s.Y - 7), new Vector2(s.X - 30 - i * 7, s.Y - 3), a with { A = 0.6f }, 1f);
    }
}
