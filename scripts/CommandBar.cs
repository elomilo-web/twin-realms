using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>
/// The in-game command bar (focus F-42). "/" or "~" opens a one-line input at the bottom of the screen; above it the suggestions
/// (commands by prefix, then the argument choices of the command) and a short scrollback of output. Tab completes (again: the next
/// suggestion), Up / Down walk the history (user://command_history.txt, the last 100 lines, kept across sessions), Enter runs, Esc closes.
/// While it is open the mouse is released, which suspends all gameplay input (the hero only reads keys while the mouse is captured).
/// It does not open while a menu is up or another text field has the keyboard.
/// A line that is not a command is tried as the admin login (Admin.PasswordOk, F-43): such a line is never echoed, never put in the
/// history and never logged, whether it was right or wrong.
/// </summary>
public partial class CommandBar : CanvasLayer
{
    static CommandBar? _i;
    public static bool IsOpen => _i != null && _i._open;
    const string HistoryPath = "user://command_history.txt"; const int HistoryMax = 100, LinesMax = 300, LinesShown = 14;
    static readonly List<string> _lines = new();
    readonly List<string> _history = new();
    bool _open; int _histAt = -1; string _draft = ""; float _showT;
    Control _root = null!; LineEdit _edit = null!; Label _out = null!, _sug = null!; PanelContainer _outBox = null!, _sugBox = null!;
    List<(string line, string label)> _sugs = new(); int _tabAt = -1; string _tabText = "";

    public override void _Ready()
    {
        _i = this; Layer = 60; ProcessMode = ProcessModeEnum.Always;
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        var v = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        v.AnchorLeft = 0; v.AnchorRight = 1; v.AnchorTop = 1; v.AnchorBottom = 1; v.OffsetLeft = 16; v.OffsetRight = -16; v.OffsetBottom = -14; v.OffsetTop = -14;
        v.GrowVertical = Control.GrowDirection.Begin; v.AddThemeConstantOverride("separation", 4); _root.AddChild(v);
        StyleBoxFlat Box(float a) => new() { BgColor = new Color(.02f, .04f, .07f, a), ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        _outBox = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _outBox.AddThemeStyleboxOverride("panel", Box(.72f)); v.AddChild(_outBox);
        _out = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore }; _out.AddThemeFontSizeOverride("font_size", 15); _out.AddThemeColorOverride("font_color", UiTheme.Text); _outBox.AddChild(_out);
        _sugBox = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _sugBox.AddThemeStyleboxOverride("panel", Box(.88f)); v.AddChild(_sugBox);
        _sug = new Label { MouseFilter = Control.MouseFilterEnum.Ignore, ClipText = true }; _sug.AddThemeFontSizeOverride("font_size", 15); _sug.AddThemeColorOverride("font_color", UiTheme.Highlight); _sugBox.AddChild(_sug);
        _edit = new LineEdit { PlaceholderText = "command (/help), Tab completes, Up / Down history, Esc closes", MaxLength = 300, CustomMinimumSize = new Vector2(0, 36), CaretBlink = true, ContextMenuEnabled = false };
        _edit.AddThemeFontSizeOverride("font_size", 17); _edit.AddThemeColorOverride("font_color", UiTheme.Text);
        var eb = Box(.92f); eb.BorderColor = UiTheme.Accent; eb.BorderWidthLeft = eb.BorderWidthRight = eb.BorderWidthTop = eb.BorderWidthBottom = 1;
        _edit.AddThemeStyleboxOverride("normal", eb); _edit.AddThemeStyleboxOverride("focus", eb); v.AddChild(_edit);
        _edit.TextSubmitted += OnSubmit; _edit.TextChanged += _ => { _tabAt = -1; Refresh(); };
        LoadHistory();
        AddChild(new ChatBox { Name = "ChatBox" });   // A-31: the chat box (UiChat.cs); it also registers the chat commands (/w /r /p /l /g ...) this bar runs
    }
    public override void _ExitTree() { if (_i == this) _i = null; }

    // ---------------------------------------------------------------- output
    /// <summary>Adds output to the scrollback and the game log (also the server's answers when they arrive).</summary>
    public static void Print(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var l in text.Split('\n')) { string s = l.TrimEnd('\r'); _lines.Add(s); GD.Print("[CMD] " + s); }
        if (_lines.Count > LinesMax) _lines.RemoveRange(0, _lines.Count - LinesMax);
        if (_i != null && IsInstanceValid(_i)) { _i._showT = 8f; _i.Refresh(); }
    }
    public static void ClearLog() { _lines.Clear(); if (_i != null && IsInstanceValid(_i)) _i.Refresh(); }
    /// <summary>Opens the bar with this text typed (dev captures).</summary>
    public static void OpenWith(string text) { if (_i != null && IsInstanceValid(_i)) _i.Open(text); }
    /// <summary>Runs a line as if it was typed and Enter pressed (dev captures).</summary>
    public static void Type(string line) { if (_i != null && IsInstanceValid(_i)) _i.OnSubmit(line); }

    void Refresh()
    {
        if (!IsInstanceValid(_root)) return;
        _root.Visible = _open || _showT > 0; _edit.Visible = _open;
        _out.Text = string.Join("\n", _lines.Skip(Math.Max(0, _lines.Count - LinesShown))); _outBox.Visible = _lines.Count > 0;
        _sugs = _open && _edit.Text.Trim().Length > 0 ? Commands.Suggest(_edit.Text) : new();
        _sug.Text = string.Join("\n", _sugs.Select((s, i) => (i == Math.Max(0, _tabAt) ? "> " : "   ") + s.label)); _sugBox.Visible = _open && _sugs.Count > 0;
    }

    // ---------------------------------------------------------------- open / close
    static bool TypingElsewhere(Viewport vp) => vp.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
    void Open(string text)
    {
        if (_open) { _edit.Text = text; _edit.CaretColumn = text.Length; Refresh(); return; }
        _open = true; _histAt = -1; _draft = ""; _tabAt = -1;
        Input.MouseMode = Input.MouseModeEnum.Visible;   // gameplay input stops while the mouse is free
        _edit.Text = text; Refresh(); _edit.GrabFocus(); _edit.CaretColumn = text.Length;
    }
    void Close()
    {
        if (!_open) return; _open = false; _edit.ReleaseFocus(); _edit.Text = "";
        if (Ui.InGame && !Ui.AnyOpen && !GetTree().Paused) Input.MouseMode = Input.MouseModeEnum.Captured;
        Refresh();
    }

    public override void _Process(double delta)
    {
        Net.I?.AdminHelloTick();   // as a client: ask the host once per session whether we are an admin
        if (_open && (!Ui.InGame || Ui.AnyOpen)) Close();
        if (_open && Input.MouseMode != Input.MouseModeEnum.Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
        if (_open && !_edit.HasFocus()) _edit.GrabFocus();
        if (!_open && _showT > 0) { _showT -= (float)delta; if (_showT <= 0) Refresh(); }
    }
    public override void _PhysicsProcess(double delta) => Admin.Tick();

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        if (!_open)
        {
            if (k.Echo || !Ui.InGame || Ui.AnyOpen || GetTree().Paused || Player.Instance == null || TypingElsewhere(GetViewport())) return;
            bool slash = k.Keycode == Key.Slash || k.Unicode == '/' || k.Keycode == Key.KpDivide, tilde = k.Keycode == Key.Quoteleft || k.Keycode == Key.Asciitilde || k.Unicode == '~' || k.Unicode == '`';
            if (!slash && !tilde) return;
            GetViewport().SetInputAsHandled(); Open("/");
            return;
        }
        switch (k.Keycode)
        {
            case Key.Escape: GetViewport().SetInputAsHandled(); Close(); break;
            case Key.Tab: GetViewport().SetInputAsHandled(); Complete(); break;
            case Key.Up: GetViewport().SetInputAsHandled(); History(1); break;
            case Key.Down: GetViewport().SetInputAsHandled(); History(-1); break;
        }
    }

    // ---------------------------------------------------------------- completion and history
    void Complete()
    {
        // Tab again right after a completion: the next suggestion of the same list
        if (_tabAt >= 0 && _edit.Text == _tabText && _tabList.Count > 1) _tabAt = (_tabAt + 1) % _tabList.Count;
        else { _tabList = Commands.Suggest(_edit.Text); _tabAt = 0; }
        if (_tabList.Count == 0) { _tabAt = -1; return; }
        _tabText = _tabList[_tabAt].line; int keep = _tabAt;
        _edit.Text = _tabText; _edit.CaretColumn = _tabText.Length;   // setting Text raises no TextChanged, so the cycle survives
        _sugs = _tabList; _sug.Text = string.Join("\n", _tabList.Select((s, i) => (i == keep ? "> " : "   ") + s.label)); _sugBox.Visible = true;
    }
    List<(string line, string label)> _tabList = new();

    void History(int step)
    {
        if (_history.Count == 0) return;
        if (_histAt < 0) { if (step < 0) return; _draft = _edit.Text; }
        _histAt = Math.Clamp(_histAt + step, -1, _history.Count - 1);
        string t = _histAt < 0 ? _draft : _history[_history.Count - 1 - _histAt];
        _edit.Text = t; _edit.CaretColumn = t.Length; _tabAt = -1; Refresh();
    }
    void LoadHistory()
    {
        try
        {
            using var f = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Read); if (f == null) return;
            foreach (var l in f.GetAsText().Split('\n')) { string s = l.Trim(); if (s != "") _history.Add(s); }
            if (_history.Count > HistoryMax) _history.RemoveRange(0, _history.Count - HistoryMax);
        }
        catch { }
    }
    /// <summary>Only lines that ran as a command get here (never a login attempt or a mistyped word).</summary>
    void Remember(string line)
    {
        _history.Remove(line); _history.Add(line);
        if (_history.Count > HistoryMax) _history.RemoveRange(0, _history.Count - HistoryMax);
        try { using var f = FileAccess.Open(HistoryPath, FileAccess.ModeFlags.Write); f?.StoreString(string.Join("\n", _history) + "\n"); } catch { }
    }

    // ---------------------------------------------------------------- running a line
    void OnSubmit(string text)
    {
        string line = (text ?? "").Trim(); _edit.Text = ""; _histAt = -1; _tabAt = -1;
        if (line == "") { Close(); return; }
        var w = Commands.Words(line);
        if (w.Length > 0 && Commands.Find(w[0]) != null)
        {
            Remember(line); Print("> " + line);
            Commands.Submit(line, Print);
            Refresh(); return;
        }
        // not a command: the admin login, or a typing mistake. Either way the text is not shown, stored or logged.
        if (Admin.PasswordOk(line))
        {
            Admin.SetLocal(true);
            if (Net.IsClient && Game.Online) { Net.I!.AdminLogin(line); Print("Admin access is saved on this computer. Asking the online server for your account ..."); }
            else if (Net.IsClient) Print("Admin access granted on this computer. In a friend's world only the host is an admin.");
            else Print("Admin access granted. It is permanent on this computer (every hero). Try /help.");
        }
        else
        {
            string near = w.Length > 0 ? Commands.Near(w[0], Commands.Entries.Where(c => Commands.Allowed(c, CmdCtx.Local())).Select(c => c.Name)) : "";
            Print("unknown command" + (near != "" ? $"; did you mean /{near}?" : "") + " (try /help)");
        }
        Refresh();
    }
}
