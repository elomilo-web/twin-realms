using System;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The spell icon window (Spellcraft, "Icon" next to the name): Library (every built-in skill icon), Editor (shape, symbol, two
/// colours, border) and Import (PNG files in user://icons/). The pick is saved on the spell as compact data (SpellIcon.cs).
/// </summary>
public partial class Ui
{
    static string _icTab = "library";
    static int _edShape, _edSym, _edBorder = 2;
    static Color _edBg = new(.45f, .15f, .08f), _edFg = new(1f, .7f, .3f);
    const float IcW = 760, IcH = 560;

    public void OpenSpellIcon(string? tab)
    {
        if (tab != null)
        {
            _icTab = tab;
            if (SpellIcon.ParseEd(_scWork.Icon) is { } e) { _edShape = e.shape; _edSym = e.symbol; _edBg = e.bg; _edFg = e.fg; _edBorder = e.border; }
        }
        var v = Frame("spellcraft", "Spell icon", new Vector2(IcW, IcH), () => OpenSpellIcon(null)); HideInfo();
        string spellName = _scWork.Name.Trim() != "" ? _scWork.Name.Trim() : _scWork.Sec(0).Count > 0 ? Spellcraft.AutoName(_scWork) : "New spell";

        // header: current icon, tabs, back
        var top = new HBoxContainer(); top.AddThemeConstantOverride("separation", 8); v.AddChild(top);
        top.AddChild(new IconBox { Icon = _scWork.Icon != "" ? _scWork.Icon : _scWork.Sec(0).Count > 0 && Spellcraft.Preview(_scWork) is { } pv ? SpellIcon.Auto(pv) : "trinket", CustomMinimumSize = new Vector2(52, 52), MouseFilter = Control.MouseFilterEnum.Ignore });
        var tl = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; top.AddChild(tl);
        var nl = Lbl(spellName, 17, Parch, tl); nl.ClipText = true;
        Lbl(_scWork.Icon == "" ? "Automatic icon (from its delivery and element)" : _scWork.Icon.StartsWith("lib:") ? "From the library" : _scWork.Icon.StartsWith("ed:") ? "Drawn in the editor" : "Imported picture", 12, Dim, tl);
        var auto = Btn("Use automatic", () => SetSpellIcon(""), true); auto.Disabled = _scWork.Icon == ""; top.AddChild(auto);
        top.AddChild(Btn("Back", () => OpenSpellcraft(null), true));

        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 4); v.AddChild(tabs);
        foreach (var (k, label) in new[] { ("library", "Library"), ("editor", "Editor"), ("import", "Import") })
        {
            var b = Btn(label, () => OpenSpellIcon(k)); b.CustomMinimumSize = new Vector2(120, 32);
            if (k == _icTab) { b.AddThemeColorOverride("font_color", UiTheme.Highlight); b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.16f, .12f, .05f, .95f), UiTheme.Highlight, 1, 6, 4, 0)); }
            tabs.AddChild(b);
        }
        v.AddChild(new ColorRect { Color = UiTheme.Accent with { A = .35f }, CustomMinimumSize = new Vector2(0, 1) });

        if (_icTab == "library") IcLibrary(v);
        else if (_icTab == "editor") IcEditor(v);
        else IcImport(v);
    }

    static Button IconButton(string icon, float size, Action click, string tip)
    {
        var b = new Button { CustomMinimumSize = new Vector2(size + 8, size + 8), TooltipText = tip, FocusMode = Control.FocusModeEnum.All };
        var box = new IconBox { Icon = icon, MouseFilter = Control.MouseFilterEnum.Ignore }; box.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); box.OffsetLeft = 4; box.OffsetTop = 4; box.OffsetRight = -4; box.OffsetBottom = -4;
        b.AddChild(box); b.MouseEntered += Sfx.UiHover; b.Pressed += () => { Sfx.Play("click"); click(); }; return b;
    }

    void IcLibrary(Control v)
    {
        Lbl("Pick any skill's icon for this spell.", 13, Dim, v);
        var grid = new GridContainer { Columns = 11 }; grid.AddThemeConstantOverride("h_separation", 4); grid.AddThemeConstantOverride("v_separation", 4); v.AddChild(grid);
        foreach (var k in Skills.All.Where(s => !s.Crafted && SpellIcon.HasBuiltin(s.Id)))
        {
            string ic = "lib:" + k.Id;
            var b = IconButton(ic, 48, () => SetSpellIcon(ic), k.Name);
            if (_scWork.Icon == ic) b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.2f, .15f, .05f, .95f), UiTheme.Highlight, 2, 4, 0, 0));
            grid.AddChild(b);
        }
    }

    void IcEditor(Control v)
    {
        string cur = SpellIcon.Ed(_edShape, _edSym, _edBg, _edFg, _edBorder);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", 14); v.AddChild(row);
        // left: the big preview, colours and the use button
        var lc = new VBoxContainer { CustomMinimumSize = new Vector2(200, 0) }; lc.AddThemeConstantOverride("separation", 8); row.AddChild(lc);
        lc.AddChild(new IconBox { Icon = cur, CustomMinimumSize = new Vector2(150, 150), MouseFilter = Control.MouseFilterEnum.Ignore });
        foreach (var (label, isBg) in new[] { ("Background", true), ("Symbol", false) })
        {
            var cr = new HBoxContainer(); cr.AddThemeConstantOverride("separation", 6); lc.AddChild(cr);
            var l = Lbl(label, 13, Parch, cr); l.CustomMinimumSize = new Vector2(90, 0);
            var cp = new ColorPickerButton { Color = isBg ? _edBg : _edFg, EditAlpha = false, CustomMinimumSize = new Vector2(90, 28) };
            cp.PopupClosed += () => { if (isBg) _edBg = cp.Color; else _edFg = cp.Color; OpenSpellIcon(null); };
            cr.AddChild(cp);
        }
        var use = Btn(_scWork.Icon == cur ? "In use" : "Use this icon", () => SetSpellIcon(cur)); use.Disabled = _scWork.Icon == cur; use.CustomMinimumSize = new Vector2(0, 36);
        use.AddThemeColorOverride("font_color", UiTheme.Highlight); lc.AddChild(use);

        // right: shape, border, symbol
        var rc = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rc.AddThemeConstantOverride("separation", 6); row.AddChild(rc);
        void Choices(string title, string[] names, int sel, Action<int> pick)
        {
            Lbl(title, 11, UiTheme.Accent, rc);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 4); rc.AddChild(h);
            for (int i = 0; i < names.Length; i++)
            {
                int k = i; var b = Btn(names[i], () => { pick(k); OpenSpellIcon(null); }, true); b.CustomMinimumSize = new Vector2(0, 28);
                if (i == sel) b.AddThemeColorOverride("font_color", UiTheme.Highlight);
                h.AddChild(b);
            }
        }
        Choices("SHAPE", IconArt.EdShapes, _edShape, i => _edShape = i);
        Choices("BORDER", IconArt.EdBorders, _edBorder, i => _edBorder = i);
        Lbl("SYMBOL", 11, UiTheme.Accent, rc);
        var grid = new GridContainer { Columns = 9 }; grid.AddThemeConstantOverride("h_separation", 3); grid.AddThemeConstantOverride("v_separation", 3); rc.AddChild(grid);
        for (int i = 0; i < IconArt.EdSymbols.Length; i++)
        {
            int k = i; string ic = SpellIcon.Ed(_edShape, i, _edBg, _edFg, _edBorder);
            var b = IconButton(ic, 40, () => { _edSym = k; OpenSpellIcon(null); }, IconArt.EdSymbols[i].name);
            if (i == _edSym) b.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.2f, .15f, .05f, .95f), UiTheme.Highlight, 2, 4, 0, 0));
            grid.AddChild(b);
        }
    }

    void IcImport(Control v)
    {
        string dir = ProjectSettings.GlobalizePath(SpellIcon.ImportDir);
        var files = SpellIcon.ImportFiles();
        Row(v, $"Put PNG pictures in this folder, then pick one. It is shrunk to {SpellIcon.ImportPx} px and saved inside the spell (it goes online with your hero; at most {SpellIcon.MaxLen / 1000} KB).", Dim, 13);
        var pr = new HBoxContainer(); pr.AddThemeConstantOverride("separation", 6); v.AddChild(pr);
        var pl = Lbl(dir, 12, Parch, pr); pl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; pl.ClipText = true;
        pr.AddChild(Btn("Open folder", () => OS.ShellOpen(dir), true));
        pr.AddChild(Btn("Refresh", () => OpenSpellIcon(null), true));
        if (files.Count == 0) { Row(v, "No PNG files there yet.", Dim, 14); return; }
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 8); v.AddChild(grid);
        foreach (var f in files)
        {
            string path = f; var b = Btn(System.IO.Path.GetFileName(f), () =>
            {
                var ic = SpellIcon.FromPng(path, out var why);
                if (ic == null) { Note(why, ScErr); return; }
                SetSpellIcon(ic);
            }, true);
            b.CustomMinimumSize = new Vector2(340, 30); b.ClipText = true; grid.AddChild(b);
        }
    }

    /// <summary>Sets the icon of the spell being built; a saved spell keeps it at once (no re-forge needed).</summary>
    void SetSpellIcon(string icon)
    {
        _scWork.Icon = SpellIcon.Clean(icon);
        if (_scEdit != "" && Game.D.Spells.FirstOrDefault(x => x.Id == _scEdit) is { } s) { s.Icon = _scWork.Icon; Game.Notify(); Game.Save(); }
        Note(_scWork.Icon == "" ? "Spell icon: automatic" : "Spell icon set" + (_scEdit == "" ? " (kept when you forge it)" : ""), UiTheme.Highlight);
        OpenSpellIcon(null);
    }

    /// <summary>Dev capture "icon_editor": Spellcraft on the preloaded three-section spell with the icon editor open.</summary>
    internal static void IcDebugOpen()
    {
        ScDebugPreload3();
        _edShape = 3; _edSym = 6; _edBg = new Color(.3f, .12f, .45f); _edFg = new Color(.85f, .6f, 1f); _edBorder = 2;
        I?.OpenSpellIcon("editor");
        _edShape = 3; _edSym = 6; _edBg = new Color(.3f, .12f, .45f); _edFg = new Color(.85f, .6f, 1f); _edBorder = 2; I?.OpenSpellIcon(null);
    }
}
