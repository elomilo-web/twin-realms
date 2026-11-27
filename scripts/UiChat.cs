using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Godot;

namespace TwinRealms;

/// <summary>
/// The chat box (focus A-31), lower left. Shows the last lines (World, Local, Party, Whisper, System, each in its colour, data/social.json
/// chat.colors) and fades out when nothing was said for chat.fadeAfterSec; offline it only appears for system lines. Enter opens it for
/// typing (in a shared world, when no menu is up and nothing else is being typed in): the mouse is released like for the command bar,
/// which stops gameplay input. While open: tabs filter the channels, the channel button (or Tab) picks where plain text goes, the mouse
/// wheel and PageUp / PageDown scroll back, Up / Down recall what you typed, a click on a name offers whisper / invite / add friend /
/// trade / block, Esc closes. A line starting with "/" is a command (Commands registry; /w /r /p /l /g ...); anything else is chat.
/// The admin login line is NOT tried here: that stays with the command bar ("/" or "~").
/// This node also runs the small per-frame upkeep of the social systems (hello to the host, recent players, the trade countdown).
/// </summary>
public partial class ChatBox : CanvasLayer
{
    static ChatBox? _i;
    public static bool IsOpen => _i != null && _i._open;
    bool _open; int _rev = -1, _tab = -2, _histAt = -1; bool _stamps; float _alpha;
    Control _root = null!; VBoxContainer _col = null!; HBoxContainer _tabs = null!, _inRow = null!; PanelContainer _box = null!; RichTextLabel _text = null!; LineEdit _edit = null!; Button _chan = null!; PopupMenu _menu = null!;
    StyleBoxFlat _bg = null!; readonly List<Button> _tabBtns = new(); readonly List<string> _hist = new(); long _menuPeer; string _menuName = "";
    static readonly int[] TabIds = { -1, ChatServer.World, ChatServer.Local, ChatServer.Party, ChatServer.Whisper };
    static readonly string[] ColKeys = { "world", "local", "party", "whisper", "system" }, ColDef = { "#e8c26a", "#f2f2f2", "#6fe08a", "#e08adf", "#8fd3ff" };
    static string Hex(int ch) => SocialCfg.Hex(ColKeys[Math.Clamp(ch, 0, 4)], ColDef[Math.Clamp(ch, 0, 4)]);

    public override void _Ready()
    {
        _i = this; Layer = 55; ProcessMode = ProcessModeEnum.Always; Chat.Register();
        int fs = Math.Clamp(SocialCfg.I("chat.fontSize", 15), 10, 28), lines = Math.Clamp(SocialCfg.I("chat.shownLines", 9), 3, 30); float w = Mathf.Clamp(SocialCfg.F("chat.width", 470), 260, 1200);
        _root = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false }; _root.SetAnchorsPreset(Control.LayoutPreset.FullRect); AddChild(_root);
        _col = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        _col.AnchorLeft = 0; _col.AnchorRight = 0; _col.AnchorTop = 1; _col.AnchorBottom = 1; _col.OffsetLeft = 16; _col.OffsetRight = 16 + w;
        _col.OffsetBottom = -Mathf.Max(8, SocialCfg.F("chat.bottom", 150)); _col.OffsetTop = _col.OffsetBottom; _col.GrowVertical = Control.GrowDirection.Begin;
        _col.AddThemeConstantOverride("separation", 3); _root.AddChild(_col);
        StyleBoxFlat Box(float a) => new() { BgColor = new Color(.02f, .04f, .07f, a), ContentMarginLeft = 8, ContentMarginRight = 8, ContentMarginTop = 4, ContentMarginBottom = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4 };
        Button Small(string t, Action a)
        {
            var b = new Button { Text = t, FocusMode = Control.FocusModeEnum.None }; b.AddThemeFontSizeOverride("font_size", 13);
            b.AddThemeStyleboxOverride("normal", Box(.8f)); b.AddThemeStyleboxOverride("hover", Box(.95f)); b.AddThemeStyleboxOverride("pressed", Box(.95f));
            b.Pressed += a; return b;
        }
        // ---- tabs (only while typing)
        _tabs = new HBoxContainer { Visible = false }; _tabs.AddThemeConstantOverride("separation", 3); _col.AddChild(_tabs);
        string[] tabNames = { "All", "World", "Local", "Party", "Whisper" };
        for (int i = 0; i < TabIds.Length; i++) { int id = TabIds[i]; var b = Small(tabNames[i], () => { Chat.Tab = id; Refresh(); }); _tabs.AddChild(b); _tabBtns.Add(b); }
        _tabs.AddChild(new Control { CustomMinimumSize = new Vector2(10, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        _tabs.AddChild(Small("Time", () => { Chat.Stamps = !Chat.Stamps; Chat.SavePrefs(); Refresh(); }));
        _tabs.AddChild(Small("Friends", () => { Close(); Ui.I?.OpenFriends(); }));
        // ---- the lines
        _box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _bg = Box(.3f); _box.AddThemeStyleboxOverride("panel", _bg); _col.AddChild(_box);
        _text = new RichTextLabel { BbcodeEnabled = true, ScrollActive = true, ScrollFollowing = true, SelectionEnabled = false, MetaUnderlined = false, MouseFilter = Control.MouseFilterEnum.Ignore, CustomMinimumSize = new Vector2(0, lines * (fs + 6)), FocusMode = Control.FocusModeEnum.None };
        _text.AddThemeFontSizeOverride("normal_font_size", fs); _text.AddThemeColorOverride("default_color", UiTheme.Text);
        _text.AddThemeColorOverride("font_outline_color", Colors.Black); _text.AddThemeConstantOverride("outline_size", 4);
        _text.MetaClicked += OnMeta; _box.AddChild(_text);
        // ---- the input row (only while typing)
        _inRow = new HBoxContainer { Visible = false }; _inRow.AddThemeConstantOverride("separation", 4); _col.AddChild(_inRow);
        _chan = Small("World", CycleChannel); _chan.CustomMinimumSize = new Vector2(74, 32); _inRow.AddChild(_chan);
        _edit = new LineEdit { MaxLength = Math.Max(40, ChatServer.MaxLen + 40), PlaceholderText = "say something, or /w <player> text ... Enter sends, Esc closes", CustomMinimumSize = new Vector2(0, 32), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CaretBlink = true, ContextMenuEnabled = false };
        _edit.AddThemeFontSizeOverride("font_size", fs + 1); _edit.AddThemeColorOverride("font_color", UiTheme.Text);
        var eb = Box(.92f); eb.BorderColor = UiTheme.Accent; eb.BorderWidthLeft = eb.BorderWidthRight = eb.BorderWidthTop = eb.BorderWidthBottom = 1;
        _edit.AddThemeStyleboxOverride("normal", eb); _edit.AddThemeStyleboxOverride("focus", eb); _inRow.AddChild(_edit);
        _edit.TextSubmitted += OnSubmit;
        // ---- the menu behind a clicked name
        _menu = new PopupMenu(); _root.AddChild(_menu); _menu.IdPressed += OnMenu;
    }
    public override void _ExitTree() { if (_i == this) _i = null; }

    /// <summary>Opens the box with this text typed (the Whisper buttons of other windows).</summary>
    public static void OpenWith(string text) => Callable.From(() => { if (_i != null && IsInstanceValid(_i) && Ui.InGame && !Ui.AnyOpen) _i.Open(text); }).CallDeferred();

    // ---------------------------------------------------------------- drawing the lines
    static string Esc(string s) => s.Replace("[", "[lb]");
    void Refresh()
    {
        _rev = Chat.Rev; _tab = Chat.Tab; _stamps = Chat.Stamps;
        var sb = new StringBuilder(); string tc = SocialCfg.Hex("time", "#8a8f98"), nc = SocialCfg.Hex("name", "#ffffff"); bool first = true;
        foreach (var l in Chat.Lines)
        {
            if (Chat.Tab >= 0 && l.Ch != Chat.Tab && l.Ch != ChatServer.Sys) continue;
            if (!first) sb.Append('\n'); first = false;
            if (Chat.Stamps) sb.Append($"[color={tc}]{DateTimeOffset.FromUnixTimeSeconds((long)l.At).ToLocalTime():HH:mm}[/color] ");
            sb.Append($"[color={Hex(l.Ch)}]");
            if (l.Ch == ChatServer.Sys) sb.Append(l.Link != "" ? $"[url=c|{l.Link.Replace("[", "").Replace("]", "")}]{Esc(l.Text)}  [u](click)[/u][/url]" : Esc(l.Text));
            else
            {
                bool mine = l.Peer == Net.MyId; string name = Esc(l.From);
                string who = mine ? name : $"[url=p|{l.Peer}][color={nc}]{name}[/color][/url]";
                if (l.Ch == ChatServer.Whisper) sb.Append(l.To != "" ? $"To {Esc(l.To)}: " : $"From {who}: ");
                else sb.Append($"[{ChatServer.Names[l.Ch]}] {who}: ");
                sb.Append(Esc(l.Text));
            }
            sb.Append("[/color]");
        }
        _text.Text = sb.ToString();
        for (int i = 0; i < _tabBtns.Count; i++) _tabBtns[i].AddThemeColorOverride("font_color", TabIds[i] == Chat.Tab ? UiTheme.Highlight : TabIds[i] < 0 ? UiTheme.Text : Color.FromHtml(Hex(TabIds[i])));
        _chan.Text = ChatServer.Names[Math.Clamp(Chat.Channel, 0, 2)]; _chan.AddThemeColorOverride("font_color", Color.FromHtml(Hex(Chat.Channel)));
    }
    void CycleChannel() { Chat.Channel = (Math.Clamp(Chat.Channel, 0, 2) + 1) % 3; Chat.SavePrefs(); Refresh(); }

    // ---------------------------------------------------------------- names and links
    void OnMeta(Variant meta)
    {
        string m = meta.AsString();
        if (m.StartsWith("c|")) { Chat.Submit(m[2..]); return; }
        if (!m.StartsWith("p|") || !long.TryParse(m[2..], out long peer) || peer == Net.MyId) return;
        var net = Net.I; string name = Net.Active && net!.Players.TryGetValue(peer, out var pi) ? pi.Name : Chat.Lines.LastOrDefault(l => l.Peer == peer && l.Ch != ChatServer.Sys)?.From ?? "";
        if (name == "") return; bool here = Net.Active && net!.Players.ContainsKey(peer);
        _menuPeer = here ? peer : 0; _menuName = name;
        _menu.Clear(); _menu.AddItem(name, 99); _menu.SetItemDisabled(0, true);
        _menu.AddItem("Whisper", 0); _menu.AddItem("Invite to party", 1); _menu.AddItem("Trade", 2);
        _menu.AddItem(Friends.IsFriend(name) ? "Already a friend" : "Add friend", 3); _menu.AddItem(Friends.IsBlocked(name) ? "Unblock" : "Block (ignore)", 4);
        // (index 0 is the name; the entries follow in the order added)
        _menu.SetItemDisabled(1, !here); _menu.SetItemDisabled(2, !here || (net != null && net.SameParty(Net.MyId, peer))); _menu.SetItemDisabled(3, !here);
        _menu.SetItemDisabled(4, !Friends.ServerSide || Friends.IsFriend(name));
        var at = GetViewport().GetMousePosition(); _menu.Popup(new Rect2I((int)at.X, (int)at.Y - 150, 0, 0));
    }
    void OnMenu(long id)
    {
        string name = _menuName, quoted = name.Contains(' ') ? "\"" + name + "\"" : name; long peer = _menuPeer;
        switch (id)
        {
            case 0: Open("/w " + quoted + " "); break;
            case 1: if (peer != 0) { Net.I?.Invite(peer); Chat.System("Party invite sent to " + name); } break;
            case 2: if (peer != 0) Chat.System(Trade.Request(peer)); break;
            case 3: Chat.System(Friends.Add(name)); break;
            case 4: Chat.System(Friends.IsBlocked(name) ? Friends.Unblock(name) : Friends.Block(name)); break;
        }
    }

    // ---------------------------------------------------------------- open / close
    static bool TypingElsewhere(Viewport vp) => vp.GuiGetFocusOwner() is LineEdit or TextEdit or SpinBox;
    void Open(string text)
    {
        if (!_open)
        {
            _open = true; _histAt = -1; Input.MouseMode = Input.MouseModeEnum.Visible;   // gameplay input stops while the mouse is free
            _tabs.Visible = true; _inRow.Visible = true; _text.MouseFilter = Control.MouseFilterEnum.Stop; _bg.BgColor = new Color(.02f, .04f, .07f, .78f);
            Refresh();
        }
        _edit.Text = text; _edit.GrabFocus(); _edit.CaretColumn = text.Length;
    }
    void Close()
    {
        if (!_open) return; _open = false; _edit.ReleaseFocus(); _edit.Text = ""; _menu.Hide();
        _tabs.Visible = false; _inRow.Visible = false; _text.MouseFilter = Control.MouseFilterEnum.Ignore; _bg.BgColor = new Color(.02f, .04f, .07f, .3f);
        _text.ScrollToLine(Math.Max(0, _text.GetLineCount() - 1));
        if (Ui.InGame && !Ui.AnyOpen && !GetTree().Paused && !CommandBar.IsOpen) Input.MouseMode = Input.MouseModeEnum.Captured;
        Chat.LastAt = Time.GetTicksMsec() / 1000.0;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;
        Net.I?.SocialHelloTick(); Friends.Tick(dt); Trade.Tick(dt);
        if (_open && (!Ui.InGame || Ui.AnyOpen || CommandBar.IsOpen)) Close();
        if (_open && Input.MouseMode != Input.MouseModeEnum.Visible) Input.MouseMode = Input.MouseModeEnum.Visible;
        if (_open && !_edit.HasFocus() && !_menu.Visible) _edit.GrabFocus();
        if (_rev != Chat.Rev || _tab != Chat.Tab || _stamps != Chat.Stamps) Refresh();
        // shown while typing, and for a while after the last line; offline only system lines ever arrive, so it stays hidden
        float want = 0;
        if (Ui.InGame && !Ui.AnyOpen && Chat.Lines.Count + (_open ? 1 : 0) > 0)
        {
            double idle = Time.GetTicksMsec() / 1000.0 - Chat.LastAt;
            want = _open ? 1 : idle < SocialCfg.F("chat.fadeAfterSec", 10) ? 1 : Mathf.Clamp(SocialCfg.F("chat.idleAlpha", 0), 0, 1);
        }
        _alpha = Mathf.MoveToward(_alpha, want, dt * (want > _alpha ? 6f : 1.2f));
        _root.Visible = _alpha > .01f; _root.Modulate = new Color(1, 1, 1, _alpha);
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true } k) return;
        if (!_open)
        {
            if (k.Echo || (k.Keycode != Key.Enter && k.Keycode != Key.KpEnter)) return;
            if (!Ui.InGame || Ui.AnyOpen || GetTree().Paused || Player.Instance == null || CommandBar.IsOpen || TypingElsewhere(GetViewport()) || !Net.Active) return;
            GetViewport().SetInputAsHandled(); Open("");
            return;
        }
        switch (k.Keycode)
        {
            case Key.Escape: GetViewport().SetInputAsHandled(); Close(); break;
            case Key.Tab: GetViewport().SetInputAsHandled(); CycleChannel(); break;
            case Key.Up: GetViewport().SetInputAsHandled(); History(1); break;
            case Key.Down: GetViewport().SetInputAsHandled(); History(-1); break;
            case Key.Pageup: GetViewport().SetInputAsHandled(); Page(-1); break;
            case Key.Pagedown: GetViewport().SetInputAsHandled(); Page(1); break;
        }
    }
    void Page(int dir) { var bar = _text.GetVScrollBar(); bar.Value = Math.Clamp(bar.Value + dir * _text.Size.Y * .8, bar.MinValue, bar.MaxValue); }
    void History(int step)
    {
        if (_hist.Count == 0) return;
        _histAt = Math.Clamp(_histAt + step, -1, _hist.Count - 1);
        string t = _histAt < 0 ? "" : _hist[_hist.Count - 1 - _histAt]; _edit.Text = t; _edit.CaretColumn = t.Length;
    }
    void OnSubmit(string text)
    {
        string line = (text ?? "").Trim();
        if (line != "") { _hist.Remove(line); _hist.Add(line); if (_hist.Count > 40) _hist.RemoveAt(0); Chat.Submit(line); }
        Close();
    }
}

/// <summary>Local chat above the speaker's head for a few seconds (chat.bubbleSec): a billboard label on their hero (ours, or a RemoteHero).</summary>
public partial class ChatBubble : Label3D
{
    float _t;
    public static void Say(long peer, string text)
    {
        Node3D? host = peer == Net.MyId ? Player.Instance : Net.I?.HeroOf(peer);
        if (host == null || !IsInstanceValid(host) || !host.IsInsideTree()) return;
        var b = host.GetNodeOrNull<ChatBubble>("ChatBubble");
        if (b == null)
        {
            b = new ChatBubble
            {
                Name = "ChatBubble", FontSize = 26, OutlineSize = 8, PixelSize = .005f, Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, Position = new Vector3(0, 2.8f, 0),
                Width = 520, AutowrapMode = TextServer.AutowrapMode.WordSmart, VerticalAlignment = VerticalAlignment.Bottom, VisibilityRangeEnd = SocialCfg.F("chat.localRadius", 40) + 10,
            };
            host.AddChild(b);
        }
        int max = Math.Max(10, SocialCfg.I("chat.bubbleChars", 90)); if (text.Length > max) text = text[..max] + "...";
        b.Text = text; b._t = Mathf.Max(1, SocialCfg.F("chat.bubbleSec", 5)); b.Modulate = SocialCfg.C("local", "#f2f2f2");
    }
    public override void _Process(double delta)
    {
        _t -= (float)delta; if (_t <= 0) { QueueFree(); return; }
        if (_t < .6f) Modulate = Modulate with { A = _t / .6f };
    }
}
