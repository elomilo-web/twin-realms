using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>A floating card next to the mouse: a title and a few coloured lines. Used by the skills, modifier tree and skill tree menus.</summary>
public partial class InfoTip : PanelContainer
{
    VBoxContainer _v = null!;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; ZIndex = 60; TopLevel = true;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel with { A = .97f }, UiTheme.Accent, 1, 12, 9, 10));
        _v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _v.AddThemeConstantOverride("separation", 2); AddChild(_v);
    }
    public void Show(Vector2 at, string title, Color tc, IEnumerable<(string text, Color c, int size)> lines, float width = 300)
    {
        foreach (var c in _v.GetChildren()) { _v.RemoveChild(c); c.QueueFree(); }
        Label L(string t, int size, Color c) { var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore, AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(width, 0) }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c); _v.AddChild(l); return l; }
        L(title, 17, tc);
        foreach (var (t, c, s) in lines) if (t != "") L(t, s, c);
        Size = Vector2.Zero; Visible = true; ResetSize();
        var scr = GetViewport().GetVisibleRect().Size; var sz = GetCombinedMinimumSize();
        var p = at + new Vector2(20, 18); if (p.X + sz.X > scr.X - 8) p.X = at.X - sz.X - 16; if (p.Y + sz.Y > scr.Y - 8) p.Y = scr.Y - sz.Y - 8;
        GlobalPosition = new Vector2(Mathf.Max(8, p.X), Mathf.Max(8, p.Y));
    }
}

/// <summary>A square skill button: icon, rank pips, the hotbar key it is on, a lock when it cannot be learned yet.</summary>
public partial class SkillTile : Control
{
    public string Icon = "", Corner = ""; public int Rank, MaxRank = 5, Key = -1; public bool Known, Locked, CanLearn, Selected; public Color Tint = UiTheme.Accent;
    public Action? Click, RightClick; public Action<bool, Vector2>? Hover;
    bool _over;
    static Font F => ThemeDB.FallbackFont;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; MouseDefaultCursorShape = CursorShape.PointingHand;
        MouseEntered += () => { _over = true; QueueRedraw(); Hover?.Invoke(true, GetGlobalMousePosition()); };
        MouseExited += () => { _over = false; QueueRedraw(); Hover?.Invoke(false, Vector2.Zero); };
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left) { Sfx.Play("click"); Click?.Invoke(); AcceptEvent(); }
        else if (e is InputEventMouseButton rb && rb.Pressed && rb.ButtonIndex == MouseButton.Right && RightClick != null) { Sfx.Play("click"); RightClick(); AcceptEvent(); }
    }
    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, Known ? new Color(.03f, .1f, .18f, .92f) : new Color(.02f, .04f, .08f, .85f));
        if (Known) DrawRect(r.Grow(-2), Tint with { A = .08f });
        var border = Selected ? UiTheme.Highlight : CanLearn ? UiTheme.Highlight with { A = .75f } : Known ? UiTheme.Accent with { A = .7f } : UiTheme.AccentSoft with { A = .3f };
        DrawRect(r, border, false, Selected ? 2.5f : 1.2f);
        if (_over && !Selected) DrawRect(r.Grow(-1), Colors.White with { A = .35f }, false, 1f);
        if (Icon != "") Icons.Draw(this, Icon, new Vector2(Size.X / 2, Size.Y * .44f), Size.Y * .52f, Locked ? .3f : Known ? 1f : .6f);
        // rank pips
        if (Known) { float w = (Size.X - 12) / MaxRank; for (int i = 0; i < MaxRank; i++) DrawRect(new Rect2(6 + i * w + 1, Size.Y - 8, w - 2, 4), i < Rank ? UiTheme.Highlight : new Color(1, 1, 1, .15f)); }
        if (Key >= 0) { DrawRect(new Rect2(2, 2, 15, 15), new Color(0, 0, 0, .75f)); DrawString(F, new Vector2(5.5f, 14), (Key + 1).ToString(), HorizontalAlignment.Left, -1, 12, UiTheme.Highlight); }
        if (Corner != "") { float w = F.GetStringSize(Corner, HorizontalAlignment.Left, -1, 11).X; DrawRect(new Rect2(Size.X - w - 6, Size.Y - 16, w + 5, 14), new Color(0, 0, 0, .7f)); DrawString(F, new Vector2(Size.X - w - 3.5f, Size.Y - 5), Corner, HorizontalAlignment.Left, -1, 11, CanLearn ? UiTheme.Highlight : UiTheme.Dim); }
    }
}

/// <summary>
/// The modifier tree drawn as lanes: one row per family, modifiers left to right in unlock order, linked by a line that lights up as you
/// unlock them. Grows to the right and down as modifiers are added, inside a scroll box.
/// </summary>
public partial class ModLanes : Control
{
    public const float LaneH = 70f, X0 = 176f, StepX = 118f, R = 18f;
    public string Sel = ""; public Action<ModTree.Node>? Select, Activate; public Action<ModTree.Node?, Vector2>? Hover;
    string _hover = "";
    static Font F => ThemeDB.FallbackFont;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop; ModTree.Load();
        int most = 1; foreach (var b in ModTree.Branches) most = Math.Max(most, b.Nodes.Count);
        CustomMinimumSize = new Vector2(X0 + most * StepX + 20, ModTree.Branches.Count * LaneH + 26);   // room under the last lane for the scrollbar
        MouseExited += () => { if (_hover != "") { _hover = ""; QueueRedraw(); Hover?.Invoke(null, Vector2.Zero); } };
    }
    public Vector2 Pos(ModTree.Node n) => new(X0 + 40 + n.Index * StepX, ModTree.Branches.IndexOf(n.Branch) * LaneH + 30);

    public override void _Draw()
    {
        var d = Game.D;
        for (int li = 0; li < ModTree.Branches.Count; li++)
        {
            var b = ModTree.Branches[li]; float y0 = li * LaneH;
            DrawRect(new Rect2(0, y0 + 2, Size.X, LaneH - 4), li % 2 == 0 ? new Color(1, 1, 1, .025f) : new Color(0, 0, 0, .12f));
            // lane header: family icon, name and how many are unlocked
            int got = 0; foreach (var n in b.Nodes) if (ModTree.Unlocked(n)) got++;
            DrawCircle(new Vector2(24, y0 + 30), 16, b.Col with { A = .18f }); DrawArc(new Vector2(24, y0 + 30), 16, 0, Mathf.Tau, 24, b.Col, 1.5f);
            Icons.Draw(this, b.Icon, new Vector2(24, y0 + 30), 19);
            DrawString(F, new Vector2(48, y0 + 27), b.Name, HorizontalAlignment.Left, X0 - 52, 15, b.Col.Lightened(.25f));
            DrawString(F, new Vector2(48, y0 + 45), $"{got}/{b.Nodes.Count} unlocked", HorizontalAlignment.Left, X0 - 52, 12, UiTheme.Dim);
            // links
            var prevP = new Vector2(X0 - 6, y0 + 30); bool prevOn = true;
            foreach (var n in b.Nodes)
            {
                var p = Pos(n); bool on = ModTree.Unlocked(n);
                DrawLine(prevP, p - new Vector2(R + 2, 0), on && prevOn ? b.Col : prevOn ? b.Col with { A = .45f } : new Color(1, 1, 1, .12f), on && prevOn ? 3f : 2f);
                prevP = p + new Vector2(R + 2, 0); prevOn = on;
            }
            // nodes
            foreach (var n in b.Nodes)
            {
                var p = Pos(n); bool on = ModTree.Unlocked(n); string? why = on ? "x" : ModTree.Why(n); bool can = why == null;
                bool reachable = n.Prev == null || ModTree.Unlocked(n.Prev);
                DrawCircle(p, R + 3, n.Key == Sel ? Colors.White : can ? UiTheme.Highlight : on ? b.Col : new Color(.16f, .18f, .24f));
                DrawCircle(p, R, on ? b.Col.Darkened(.25f) : new Color(.04f, .06f, .1f));
                Icons.Draw(this, n.Attack ? "sword" : b.Icon, p, R * 1.15f, on ? 1f : reachable ? .55f : .25f);
                if (n.Key == _hover) DrawArc(p, R + 7, 0, Mathf.Tau, 28, Colors.White with { A = .7f }, 1.5f);
                if (!on && d.Level < n.Req) { string lv = "Lv " + n.Req; float w = F.GetStringSize(lv, HorizontalAlignment.Left, -1, 11).X; DrawRect(new Rect2(p.X + R - w + 2, p.Y - R - 6, w + 6, 14), new Color(0, 0, 0, .8f)); DrawString(F, new Vector2(p.X + R - w + 5, p.Y - R + 5), lv, HorizontalAlignment.Left, -1, 11, new Color(1f, .6f, .5f)); }
                if (!on && n.Cost > 1) DrawString(F, new Vector2(p.X - R - 4, p.Y - R + 5), n.Cost + "pt", HorizontalAlignment.Left, -1, 11, UiTheme.Highlight);
                // name under the node, shrunk to fit its slot
                int fs = 12; while (fs > 9 && F.GetStringSize(n.Name, HorizontalAlignment.Left, -1, fs).X > StepX - 10) fs--;
                DrawString(F, new Vector2(p.X - (StepX - 10) / 2, p.Y + R + 15), n.Name, HorizontalAlignment.Center, StepX - 10, fs, on ? Colors.White : can ? UiTheme.Highlight : reachable ? UiTheme.Text with { A = .75f } : UiTheme.Dim with { A = .7f });
            }
        }
    }

    ModTree.Node? Pick(Vector2 m)
    {
        foreach (var b in ModTree.Branches) foreach (var n in b.Nodes) if (Pos(n).DistanceTo(m) < R + 6) return n;
        return null;
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            var n = Pick(mm.Position); string k = n?.Key ?? "";
            if (k != _hover) { _hover = k; QueueRedraw(); MouseDefaultCursorShape = n != null ? CursorShape.PointingHand : CursorShape.Arrow; }
            Hover?.Invoke(n, GetGlobalMousePosition());
        }
        else if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            var n = Pick(mb.Position); if (n == null) return;
            if (mb.DoubleClick) Activate?.Invoke(n); else { Sfx.Play("click"); Select?.Invoke(n); }
            AcceptEvent();
        }
    }
}
