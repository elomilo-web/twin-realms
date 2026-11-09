using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>One inventory cell: rarity frame, vector icon, level or count, hover glow. Raises Hover(on) and Clicked(rightButton).</summary>
public partial class ItemSlot : Control
{
    public Item? Item; public string Empty = ""; public bool Wide; public string IconId = ""; public int Count; public Color Tint = Colors.White;
    public event Action<bool>? Hover; public event Action<bool>? Clicked;
    bool _over;
    static Font F => ThemeDB.FallbackFont;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        MouseEntered += () => { _over = true; QueueRedraw(); Hover?.Invoke(true); };
        MouseExited += () => { _over = false; QueueRedraw(); Hover?.Invoke(false); };
    }
    /// <summary>Used by the observer to show the tooltip without a mouse.</summary>
    public void SimulateHover() { _over = true; QueueRedraw(); Hover?.Invoke(true); }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton b && b.Pressed && (b.ButtonIndex == MouseButton.Left || b.ButtonIndex == MouseButton.Right)) { Clicked?.Invoke(b.ButtonIndex == MouseButton.Right); AcceptEvent(); }
    }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        if (Item != null)
        {
            var rc = Item.Color; Icons.Frame(this, r, rc, Item.Rar);
            if (Item.Rar >= 3) DrawRect(r.Grow(-1), rc with { A = .1f + .05f * Mathf.Sin(Time.GetTicksMsec() / 300f) }); // epic and legendary shimmer
            float ic = Mathf.Min(r.Size.Y, 66) * .62f; var c = Wide ? new Vector2(r.Size.Y * .5f + 4, r.Size.Y * .5f) : r.GetCenter() + new Vector2(0, -3);
            Icons.Draw(this, Icons.ForItem(Item.Slot, Item.Wt ?? ""), c, ic);
            if (Wide)
            {
                float tx = r.Size.Y + 6, tw = r.Size.X - tx - 4;
                DrawString(F, new Vector2(tx, 22), Fit(Item.Name, tw, 13), HorizontalAlignment.Left, tw, 13, rc.Lightened(.2f));
                DrawString(F, new Vector2(tx, 40), Stat(Item), HorizontalAlignment.Left, tw, 12, UiTheme.Text);
                DrawString(F, new Vector2(tx, 56), $"Lv {Item.Lvl}", HorizontalAlignment.Left, tw, 11, UiTheme.Dim);
            }
            else
            {
                DrawStringOutline(F, new Vector2(4, r.Size.Y - 5), $"{Item.Lvl}", HorizontalAlignment.Left, -1, 11, 3, Colors.Black);
                DrawString(F, new Vector2(4, r.Size.Y - 5), $"{Item.Lvl}", HorizontalAlignment.Left, -1, 11, UiTheme.Dim.Lightened(.3f));
                if (Item.Up > 0) DrawString(F, new Vector2(0, 14), $"+{Item.Up}", HorizontalAlignment.Right, r.Size.X - 4, 11, UiTheme.Highlight);
            }
        }
        else if (IconId != "")
        {
            DrawRect(r, new Color(.02f, .05f, .1f, .75f)); DrawRect(r, Tint with { A = .5f }, false, 1f);
            Icons.Draw(this, IconId, r.GetCenter() + new Vector2(0, -3), r.Size.Y * .6f);
            DrawStringOutline(F, new Vector2(0, r.Size.Y - 4), $"{Count}", HorizontalAlignment.Right, r.Size.X - 4, 12, 3, Colors.Black);
            DrawString(F, new Vector2(0, r.Size.Y - 4), $"{Count}", HorizontalAlignment.Right, r.Size.X - 4, 12, Colors.White);
        }
        else
        {
            DrawRect(r, new Color(.02f, .05f, .1f, .5f)); DrawRect(r, UiTheme.AccentSoft with { A = .25f }, false, 1f);
            if (Empty != "")
            {
                Icons.Draw(this, Icons.ForItem(Empty, "sword"), Wide ? new Vector2(r.Size.Y * .5f + 4, r.Size.Y * .5f) : r.GetCenter(), r.Size.Y * .5f, .18f);
                if (Wide) DrawString(F, new Vector2(r.Size.Y + 6, r.Size.Y * .5f + 5), char.ToUpper(Empty[0]) + Empty[1..], HorizontalAlignment.Left, -1, 13, UiTheme.Dim);
            }
        }
        if (_over) { DrawRect(r, UiTheme.Accent, false, 2f); DrawRect(r, UiTheme.Accent with { A = .08f }); }
    }
    public override void _Process(double delta) { if (Item != null && Item.Rar >= 3) QueueRedraw(); }

    static string Fit(string s, float w, int size) { while (s.Length > 4 && F.GetStringSize(s, HorizontalAlignment.Left, -1, size).X > w) s = s[..^2] + "…"; return s.Replace("……", "…"); }
    public static string Stat(Item it) => it.Slot == "weapon" ? (it.Sp > 0 && it.Dmg <= 0 ? $"+{it.Sp * 100:0}% spell" : $"{it.Dmg} damage") : it.Def > 0 ? $"{it.Def} defense" : $"+{it.Hp} health";
}

/// <summary>Item tooltip in the system-window style: name in rarity colour, type, stats with green/red arrows against the worn item.</summary>
public partial class ItemTip : PanelContainer
{
    VBoxContainer _v = null!;
    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore; ZIndex = 50; TopLevel = true;
        AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Panel with { A = .96f }, UiTheme.Accent, 1, 12, 10, 10));
        _v = new VBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _v.AddThemeConstantOverride("separation", 2); AddChild(_v);
    }
    Label L(string t, int size, Color c) { var l = new Label { Text = t, MouseFilter = MouseFilterEnum.Ignore }; l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c); _v.AddChild(l); return l; }

    public void Show(Item it, Item? worn, Rect2 anchor, Vector2 screen)
    {
        foreach (var c in _v.GetChildren()) { _v.RemoveChild(c); c.QueueFree(); }
        L(it.Name, 17, it.Color.Lightened(.15f));
        string kind = it.Slot == "weapon" ? char.ToUpper((it.Wt ?? "weapon")[0]) + (it.Wt ?? "weapon")[1..] + (it.Two ? ", two-handed" : "") : char.ToUpper(it.Slot[0]) + it.Slot[1..];
        L($"{Items.RarityName(it.Rar)} {kind}   Lv {it.Lvl}" + (it.Up > 0 ? $"   +{it.Up}" : ""), 13, UiTheme.Dim);
        var rows = new List<(string n, float a, float b, string fmt)>
        {
            ("Damage", it.Dmg, worn?.Dmg ?? 0, "0"), ("Defense", it.Def, worn?.Def ?? 0, "0"), ("Health", it.Hp, worn?.Hp ?? 0, "0"),
            ("Mana", it.Mp, worn?.Mp ?? 0, "0"), ("Spell power", it.Sp * 100, (worn?.Sp ?? 0) * 100, "0'%'"), ("Mana cost", -it.Mc * 100, -(worn?.Mc ?? 0) * 100, "0'%'"),
        };
        foreach (var (n, a, b, fmt) in rows)
        {
            if (a == 0 && b == 0) continue;
            var h = new HBoxContainer { MouseFilter = MouseFilterEnum.Ignore }; _v.AddChild(h);
            var nl = new Label { Text = n, CustomMinimumSize = new Vector2(110, 0), MouseFilter = MouseFilterEnum.Ignore }; nl.AddThemeFontSizeOverride("font_size", 14); nl.AddThemeColorOverride("font_color", UiTheme.Text); h.AddChild(nl);
            var vl = new Label { Text = a.ToString(fmt), CustomMinimumSize = new Vector2(50, 0), MouseFilter = MouseFilterEnum.Ignore }; vl.AddThemeFontSizeOverride("font_size", 14); vl.AddThemeColorOverride("font_color", Colors.White); h.AddChild(vl);
            if (worn != null && worn != it && Mathf.Abs(a - b) > .001f)
            {
                bool up = a > b; var dl = new Label { Text = (up ? "▲ +" : "▼ ") + (a - b).ToString(fmt), MouseFilter = MouseFilterEnum.Ignore };
                dl.AddThemeFontSizeOverride("font_size", 13); dl.AddThemeColorOverride("font_color", up ? new Color("5be37d") : new Color("ff6a5f")); h.AddChild(dl);
            }
        }
        if (worn == null && it.Slot != "") L("Nothing worn in this slot", 12, UiTheme.Dim);
        else if (worn != null && worn != it) L($"vs worn: {worn.Name}", 12, UiTheme.Dim);
        L($"Sells for {it.Value} G", 12, new Color("e3b04b"));
        Visible = true; ResetSize();
        var sz = GetCombinedMinimumSize(); var pos = new Vector2(anchor.End.X + 10, anchor.Position.Y);
        if (pos.X + sz.X > screen.X - 8) pos.X = anchor.Position.X - sz.X - 10;
        pos.Y = Mathf.Clamp(pos.Y, 8, screen.Y - sz.Y - 8); Position = pos;
    }
}
