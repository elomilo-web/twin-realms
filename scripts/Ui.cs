using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace TwinRealms;

/// <summary>All menus and overlays, built in code: notes, banners, dialogue, shop, smith, pack, character, skill tree, journal, map, settings, pause and title.</summary>
public partial class Ui : CanvasLayer
{
    public static Ui? I;
    public static bool AnyOpen => I != null && I._panel != null;
    public string PanelName => _panelName;
    public static Action<string, string, string>? OnNew; public static Action? OnContinue;
    public static bool InGame;

    VBoxContainer _notes = null!; Label _banner = null!, _bannerSub = null!; ColorRect _flash = null!;
    Control? _panel; string _panelName = ""; Action? _rebuild;
    // "Gold" is the menu highlight colour (now the system-window cyan); names kept so existing call sites read the same
    public static readonly Color Gold = UiTheme.Accent, Dim = UiTheme.Dim, PanelBg = UiTheme.Panel, Line = UiTheme.AccentSoft, Panel2 = UiTheme.Button, Fg = UiTheme.Text;
    static Theme? _theme;
    static Theme MakeTheme()
    {
        if (_theme != null) return _theme; var t = new Theme();
        var B = UiTheme.Box;
        t.SetStylebox("normal", "Button", B(UiTheme.Button, UiTheme.AccentSoft, 1, 14, 5, 0)); t.SetStylebox("hover", "Button", B(UiTheme.ButtonHover, UiTheme.Accent, 1, 14, 5, 6));
        t.SetStylebox("pressed", "Button", B(UiTheme.ButtonPressed, UiTheme.ButtonPressed, 1, 14, 5, 8)); t.SetStylebox("focus", "Button", PadFocusBox()); t.SetStylebox("focus", "CheckButton", PadFocusBox()); t.SetStylebox("focus", "HSlider", PadFocusBox());   // gold outline: only controller navigation gives buttons focus (UiPad.cs)
        t.SetStylebox("disabled", "Button", B(UiTheme.Button with { A = .4f }, UiTheme.AccentSoft with { A = .2f }, 1, 14, 5, 0));
        t.SetColor("font_color", "Button", UiTheme.Text); t.SetColor("font_hover_color", "Button", Colors.White); t.SetColor("font_pressed_color", "Button", new Color("04111f")); t.SetColor("font_color", "Label", UiTheme.Text);
        t.SetStylebox("normal", "LineEdit", B(new Color(0.01f, 0.05f, 0.1f, 0.85f), UiTheme.AccentSoft, 1, 10, 4, 0)); t.SetStylebox("focus", "LineEdit", B(new Color(0.01f, 0.05f, 0.1f, 0.85f), UiTheme.Accent, 1, 10, 4, 6)); t.SetColor("font_color", "LineEdit", UiTheme.Text);
        // thin cyan scrollbars and sliders
        t.SetStylebox("scroll", "VScrollBar", B(new Color(0, 0, 0, .25f), new Color(0, 0, 0, 0), 0, 3, 0, 0)); t.SetStylebox("grabber", "VScrollBar", B(UiTheme.AccentSoft, UiTheme.AccentSoft, 0, 3, 0, 0));
        t.SetStylebox("grabber_highlight", "VScrollBar", B(UiTheme.Accent, UiTheme.Accent, 0, 3, 0, 0)); t.SetStylebox("grabber_pressed", "VScrollBar", B(UiTheme.Accent, UiTheme.Accent, 0, 3, 0, 0));
        t.SetStylebox("slider", "HSlider", B(new Color(0, 0, 0, .4f), UiTheme.AccentSoft, 1, 0, 2, 0)); t.SetStylebox("grabber_area", "HSlider", B(UiTheme.AccentSoft, UiTheme.AccentSoft, 0, 0, 2, 0)); t.SetStylebox("grabber_area_highlight", "HSlider", B(UiTheme.Accent, UiTheme.Accent, 0, 0, 2, 0));
        t.SetColor("font_color", "CheckBox", UiTheme.Text); t.SetColor("font_hover_color", "CheckBox", Colors.White);
        t.SetFontSize("font_size", "Button", 16); t.SetFontSize("font_size", "Label", 16); _theme = t; return t;
    }

    // ---------------------------------------------------------------- static helpers (safe before the UI exists)
    public static void Note(string text, Color? col = null) { Flag.Event("note: " + text); I?.AddNote(text, col ?? Colors.White); }
    public static void Banner(string title, string sub = "") => I?.ShowBanner(title, sub);
    public static void Flash(Color c) => I?.DoFlash(c);

    public override void _Ready()
    {
        I = this; Layer = 20; ProcessMode = ProcessModeEnum.Always;
        _notes = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _notes.Position = new Vector2(22, 120); _notes.CustomMinimumSize = new Vector2(420, 0); _notes.AddThemeConstantOverride("separation", 3); AddChild(_notes);
        _banner = Lbl("", 52, UiTheme.Title); _banner.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.1f, 0.2f)); _banner.AddThemeConstantOverride("outline_size", 10); _banner.HorizontalAlignment = HorizontalAlignment.Center; _banner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _banner.OffsetTop = 130; _banner.OffsetLeft = -400; _banner.OffsetRight = 400; _banner.Modulate = new Color(1, 1, 1, 0);
        _bannerSub = Lbl("", 20, UiTheme.Accent); _bannerSub.HorizontalAlignment = HorizontalAlignment.Center; _bannerSub.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _bannerSub.OffsetTop = 200; _bannerSub.OffsetLeft = -400; _bannerSub.OffsetRight = 400; _bannerSub.Modulate = new Color(1, 1, 1, 0);
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore }; _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_flash);
        Game.Changed += OnChanged; Net.OnlineChanged += OnOnlineChanged;
    }
    void OnChanged() { if (_rebuild != null && _panel != null) _rebuild(); }
    public override void _ExitTree() { Game.Changed -= OnChanged; Net.OnlineChanged -= OnOnlineChanged; if (I == this) I = null; }

    public override void _Process(double delta) { Feel.Tick(); UpdateTick(); PadTick(delta); }   // PadTick: controller focus upkeep (UiPad.cs) // Ui always processes, so hit-stop always ends; UpdateTick: download progress (Updater.cs)

    Label Lbl(string t, int size, Color? c = null, Node? parent = null)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c ?? Colors.White); l.AddThemeColorOverride("font_outline_color", Colors.Black); l.AddThemeConstantOverride("outline_size", 5);
        (parent ?? this).AddChild(l); return l;
    }

    void AddNote(string t, Color c)
    {
        var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pc.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.04f, .035f, .03f, .8f), BorderColor = c, BorderWidthLeft = 3, CornerRadiusTopRight = 6, CornerRadiusBottomRight = 6, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 3, ContentMarginBottom = 3 });
        var l = Lbl(t, 16, c, pc); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(300, 0);
        while (_notes.GetChildCount() >= 6) { var old = _notes.GetChild(0); _notes.RemoveChild(old); old.QueueFree(); }
        _notes.AddChild(pc);
        var tw = pc.CreateTween(); tw.TweenInterval(3.2); tw.TweenProperty(pc, "modulate:a", 0f, 0.8); tw.TweenCallback(Callable.From(pc.QueueFree));
    }
    void ShowBanner(string t, string s)
    {
        string spaced = string.Join(" ", t.ToUpper().Select(ch => ch == ' ' ? "  " : ch.ToString()));
        _banner.Text = spaced; _bannerSub.Text = s.ToUpper(); _banner.AddThemeColorOverride("font_color", UiTheme.Accent.Lerp(new Color(.85f, .68f, .3f), .5f)); _bannerSub.AddThemeColorOverride("font_color", Parch);
        if (_bannerRules == null)
        {
            _bannerRules = new Control[2];
            for (int i = 0; i < 2; i++) { var r = new ColorRect { Color = new Color(.8f, .62f, .26f, .8f), MouseFilter = Control.MouseFilterEnum.Ignore }; r.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); AddChild(r); _bannerRules[i] = r; }
        }
        float w = ThemeDB.FallbackFont.GetStringSize(spaced, HorizontalAlignment.Left, -1, 52).X / 2 + 24; float y = 130 + 34;
        _bannerRules[0].OffsetLeft = -w - 160; _bannerRules[0].OffsetRight = -w; _bannerRules[1].OffsetLeft = w; _bannerRules[1].OffsetRight = w + 160;
        foreach (var r in _bannerRules) { r.OffsetTop = y; r.OffsetBottom = y + 1; }
        _bannerTw?.Kill(); // a new banner restarts the fade instead of fighting the old one
        var clear = new Color(1, 1, 1, 0); _banner.Modulate = clear; _bannerSub.Modulate = clear; foreach (var r in _bannerRules) r.Modulate = clear;
        var tw = _bannerTw = CreateTween().SetParallel();
        foreach (CanvasItem ci in new CanvasItem[] { _banner, _bannerSub, _bannerRules[0], _bannerRules[1] }) { tw.TweenProperty(ci, "modulate:a", 1f, 0.4); tw.TweenProperty(ci, "modulate:a", 0f, 1.2).SetDelay(2.4); }
    }
    Tween? _bannerTw; Control[]? _bannerRules;
    public static void ClearBanner() { if (I == null) return; I._bannerTw?.Kill(); I._banner.Modulate = new Color(1, 1, 1, 0); I._bannerSub.Modulate = new Color(1, 1, 1, 0); if (I._bannerRules != null) foreach (var r in I._bannerRules) r.Modulate = new Color(1, 1, 1, 0); }
    void DoFlash(Color c) { _flash.Color = new Color(c, 0.85f); CreateTween().TweenProperty(_flash, "color:a", 0f, 0.7); }

    // ---------------------------------------------------------------- input
    public override void _UnhandledInput(InputEvent e)
    {
        if (PadMenuInput(e)) { GetViewport().SetInputAsHandled(); return; }   // B backs out, LB / RB switch settings tabs (UiPad.cs)
        if (!InGame) return;
        if (e.IsActionPressed(Inputs.Pause)) { if (_panel != null) Close(); else OpenPause(); GetViewport().SetInputAsHandled(); return; }
        if (e.IsActionPressed(Inputs.Pack)) { Toggle("pack", OpenPack); } else if (e.IsActionPressed(Inputs.Journal)) Toggle("journal", OpenJournal);
        else if (e.IsActionPressed(Inputs.Map)) Toggle("map", OpenMap); else if (e.IsActionPressed(Inputs.Tree)) Toggle("tree", OpenTree);
        else if (e.IsActionPressed(Inputs.SkillsUi)) Toggle("skills", OpenSkills);
        else if (e.IsActionPressed(Inputs.AttacksUi)) Toggle("attacks", () => OpenAttacks(null));
        else if (e.IsActionPressed(Inputs.ModsUi)) Toggle("mods", OpenMods);
        else if (e.IsActionPressed(Inputs.SpellcraftUi)) Toggle("spellcraft", () => OpenSpellcraft(null));
        else if (e.IsActionPressed(Inputs.PetsUi)) Toggle("pets", () => OpenPets(null));
    }
    void Toggle(string name, Action open) { if (_panelName == name) Close(); else if (_panel == null && Player.Instance != null && !Player.Instance.Dead) open(); }

    // ---------------------------------------------------------------- panel frame
    /// <summary>Slim left column over a darkened, desaturated live world (pause menu). Returns the column's content box.</summary>
    VBoxContainer FrameSlim(string name, Action rebuild)
    {
        _scrapMode = false; RememberFocus(); CloseNoUnpause(); _panelName = name; _rebuild = rebuild; Sfx.UiPanel(name); Callable.From(PadFocus).CallDeferred(); _padTabs.Clear();
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = MakeTheme() }; root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var sh = new Shader { Code = "shader_type canvas_item;\nuniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;\nvoid fragment() {\n vec3 c = textureLod(screen_tex, SCREEN_UV, 0.0).rgb;\n float g = dot(c, vec3(0.299, 0.587, 0.114));\n c = mix(c, vec3(g), 0.6) * 0.65;\n COLOR = vec4(c, 1.0);\n}\n" };
        var dim = new ColorRect { Color = Colors.White, Material = new ShaderMaterial { Shader = sh }, MouseFilter = Control.MouseFilterEnum.Ignore }; dim.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(dim);
        const float W = 360;
        var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop, AnchorBottom = 1, OffsetRight = W };
        pc.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.04f, .035f, .03f, .78f), BorderColor = UiTheme.AccentSoft, BorderWidthRight = 1, ContentMarginLeft = 34, ContentMarginRight = 26, ContentMarginTop = 28, ContentMarginBottom = 26 });
        root.AddChild(pc);
        // soft right-edge fade
        var gr = new Gradient { Colors = new[] { new Color(.04f, .035f, .03f, .5f), new Color(.04f, .035f, .03f, 0f) } };
        var fade = new TextureRect { Texture = new GradientTexture2D { Gradient = gr, FillFrom = new Vector2(0, 0), FillTo = new Vector2(1, 0), Width = 64, Height = 4 }, StretchMode = TextureRect.StretchModeEnum.Scale, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, MouseFilter = Control.MouseFilterEnum.Ignore, AnchorBottom = 1, OffsetLeft = W, OffsetRight = W + 64 };
        root.AddChild(fade);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 2); pc.AddChild(v);
        AddChild(root); _panel = root;
        root.Modulate = new Color(1, 1, 1, 0); root.CreateTween().TweenProperty(root, "modulate:a", 1f, UiTheme.OpenTime);
        GetTree().Paused = !Net.Active; Input.MouseMode = Input.MouseModeEnum.Visible;
        return v;
    }

    VBoxContainer Frame(string name, string title, Vector2 size, Action rebuild)
    {
        if (name != "pack") _scrapMode = false;   // Disassemble stays on only while you stay in the pack
        RememberFocus(); CloseNoUnpause(); _panelName = name; _rebuild = rebuild; Sfx.UiPanel(name); Callable.From(PadFocus).CallDeferred(); _padTabs.Clear();   // controller: focus the first button (or the one focused before a rebuild); LB / RB tab rows register again
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = MakeTheme() }; root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        bool create = name == "title" && title.StartsWith("NEW HERO"), cine = name == "dialog";
        bool left = name == "title" && (title == "TWIN REALMS" || create);   // title screen: narrow translucent panel on the left, 3D backdrop visible on the right
        if (name == "title" && !left) root.AddChild(new ColorRect { Color = new Color(.005f, .02f, .05f, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        if (!left && !cine) { var bd = new TechBackdrop(); bd.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(bd); }
        var pc = new TechPanel(); var ps = UiTheme.Box(left ? UiTheme.Panel with { A = 0.72f } : UiTheme.Panel, UiTheme.AccentSoft, 1, 22, 14, UiTheme.GlowSize); pc.AddThemeStyleboxOverride("panel", ps);
        var vp = GetViewport().GetVisibleRect().Size; // never ask for more than the window has
        if (left)
        {
            pc.CustomMinimumSize = new Vector2(Mathf.Min(create ? 460 : 620, vp.X - 80), 0);
            pc.AnchorLeft = 0; pc.AnchorRight = 0; pc.AnchorTop = 0; pc.AnchorBottom = 1;
            pc.OffsetLeft = 40; pc.OffsetRight = 40 + pc.CustomMinimumSize.X; pc.OffsetTop = 24; pc.OffsetBottom = -24;
            root.AddChild(pc);
        }
        else if (cine)
        {
            // cinematic dialogue: dark parchment box bottom-centre, world visible above
            ps.BgColor = new Color(.04f, .035f, .03f, .82f);
            Vector2 CineSize() { var s = GetViewport().GetVisibleRect().Size; return new Vector2(Mathf.Min(900, s.X - 48), Mathf.Min(220, s.Y - 48)); }
            void Place() { var cs = CineSize(); pc.CustomMinimumSize = cs; pc.AnchorLeft = .5f; pc.AnchorRight = .5f; pc.AnchorTop = 1; pc.AnchorBottom = 1; pc.OffsetLeft = -cs.X / 2; pc.OffsetRight = cs.X / 2; pc.OffsetTop = -cs.Y - 36; pc.OffsetBottom = -36; pc.GrowVertical = Control.GrowDirection.Begin; }
            Place(); root.AddChild(pc);
            Action onSz = Place; GetViewport().SizeChanged += onSz; root.TreeExiting += () => GetViewport().SizeChanged -= onSz;
        }
        else
        {
            // clamp to the visible viewport minus a 24 px margin each side, centred; re-clamp on resize
            void Clamp() { var s = GetViewport().GetVisibleRect().Size; var m = new Vector2(Mathf.Min(size.X, s.X - 48), Mathf.Min(size.Y, s.Y - 48)); pc.CustomMinimumSize = m; pc.AnchorLeft = .5f; pc.AnchorRight = .5f; pc.AnchorTop = .5f; pc.AnchorBottom = .5f; pc.OffsetLeft = -m.X / 2; pc.OffsetRight = m.X / 2; pc.OffsetTop = -m.Y / 2; pc.OffsetBottom = m.Y / 2; }   // centred: offsets are half the size each way
            Clamp(); pc.GrowHorizontal = Control.GrowDirection.Both; pc.GrowVertical = Control.GrowDirection.Both; root.AddChild(pc);
            Action onSz = Clamp; GetViewport().SizeChanged += onSz; root.TreeExiting += () => GetViewport().SizeChanged -= onSz;
            // content wider/taller than the requested size (e.g. the pack's tab row) makes the panel grow one way from stale offsets: re-centre on the real size
            void Recentre() { if (!GodotObject.IsInstanceValid(pc)) return; var sz = pc.Size; float l = -sz.X / 2, t2 = -sz.Y / 2; if (Mathf.Abs(pc.OffsetLeft - l) > .5f || Mathf.Abs(pc.OffsetTop - t2) > .5f) { pc.OffsetLeft = l; pc.OffsetRight = l + sz.X; pc.OffsetTop = t2; pc.OffsetBottom = t2 + sz.Y; } }
            pc.Resized += () => Callable.From(Recentre).CallDeferred();
        }
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); pc.AddChild(v);
        var hb = new HBoxContainer(); v.AddChild(hb); var t = Lbl(title.ToUpper(), 24, UiTheme.Title, hb); t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        t.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.12f, 0.25f)); t.AddThemeConstantOverride("outline_size", 6);
        if (left)
        {
            t.AddThemeFontSizeOverride("font_size", 44); t.AddThemeColorOverride("font_color", new Color(.78f, .62f, .30f)); t.AddThemeColorOverride("font_outline_color", new Color(.08f, .04f, .02f));
            t.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 6 });
        }
        if (create) t.AddThemeFontSizeOverride("font_size", 26);
        if (cine) { t.AddThemeFontSizeOverride("font_size", 20); t.AddThemeColorOverride("font_color", Gold); t.AddThemeColorOverride("font_outline_color", new Color(.08f, .04f, .02f)); t.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 3 }); }
        if (name != "title") { var cb = Btn("X", Close); cb.CustomMinimumSize = new Vector2(38, 34); hb.AddChild(cb); }
        // decoration overlay sized to the panel; the header rule sits just under the title row
        pc.Resized += () => { pc.HeaderY = 14 + hb.Size.Y + 5; pc.PivotOffset = pc.Size / 2; pc.QueueRedraw(); };
        AddChild(root); _panel = root;
        // system-window pop: fade in and grow from the centre
        root.Modulate = new Color(1, 1, 1, 0); pc.PivotOffset = pc.Size / 2; pc.Scale = new Vector2(1f, 0.92f);
        var tw = root.CreateTween().SetParallel(); tw.TweenProperty(root, "modulate:a", 1f, UiTheme.OpenTime); tw.TweenProperty(pc, "scale", Vector2.One, UiTheme.OpenTime).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);        if (name != "title") { GetTree().Paused = !Net.Active; /* a shared world never pauses */ Input.MouseMode = Input.MouseModeEnum.Visible; }
        if (left || cine) return v;
        // RULE (user, 2026-11-25): a window never stretches. CustomMinimumSize is only a minimum and a PanelContainer grows to its
        // content, so long skill lists / descriptions pushed windows off screen. The body lives in a ScrollContainer (its min size
        // does not follow its content): the window stays at the requested size (clamped to the screen) and extra content scrolls.
        // The title row stays pinned above it. Rebuilds of the same window (after a click) keep the scroll position.
        var sc = new ScrollContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Auto, VerticalScrollMode = ScrollContainer.ScrollMode.Auto, FollowFocus = true };
        v.AddChild(sc); _frameSc = sc;
        var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        body.AddThemeConstantOverride("separation", 8); sc.AddChild(body);
        if (_frameScrollName == name && _frameScroll > 0) { int keep = _frameScroll; Callable.From(() => { if (GodotObject.IsInstanceValid(sc)) sc.ScrollVertical = keep; }).CallDeferred(); }
        _frameScrollName = ""; _frameScroll = 0;
        return body;
    }
    ScrollContainer? _frameSc; string _frameScrollName = ""; int _frameScroll;
    // leaving the menus turns Disassemble back off
    public void Close() { Sfx.UiClosed(); _confirmQuit = false; _confirmUnstuck = false; _scrapMode = false; CloseNoUnpause(); if (InGame) { GetTree().Paused = false; Input.MouseMode = Input.MouseModeEnum.Captured; } }
    void CloseNoUnpause() { HideTip(); if (_frameSc != null && GodotObject.IsInstanceValid(_frameSc)) { _frameScrollName = _panelName; _frameScroll = _frameSc.ScrollVertical; } _frameSc = null; if (_panel != null) { _panel.QueueFree(); _panel = null; _panelName = ""; _rebuild = null; } }

    static Button Btn(string t, Action a, bool small = false)
    {
        var b = new Button { Text = t, FocusMode = Control.FocusModeEnum.None }; if (small) b.AddThemeFontSizeOverride("font_size", 14);
        b.MouseEntered += Sfx.UiHover; b.Pressed += () => { Sfx.Play("click"); a(); }; return b;
    }
    Label Row(Node parent, string t, Color? c = null, int size = 16) { var l = Lbl(t, size, c ?? Colors.White, parent); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; return l; }
    ScrollContainer Scroll(Node parent, float h) { var s = new ScrollContainer { CustomMinimumSize = new Vector2(0, h), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; parent.AddChild(s); var v = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; s.AddChild(v); return s; }
    static VBoxContainer Inner(ScrollContainer s) => (VBoxContainer)s.GetChild(0);

    // ---------------------------------------------------------------- dialogue
    public static void OpenDialogue(Npc n) => I?.Talk(n);
    void Talk(Npc n)
    {
        if (n.Role == "shop") { OpenShop("buy"); return; }
        if (n.Role == "smith") { OpenSmith(); return; }
        if (n.Role == "valor") { OpenValor("gacha"); return; }
        var v = Frame("dialog", n.NpcName, new Vector2(900, 220), () => { });
        var d = Game.D; int k = 0;
        Row(v, n.Role == "arena" ? $"Welcome to the Crimson Arena. Best wave: {d.BestWave}. Best duel streak: {d.BestDuel}. Waves pay valor (first at wave {Valor.NextMilestone(0)}), spent with Sera in Oakhaven; leave by the gate whenever you like." : n.NextLine(), Parch, 18);
        foreach (var id in n.QuestIds)
        {
            var q = Quests.ById(id); if (q == null) continue; var s = Game.Q(id);
            if (s.S == 2) DlgOpt(v, ++k, "Report: " + q.Name, () => Say(n, q.Done, ("Claim reward", () => { Quests.Reward(q); Talk(n); })));
            else if (s.S == 1) DlgOpt(v, ++k, q.Name + " (in progress)", () => Say(n, q.Goal + (q.Need > 1 ? $" ({s.N}/{q.Need})" : "") + ".", ("Back", () => Talk(n))));
            else if (Quests.Available(q)) DlgOpt(v, ++k, "Quest: " + q.Name + (q.Lvl > 0 ? $" (lvl {q.Lvl}+)" : ""), () => Say(n, q.Offer, ("Accept", () => { Quests.Accept(q); Close(); }), ("Not now", () => Talk(n))));
        }
        if (n.Role == "arena")
        {
            DlgOpt(v, ++k, "Wave Survival", () => { Close(); Arenas.Start("waves"); });
            DlgOpt(v, ++k, "Shadow Duel (fight your own double)", () => { Close(); Arenas.Start("duel"); });
            if (!Arenas.Active) DlgOpt(v, ++k, $"Soul Duels (your pets against the Duelists of the Pit; next: {DuelDb.RankTitle(d.DuelRank)})", () => { if (d.Pets.Count == 0) Say(n, "No bound souls? Catch a monster in a Soul Cube first, then come back.", ("Back", () => Talk(n))); else OpenSoulTeam(); });
        }
        if (n.Role == "pettrader")
        {
            DlgOpt(v, ++k, "Trade a pet", () => { PetTrade.StartNpc(); OpenPetTrade(); });
        }
        DlgOpt(v, ++k, "Farewell", Close);
    }
    void Say(Npc n, string text, params (string, Action)[] opts)
    {
        var v = Frame("dialog", n.NpcName, new Vector2(900, 220), () => { }); Row(v, text, Parch, 18); int k = 0;
        foreach (var (l, a) in opts) DlgOpt(v, ++k, l, a);
    }
    /// <summary>Numbered dialogue response: text row that gets a gold marker and gold text on hover.</summary>
    void DlgOpt(Node parent, int num, string text, Action a)
    {
        var b = new Button { Text = $"{num}.  {text}", Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", 17);
        foreach (var s in new[] { "normal", "hover", "pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(s, new StyleBoxEmpty());
        b.AddThemeColorOverride("font_color", Parch with { A = .85f }); b.AddThemeColorOverride("font_hover_color", Gold); b.AddThemeColorOverride("font_pressed_color", Gold);
        string plain = b.Text;
        b.MouseEntered += () => b.Text = "▸ " + plain; b.MouseExited += () => b.Text = plain;
        b.Pressed += () => { Sfx.Play("click"); a(); };
        parent.AddChild(b);
    }
    /// <summary>Flat text row with a gold marker when selected (character creation lists).</summary>
    Button PickRow(Node parent, string text, bool sel, Action a, int size = 18)
    {
        var b = new Button { Text = (sel ? "◆  " : "     ") + text, Flat = true, Alignment = HorizontalAlignment.Left, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        b.AddThemeFontSizeOverride("font_size", size);
        foreach (var s in new[] { "normal", "hover", "pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(s, new StyleBoxEmpty());
        var c = sel ? new Color(1f, .96f, .82f) : Parch with { A = .6f };
        b.AddThemeColorOverride("font_color", c); b.AddThemeColorOverride("font_hover_color", sel ? c : Gold); b.AddThemeColorOverride("font_pressed_color", Gold);
        if (sel) b.AddThemeColorOverride("icon_normal_color", Gold);
        b.Pressed += () => { Sfx.Play("click"); a(); };
        parent.AddChild(b); return b;
    }

    /// <summary>An item as a shop row: its icon in a rarity frame, its name and stats, a button; hovering compares it with what you wear.</summary>
    void ItemRow(Node parent, Item it, string btn, Action? a, bool ok = true)
    {
        var d = Game.D; var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(parent).AddChild(h);
        var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(46, 46) }; h.AddChild(cell);
        cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
        var l = Lbl(it.Describe(), 16, it.Color, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.VerticalAlignment = VerticalAlignment.Center;
        if (a != null) { var b = Btn(btn, () => { HideTip(); a(); }, true); PriceStyle(b, ok); h.AddChild(b); }
    }
    /// <summary>Gold-underlined text tabs; the current one is gold with an underline.</summary>
    void Tabs(Node parent, string cur, params (string id, string label, Action open)[] tabs)
    {
        PadTabs(tabs.Select(t => (t.open, t.id == cur)));   // LB / RB switch them (UiPad.cs)
        foreach (var (id, label, open) in tabs)
        {
            var b = Btn(label, open); bool on = id == cur; b.AddThemeFontSizeOverride("font_size", 18);
            StyleBoxFlat Sb(Color ul, int w) => new() { BgColor = new Color(0, 0, 0, 0), BorderColor = ul, BorderWidthBottom = w, ContentMarginLeft = 12, ContentMarginRight = 12, ContentMarginTop = 4, ContentMarginBottom = 4 };
            var line = on ? Sb(Gold, 2) : Sb(new Color(0, 0, 0, 0), 2);
            foreach (var s in new[] { "normal", "pressed", "focus", "disabled" }) b.AddThemeStyleboxOverride(s, line);
            b.AddThemeStyleboxOverride("hover", Sb(Gold with { A = .5f }, 2));
            var col = on ? Gold : Parch; b.AddThemeColorOverride("font_color", col); b.AddThemeColorOverride("font_pressed_color", col); b.AddThemeColorOverride("font_hover_color", Gold.Lightened(.2f));
            parent.AddChild(b);
        }
    }

    static readonly Color Muted = new(.72f, .34f, .30f), Parch = new(.89f, .83f, .70f);
    /// <summary>Framed list row / card with a hover highlight. Returns the box to fill.</summary>
    PanelContainer CardBox(Node parent, bool selected = false)
    {
        var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Pass, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        StyleBoxFlat Sb(float a, Color b) => new() { BgColor = new Color(.05f, .045f, .04f, a), BorderColor = b, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, BorderWidthBottom = 1, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 6, ContentMarginBottom = 6, CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2 };
        var norm = Sb(.7f, selected ? Gold : Line with { A = .5f }); var hov = Sb(.9f, Gold);
        pc.AddThemeStyleboxOverride("panel", norm);
        pc.MouseEntered += () => pc.AddThemeStyleboxOverride("panel", hov); pc.MouseExited += () => pc.AddThemeStyleboxOverride("panel", norm);
        parent.AddChild(pc); return pc;
    }
    /// <summary>Italic-ish dim hint shown instead of blank space.</summary>
    void Empty(Node parent, string t) { var l = Row(parent, t, Dim, 15); l.HorizontalAlignment = HorizontalAlignment.Center; l.CustomMinimumSize = new Vector2(0, 60); l.VerticalAlignment = VerticalAlignment.Center; }
    /// <summary>Price/action button: gold when affordable, muted red when not.</summary>
    static void PriceStyle(Button b, bool ok)
    {
        var c = ok ? UiTheme.Accent : Muted; b.AddThemeColorOverride("font_color", c); b.AddThemeColorOverride("font_hover_color", ok ? c.Lightened(.25f) : c); b.CustomMinimumSize = new Vector2(96, 32); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; b.AddThemeFontSizeOverride("font_size", 15);
    }

    void AddRow(Node parent, string text, Color col, string btn, Action a, bool ok = true)
    {
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); CardBox(parent).AddChild(h); var l = Lbl(text, 16, col, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.VerticalAlignment = VerticalAlignment.Center;
        var b = Btn(btn, a, true); PriceStyle(b, ok); h.AddChild(b);
    }

    // ---------------------------------------------------------------- pack
    // grid pack: paper-doll slots on the left, a 7x4 icon grid on the right, a hover tooltip with comparison arrows.
    // Click an item to equip it, right-click to drop it; click an equipped slot to take it off.
    ItemTip? _tip;
    public void OpenPack()
    {
        // Fixed-size window: every zone has a fixed footprint and the grids scroll, so item / material counts never resize or shift it.
        var d = Game.D; var v = Frame("pack", "Pack and Equipment", new Vector2(PackW, PackH), OpenPack);
        const int Cell = 56, Gap = 6; const float GridW = 7 * Cell + 6 * Gap, GridH = 4 * Cell + 3 * Gap, CentreW = GridW + 20;
        // ---- slim header line: gold, potions, pack count
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 20);
        Lbl($"{d.Gold} G", 16, Gold, top); Lbl($"Health potions {d.HpPot} (H)", 14, new Color("ff8f86"), top); Lbl($"Mana potions {d.MpPot} (G)", 14, new Color("8fb8ff"), top);
        var cap = Lbl($"Pack {d.Inv.Count}/28", 14, d.Inv.Count >= 26 ? new Color(1, .6f, .5f) : Dim, top); cap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; cap.HorizontalAlignment = HorizontalAlignment.Right;
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 24); v.AddChild(cols);
        // ---- left: equipment slots around a silhouette, compact stats
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(DollW, 0) }; left.AddThemeConstantOverride("separation", 4); cols.AddChild(left);
        Lbl("EQUIPPED", 14, UiTheme.Accent, left);
        var doll = new PaperDoll { CustomMinimumSize = new Vector2(DollW, DollH) }; left.AddChild(doll);
        // slot centres on the 320 x 344 reference layout, scaled to the doll's size
        var dollPos = new Dictionary<string, Vector2> { ["head"] = new(160, 28), ["armor"] = new(160, 136), ["hands"] = new(50, 206), ["feet"] = new(160, 314), ["weapon"] = new(270, 178), ["shield"] = new(50, 134), ["trinket"] = new(270, 106), ["cape"] = new(50, 62) };
        const float Ds = 52;
        foreach (var slot in PlayerStats.Slots)
        {
            var it = d.Eq.GetValueOrDefault(slot); string sl = slot;
            var c0 = dollPos.GetValueOrDefault(slot, new Vector2(Ds / 2, Ds / 2)) * new Vector2(DollW / PaperDoll.RefW, DollH / PaperDoll.RefH);
            var pos = new Vector2(Mathf.Clamp(c0.X - Ds / 2, 0, DollW - Ds), Mathf.Clamp(c0.Y - Ds / 2, 0, DollH - Ds));
            var cell = new ItemSlot { Item = it, Empty = slot, CustomMinimumSize = new Vector2(Ds, Ds), Size = new Vector2(Ds, Ds), Position = pos, TooltipText = it == null ? Cap(slot) : "" }; doll.AddChild(cell);
            cell.Hover += on => ShowTip(on ? it : null, cell, null);
            if (it != null) cell.DropGem = key => SocketGem(it, -1, key);   // drop a gem on worn gear: its first empty socket (F-10)
            cell.Clicked += right => { if (it == null) return; if (_packTab == 2 && _gemSel != "" && !right) { SocketGem(it, -1, _gemSel); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Eq[sl] = null; d.Inv.Add(it); Sfx.Play("click"); Game.Notify(); OpenPack(); };
        }
        var stats = new GridContainer { Columns = 2 }; stats.AddThemeConstantOverride("h_separation", 16); stats.AddThemeConstantOverride("v_separation", 0); left.AddChild(stats);
        foreach (var (k, val) in new[] { ("Damage", $"{d.WpnDmg():0}"), ("Defense", $"{d.Defense()}"), ("Crit", $"{d.CritChance() * 100:0}%"), ("Spell power", $"x{d.SpellPow():0.00}"), ("Health", $"{d.MaxHp()}"), ("Mana", $"{d.MaxMp()}") })
        { var row = new HBoxContainer { CustomMinimumSize = new Vector2((DollW - 16) / 2, 0) }; stats.AddChild(row); var a = Lbl(k, 13, GoldC, row); a.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; Lbl(val, 13, Colors.White, row); }
        // ---- centre: Items | Materials tabs over one fixed-size scrolling grid, toolbar under it
        var right = new VBoxContainer { CustomMinimumSize = new Vector2(CentreW, 0), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        int kinds = 0; foreach (var kv in d.Mats) if (kv.Value > 0) kinds++;
        var tabs = new HBoxContainer(); tabs.AddThemeConstantOverride("separation", 4); right.AddChild(tabs);
        tabs.AddChild(GoldTab("Items", () => { if (_packTab == 0) return; _packTab = 0; OpenPack(); }, _packTab == 0));
        tabs.AddChild(GoldTab($"Materials ({kinds})", () => { if (_packTab == 1) return; _packTab = 1; OpenPack(); }, _packTab == 1));
        tabs.AddChild(GoldTab($"Gems ({GemDb.Total(d)})", () => { if (_packTab == 2) return; _packTab = 2; OpenPack(); }, _packTab == 2));
        PadTabs(new (Action, bool)[] { (() => { _packTab = 0; OpenPack(); }, _packTab == 0), (() => { _packTab = 1; OpenPack(); }, _packTab == 1), (() => { _packTab = 2; OpenPack(); }, _packTab == 2) });
        int tab = _packTab;
        var sc = new ScrollContainer { CustomMinimumSize = new Vector2(CentreW, GridH + 2), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; right.AddChild(sc);
        var grid = new GridContainer { Columns = 7 }; grid.AddThemeConstantOverride("h_separation", Gap); grid.AddThemeConstantOverride("v_separation", Gap); sc.AddChild(grid);
        // keep the scroll position across the rebuild that follows every click
        int want = _packScroll[tab]; var vbar = sc.GetVScrollBar();
        void Restore() { if (GodotObject.IsInstanceValid(sc) && want > 0) sc.ScrollVertical = want; }
        vbar.Changed += Restore; vbar.ValueChanged += val => { _packScroll[tab] = (int)val; if (Mathf.Abs((float)val - want) > 1 && vbar.MaxValue - vbar.Page >= want) want = -1; };
        Callable.From(Restore).CallDeferred();
        if (tab == 2) { grid.Columns = 1; GemsPane(grid, Cell, Gap, GridW); }   // UiGems.cs
        else if (tab == 1)
        {
            foreach (var kv in d.Mats) if (kv.Value > 0)
            {
                var m = GatherDb.Mat(kv.Key); var cell = new ItemSlot { IconId = m.Name, Count = kv.Value, Tint = m.Col, CustomMinimumSize = new Vector2(Cell, Cell), TooltipText = $"{m.Name} x{kv.Value}  ({m.Value}g each)" }; grid.AddChild(cell);
            }
            if (kinds == 0) { grid.Columns = 1; var l = Row(grid, $"Nothing gathered yet. Press {Inputs.KeyOf(Inputs.Use)} near herbs, mushrooms, berries, ore, crystals and fallen logs.", Dim, 13); l.CustomMinimumSize = new Vector2(GridW, 0); }
        }
        else for (int i = 0; i < Math.Max(28, (d.Inv.Count + 6) / 7 * 7); i++)
        {
            int k = i; var it = i < d.Inv.Count ? d.Inv[i] : null;
            var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(Cell, Cell) }; grid.AddChild(cell);
            if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += rightBtn =>
            {
                HideTip();
                // right-click: drop it on the ground in front of you (a friend can take it, or walk away and back to pick it up again)
                if (rightBtn) { d.Inv.RemoveAt(k); Loot.DropFromPack(it); Game.Notify(); OpenPack(); }
                else if (_scrapMode) { d.Inv.RemoveAt(k); Note($"Scrapped {it.Name}: {LootTables.Disassemble(it)}", Dim); OpenPack(); }
                else Equip(k);
            };
        }
        var hdr = new HBoxContainer(); hdr.AddThemeConstantOverride("separation", 4); right.AddChild(hdr);
        hdr.AddChild(GoldTab("Sort", () => { d.Inv.Sort((a, b) => b.Rar != a.Rar ? b.Rar.CompareTo(a.Rar) : string.Compare(a.Slot, b.Slot, StringComparison.Ordinal)); Sfx.Play("click"); OpenPack(); }));
        hdr.AddChild(GoldTab("Disassemble", () => { _scrapMode = !_scrapMode; OpenPack(); }, _scrapMode));
        hdr.AddChild(GoldTab("Scrap commons", () =>
        {
            int n = 0; var parts = new Dictionary<string, int>();
            foreach (var it in d.Inv.FindAll(x => x.Rar == 0 && x.Set == null && !x.Unique)) { GemDb.ReturnAll(d, it); foreach (var (id, c) in LootTables.Scraps(it)) { d.Mats[id] = d.Mats.GetValueOrDefault(id) + c; parts[id] = parts.GetValueOrDefault(id) + c; } d.Inv.Remove(it); n++; }
            if (n > 0) { Note($"Scrapped {n} common items: " + string.Join(", ", parts.Select(kv => $"+{kv.Value} {GatherDb.Mat(kv.Key).Name}")), Dim); Sfx.Play("hit"); }
            Game.Notify(); OpenPack();
        }));
        hdr.AddChild(GoldTab("Pickup filter", OpenPickFilter));
        hdr.AddChild(tab == 2 ? GoldTab("Fuse gems", () => OpenCraft("gems")) : GoldTab("Crafting", () => OpenCraft("gear")));
        string filt = d.PickRar > 0 || d.PickSkip.Count > 0 ? $"Pickup filter: {Items.RarityName(d.PickRar)} and better" + (d.PickSkip.Count > 0 ? $", skipping {string.Join(", ", d.PickSkip)}" : "") + (d.PickScrap ? " (the rest is scrapped)" : " (the rest stays on the ground)") : "Pickup filter: everything";
        var fl = Lbl(filt, 12, Dim, right); fl.ClipText = true; fl.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis; fl.TooltipText = filt; fl.MouseFilter = Control.MouseFilterEnum.Pass;
        // ---- one short help line at the bottom (clipped, so it can never widen the window)
        var help = Lbl(tab == 2 ? "Drag a gem onto a socket or worn item (or click a gem, then a socket)     Click a filled socket: take the gem out" : _scrapMode ? "Disassemble ON: click an item to scrap it     Right-click: drop" : "Click: equip     Right-click: drop     Hover: compare     Click a worn slot: take off", 12, _scrapMode ? new Color(1, .7f, .45f) : Dim, v);
        help.ClipText = true; help.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
    }
    const float PackW = 820, PackH = 560, DollW = 260, DollH = 300;
    static int _packTab;                         // 0 = Items, 1 = Materials, 2 = Gems; kept across the reopen after every click
    static readonly int[] _packScroll = new int[3];
    static bool _scrapMode;
    /// <summary>Flat gold text tab with an underline (brighter when active or hovered).</summary>
    static Button GoldTab(string t, Action a, bool active = false)
    {
        var b = Btn(t, a, true); b.Flat = true;
        StyleBoxFlat Sb(Color ul) { var s = new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0), BorderColor = ul, ContentMarginLeft = 6, ContentMarginRight = 6, ContentMarginTop = 2, ContentMarginBottom = 2 }; s.BorderWidthBottom = 2; return s; }
        var dimU = new Color(GoldC, .35f); var hot = GoldC;
        b.AddThemeStyleboxOverride("normal", Sb(active ? hot : dimU)); b.AddThemeStyleboxOverride("hover", Sb(hot)); b.AddThemeStyleboxOverride("pressed", Sb(hot)); b.AddThemeStyleboxOverride("focus", Sb(hot));
        foreach (var n in new[] { "font_color", "font_hover_color", "font_pressed_color" }) b.AddThemeColorOverride(n, active ? GoldC.Lightened(.25f) : GoldC);
        return b;
    }
    /// <summary>Dim iron silhouette of a standing figure with a faint gold outline; equipment slots are placed over it.</summary>
    sealed partial class PaperDoll : Control
    {
        /// <summary>Reference layout the silhouette is authored in; drawing scales to the real Size.</summary>
        public const float RefW = 320, RefH = 344;
        public override void _Draw()
        {
            var iron = new Color(.16f, .16f, .18f, .85f); var edge = new Color(.72f, .6f, .3f, .28f);
            float sx = Size.X / RefW, sy = Size.Y / RefH, cx = Size.X / 2;
            Vector2 P(float dx, float y) => new(cx + dx * sx, y * sy);   // dx: offset from the centre line, y: reference height
            void Shape(params Vector2[] p) { DrawColoredPolygon(p, iron); var l = new Vector2[p.Length + 1]; p.CopyTo(l, 0); l[^1] = p[0]; DrawPolyline(l, edge, 1.5f, true); }
            float r = 17 * Mathf.Min(sx, sy);
            DrawCircle(P(0, 80), r, iron); DrawArc(P(0, 80), r, 0, Mathf.Tau, 32, edge, 1.5f, true);
            Shape(P(-12, 94), P(12, 94), P(14, 106), P(-14, 106));   // neck
            Shape(P(-44, 106), P(44, 106), P(34, 200), P(-34, 200));   // torso
            Shape(P(-46, 108), P(-66, 112), P(-80, 210), P(-66, 214), P(-50, 140));   // arms
            Shape(P(46, 108), P(66, 112), P(80, 210), P(66, 214), P(50, 140));
            Shape(P(-34, 200), P(-2, 200), P(-6, 290), P(-26, 290));   // legs
            Shape(P(2, 200), P(34, 200), P(26, 290), P(6, 290));
        }
    }
    void ShowTip(Item? it, Control anchor, Item? worn)
    {
        if (it == null) { HideTip(); return; }
        if (_tip == null || !IsInstanceValid(_tip)) { _tip = new ItemTip(); AddChild(_tip); }
        _tip.Show(it, worn, anchor.GetGlobalRect(), GetViewport().GetVisibleRect().Size);
    }
    void HideTip() { if (_tip != null && IsInstanceValid(_tip)) _tip.Visible = false; HideInfo(); }
    static string Cap(string s) => char.ToUpper(s[0]) + s.Substring(1);
    void Equip(int idx)
    {
        var d = Game.D; var it = d.Inv[idx]; var slot = it.Slot;
        if (PetDb.IsGear(slot)) { OpenPets(null, "gear"); return; }   // pet gear goes on a pet (Pets menu > Gear)
        if (slot == "shield" && d.Weapon?.Two == true) { Note("Your weapon needs both hands", new Color(1, .6f, .5f)); return; }
        var old = d.Eq.GetValueOrDefault(slot); d.Inv.RemoveAt(idx); d.Eq[slot] = it; if (old != null) d.Inv.Add(old);
        if (slot == "weapon" && it.Two && d.Eq.GetValueOrDefault("shield") is Item sh) { d.Eq["shield"] = null; d.Inv.Add(sh); }
        Sfx.Play("click"); Game.Notify(); OpenPack();
    }

    // ---------------------------------------------------------------- character
    public void OpenCharacter() => OpenCharacterPanel();
    /// <summary>What one attribute point gives, measured: the derived stats before and after ten extra points, divided by ten.</summary>
    static string PerPoint(Action<int> add)
    {
        var d = Game.D; float[] S() => new[] { d.MaxHp(), d.MaxMp(), d.MaxSt(), d.WpnDmg(), d.SpellPow() * 100, d.Defense(), d.CritChance() * 100, d.AtkSpeed() * 100 };
        var a = S(); add(10); var b = S(); add(-10);
        string[] names = { "health", "mana", "stamina", "weapon damage", "% spell power", "armor", "% crit chance", "% attack speed" };
        var parts = new List<string>(); for (int i = 0; i < a.Length; i++) { float dv = (b[i] - a[i]) / 10f; if (Mathf.Abs(dv) > .005f) parts.Add($"+{dv:0.##} {names[i]}".Replace(" %", "% ").Replace("%  ", "% ")); }
        return parts.Count > 0 ? "Each point: " + string.Join(", ", parts) : "";
    }
    /// <summary>Attributes a new hero of this race and class starts with (Game.NewGame): 5 each plus the class's and race's bonuses.</summary>
    static (int str, int dex, int vit, int mag) BaseAttributes(PlayerStats d)
    {
        int s = 5, x = 5, v = 5, m = 5;
        void Add(Dictionary<string, int> a) { foreach (var kv in a) switch (kv.Key) { case "str": s += kv.Value; break; case "dex": x += kv.Value; break; case "vit": v += kv.Value; break; case "mag": m += kv.Value; break; } }
        Add(Classes.Get(d.Class).Attr); Add(Races.Get(d.Race).Attr);
        return (s, x, v, m);
    }
    void OpenCharacterPanel()
    {
        var d = Game.D; var TS = Game.TS; var v = Frame("char", "Character", new Vector2(1120, 620), OpenCharacterPanel); HideInfo();
        var gold = UiTheme.Highlight;
        // top strip: points, respec, shortcuts
        var pts = new HBoxContainer(); pts.AddThemeConstantOverride("separation", 8); v.AddChild(pts);
        Chip(pts, $"Attribute points  {d.AttrPts}", d.AttrPts > 0); Chip(pts, $"Skill points  {d.RankPts}", d.RankPts > 0); Chip(pts, $"Tree points  {d.SkillPts}", d.SkillPts > 0);
        pts.AddChild(Btn("Reset attributes", () =>
        {
            // every spent attribute point comes back: Strength, Dexterity, Vitality and Magic return to what the race and class start with
            var (s0, d0, v0, m0) = BaseAttributes(d); int back = Math.Max(0, d.Str - s0) + Math.Max(0, d.Dex - d0) + Math.Max(0, d.Vit - v0) + Math.Max(0, d.Mag - m0);
            if (back == 0) { Note("No attribute points spent yet", Dim); return; }
            d.Str = Math.Min(d.Str, s0); d.Dex = Math.Min(d.Dex, d0); d.Vit = Math.Min(d.Vit, v0); d.Mag = Math.Min(d.Mag, m0); d.AttrPts += back;
            Note($"Attributes reset: {back} points to spend again", UiTheme.Highlight); Game.Notify(); Game.Save(); OpenCharacterPanel();
        }, true));
        pts.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        pts.AddChild(Btn($"Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true)); pts.AddChild(Btn($"Skill tree ({Inputs.KeyOf(Inputs.Tree)})", OpenTree, true)); pts.AddChild(Btn($"Modifiers ({Inputs.KeyOf(Inputs.ModsUi)})", OpenMods, true)); pts.AddChild(Btn("All bonuses", OpenTotals, true));

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 22); v.AddChild(cols);
        // ---- left: who you are
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        Lbl(d.Name, 26, UiTheme.Title, left);
        Lbl($"{Races.Get(d.Race).Name} {Classes.Get(d.Class).Name}", 15, Dim, left);
        if (d.Titles.Count > 0)
        {
            // earned titles: wear one (it shows by your name and gives its bonus)
            var tb = new OptionButton { TooltipText = "Your title shows beside your name and gives its bonus" }; tb.AddItem("No title"); int sel = 0, idx = 1;
            foreach (var t in Titles.All) if (d.Titles.Contains(t.Id)) { tb.AddItem(t.Name + (t.Mods.Count > 0 ? "  (" + string.Join(", ", System.Linq.Enumerable.Select(t.Mods, kv => TreeDb.StatText(kv.Key, kv.Value))) + (t.Feathers ? ", feather trail" : "") + ")" : "")); tb.SetItemMetadata(idx, t.Id); if (d.Title == t.Id) sel = idx; idx++; }
            tb.Select(sel); tb.ItemSelected += i => { Titles.Wear(i == 0 ? "" : tb.GetItemMetadata((int)i).AsString()); Sfx.Play("click"); };
            left.AddChild(tb);
        }
        Lbl($"Level {d.Level}", 30, gold, left);
        var xpBar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, d.XpNeed()), Value = Math.Min(d.Xp, d.XpNeed()), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MouseFilter = Control.MouseFilterEnum.Ignore };
        xpBar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = gold }); xpBar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, .55f) });
        left.AddChild(xpBar); Lbl($"XP {d.Xp} / {d.XpNeed()}", 12, Dim, left);
        left.AddChild(new Control { CustomMinimumSize = new Vector2(0, 8) });
        Lbl($"Gold   {d.Gold}", 17, gold, left); Lbl($"Valor   {d.Valor}", 17, UiTheme.Text, left); Lbl($"Kills   {d.Kills}", 14, Dim, left);

        // ---- right: derived stats (built first so the attribute previews can reach their delta labels)
        float[] Snap() => new[] { d.MaxHp(), d.MaxMp(), d.MaxSt(), d.WpnDmg(), d.SpellPow(), d.Defense(), d.CritChance() * 100, d.AtkSpeed() };
        var deltas = new Dictionary<int, Label>();
        var fmts = new Dictionary<int, string> { [0] = "0", [1] = "0", [2] = "0", [3] = "0.#", [4] = "0.00", [5] = "0", [6] = "0.#", [7] = "0.00" };
        var mid = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) }; mid.AddThemeConstantOverride("separation", 8); cols.AddChild(mid);
        var right = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 20); cols.AddChild(right);
        string P(float x) => $"{x:0.#}%";
        void Section(Control parent, string title, (string n, string v, Color? c, int di)[] rows)
        {
            var box = new VBoxContainer(); box.AddThemeConstantOverride("separation", 1); parent.AddChild(box);
            Lbl(title.ToUpper(), 13, gold, box); box.AddChild(new HSeparator());
            var g = new GridContainer { Columns = 3 }; g.AddThemeConstantOverride("h_separation", 8); g.AddThemeConstantOverride("v_separation", 1); box.AddChild(g);
            foreach (var (n, val, c, di) in rows)
            {
                Lbl(n, 13, Dim, g).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                var vl = Lbl(val, 15, c ?? Colors.White, g); vl.HorizontalAlignment = HorizontalAlignment.Right; vl.CustomMinimumSize = new Vector2(64, 0);
                var dl = Lbl("", 12, Colors.White, g); dl.CustomMinimumSize = new Vector2(40, 0); if (di >= 0) deltas[di] = dl;
            }
        }
        var rc1 = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rc1.AddThemeConstantOverride("separation", 10); right.AddChild(rc1);
        var rc2 = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rc2.AddThemeConstantOverride("separation", 10); right.AddChild(rc2);
        Section(rc1, "Offense", new (string, string, Color?, int)[] { ("Weapon damage", $"{d.WpnDmg():0}", null, 3), ("Spell power", $"x{d.SpellPow():0.00}", null, 4), ("Crit chance", P(d.CritChance() * 100), null, 6),
            ("Crit damage", $"x{d.CritMult():0.0}", null, -1), ("Attack speed", $"x{d.AtkSpeed():0.00}", null, 7), ("Cast speed", "+" + P(TS["castSpd"]), null, -1),
            ("Cooldowns", "-" + P(TS["cdr"]), null, -1), ("Skill damage", "+" + P(TS["skillP"]), null, -1), ("All damage", "+" + P(TS["allP"]), null, -1) });
        Section(rc1, "Other", new (string, string, Color?, int)[] { ("Armor pen", $"{TS["armorPen"]:0} + {TS["armorPenP"]:0}%", null, -1), ("Magic pen", $"{TS["magicPen"]:0} + {TS["magicPenP"]:0}%", null, -1), ("Status time", "+" + P(TS["statusP"]), null, -1),
            ("Move speed", "+" + P(TS["move"]), null, -1), ("XP bonus", "+" + P(TS["xpP"]), null, -1), ("Tree nodes", $"{d.Tree.Count}", null, -1) });
        Section(rc2, "Defense", new (string, string, Color?, int)[] { ("Health", $"{d.MaxHp()}", new Color("ff8a7a"), 0), ("Mana", $"{d.MaxMp()}", new Color("8fb0ff"), 1), ("Stamina", $"{d.MaxSt()}", new Color("7ee08a"), 2),
            ("Armor", $"{d.Defense()}", null, 5), ("Magic resist", $"{TS["mr"] + d.Defense() * .5f:0}", null, -1), ("Evade", P(TS["evade"]), null, -1),
            ("Tenacity", P(TS["tenacity"]), null, -1), ("Block", P(TS["block"]), null, -1), ("Life leech", P(TS["leech"]), null, -1) });
        var res = new List<(string, string, Color?, int)>(); foreach (var t in new[] { "fire", "frost", "lightning", "poison", "shadow", "holy" }) res.Add((char.ToUpper(t[0]) + t[1..], P(TS["res_" + t]), Dmg.TypeColor(t), -1));
        Section(rc2, "Resistances", res.ToArray());

        // ---- middle: attributes as large rows
        Lbl("ATTRIBUTES", 13, gold, mid);
        void Preview(Action<int> add, bool on)
        {
            foreach (var l in deltas.Values) l.Text = "";
            if (!on) return;
            var a = Snap(); add(1); var b = Snap(); add(-1);
            foreach (var kv in deltas) { float dv = b[kv.Key] - a[kv.Key]; if (Mathf.Abs(dv) < .004f) continue; kv.Value.Text = (dv > 0 ? "+" : "") + dv.ToString(fmts[kv.Key]); kv.Value.AddThemeColorOverride("font_color", dv > 0 ? new Color("7ee08a") : new Color("ff6a5a")); }
        }
        void Attr(string name, string desc, string icon, int val, Action<int> add, Color col)
        {
            var pc = new PanelContainer { TooltipText = PerPoint(add) }; pc.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.03f, .025f, .02f, .7f), col with { A = .45f }, 1, 12, 8, 0)); mid.AddChild(pc);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 12); pc.AddChild(h);
            var ic = new Control { CustomMinimumSize = new Vector2(40, 40), MouseFilter = Control.MouseFilterEnum.Ignore }; ic.Draw += () => Icons.Draw(ic, icon, new Vector2(20, 20), 36); h.AddChild(ic);
            var nv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; nv.AddThemeConstantOverride("separation", 0); h.AddChild(nv);
            Lbl(name, 18, col.Lightened(.3f), nv); var dd = Lbl(desc, 12, Dim, nv); dd.CustomMinimumSize = new Vector2(150, 0); dd.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            var vl = Lbl(val.ToString(), 32, Colors.White, h); vl.CustomMinimumSize = new Vector2(52, 0); vl.HorizontalAlignment = HorizontalAlignment.Right;
            if (d.AttrPts < 1) return;
            var bv = new VBoxContainer(); bv.AddThemeConstantOverride("separation", 2); h.AddChild(bv);
            foreach (int k in new[] { 1, 5 })
            {
                if (k == 5 && d.AttrPts < 5) continue;
                int kk = k; var b = Btn("+" + k, () => { int n = Math.Min(kk, d.AttrPts); if (n <= 0) return; d.AttrPts -= n; add(n); Game.Notify(); }, true);
                b.CustomMinimumSize = new Vector2(46, 26); b.AddThemeColorOverride("font_color", gold); bv.AddChild(b);
                if (k == 1) { b.MouseEntered += () => Preview(add, true); b.MouseExited += () => Preview(add, false); var tw = b.CreateTween().SetLoops(); tw.TweenProperty(b, "modulate:a", .65f, .8f); tw.TweenProperty(b, "modulate:a", 1f, .8f); }
            }
        }
        Attr("Strength", "Melee damage and heavy weapons", "sword", d.Str, n => d.Str += n, new Color("ff7a5a")); Attr("Dexterity", "Bows, attack speed and crits", "bow", d.Dex, n => d.Dex += n, new Color("7ee08a"));
        Attr("Vitality", "Health, stamina and armor", "hp", d.Vit, n => d.Vit += n, new Color("ffd36a")); Attr("Magic", "Mana and spell power", "zap", d.Mag, n => d.Mag += n, new Color("8fb0ff"));
    }
    // ---------------------------------------------------------------- journal
    public void OpenJournal()
    {
        var v = Frame("journal", "Journal", new Vector2(700, 560), OpenJournal); var d = Game.D;
        Header("Records", v);
        var gr = new GridContainer { Columns = 3 }; gr.AddThemeConstantOverride("h_separation", 28); gr.AddThemeConstantOverride("v_separation", 2); v.AddChild(gr);
        foreach (var (k, val) in new[] { ("Best wave", d.BestWave), ("Best duel streak", d.BestDuel), ("Deepest depth", d.Deepest), ("Kills", d.Kills), ("Valor", d.Valor) })
        { var cell = new HBoxContainer(); cell.AddThemeConstantOverride("separation", 8); gr.AddChild(cell); Lbl(k, 15, Parch, cell); Lbl(val.ToString(), 17, Gold, cell); }
        Header("Active quests", v);
        var sc = Scroll(v, 400); var list = Inner(sc); var act = Quests.Active(); list.AddThemeConstantOverride("separation", 8); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        if (act.Count == 0) Empty(list, "No active quests. Speak to Elder Maren, Guard Tomas or Kael.");
        foreach (var q in act)
        {
            var s = Game.Q(q.Id); bool rdy = s.S == 2; var card = CardBox(list, rdy); var cv = new VBoxContainer(); cv.AddThemeConstantOverride("separation", 4); card.AddChild(cv);
            Row(cv, q.Name.ToUpperInvariant() + (rdy ? "   READY TO TURN IN" : ""), rdy ? new Color(.55f, 1f, .55f) : Gold, 17);
            var ob = Row(cv, (rdy ? "[x] " : "[ ] ") + q.Goal + (q.Need > 1 && s.S == 1 ? $"  ({s.N}/{q.Need})" : ""), rdy ? Dim : Parch, 15);
            if (q.Need > 1) cv.AddChild(new ProgressBar { MinValue = 0, MaxValue = q.Need, Value = rdy ? q.Need : Math.Min(s.N, q.Need), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 5), MouseFilter = Control.MouseFilterEnum.Ignore });
            if (rdy) Row(cv, "Return to " + Quests.GiverName(q.Giver) + ".", Parch, 15);
            if (q.Gold > 0 || q.Xp > 0) Row(cv, $"Reward: {(q.Gold > 0 ? q.Gold + " gold" : "")}{(q.Gold > 0 && q.Xp > 0 ? ", " : "")}{(q.Xp > 0 ? q.Xp + " xp" : "")}", Gold with { A = .8f }, 14);
        }
        int done = 0; foreach (var q in Quests.All) if (Game.Q(q.Id).S == 3) done++;
        Row(list, $"Completed: {done}", Dim, 14);
    }

    // ---------------------------------------------------------------- skill tree
    static string _treeSearch = "";
    public void OpenTree()
    {
        TreeDb.Load(); if (_panelName != "tree") TreeView.HasSaved = false;   // a fresh open centres on your start; rebuilds after taking a node keep the view
        var v = Frame("tree", "Skill Tree", new Vector2(1120, 640), OpenTree); HideInfo(); var d = Game.D;
        var hb = new HBoxContainer(); v.AddChild(hb); hb.AddThemeConstantOverride("separation", 8);
        Chip(hb, $"Tree points  {d.SkillPts}", d.SkillPts > 0);
        var search = new LineEdit { Text = _treeSearch, PlaceholderText = "Find a bonus: crit, fire, mana...", CustomMinimumSize = new Vector2(240, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; hb.AddChild(search);
        var found = Lbl("", 14, Gold, hb); found.CustomMinimumSize = new Vector2(70, 0);
        TreeView? tvRef = null;
        hb.AddChild(Btn("-", () => tvRef?.ZoomBy(1 / 1.3f), true)); hb.AddChild(Btn("+", () => tvRef?.ZoomBy(1.3f), true));
        hb.AddChild(Btn("My start", () => tvRef?.CenterOn(TreeDb.Get(TreeDb.StartFor(d.Class))!, 3.2f), true)); hb.AddChild(Btn("Whole tree", () => tvRef?.CenterOn(TreeDb.Nodes[0], 1f), true));


        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 12); v.AddChild(cols);
        var vp = GetViewport().GetVisibleRect().Size;
        var tv = new TreeView { CustomMinimumSize = new Vector2(Mathf.Clamp(vp.X - 400, 400, 820), Mathf.Clamp(vp.Y - 170, 240, 600)), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddChild(tv); tvRef = tv;
        tv.Changed = () => { Game.RecalcTree(); Game.Notify(); };
        void Find(string s) { _treeSearch = s; tv.SetSearch(s); found.Text = s.Trim().Length >= 2 ? $"{tv.Matches} found" : ""; }
        search.TextChanged += Find; Find(_treeSearch);
        tv.HoverAt = (id, at) =>
        {
            if (id < 0) { HideInfo(); return; }
            var n = TreeDb.Nodes[id]; string kind = n.Type switch { "key" => "KEYSTONE", "notable" => "NOTABLE", "start" => "CLASS START", "hub" => "CENTRE", _ => "" };
            var lines = new List<(string, Color, int)>();
            if (kind != "" || n.Arm == "hybrid") lines.Add((kind + (n.Arm == "hybrid" ? "   hybrid path" : ""), n.Col.Lightened(.3f), 12));
            if (n.Desc != "") lines.Add((n.Desc, Colors.White, 13));
            foreach (var kv in n.Mods) lines.Add((TreeDb.StatText(kv.Key, kv.Value), new Color(.75f, .92f, 1f), 14));
            bool myStart = n.Id == TreeDb.StartFor(d.Class);
            if (n.Type == "start") lines.Add((myStart ? "You start here" : $"{Classes.Get(n.Arm).Name} start. Costs a point and gives no bonus: it lets a hybrid build on through it.", UiTheme.Dim, 12));
            if (myStart) { }
            else if (d.Tree.Contains(id)) lines.Add(("Taken. Right click to refund.", UiTheme.Accent, 12));
            else { var path = tv.PathTo(id); if (path == null) lines.Add(("Cannot be reached", new Color(1f, .6f, .5f), 12)); else lines.Add((path.Count == 1 ? "Click to take (1 point)" : $"Click to take the path: {path.Count} points" + (path.Count > d.SkillPts ? $" (you have {d.SkillPts})" : ""), path.Count <= d.SkillPts ? new Color(.45f, 1f, .6f) : new Color(1f, .6f, .5f), 12)); }
            Info(at, n.Name == "" ? "Passive" : n.Name, n.Type == "key" ? new Color(1f, .85f, .5f) : Colors.White, lines, 270);
        };
        // ---- right: what your tree gives you now
        var side = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) }; side.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.05f, .045f, .04f, .85f), UiTheme.AccentSoft, 1, 10, 8, 0)); cols.AddChild(side);
        var sv = new VBoxContainer(); sv.AddThemeConstantOverride("separation", 4); side.AddChild(sv);
        Header($"Your bonuses  -  {d.Tree.Count} nodes", sv);
        var ss = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; sv.AddChild(ss);
        var sl = new VBoxContainer(); ss.AddChild(sl);
        var keys = new List<string>(); foreach (var k in TreeDb.Known) if (Mathf.Abs(Game.TS[k]) > .001f) keys.Add(k); keys.Sort();
        if (keys.Count == 0) Row(sl, "Nothing yet. Take nodes next to your start.", Dim, 14);
        foreach (var k in keys) { var l = Row(sl, TreeDb.StatText(k, Game.TS[k]), Parch, 14); l.CustomMinimumSize = new Vector2(220, 0); }
        Row(sv, "Click: take a node or the path to it. Right click: refund. Drag: move. Wheel: zoom.", Dim, 14).CustomMinimumSize = new Vector2(220, 0);
    }
    /// <summary>All bonuses you currently get (tree + race + class + buffs), one line each.</summary>
    static string TreeTotals()
    {
        var keys = new List<string>(); foreach (var k in TreeDb.Known) if (Mathf.Abs(Game.TS[k]) > .001f) keys.Add(k); keys.Sort();
        if (keys.Count == 0) return "No bonuses yet.";
        var l = new List<string>(); foreach (var k in keys) l.Add(TreeDb.StatText(k, Game.TS[k])); return string.Join("\n", l);
    }

    // ---------------------------------------------------------------- map and fast travel
    static float _travelAt;
    public void OpenMap()
    {
        // the map is square; size it to the window height left after the title and hint rows
        var vp = GetViewport().GetVisibleRect().Size; float side = Mathf.Clamp(vp.Y - 150, 240, 680);
        var v = Frame("map", "Map", new Vector2(side + 40, side + 110), OpenMap); var d = Game.D;
        Row(v, Dungeon.Active ? "This floor as far as you have explored it. Scroll to zoom, drag to pan." : "Click a discovered place to fast travel. Scroll to zoom, drag to pan.", Dim, 15);
        var frame = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.03f, .028f, .025f), BorderColor = new Color(.62f, .52f, .32f), BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3, ContentMarginLeft = 4, ContentMarginRight = 4, ContentMarginTop = 4, ContentMarginBottom = 4, ShadowColor = new Color(0, 0, 0, .5f), ShadowSize = 6 }); v.AddChild(frame);
        var mv = new MapView { CustomMinimumSize = new Vector2(side - 14, side - 14), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter }; frame.AddChild(mv);
        if (!Dungeon.Active) { var lg = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center }; lg.AddThemeConstantOverride("separation", 10); v.AddChild(lg); Lbl("*", 18, Gold, lg); Lbl("Discovered place - click to travel", 14, Parch, lg); }
        mv.Travel = poi => { if (Dungeon.Active || Arenas.Active) { Note("You cannot travel from here", new Color(1, .6f, .5f)); return; }
            var warn = new Color(1, .6f, .5f); float cr = Tune.F("fastTravel.combatRange", 20f), wait = _travelAt - Time.GetTicksMsec() / 1000f;
            if (wait > 0) { Note($"Fast travel ready in {Mathf.CeilToInt(wait)} s", warn); return; }
            var me = Player.Instance; if (me != null) foreach (var n in me.GetTree().GetNodesInGroup("enemies")) if (n is Enemy ce && ce.Hunting && !ce.Dead && ce.GlobalPosition.DistanceTo(me.GlobalPosition) < cr) { Note("You cannot travel while enemies hunt you", warn); return; }
            _travelAt = Time.GetTicksMsec() / 1000f + Tune.F("fastTravel.cooldown", 30f); Close();
            LoadScreen.Cut(poi.Name, "fast travel", "Raising the land");   // the far place streams its blocks in behind the screen
            World.ForceRealm(0); Player.Instance?.Teleport(World.TravelPoint(poi), 0f);
            LoadScreen.Lift(() => { Ui.Flash(Colors.White); Ui.Note("Travelled to " + poi.Name, Gold); }, .6f, 8f); };
    }

    // ---------------------------------------------------------------- pause and settings live in UiSystem.cs

    // ---------------------------------------------------------------- title
    static string _pickClass = "warrior", _pickRace = "human", _pickName = "Hero", _pickBody = "male";
    static bool _slotInit; static int _confirmDelete = -1;

    /// <summary>Title = character select: three hero slots that live in the same save file. Pick one to play, host or join with; empty slots create a new hero.</summary>
    public void ShowTitle()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        if (!_slotInit) { _slotInit = true; Game.Slot = Game.File().Last; }
        var v = Frame("title", "TWIN REALMS", new Vector2(1060, 640), ShowTitle);
        Row(v, "Two worlds. One hero. Press R to cross between Light and Shadow.", Dim, 15);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 8);
        if (Game.Online) top.AddChild(Btn("Log out", () => { Net.I?.Logout(); ShowTitle(); }));
        else
        {
            var po = Btn("Play Online", ShowOnline); po.Disabled = !Updater.OnlineOk; if (!Updater.OnlineOk) po.TooltipText = Updater.OnlineWhy; top.AddChild(po);   // locked until the launch check matched the server (Updater.cs)
            top.AddChild(Btn("Multiplayer", OpenMultiplayer));
        }
        top.AddChild(Btn("Settings", OpenSettings));
        top.AddChild(Btn("Quit game", () => GetTree().Quit()));
        Lbl("Version " + Game.Version, 13, Dim, v);
        if (Net.LastNote != "") Row(v, Net.LastNote, new Color(1f, .7f, .5f), 15);
        if (!Game.Online && !Updater.OnlineOk) Row(v, "Play Online is locked: " + Updater.OnlineWhy + " Restart the game to try again.", new Color(1f, .7f, .5f), 14);
        if (Game.Online)
        {
            Lbl("ONLINE AS " + Game.OnlineEmail.ToUpper() + " - CHOOSE YOUR HERO", 18, UiTheme.Accent, v);
            Row(v, "These heroes are saved on the server, so you can play them from any computer. Play enters the shared world with everyone else online.", Dim, 14);
            if (Net.AuthMsg != "" && Net.OnlineState == "entering") Row(v, Net.AuthMsg, UiTheme.Highlight, 14);
        }
        else
        {
            Lbl("CHOOSE YOUR HERO", 18, UiTheme.Accent, v);
            Row(v, "Three heroes share this save. Click a hero to select it (Multiplayer brings the selected hero), Play to enter the world, or create a hero in an empty slot.", Dim, 14);
        }
        var cardScroll = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; v.AddChild(cardScroll);
        var cards = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cards.AddThemeConstantOverride("separation", 10); cardScroll.AddChild(cards);
        for (int i = 0; i < Game.MaxSlots; i++)
        {
            int slot = i; var p = Game.Peek(slot); bool sel = slot == Game.Slot;
            var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 0), MouseFilter = Control.MouseFilterEnum.Stop };
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(sel ? UiTheme.ButtonHover : UiTheme.Button, sel ? UiTheme.Highlight : UiTheme.AccentSoft, sel ? 2 : 1, 16, 8, sel ? 8 : 0));
            card.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && Game.Slot != slot) { Game.Slot = slot; _confirmDelete = -1; Sfx.Play("click"); ShowTitle(); } };
            cards.AddChild(card);
            var c = new VBoxContainer(); c.AddThemeConstantOverride("separation", 4); card.AddChild(c);
            Lbl($"SLOT {slot + 1}" + (sel ? "   - selected" : ""), 13, sel ? UiTheme.Highlight : Dim, c);
            if (p == null)
            {
                var er = new HBoxContainer(); er.AddThemeConstantOverride("separation", 10); c.AddChild(er);
                Lbl("Empty", 22, Dim, er).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                var nb = Btn("Create a hero", () => { Game.Slot = slot; _confirmDelete = -1; ShowCreate(); }); nb.CustomMinimumSize = new Vector2(0, 40); er.AddChild(nb);
                continue;
            }
            Lbl(p.Name, 22, UiTheme.Title, c);
            Row(c, $"Level {p.Level} {Races.Get(p.Race).Name} {Classes.Get(p.Class).Name}", Colors.White, 15);
            Row(c, $"Gold {p.Gold}  Kills {p.Kills}  Floor {p.Deepest}  Wave {p.BestWave}  Played {Game.Played(p)}", Dim, 13);
            var br = new HBoxContainer(); br.AddThemeConstantOverride("separation", 10); c.AddChild(br);
            var play = Btn("Play", () => { Game.Slot = slot; _confirmDelete = -1; CloseNoUnpause(); OnContinue?.Invoke(); }); play.CustomMinimumSize = new Vector2(0, 40); play.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; br.AddChild(play);
            var del = Btn(_confirmDelete == slot ? "Really delete " + p.Name + "?" : "Delete", () =>
            {
                if (_confirmDelete != slot) { _confirmDelete = slot; ShowTitle(); return; }
                _confirmDelete = -1; Game.DeleteSlot(slot); Note(p.Name + " was deleted", Dim); ShowTitle();
            }, true);
            if (_confirmDelete == slot) del.AddThemeColorOverride("font_color", new Color(1f, .5f, .45f));
            br.AddChild(del);
        }
    }

    /// <summary>New hero for the selected slot: race, class, name.</summary>
    public void ShowCreate()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        var v = Frame("title", $"NEW HERO - SLOT {Game.Slot + 1}", new Vector2(1060, 640), ShowCreate);
        var back = Btn("Back to heroes", ShowTitle, true); back.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin; v.AddChild(back);
        Row(v, "Choose your race and class, then name your hero.", Dim, 14);
        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; v.AddChild(sc);
        var cols = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 14); sc.AddChild(cols);
        void Cap(Node p, string t) { var l = Lbl(t, 13, Gold, p); l.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 3 }); var ln = new ColorRect { Color = Line with { A = .6f }, CustomMinimumSize = new Vector2(0, 1) }; p.AddChild(ln); }
        var rc = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; rc.AddThemeConstantOverride("separation", 0); cols.AddChild(rc); Cap(rc, "RACE");
        foreach (var r in Races.All) { string id = r.Id; PickRow(rc, r.Name, id == _pickRace, () => { _pickRace = id; ShowCreate(); }); }
        var cc = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; cc.AddThemeConstantOverride("separation", 0); cols.AddChild(cc); Cap(cc, "CLASS");
        foreach (var c in Classes.All) { string id = c.Id; PickRow(cc, c.Name, id == _pickClass, () => { _pickClass = id; ShowCreate(); }); }
        // ---- body, name, begin
        var foot = new VBoxContainer(); foot.AddThemeConstantOverride("separation", 8); v.AddChild(foot); Cap(foot, "BODY");
        var bodies = new HBoxContainer(); bodies.AddThemeConstantOverride("separation", 8); foot.AddChild(bodies);
        foreach (var (id, label) in new[] { ("male", "Male"), ("female", "Female") })
        {
            bool on = id == _pickBody; var b = Btn(label, () => { _pickBody = id; ShowCreate(); }); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.CustomMinimumSize = new Vector2(0, 34);
            var sbn = UiTheme.Box(on ? new Color(.20f, .15f, .06f, .9f) : new Color(.05f, .045f, .04f, .7f), on ? Gold : Line with { A = .5f }, 1, 10, 4, 0);
            b.AddThemeStyleboxOverride("normal", sbn); b.AddThemeStyleboxOverride("hover", UiTheme.Box(new Color(.14f, .11f, .06f, .9f), Gold, 1, 10, 4, 0)); b.AddThemeStyleboxOverride("pressed", sbn);
            b.AddThemeColorOverride("font_color", on ? Gold : Parch with { A = .7f }); bodies.AddChild(b);
        }
        Cap(foot, "NAME");
        var name = new LineEdit { Text = _pickName, PlaceholderText = "Name your hero", CustomMinimumSize = new Vector2(0, 36) }; foot.AddChild(name); name.TextChanged += s => _pickName = s;
        name.AddThemeStyleboxOverride("normal", UiTheme.Box(new Color(.02f, .018f, .015f, .95f), Line with { A = .5f }, 1, 10, 4, 0));
        name.AddThemeStyleboxOverride("focus", UiTheme.Box(new Color(.03f, .025f, .02f, .98f), Gold, 2, 10, 4, 0));
        name.AddThemeColorOverride("font_color", Parch);
        var go = Btn("Begin the journey", () => { CloseNoUnpause(); OnNew?.Invoke(_pickClass, name.Text, _pickRace); }); go.CustomMinimumSize = new Vector2(0, 50); go.AddThemeFontSizeOverride("font_size", 20); foot.AddChild(go);
        var gn = UiTheme.Box(new Color(.62f, .48f, .20f, .95f), Gold, 1, 12, 6, 6); var gh = UiTheme.Box(new Color(.80f, .64f, .28f, 1f), new Color(1f, .9f, .6f), 1, 12, 6, 10);
        go.AddThemeStyleboxOverride("normal", gn); go.AddThemeStyleboxOverride("hover", gh); go.AddThemeStyleboxOverride("pressed", gn);
        foreach (var cn in new[] { "font_color", "font_hover_color", "font_pressed_color" }) go.AddThemeColorOverride(cn, new Color(.08f, .05f, .02f));
        // ---- right-hand info card over the backdrop
        var rd = Races.Get(_pickRace); var cd = Classes.Get(_pickClass);
        var vp = GetViewport().GetVisibleRect().Size; float cw = Mathf.Clamp(vp.X - 40 - 460 - 60, 280, 520);
        var card = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Stop }; card.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.04f, .035f, .03f, .78f), Line with { A = .7f }, 1, 20, 16, 0));
        card.AnchorLeft = 1; card.AnchorRight = 1; card.AnchorTop = 0; card.AnchorBottom = 0; card.OffsetRight = -40; card.OffsetLeft = -40 - cw; card.OffsetTop = 40; card.GrowVertical = Control.GrowDirection.End; card.GrowHorizontal = Control.GrowDirection.Begin;
        _panel?.AddChild(card);
        var dc = new VBoxContainer(); dc.AddThemeConstantOverride("separation", 8); card.AddChild(dc);
        var tt = Lbl($"{rd.Name.ToUpper()}  {cd.Name.ToUpper()}", 26, Gold, dc); tt.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 4 }); tt.AddThemeColorOverride("font_outline_color", new Color(.08f, .04f, .02f)); tt.AddThemeConstantOverride("outline_size", 5);
        dc.AddChild(new ColorRect { Color = Line with { A = .7f }, CustomMinimumSize = new Vector2(0, 1) });
        Row(dc, rd.Desc, Parch, 15); Row(dc, cd.Desc, Parch, 15);
        void Stats(string head, Dictionary<string, int> attr, Dictionary<string, float> mods, string extra)
        {
            Cap(dc, head);
            var fl = new HFlowContainer(); fl.AddThemeConstantOverride("h_separation", 14); fl.AddThemeConstantOverride("v_separation", 2); dc.AddChild(fl);
            foreach (var kv in attr) { var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 5); fl.AddChild(h); Lbl((kv.Value > 0 ? "+" : "") + kv.Value, 13, Gold, h); Lbl(kv.Key, 14, Parch, h); }
            if (extra != "") { var l = Lbl(extra, 14, Parch, fl); }
            foreach (var kv in mods) Lbl(TreeDb.StatText(kv.Key, kv.Value), 13, Dim, fl);
        }
        Stats("RACE BONUSES", rd.Attr, rd.Mods, rd.Pts > 0 ? $"+{rd.Pts} skill point" : "");
        Stats("CLASS BONUSES", cd.Attr, cd.Mods, "");
        Row(dc, $"Weapon: {cd.WName} ({Weapons.Get(cd.Wt).Name}{(cd.Shield ? " and shield" : "")})", Parch, 14);
        Cap(dc, "STARTING SKILLS");
        var chips = new HFlowContainer(); chips.AddThemeConstantOverride("h_separation", 8); chips.AddThemeConstantOverride("v_separation", 8); dc.AddChild(chips);
        foreach (var sid in cd.Skills)
        {
            var sk = Skills.Get(sid); var chip = new PanelContainer { TooltipText = sk?.Name ?? sid }; chip.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.05f, .045f, .04f, .85f), Gold with { A = .8f }, 1, 6, 5, 0)); chips.AddChild(chip);
            var ch = new HBoxContainer(); ch.AddThemeConstantOverride("separation", 6); chip.AddChild(ch);
            if (sk != null) { string ic = Icons.ForSkill(sk); var ctl = new Control { CustomMinimumSize = new Vector2(30, 30), MouseFilter = Control.MouseFilterEnum.Ignore }; ctl.Draw += () => Icons.Draw(ctl, ic, new Vector2(15, 15), 28); ch.AddChild(ctl); }
            Lbl(sk?.Name ?? sid, 13, Parch, ch).VerticalAlignment = VerticalAlignment.Center;
        }
        Row(dc, "Every class starts on its own part of the skill tree; bridges lead into neighbouring paths for hybrid builds.", Dim, 13);
    }
}