using Godot;

namespace TwinRealms;

/// <summary>Settings > Controls: every action with its key; click one, then press the new key or mouse button (Esc cancels).</summary>
public partial class Ui
{
    string? _capture;           // action waiting for a new key
    string _controlsMsg = "";

    public void OpenControls()
    {
        var v = Frame("controls", "Settings", new Vector2(760, 600), OpenControls);
        SettingsTabs(v, "controls");
        Row(v, _capture != null ? "Press the new key or mouse button for \"" + NameOfAction(_capture) + "\" (Esc cancels)." : "Click an action, then press the key or mouse button you want. A key already in use swaps with it.",
            _capture != null ? UiTheme.Highlight : Dim, 15);
        if (_controlsMsg != "") Row(v, _controlsMsg, new Color(1f, .85f, .5f), 14);
        var scroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) }; v.AddChild(scroll);
        var grid = new GridContainer { Columns = 2, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; grid.AddThemeConstantOverride("h_separation", 18); grid.AddThemeConstantOverride("v_separation", 4); scroll.AddChild(grid);
        foreach (var (id, name) in Inputs.Rebindable)
        {
            string a = id;
            var l = Lbl(name, 16, Colors.White, grid); l.CustomMinimumSize = new Vector2(330, 0); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            bool waiting = _capture == a;
            var b = Btn(waiting ? "press a key..." : Inputs.Name(Inputs.Current(a)), () => { _capture = a; _controlsMsg = ""; OpenControls(); }, true);
            b.CustomMinimumSize = new Vector2(220, 30); if (waiting) b.AddThemeColorOverride("font_color", UiTheme.Highlight);
            else if (Game.Set.Binds.ContainsKey(a)) b.AddThemeColorOverride("font_color", new Color(.75f, 1f, .8f));   // changed from the default
            grid.AddChild(b);
        }
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 10); v.AddChild(bot);
        bot.AddChild(Btn("Reset to defaults", () => { Inputs.ResetAll(); _capture = null; _controlsMsg = "All controls are back to their defaults."; OpenControls(); }));
        bot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var back = Btn(InGame ? "Back" : "Done", () => { _capture = null; _controlsMsg = ""; if (InGame) OpenPause(); else ShowTitle(); }); back.CustomMinimumSize = new Vector2(140, 36); bot.AddChild(back);
    }
    static string NameOfAction(string id) { foreach (var (a, n) in Inputs.Rebindable) if (a == id) return n; return id; }

    /// <summary>While waiting for a key, the next key or mouse button press becomes the binding (before anything else sees it).</summary>
    public override void _Input(InputEvent e)
    {
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
