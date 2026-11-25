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
        t.SetStylebox("pressed", "Button", B(UiTheme.ButtonPressed, UiTheme.ButtonPressed, 1, 14, 5, 8)); t.SetStylebox("focus", "Button", new StyleBoxEmpty());
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

    public override void _Process(double delta) { Feel.Tick(); UpdateTick(); } // Ui always processes, so hit-stop always ends; UpdateTick: download progress (Updater.cs)

    Label Lbl(string t, int size, Color? c = null, Node? parent = null)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c ?? Colors.White); l.AddThemeColorOverride("font_outline_color", Colors.Black); l.AddThemeConstantOverride("outline_size", 5);
        (parent ?? this).AddChild(l); return l;
    }

    void AddNote(string t, Color c)
    {
        var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pc.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = UiTheme.Panel with { A = .7f }, BorderColor = UiTheme.Accent, BorderWidthLeft = 3, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 2, ContentMarginBottom = 2 });
        var l = Lbl(t, 16, c, pc); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(300, 0);
        while (_notes.GetChildCount() >= 6) { var old = _notes.GetChild(0); _notes.RemoveChild(old); old.QueueFree(); }
        _notes.AddChild(pc);
        var tw = pc.CreateTween(); tw.TweenInterval(3.2); tw.TweenProperty(pc, "modulate:a", 0f, 0.8); tw.TweenCallback(Callable.From(pc.QueueFree));
    }
    void ShowBanner(string t, string s)
    {
        _banner.Text = t; _bannerSub.Text = s.ToUpper(); _banner.Modulate = Colors.White; _bannerSub.Modulate = Colors.White;
        _bannerTw?.Kill(); // a new banner restarts the fade instead of fighting the old one
        var tw = _bannerTw = CreateTween().SetParallel(); tw.TweenProperty(_banner, "modulate:a", 0f, 1.2).SetDelay(2.0); tw.TweenProperty(_bannerSub, "modulate:a", 0f, 1.2).SetDelay(2.0);
    }
    Tween? _bannerTw;
    public static void ClearBanner() { if (I == null) return; I._bannerTw?.Kill(); I._banner.Modulate = new Color(1, 1, 1, 0); I._bannerSub.Modulate = new Color(1, 1, 1, 0); }
    void DoFlash(Color c) { _flash.Color = new Color(c, 0.85f); CreateTween().TweenProperty(_flash, "color:a", 0f, 0.7); }

    // ---------------------------------------------------------------- input
    public override void _UnhandledInput(InputEvent e)
    {
        if (!InGame) return;
        if (e.IsActionPressed(Inputs.Pause)) { if (_panel != null) Close(); else OpenPause(); GetViewport().SetInputAsHandled(); return; }
        if (e.IsActionPressed(Inputs.Pack)) { Toggle("pack", OpenPack); } else if (e.IsActionPressed(Inputs.Journal)) Toggle("journal", OpenJournal);
        else if (e.IsActionPressed(Inputs.Map)) Toggle("map", OpenMap); else if (e.IsActionPressed(Inputs.Tree)) Toggle("tree", OpenTree);
        else if (e.IsActionPressed(Inputs.SkillsUi)) Toggle("skills", OpenSkills);
        else if (e.IsActionPressed(Inputs.AttacksUi)) Toggle("attacks", () => OpenAttacks(null));
        else if (e.IsActionPressed(Inputs.ModsUi)) Toggle("mods", OpenMods);
        else if (e.IsActionPressed(Inputs.SpellcraftUi)) Toggle("spellcraft", () => OpenSpellcraft(null));
    }
    void Toggle(string name, Action open) { if (_panelName == name) Close(); else if (_panel == null && Player.Instance != null && !Player.Instance.Dead) open(); }

    // ---------------------------------------------------------------- panel frame
    /// <summary>Slim left column over a darkened, desaturated live world (pause menu). Returns the column's content box.</summary>
    VBoxContainer FrameSlim(string name, Action rebuild)
    {
        _scrapMode = false; CloseNoUnpause(); _panelName = name; _rebuild = rebuild; Sfx.UiPanel(name);
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
        CloseNoUnpause(); _panelName = name; _rebuild = rebuild; Sfx.UiPanel(name);
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
        return v;
    }
    // leaving the menus turns Disassemble back off
    public void Close() { Sfx.UiClosed(); _confirmQuit = false; _confirmUnstuck = false; _scrapMode = false; CloseNoUnpause(); if (InGame) { GetTree().Paused = false; Input.MouseMode = Input.MouseModeEnum.Captured; } }
    void CloseNoUnpause() { HideTip(); if (_panel != null) { _panel.QueueFree(); _panel = null; _panelName = ""; _rebuild = null; } }

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

    // ---------------------------------------------------------------- shop and smith
    static int _stockLvl = -1; static List<Item> _stock = new();
    static int BuyPrice(Item it) => Mathf.RoundToInt(it.Value * Tune.F("shop.buyMul", 3) + Tune.F("shop.buyAdd", 12));   // data/tuning.json "game.shop"
    public void OpenShop(string tab)
    {
        var d = Game.D; if (_stockLvl != d.Level) { _stockLvl = d.Level; _stock.Clear(); for (int i = 0; i < 6; i++) _stock.Add(Items.Roll(d.Level + 1, .2f)); }
        var v = Frame("shop", "Brynn's Wares", new Vector2(720, 560), () => OpenShop(tab));
        var top = new HBoxContainer(); v.AddChild(top); Tabs(top, tab, ("buy", "Buy", () => OpenShop("buy")), ("sell", "Sell", () => OpenShop("sell"))); top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); Lbl($"{d.Gold} gold", 20, Gold, top);
        var sc = Scroll(v, 420); var list = Inner(sc); list.AddThemeConstantOverride("separation", 6); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        if (tab == "buy")
        {
            int hpc = Tune.I("shop.healthPotion", 30), mpc = Tune.I("shop.manaPotion", 35), hc = Tune.I("shop.horse", 350);
            AddRow(list, "Health potion  (have " + d.HpPot + ")", Colors.White, $"Buy {hpc}g", () => { if (d.Gold < hpc) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hpc; d.HpPot++; Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= hpc);
            AddRow(list, "Mana potion  (have " + d.MpPot + ")", Colors.White, $"Buy {mpc}g", () => { if (d.Gold < mpc) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= mpc; d.MpPot++; Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= mpc);
            if (!d.Horse) AddRow(list, "Horse (ride with T)", Colors.White, $"Buy {hc}g", () => { if (d.Gold < hc) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hc; d.Horse = true; Sfx.Play("coin"); Note("You bought a horse. Press T to ride.", Gold); OpenShop(tab); }, d.Gold >= hc);
            for (int i = 0; i < _stock.Count; i++) { int k = i; var it = _stock[i]; ItemRow(list, it, BuyPrice(it) + "g", () => { int c = BuyPrice(it); if (d.Gold < c) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Gold -= c; d.Inv.Add(it); _stock.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }, d.Gold >= BuyPrice(it)); }
        }
        else
        {
            if (d.Inv.Count == 0 && d.Mats.Count == 0) Empty(list, "Your pack is empty. Nothing here to sell.");
            // gathered materials sell by the stack
            // "sell all" keeps crafting scraps (sell those one stack at a time below)
            int matGold = 0; foreach (var kv in d.Mats) if (!GatherDb.Mat(kv.Key).Scrap) matGold += kv.Value * GatherDb.Mat(kv.Key).Value;
            if (matGold > 0) list.AddChild(Btn($"Sell all gathered materials ({matGold}g, keeps scraps)", () => { d.Gold += matGold; foreach (var k in d.Mats.Keys.ToList()) if (!GatherDb.Mat(k).Scrap) d.Mats.Remove(k); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); }, true));
            foreach (var kv in new List<KeyValuePair<string, int>>(d.Mats))
            {
                if (kv.Value <= 0) continue; var m = GatherDb.Mat(kv.Key); string id = kv.Key; int n = kv.Value;
                AddRow(list, $"{m.Name}  x{n}", m.Col.Lightened(0.25f), $"Sell {n * m.Value}g", () => { d.Gold += n * m.Value; d.Mats.Remove(id); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); });
            }
            list.AddChild(Btn("Sell all Common", () => { int g = 0; d.Inv.RemoveAll(it => { if (it.Rar == 0) { g += it.Value; return true; } return false; }); d.Gold += g; if (g > 0) Sfx.Play("coin"); OpenShop(tab); }, true));
            for (int i = 0; i < d.Inv.Count; i++) { int k = i; var it = d.Inv[i]; ItemRow(list, it, "Sell " + it.Value + "g", () => { d.Gold += it.Value; d.Inv.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }); }
        }
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
    // ---------------------------------------------------------------- valor quartermaster (data/valor.json)
    static readonly List<Item> _lastPulls = new();
    public void OpenValor(string tab)
    {
        var d = Game.D; var v = Frame("valor", "Sera, Valor Quartermaster", new Vector2(760, 580), () => OpenValor(tab));
        var top = new HBoxContainer(); v.AddChild(top);
        Tabs(top, tab, ("gacha", "Pulls", () => OpenValor("gacha")), ("gear", "Gear", () => OpenValor("gear")), ("gold", "Gold", () => OpenValor("gold")));
        top.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }); Lbl($"{d.Valor} valor", 20, new Color(1f, .78f, .35f), top); Lbl($"   {d.Gold} gold", 18, Gold, top);
        var sc = Scroll(v, 430); var list = Inner(sc); list.AddThemeConstantOverride("separation", 6); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        bool Pay(int c) { if (d.Valor < c) { Note("Not enough valor", new Color(1, .5f, .5f)); return false; } d.Valor -= c; return true; }
        bool Room(int n) { if (d.Inv.Count + n > 28) { Note("Pack is full", new Color(1, .5f, .5f)); return false; } return true; }
        if (tab == "gacha")
        {
            Row(list, $"A sealed cache of arena spoils: one random item at your level. Every {Valor.Pity} pulls without an epic, the next is at least epic ({d.GachaPity}/{Valor.Pity}).", Parch, 16); Header("Pulls", list);
            AddRow(list, "One pull", Colors.White, $"{Valor.PullCost} valor", () => { if (!Room(1) || !Pay(Valor.PullCost)) return; _lastPulls.Clear(); var it = Valor.Pull(); d.Inv.Add(it); _lastPulls.Add(it); Sfx.Play("level"); Game.Notify(); OpenValor(tab); }, d.Valor >= Valor.PullCost);
            AddRow(list, "Ten pulls", Colors.White, $"{Valor.PullTenCost} valor", () => { if (!Room(10) || !Pay(Valor.PullTenCost)) return; _lastPulls.Clear(); for (int i = 0; i < 10; i++) { var it = Valor.Pull(); d.Inv.Add(it); _lastPulls.Add(it); } Sfx.Play("level"); Game.Notify(); OpenValor(tab); }, d.Valor >= Valor.PullTenCost);
            if (_lastPulls.Count > 0) { Header("You received", list); foreach (var it in _lastPulls) ItemRow(list, it, "", null); }
        }
        else if (tab == "gear")
        {
            Row(list, "Pick a slot and a rarity: a new item at your level.", Parch, 16); Header("Gear", list);
            foreach (var slot in Valor.GearSlots)
                for (int rar = 2; rar <= 4; rar++)
                {
                    int c = Valor.GearCost(rar); if (c <= 0) continue; string s = slot; int r = rar;
                    AddRow(list, $"{Items.RarityName(rar)} {slot}", Items.RarityColor(rar), $"{c} valor", () => { if (!Room(1) || !Pay(c)) return; var it = Valor.Gear(s, r); d.Inv.Add(it); Note("Received " + it.Name, it.Color); Sfx.Play("coin"); Game.Notify(); OpenValor(tab); }, d.Valor >= c);
                }
        }
        else
        {
            Row(list, "Trade valor for gold.", Parch, 16); Header("Gold", list); int nTr = 0;
            foreach (var (val, gold) in Valor.GoldTrades())
            { nTr++; AddRow(list, $"{gold} gold", Gold, $"{val} valor", () => { if (!Pay(val)) return; d.Gold += gold; Sfx.Play("coin"); Game.Notify(); OpenValor(tab); }, d.Valor >= val); }
            if (nTr == 0) Empty(list, "No trades on offer.");
        }
        v.AddChild(Btn("Leave", Close));
    }


    // ---------------------------------------------------------------- pack
    // grid pack: paper-doll slots on the left, a 7x4 icon grid on the right, a hover tooltip with comparison arrows.
    // Click an item to equip it, right-click to drop it; click an equipped slot to take it off.
    ItemTip? _tip;
    public void OpenPack()
    {
        var d = Game.D; var v = Frame("pack", "Pack and Equipment", new Vector2(900, 620), OpenPack);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 22);
        Lbl($"{d.Gold} G", 18, Gold, top); Lbl($"Health potions {d.HpPot} (H)", 16, new Color("ff8f86"), top); Lbl($"Mana potions {d.MpPot} (G)", 16, new Color("8fb8ff"), top);
        var cap = Lbl($"Pack {d.Inv.Count}/28", 16, d.Inv.Count >= 26 ? new Color(1, .6f, .5f) : Dim, top); cap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; cap.HorizontalAlignment = HorizontalAlignment.Right;
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 22); v.AddChild(cols);
        // ---- left: equipment slots around a silhouette, stats, materials
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        Lbl("EQUIPPED", 15, UiTheme.Accent, left);
        var doll = new GridContainer { Columns = 2 }; doll.AddThemeConstantOverride("h_separation", 8); doll.AddThemeConstantOverride("v_separation", 6); left.AddChild(doll);
        foreach (var slot in PlayerStats.Slots)
        {
            var it = d.Eq.GetValueOrDefault(slot); string sl = slot;
            var cell = new ItemSlot { Item = it, Empty = slot, Wide = true, CustomMinimumSize = new Vector2(144, 50) }; doll.AddChild(cell);
            cell.Hover += on => ShowTip(on ? it : null, cell, null);
            cell.Clicked += right => { if (it == null) return; if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Eq[sl] = null; d.Inv.Add(it); Sfx.Play("click"); Game.Notify(); OpenPack(); };
        }
        var stats = new GridContainer { Columns = 2 }; stats.AddThemeConstantOverride("h_separation", 18); left.AddChild(stats);
        foreach (var (k, val) in new[] { ("Damage", $"{d.WpnDmg():0}"), ("Defense", $"{d.Defense()}"), ("Crit", $"{d.CritChance() * 100:0}%"), ("Spell power", $"x{d.SpellPow():0.00}"), ("Health", $"{d.MaxHp()}"), ("Mana", $"{d.MaxMp()}") })
        { var row = new HBoxContainer { CustomMinimumSize = new Vector2(138, 0) }; stats.AddChild(row); var a = Lbl(k, 14, Dim, row); a.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; Lbl(val, 14, Colors.White, row); }
        Lbl("MATERIALS", 15, UiTheme.Accent, left);
        var mg = new GridContainer { Columns = 5 }; mg.AddThemeConstantOverride("h_separation", 6); mg.AddThemeConstantOverride("v_separation", 6); left.AddChild(mg); int kinds = 0;
        foreach (var kv in d.Mats) if (kv.Value > 0)
        {
            var m = GatherDb.Mat(kv.Key); var cell = new ItemSlot { IconId = m.Name, Count = kv.Value, Tint = m.Col, CustomMinimumSize = new Vector2(52, 52), TooltipText = $"{m.Name} x{kv.Value}  ({m.Value}g each)" }; mg.AddChild(cell); kinds++;
        }
        if (kinds == 0) { var l = Row(left, "Nothing gathered yet. Press E near herbs, mushrooms, berries, ore, crystals and fallen logs.", Dim, 13); l.CustomMinimumSize = new Vector2(290, 0); }
        // ---- right: 7 x 4 item grid
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 6); cols.AddChild(right);
        var hdr = new HBoxContainer(); right.AddChild(hdr); var il = Lbl("ITEMS", 15, UiTheme.Accent, hdr); il.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hdr.AddChild(Btn("Sort", () => { d.Inv.Sort((a, b) => b.Rar != a.Rar ? b.Rar.CompareTo(a.Rar) : string.Compare(a.Slot, b.Slot, StringComparison.Ordinal)); Sfx.Play("click"); OpenPack(); }, true));
        var sb = Btn(_scrapMode ? "Disassemble: ON" : "Disassemble", () => { _scrapMode = !_scrapMode; OpenPack(); }, true); Mark(sb, _scrapMode); hdr.AddChild(sb);
        hdr.AddChild(Btn("Scrap commons", () =>
        {
            int n = 0; var parts = new Dictionary<string, int>();
            foreach (var it in d.Inv.FindAll(x => x.Rar == 0 && x.Set == null && !x.Unique)) { foreach (var (id, c) in LootTables.Scraps(it)) { d.Mats[id] = d.Mats.GetValueOrDefault(id) + c; parts[id] = parts.GetValueOrDefault(id) + c; } d.Inv.Remove(it); n++; }
            if (n > 0) { Note($"Scrapped {n} common items: " + string.Join(", ", parts.Select(kv => $"+{kv.Value} {GatherDb.Mat(kv.Key).Name}")), Dim); Sfx.Play("hit"); }
            Game.Notify(); OpenPack();
        }, true));
        hdr.AddChild(Btn("Pickup filter", OpenPickFilter, true));
        hdr.AddChild(Btn("Crafting", () => OpenCraft("gear"), true));
        var grid = new GridContainer { Columns = 7 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); right.AddChild(grid);
        for (int i = 0; i < 28; i++)
        {
            int k = i; var it = i < d.Inv.Count ? d.Inv[i] : null;
            var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(66, 66) }; grid.AddChild(cell);
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
        Lbl(_scrapMode ? "Disassemble is ON: click an item to break it into scraps for crafting.     Right-click: drop on the ground" : "Click: equip     Right-click: drop on the ground     Hover: compare with what you wear", 13, _scrapMode ? new Color(1, .7f, .45f) : Dim, right);
        string filt = d.PickRar > 0 || d.PickSkip.Count > 0 ? $"Pickup filter: {Items.RarityName(d.PickRar)} and better" + (d.PickSkip.Count > 0 ? $", skipping {string.Join(", ", d.PickSkip)}" : "") + (d.PickScrap ? " (the rest is scrapped)" : " (the rest stays on the ground)") : "Pickup filter: everything";
        Lbl(filt, 12, Dim, right);
    }
    static bool _scrapMode;
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
    /// <summary>Panel listing every bonus you currently have, with where the numbers come from.</summary>
    public void OpenTotals()
    {
        var v = Frame("totals", "All bonuses", new Vector2(620, 600), OpenTotals);
        Header($"{Races.Get(Game.D.Race).Name} {Classes.Get(Game.D.Class).Name}", v);
        Row(v, $"Skill tree ({Game.D.Tree.Count} nodes) + race + class + active buffs", Dim, 14);
        var sc = Scroll(v, 440); sc.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled; var tl = Inner(sc); tl.AddThemeConstantOverride("separation", 4);
        var keys = new List<string>(); foreach (var k in TreeDb.Known) if (Mathf.Abs(Game.TS[k]) > .001f) keys.Add(k); keys.Sort();
        if (keys.Count == 0) Empty(tl, "No bonuses yet. Spend points in the skill tree.");
        foreach (var k in keys) { var h = new HBoxContainer(); CardBox(tl).AddChild(h); Row(h, TreeDb.StatText(k, Game.TS[k]), Parch, 15).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        v.AddChild(Btn("Back to tree", OpenTree));
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
            _travelAt = Time.GetTicksMsec() / 1000f + Tune.F("fastTravel.cooldown", 30f); Close(); World.ForceRealm(0); Player.Instance?.Teleport(World.TravelPoint(poi), 0f); Ui.Flash(Colors.White); Ui.Note("Travelled to " + poi.Name, Gold); };
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

/// <summary>
/// Interactive passive tree (about 420 nodes). Opens centred on your class start. Wheel zooms toward the cursor, drag pans,
/// left click takes a node next to your path, or the whole shortest path to a far node when you have the points; right click refunds
/// one that nothing else depends on. Hovering shows the node in a card by the mouse and the path to it. Search lights up matches.
/// The view (zoom and pan) is kept while the menu stays open, so taking a node does not throw you back to your start.
/// </summary>
public partial class TreeView : Control
{
    public Action? Changed; public Action<int, Vector2>? HoverAt;
    public static bool HasSaved; static float _sZoom; static Vector2 _sPan;
    float _zoom = 1f; Vector2 _pan = Vector2.Zero; bool _drag; int _hover = -1; float _scale = 1f; int _start;
    List<int>? _path; readonly HashSet<int> _match = new();
    static Font F => ThemeDB.FallbackFont;
    Vector2 ToScreen(TreeNode n) => Size / 2 + _pan + new Vector2(n.X, n.Y) * _scale * _zoom;
    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; TreeDb.Load(); _start = TreeDb.StartFor(Game.D.Class);
        float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f; foreach (var n in TreeDb.Nodes) { minx = Mathf.Min(minx, n.X); maxx = Mathf.Max(maxx, n.X); miny = Mathf.Min(miny, n.Y); maxy = Mathf.Max(maxy, n.Y); }
        var box = CustomMinimumSize == Vector2.Zero ? new Vector2(960, 560) : CustomMinimumSize;
        _scale = Mathf.Min((box.X - 30) / (maxx - minx + 60), (box.Y - 20) / (maxy - miny + 60));
        if (HasSaved) { _zoom = _sZoom; _pan = _sPan; } else CenterOn(TreeDb.Get(_start) ?? TreeDb.Nodes[0], 3.2f);
        MouseExited += () => { _hover = -1; _path = null; HoverAt?.Invoke(-1, Vector2.Zero); QueueRedraw(); };
    }
    void Save() { HasSaved = true; _sZoom = _zoom; _sPan = _pan; }
    public void CenterOn(TreeNode n, float zoom) { _zoom = zoom; _pan = -new Vector2(n.X, n.Y) * _scale * _zoom; Save(); QueueRedraw(); }
    public void ZoomBy(float f) => ZoomAt(Size / 2, f);
    public void SetSearch(string s)
    {
        _match.Clear(); s = s.Trim().ToLowerInvariant();
        if (s.Length >= 2) foreach (var n in TreeDb.Nodes) if (n.Name.ToLowerInvariant().Contains(s) || n.Desc.ToLowerInvariant().Contains(s) || n.Mods.Any(kv => kv.Key.ToLowerInvariant().Contains(s) || TreeDb.StatText(kv.Key, kv.Value).ToLowerInvariant().Contains(s))) _match.Add(n.Id);
        QueueRedraw();
    }
    public int Matches => _match.Count;
    /// <summary>Shortest run of untaken nodes from what you have to n (n last); null if taken or unreachable.</summary>
    public List<int>? PathTo(int target)
    {
        var have = new HashSet<int>(Game.D.Tree) { _start }; if (have.Contains(target)) return null;
        var prev = new Dictionary<int, int>(); var q = new Queue<int>(); foreach (var h in have) { prev[h] = -1; q.Enqueue(h); }
        while (q.Count > 0)
        {
            int c = q.Dequeue(); if (c == target) break;
            foreach (var a in TreeDb.Nodes[c].Adj) { if (prev.ContainsKey(a)) continue; prev[a] = c; q.Enqueue(a); }   // other class starts cost a point like any node
        }
        if (!prev.ContainsKey(target)) return null;
        var path = new List<int>(); for (int c = target; c != -1 && !have.Contains(c); c = prev[c]) path.Add(c); path.Reverse(); return path;
    }
    float Rad(TreeNode n) => (n.Type == "start" ? 15f : n.Type == "hub" ? 13f : n.Type == "key" ? 12f : n.Type == "notable" ? 8.5f : 5f) * Mathf.Clamp(_zoom / 2.5f, 0.55f, 1.5f);
    public override void _Draw()
    {
        var d = Game.D; var alloc = new HashSet<int>(d.Tree); alloc.Add(_start); var set = new HashSet<int>(d.Tree);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(.02f, .03f, .06f));
        var c0 = Size / 2 + _pan; foreach (var r in new[] { 400f, 900f, 1520f }) DrawArc(c0, r * _scale * _zoom, 0, Mathf.Tau, 96, new Color(.15f, .22f, .32f, .35f), 1f);
        var onPath = _path != null ? new HashSet<int>(_path) : new HashSet<int>();
        bool afford = _path != null && _path.Count <= d.SkillPts;
        var pathCol = afford ? new Color(.45f, 1f, .6f) : new Color(1f, .55f, .45f);
        foreach (var n in TreeDb.Nodes) foreach (var a in n.Adj) if (a > n.Id)
        {
            var m = TreeDb.Nodes[a]; bool on = alloc.Contains(n.Id) && alloc.Contains(a); var p0 = ToScreen(n); var p1 = ToScreen(m);
            if (!GetRect().Grow(400).HasPoint(p0 + Position) && !GetRect().Grow(400).HasPoint(p1 + Position)) continue;
            bool pl = (onPath.Contains(n.Id) || alloc.Contains(n.Id)) && (onPath.Contains(a) || alloc.Contains(a)) && (onPath.Contains(n.Id) || onPath.Contains(a));
            DrawLine(p0, p1, on ? new Color(.95f, .8f, .4f) : pl ? pathCol : new Color(.25f, .27f, .34f), on ? 3f : pl ? 2.6f : 1.4f);
        }
        foreach (var n in TreeDb.Nodes)
        {
            var p = ToScreen(n); if (p.X < -40 || p.Y < -40 || p.X > Size.X + 40 || p.Y > Size.Y + 40) continue;
            bool on = alloc.Contains(n.Id); bool can = TreeDb.CanAlloc(n, set, d.SkillPts, _start); float r = Rad(n);
            if (n.Type == "key") DrawColoredPolygon(new[] { p + new Vector2(0, -r - 4), p + new Vector2(r + 4, 0), p + new Vector2(0, r + 4), p + new Vector2(-r - 4, 0) }, on ? Colors.White : can ? new Color(.9f, .9f, .5f) : new Color(.25f, .25f, .3f));
            else DrawCircle(p, r + 2, on ? Colors.White : can ? new Color(.9f, .9f, .5f) : n.Type == "start" ? n.Col.Darkened(.3f) : new Color(.2f, .2f, .26f));
            DrawCircle(p, r, on ? n.Col : n.Col.Darkened(n.Type == "start" ? .3f : .65f));
            if (onPath.Contains(n.Id)) DrawArc(p, r + 4, 0, Mathf.Tau, 20, pathCol, 2f);
            if (_match.Contains(n.Id)) { DrawArc(p, r + 7, 0, Mathf.Tau, 24, new Color(.35f, .95f, 1f), 2.5f); DrawArc(p, r + 11, 0, Mathf.Tau, 24, new Color(.35f, .95f, 1f, .35f), 2f); }
            if (n.Type == "start" && n.Id == _start) DrawArc(p, r + 7, 0, Mathf.Tau, 32, UiTheme.Highlight, 2.5f);
            if (_hover == n.Id) DrawArc(p, r + 5, 0, Mathf.Tau, 24, Colors.White, 2f);
        }
        foreach (var n in TreeDb.Nodes)
        {
            if (!(n.Type == "start" || (n.Type == "key" && _zoom > 1.6f) || (n.Type == "notable" && _zoom > 3.6f))) continue;
            var p = ToScreen(n); if (p.X < -40 || p.Y < -40 || p.X > Size.X + 40 || p.Y > Size.Y + 40) continue;
            float r = Rad(n); int fs = n.Type == "start" ? 15 : n.Type == "key" ? 13 : 12;
            var col = n.Type == "start" ? n.Col.Lightened(.35f) : n.Type == "key" ? new Color(1f, .85f, .5f) : new Color(.8f, .85f, .95f);
            float w = F.GetStringSize(n.Name, HorizontalAlignment.Left, -1, fs).X;
            var tl = new Vector2(Mathf.Clamp(p.X - w / 2, 4, Mathf.Max(4, Size.X - w - 4)), Mathf.Clamp(p.Y + r + 6, 4, Size.Y - fs - 8));
            DrawRect(new Rect2(tl - new Vector2(4, 1), new Vector2(w + 8, fs + 6)), new Color(0, 0, 0, .72f));
            DrawString(F, tl + new Vector2(0, fs), n.Name, HorizontalAlignment.Left, -1, fs, col);
        }
    }
    int Pick(Vector2 pos) { int best = -1; float bd = 1e9f; foreach (var n in TreeDb.Nodes) { float d = ToScreen(n).DistanceTo(pos); if (d < Rad(n) + 6 && d < bd) { bd = d; best = n.Id; } } return best; }
    void ZoomAt(Vector2 mouse, float factor)
    {
        float z2 = Mathf.Clamp(_zoom * factor, 0.7f, 9f); var c = Size / 2; _pan = mouse - c - (mouse - c - _pan) * (z2 / _zoom); _zoom = z2; Save(); QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            if (_drag) { _pan += mm.Relative; Save(); QueueRedraw(); }
            int h = Pick(mm.Position);
            if (h != _hover) { _hover = h; _path = h >= 0 ? PathTo(h) : null; QueueRedraw(); }
            HoverAt?.Invoke(_hover, GetGlobalMousePosition());
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) ZoomAt(mb.Position, 1.15f);
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) ZoomAt(mb.Position, 1 / 1.15f);
            else if (mb.ButtonIndex == MouseButton.Middle) { _drag = mb.Pressed; }
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                int id = Pick(mb.Position); var d = Game.D;
                if (id < 0) { _drag = true; return; }
                var path = PathTo(id);
                if (path == null || path.Count == 0) { Sfx.Play("click"); return; }
                if (path.Count > d.SkillPts) { Ui.Note($"That path takes {path.Count} skill tree points; you have {d.SkillPts}", new Color(1f, .6f, .5f)); Sfx.Play("click"); return; }
                foreach (var pid in path) { var set = new HashSet<int>(d.Tree); var pn = TreeDb.Nodes[pid]; if (!TreeDb.CanAlloc(pn, set, d.SkillPts, _start)) break; d.Tree.Add(pid); d.SkillPts--; }
                Sfx.Play("level"); _path = null; Changed?.Invoke();
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed) _drag = false;
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                int id = Pick(mb.Position); if (id < 0) return; var n = TreeDb.Nodes[id]; var set = new HashSet<int>(Game.D.Tree);
                if (TreeDb.CanDealloc(n, set, _start)) { Game.D.Tree.Remove(id); Game.D.SkillPts++; Sfx.Play("click"); Changed?.Invoke(); }
                else if (set.Contains(id)) Ui.Note("Refund the nodes further out first: everything you take must stay connected to your start", new Color(1f, .6f, .5f));
            }
        }
    }
}/// <summary>World map with discovered points of interest.</summary>
public partial class MapView : Control
{
    public Action<Poi>? Travel;
    // zoom and pan: the world map keeps them while the game runs; a dungeon floor opens centred on you
    static float _zoomW = 1f; static Vector2 _panW;
    float _zoomD = 1.6f; Vector2 _panD; bool _dInit, _drag; float _moved;
    bool Dun => Dungeon.Active;
    float Zoom { get => Dun ? _zoomD : _zoomW; set { if (Dun) _zoomD = value; else _zoomW = value; } }
    Vector2 Pan { get => Dun ? _panD : _panW; set { if (Dun) _panD = value; else _panW = value; } }
    static Vector2 UV(float x, float z) => Dungeon.Active ? new Vector2((x - Dungeon.Origin.X) / (Dungeon.GW * Dungeon.Tile), (z - Dungeon.Origin.Z) / (Dungeon.GH * Dungeon.Tile)) : World.MapUV(x, z);
    Vector2 FromUV(Vector2 uv) => Size / 2 + Pan + (uv - new Vector2(.5f, .5f)) * Size * Zoom;
    Vector2 W2S(float x, float z) => FromUV(UV(x, z));
    Font F => ThemeDB.FallbackFont;
    public override void _Ready() { MouseFilter = MouseFilterEnum.Stop; ClipContents = true; }
    void Label(Vector2 at, string s, int size, Color col) { DrawStringOutline(F, at, s, HorizontalAlignment.Left, -1, size, 4, new Color(0, 0, 0, .85f)); DrawString(F, at, s, HorizontalAlignment.Left, -1, size, col); }
    public override void _Draw()
    {
        var me = Player.Instance;
        if (Dun && !_dInit && Size.X > 0 && me != null) { _dInit = true; _panD = -(UV(me.GlobalPosition.X, me.GlobalPosition.Z) - new Vector2(.5f, .5f)) * Size * _zoomD; ClampPan(); }
        DrawRect(new Rect2(Vector2.Zero, Size), Dun ? new Color(.03f, .035f, .05f) : new Color(.2f, .3f, .2f));
        if (Dun)
        {
            // the floor as far as you have explored it
            float cell = Size.X * Zoom / Dungeon.GW;
            for (int z = 0; z < Dungeon.GH; z++) for (int x = 0; x < Dungeon.GW; x++)
            {
                if (!Dungeon.Seen(x, z)) continue; bool fl = Dungeon.Floor(x, z); if (!fl && !Dungeon.Wall(x, z)) continue;
                var c = Dungeon.CellCentre(x, z); var s = W2S(c.X, c.Y);
                DrawRect(new Rect2(s - new Vector2(cell, cell) / 2, new Vector2(cell + .5f, cell + .5f)), fl ? new Color(.42f, .45f, .5f) : new Color(.16f, .17f, .22f));
            }
            foreach (var (pos, down) in Dungeon.Pads)
            {
                int px = Mathf.FloorToInt((pos.X - Dungeon.Origin.X) / Dungeon.Tile), pz = Mathf.FloorToInt((pos.Z - Dungeon.Origin.Z) / Dungeon.Tile); if (!Dungeon.Seen(px, pz)) continue;
                var s = W2S(pos.X, pos.Z); DrawCircle(s, 7, down ? new Color(1f, .75f, .38f) : new Color(.5f, .88f, 1f)); Label(s + new Vector2(10, 5), down ? "Way down" : "Exit", 14, Colors.White);
            }
        }
        else
        {
            if (World.MapTex != null) DrawTextureRect(World.MapTex, new Rect2(FromUV(Vector2.Zero), Size * Zoom), false);
            foreach (var p in World.Pois)
            {
                bool known = Game.D.Disc.Contains(p.Id); var s = W2S(p.X, p.Z);
                DrawCircle(s, known ? 7 : 4, known ? new Color(1f, .82f, .29f) : new Color(.3f, .3f, .3f));
                if (known) Label(s + new Vector2(10, 5), p.Name, 15, Colors.White);
            }
        }
        // party members, then you on top
        foreach (var fr in MapMarks.Friends()) { var s = W2S(fr.Pos.X, fr.Pos.Z); MapMarks.Dot(this, s, 6, fr.Party ? MapMarks.PartyCol : MapMarks.OtherCol); Label(s + new Vector2(9, -6), fr.Name, 14, fr.Party ? MapMarks.PartyCol : MapMarks.OtherCol); }
        if (me != null && !Arenas.Active) { var pp = me.GlobalPosition; MapMarks.Dot(this, W2S(pp.X, pp.Z), 6, new Color(1f, .2f, .2f)); }
    }
    void ClampPan() { var lim = Size * Zoom / 2; Pan = new Vector2(Mathf.Clamp(Pan.X, -lim.X, lim.X), Mathf.Clamp(Pan.Y, -lim.Y, lim.Y)); }
    void ZoomAt(Vector2 m, float k)
    {
        float z2 = Mathf.Clamp(Zoom * k, 1f, 8f); var c = Size / 2; Pan = m - c - (m - c - Pan) * (z2 / Zoom); Zoom = z2; ClampPan(); QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm && _drag) { Pan += mm.Relative; _moved += mm.Relative.Length(); ClampPan(); QueueRedraw(); AcceptEvent(); return; }
        if (e is not InputEventMouseButton mb) return;
        AcceptEvent();   // the wheel zooms the map, not the window around it
        if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) ZoomAt(mb.Position, 1.2f);
        else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) ZoomAt(mb.Position, 1 / 1.2f);
        else if (mb.ButtonIndex is MouseButton.Left or MouseButton.Right or MouseButton.Middle)
        {
            if (mb.Pressed) { _drag = true; _moved = 0; return; }
            _drag = false;
            // a click that did not drag: fast travel to a discovered place
            if (mb.ButtonIndex == MouseButton.Left && _moved < 5 && !Dun)
                foreach (var p in World.Pois) if (Game.D.Disc.Contains(p.Id) && W2S(p.X, p.Z).DistanceTo(mb.Position) < 14) { Travel?.Invoke(p); return; }
        }
    }
    public override void _Process(double delta) { QueueRedraw(); }   // friends move
}
