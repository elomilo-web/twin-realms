using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Controller support in the menus: Settings > Controller (button bindings, stick, vibration, button names), focus navigation
/// (the first button of every window takes focus while playing on a pad; D-pad / left stick move it, A presses), B backs out,
/// LB / RB switch the settings tabs.
/// </summary>
public partial class Ui
{
    string? _padCapture;        // action waiting for a new pad button
    string _padMsg = "";
    string _focusPanel = ""; int _focusIdx = -1;

    static StyleBoxFlat PadFocusBox() => new()
    {
        DrawCenter = false, BorderColor = UiTheme.Highlight, BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        ExpandMarginLeft = 2, ExpandMarginRight = 2, ExpandMarginTop = 2, ExpandMarginBottom = 2,
    };

    // ---------------------------------------------------------------- focus
    void Focusables(Node n, List<Control> list, bool on)
    {
        foreach (var ch in n.GetChildren())
        {
            if (ch is ScrollContainer sc) sc.FollowFocus = true;
            if (ch is Control c && (c is BaseButton || c is HSlider) && c.IsVisibleInTree())
            {
                if (on && c.FocusMode == Control.FocusModeEnum.None) { c.FocusMode = Control.FocusModeEnum.All; c.SetMeta("padf", true); }
                else if (!on && c.HasMeta("padf")) { c.FocusMode = Control.FocusModeEnum.None; c.RemoveMeta("padf"); }
                if (on && c.FocusMode != Control.FocusModeEnum.None && !(c is BaseButton bb && bb.Disabled)) list.Add(c);
            }
            Focusables(ch, list, on);
        }
    }
    void RememberFocus()
    {
        _focusIdx = -1; _focusPanel = _panelName;
        if (_panel == null || !Inputs.UsingPad || GetViewport().GuiGetFocusOwner() is not Control f) return;
        var l = new List<Control>(); Focusables(_panel, l, true); _focusIdx = l.IndexOf(f);
    }
    /// <summary>Playing on a pad: makes the open window's buttons focusable and focuses the first one (or the one focused before a rebuild).
    /// Back on mouse and keyboard: focus is dropped, so nothing changes for the mouse.</summary>
    public void PadFocus()
    {
        if (_panel == null || !IsInstanceValid(_panel)) return;
        var l = new List<Control>(); Focusables(_panel, l, Inputs.UsingPad);
        if (!Inputs.UsingPad) { GetViewport().GuiGetFocusOwner()?.ReleaseFocus(); return; }
        if (l.Count == 0) return;
        if (GetViewport().GuiGetFocusOwner() is Control cur && l.Contains(cur)) return;
        int i = _focusPanel == _panelName && _focusIdx >= 0 && _focusIdx < l.Count ? _focusIdx : 0;
        // prefer a real choice over the title bar's X when nothing was focused before
        if (i == 0 && l.Count > 1 && l[0] is Button xb && xb.Text == "X") i = 1;
        l[i].GrabFocus();
    }

    static readonly string[] SetTabs = { "audio", "display", "game", "controls", "pad" };
    void OpenSetTab(string t) { _capture = null; _padCapture = null; if (t == "controls") OpenControls(); else if (t == "pad") OpenPad(); else { _setTab = t; OpenSettings(); } }

    /// <summary>Raw pad buttons in menus: B backs out (like Esc), LB / RB step through the settings tabs.</summary>
    bool PadMenuInput(InputEvent e)
    {
        if (_panel == null || e is not InputEventJoypadButton { Pressed: true } jb) return false;
        bool set = _panelName is "settings" or "controls" or "pad";
        if (jb.ButtonIndex == JoyButton.B)
        {
            if (set) { _capture = null; _padCapture = null; if (InGame) OpenPause(); else ShowTitle(); return true; }
            if (InGame && _panelName != "title") { Close(); return true; }
            return false;
        }
        if (set && jb.ButtonIndex is JoyButton.LeftShoulder or JoyButton.RightShoulder)
        {
            string cur = _panelName == "settings" ? _setTab : _panelName;
            int i = System.Array.IndexOf(SetTabs, cur); if (i < 0) i = 0;
            i = (i + (jb.ButtonIndex == JoyButton.RightShoulder ? 1 : -1) + SetTabs.Length) % SetTabs.Length;
            _focusIdx = -1; Sfx.Play("click"); OpenSetTab(SetTabs[i]); return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- Settings > Controller
    public void OpenPad()
    {
        var v = Frame("pad", "Settings", new Vector2(940, 600), OpenPad); var s = Game.Set;
        v = SettingsShell(v, "pad");
        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300), FollowFocus = true }; v.AddChild(sc);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 10); sc.AddChild(list);
        static string Pct(float x) => $"{x * 100:0}%";
        Header("Controller", list);
        string conn = Inputs.HasPad ? $"Connected: {Inputs.PadLabel(Inputs.Pads[0])}  ({(Inputs.PadStyle == "playstation" ? "PlayStation" : Inputs.PadStyle == "xbox" ? "Xbox" : "generic")} layout)" : "No controller connected. Xbox, PlayStation and most other pads work as soon as they are plugged in.";
        Row(list, conn, Inputs.HasPad ? GoldC : Dim, 14);
        Choice(list, "Button names", "Which names the prompts and the hotbar show for buttons. Auto follows the connected controller.", new[] { "Auto", "Xbox", "PlayStation" }, Mathf.Clamp(s.PadGlyphs, 0, 2), i => { s.PadGlyphs = i; Inputs.ClearKeyCache(); });
        Header("Camera", list);
        SliderRow(list, "Stick sensitivity", "How fast the right stick turns the camera. Holding it all the way speeds the turn up a little.", s.PadSens, .2f, 3f, .05f, x => $"x{x:0.00}", x => s.PadSens = x);
        Choice(list, "Invert Y", "Push the stick forward to look down (shared with the mouse setting).", OffOn, s.InvertY ? 1 : 0, i => s.InvertY = i == 1);
        SliderRow(list, "Stick deadzone", "How far a stick must move before it counts. Raise it if the camera or hero drifts by itself.", s.PadDeadzone, .05f, .4f, .01f, Pct, x => s.PadDeadzone = x);
        Header("Vibration", list);
        Choice(list, "Vibration", "Rumble on heavy hits, critical hits, parries and blows you take.", OffOn, s.Vibration ? 1 : 0, i => { s.Vibration = i == 1; Inputs.Rumble(.4f, .6f, .2f); });
        SliderRow(list, "Vibration strength", "How strong the rumble is.", s.VibStrength, 0f, 1f, .05f, Pct, x => { s.VibStrength = x; Inputs.Rumble(.3f * x, .5f * x, .12f); });

        Header("Buttons", list);
        Row(list, _padCapture != null ? $"Press the new button for \"{NameOfAction(_padCapture)}\" (Start / Options or Esc cancels). Hold {(Inputs.PadStyle == "playstation" ? "L2" : "LT")} while pressing it for a combo."
            : $"Select an action, then press the button you want. {(Inputs.PadStyle == "playstation" ? "L2" : "LT")} is the combo button: hold it with another button for a second layer (for example skills 5 and 6). Left stick moves, right stick turns the camera; flick it while locked on to switch target.",
            _padCapture != null ? GoldC : Dim, 14);
        if (_padMsg != "") Row(list, _padMsg, new Color(1f, .85f, .5f), 14);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 26); grid.AddThemeConstantOverride("v_separation", 5); list.AddChild(grid);
        var used = new Dictionary<string, int>();
        foreach (var (id, _) in Inputs.Rebindable) { if (Inputs.IsMove(id)) continue; var c = Inputs.PadCode(id); if (c != "") used[c] = used.GetValueOrDefault(c) + 1; }
        PadRow(grid, "Move", "Left stick", null, false, false);
        foreach (var (id, name) in Inputs.Rebindable)
        {
            if (Inputs.IsMove(id)) continue; string a = id, code = Inputs.PadCode(id);
            bool waiting = _padCapture == a, clash = !waiting && code != "" && used.GetValueOrDefault(code) > 1;
            string label = id == Inputs.Sprint ? name + " (toggle; or hold Roll)" : id == Inputs.Lock ? "Lock on / off" : name;
            PadRow(grid, label, waiting ? "press a button..." : Inputs.PadName(code, true), () => { _padCapture = a; _padMsg = ""; OpenPad(); }, waiting, clash, Game.Set.PadBinds.ContainsKey(a));
        }
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 10); v.AddChild(bot);
        bot.AddChild(Btn("Reset controller defaults", () =>
        {
            var def = new Settings(); Inputs.ResetPad(false); s.PadSens = def.PadSens; s.PadDeadzone = def.PadDeadzone; s.Vibration = def.Vibration; s.VibStrength = def.VibStrength; s.PadGlyphs = def.PadGlyphs;
            Game.SaveSettings(); _padCapture = null; _padMsg = "The controller is back to its default layout."; OpenPad();
        }, true));
        bot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var back = Btn(InGame ? "Back" : "Done", () => { _padCapture = null; _padMsg = ""; if (InGame) OpenPause(); else ShowTitle(); }); back.CustomMinimumSize = new Vector2(140, 36); bot.AddChild(back);
    }

    /// <summary>One binding cell: action name and its button cap (same caps as Settings > Controls).</summary>
    void PadRow(GridContainer grid, string name, string keyName, System.Action? click, bool waiting, bool clash, bool changed = false)
    {
        var cell = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(220, 0) }; cell.AddThemeConstantOverride("separation", 8); grid.AddChild(cell);
        var l = Lbl(name, 15, Parch, cell); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(100, 0); l.VerticalAlignment = VerticalAlignment.Center;
        var b = Btn(keyName, click ?? (() => { }), true); b.Disabled = click == null;
        b.CustomMinimumSize = new Vector2(124, 30); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
        Color font = waiting ? GoldC : clash ? new Color(1f, .7f, .65f) : changed ? new Color(.75f, 1f, .8f) : Parch;
        StyleBoxFlat Cap(Color bg, Color bd) => new() { BgColor = bg, BorderColor = bd, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 3, CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3, ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 3, ContentMarginBottom = 3 };
        var norm = waiting ? Cap(new Color(.20f, .15f, .06f), GoldC) : clash ? Cap(new Color(.28f, .06f, .06f), new Color(.65f, .18f, .16f)) : Cap(new Color(.10f, .09f, .08f), UiTheme.AccentSoft);
        b.AddThemeStyleboxOverride("normal", norm); b.AddThemeStyleboxOverride("pressed", norm); b.AddThemeStyleboxOverride("disabled", norm);
        b.AddThemeStyleboxOverride("hover", Cap(waiting ? new Color(.24f, .18f, .07f) : clash ? new Color(.34f, .08f, .08f) : new Color(.16f, .14f, .10f), waiting || !clash ? GoldC : new Color(.8f, .25f, .2f)));
        foreach (var cn in new[] { "font_color", "font_hover_color", "font_pressed_color", "font_disabled_color" }) b.AddThemeColorOverride(cn, click == null ? Dim : font);
        cell.AddChild(b);
    }

    /// <summary>While waiting for a pad button (Settings > Controller): the next button or right trigger press becomes the binding.</summary>
    bool PadCaptureInput(InputEvent e)
    {
        if (_padCapture == null) return false;
        if (_panelName != "pad") { _padCapture = null; return false; }
        if (e is InputEventKey { Pressed: true } k && (k.PhysicalKeycode == Key.Escape || k.Keycode == Key.Escape)) { _padCapture = null; GetViewport().SetInputAsHandled(); CallDeferred(MethodName.OpenPad); return true; }
        if (e is not (InputEventJoypadButton or InputEventJoypadMotion)) return true;
        GetViewport().SetInputAsHandled();   // no focus moves or presses while waiting
        string code = "";
        if (e is InputEventJoypadButton { Pressed: true } jb)
        {
            if (jb.ButtonIndex == JoyButton.Start) { _padCapture = null; CallDeferred(MethodName.OpenPad); return true; }
            code = "b:" + (int)jb.ButtonIndex;
        }
        else if (e is InputEventJoypadMotion { Axis: JoyAxis.TriggerRight } jm && jm.AxisValue > .6f) code = "a:" + (int)JoyAxis.TriggerRight;
        if (code == "") return true;   // sticks and the combo trigger itself do not bind
        if (Inputs.PadAxis(JoyAxis.TriggerLeft) > .5f) code = "L+" + code;
        string a = _padCapture; _padCapture = null;
        var swapped = Inputs.RebindPad(a, code);
        _padMsg = $"{NameOfAction(a)}: {Inputs.PadName(code, true)}" + (swapped != null ? $"   ({swapped} took its old button)" : "");
        Sfx.Play("click"); CallDeferred(MethodName.OpenPad); return true;
    }
}
