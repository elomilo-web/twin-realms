using Godot;
using System.Collections.Generic;

namespace TwinRealms;

/// <summary>Settings > Controls: every action with its key; click one, then press the new key or mouse button (Esc cancels).</summary>
public partial class Ui
{
    string? _capture;           // action waiting for a new key
    string _controlsMsg = "";

    public void OpenControls()
    {
        var v = Frame("controls", "Settings", new Vector2(SetW, SetH), OpenControls);
        v = SettingsShell(v, "controls");
        // F-41: the "Controls" header repeated the tab and is gone; the how-to sits in the page's hint line at the bottom
        ShowOptHint(_capture != null ? "Press the new key or mouse button for \"" + NameOfAction(_capture) + "\" (Esc cancels)." : "Click an action, then press the key or mouse button you want. A key already in use swaps with it.");
        if (_capture != null) _optHint?.AddThemeColorOverride("font_color", GoldC);
        if (_controlsMsg != "") Row(v, _controlsMsg, new Color(1f, .85f, .5f), 14);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) }; v.AddChild(scroll);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 26); grid.AddThemeConstantOverride("v_separation", 5); scroll.AddChild(grid);
        var used = new System.Collections.Generic.Dictionary<string, int>();
        foreach (var (id, _) in Inputs.Rebindable)
        {
            var nm = Inputs.Name(Inputs.Current(id));
            if (id == Inputs.Dive && Inputs.SharedOk(Inputs.Dive, Inputs.Sneak) && nm == Inputs.Name(Inputs.Current(Inputs.Sneak))) continue;   // A-10: Sneak (land) and Dive (water) share a key on purpose: no clash
            used[nm] = used.GetValueOrDefault(nm) + 1;
        }
        foreach (var (id, name) in Inputs.Rebindable)
        {
            string a = id;
            var cell = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 0) }; cell.AddThemeConstantOverride("separation", 8); grid.AddChild(cell);
            var l = Lbl(name, 15, Parch, cell); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(100, 0); l.VerticalAlignment = VerticalAlignment.Center;
            bool waiting = _capture == a; string keyName = Inputs.Name(Inputs.Current(a));
            bool clash = !waiting && used.GetValueOrDefault(keyName) > 1;
            var b = Btn(waiting ? "press a key..." : keyName, () => { _capture = a; _controlsMsg = ""; OpenControls(); }, true);
            b.CustomMinimumSize = new Vector2(124, UiTheme.CtlH); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            Color font = waiting ? GoldC : clash ? new Color(1f, .7f, .65f) : Game.Set.Binds.ContainsKey(a) ? new Color(.75f, 1f, .8f) : Parch;
            StyleBoxFlat Cap(Color bg, Color bd) => new() { BgColor = bg, BorderColor = bd, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
            var norm = waiting ? Cap(new Color(.20f, .15f, .06f), GoldC) : clash ? Cap(new Color(.28f, .06f, .06f), new Color(.65f, .18f, .16f)) : Cap(new Color(.10f, .09f, .08f), UiTheme.AccentSoft);
            b.AddThemeStyleboxOverride("normal", norm); b.AddThemeStyleboxOverride("pressed", norm);
            b.AddThemeStyleboxOverride("hover", Cap(waiting ? new Color(.24f, .18f, .07f) : clash ? new Color(.34f, .08f, .08f) : new Color(.16f, .14f, .10f), waiting || !clash ? GoldC : new Color(.8f, .25f, .2f)));
            foreach (var cn in new[] { "font_color", "font_hover_color", "font_pressed_color" }) b.AddThemeColorOverride(cn, font);
            b.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 1 });
            cell.AddChild(b);
        }
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 10); v.AddChild(bot);
        bot.AddChild(Btn("Reset to defaults", () => { Inputs.ResetAll(); _capture = null; _controlsMsg = "All controls are back to their defaults."; OpenControls(); }, true));
        bot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var back = Btn(InGame ? "Back" : "Done", () => { _capture = null; _controlsMsg = ""; if (InGame) OpenPause(); else ShowTitle(); }); back.CustomMinimumSize = new Vector2(110, 28); bot.AddChild(back);
    }    static string NameOfAction(string id) { foreach (var (a, n) in Inputs.Rebindable) if (a == id) return n; return id; }

    /// <summary>While waiting for a key, the next key or mouse button press becomes the binding (before anything else sees it).</summary>
    public override void _Input(InputEvent e)
    {
        if (PadCaptureInput(e)) return;   // Settings > Controller is waiting for a pad button (UiPad.cs)
        if (_capture == null || _panelName != "controls") { _capture = null; return; }
        InputEvent? got = null;
        if (e is InputEventKey k && k.Pressed && !k.Echo)
        {
            if (k.PhysicalKeycode == Key.Escape || k.Keycode == Key.Escape) { _capture = null; GetViewport().SetInputAsHandled(); OpenControls(); return; }
            got = new InputEventKey { PhysicalKeycode = k.PhysicalKeycode != Key.None ? k.PhysicalKeycode : k.Keycode };
        }
        else if (e is InputEventMouseButton m && m.Pressed) got = new InputEventMouseButton { ButtonIndex = m.ButtonIndex };
        if (got == null) return;
        GetViewport().SetInputAsHandled();
        string a = _capture; _capture = null;
        var swapped = Inputs.Rebind(a, got);
        _controlsMsg = $"{NameOfAction(a)}: {Inputs.Name(got)}" + (swapped != null ? $"   ({swapped} took its old key)" : "");
        Sfx.Play("click"); CallDeferred(MethodName.OpenControls);   // rebuild after this click has finished
    }
}
