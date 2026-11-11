using System;
using System.Collections.Generic;
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
    public static void Note(string text, Color? col = null) => I?.AddNote(text, col ?? Colors.White);
    public static void Banner(string title, string sub = "") => I?.ShowBanner(title, sub);
    public static void Flash(Color c) => I?.DoFlash(c);

    public override void _Ready()
    {
        I = this; Layer = 20; ProcessMode = ProcessModeEnum.Always;
        _notes = new VBoxContainer { MouseFilter = Control.MouseFilterEnum.Ignore }; _notes.Position = new Vector2(22, 120); _notes.CustomMinimumSize = new Vector2(420, 0); _notes.AddThemeConstantOverride("separation", 3); AddChild(_notes);
        _banner = Lbl("", 52, UiTheme.Title); _banner.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.1f, 0.2f)); _banner.AddThemeConstantOverride("outline_size", 10); _banner.HorizontalAlignment = HorizontalAlignment.Center; _banner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _banner.OffsetTop = 130; _banner.OffsetLeft = -400; _banner.OffsetRight = 400; _banner.Modulate = new Color(1, 1, 1, 0);
        _bannerSub = Lbl("", 20, UiTheme.Accent); _bannerSub.HorizontalAlignment = HorizontalAlignment.Center; _bannerSub.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _bannerSub.OffsetTop = 200; _bannerSub.OffsetLeft = -400; _bannerSub.OffsetRight = 400; _bannerSub.Modulate = new Color(1, 1, 1, 0);
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore }; _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_flash);
        Game.Changed += OnChanged;
    }
    void OnChanged() { if (_rebuild != null && _panel != null) _rebuild(); }
    public override void _ExitTree() { Game.Changed -= OnChanged; if (I == this) I = null; }

    public override void _Process(double delta) => Feel.Tick(); // Ui always processes, so hit-stop always ends

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
    }
    void Toggle(string name, Action open) { if (_panelName == name) Close(); else if (_panel == null && Player.Instance != null && !Player.Instance.Dead) open(); }

    // ---------------------------------------------------------------- panel frame
    VBoxContainer Frame(string name, string title, Vector2 size, Action rebuild)
    {
        CloseNoUnpause(); _panelName = name; _rebuild = rebuild;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = MakeTheme() }; root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        if (name == "title") root.AddChild(new ColorRect { Color = new Color(.005f, .02f, .05f, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var bd = new TechBackdrop(); bd.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); root.AddChild(bd);
        var pc = new TechPanel(); var ps = UiTheme.Box(UiTheme.Panel, UiTheme.AccentSoft, 1, 22, 14, UiTheme.GlowSize); pc.AddThemeStyleboxOverride("panel", ps);
        var vp = GetViewport().GetVisibleRect().Size; // never ask for more than the window has
        pc.CustomMinimumSize = new Vector2(Mathf.Min(size.X, vp.X - 24), Mathf.Min(size.Y, vp.Y - 24)); pc.SetAnchorsPreset(Control.LayoutPreset.Center); pc.GrowHorizontal = Control.GrowDirection.Both; pc.GrowVertical = Control.GrowDirection.Both; root.AddChild(pc);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); pc.AddChild(v);
        var hb = new HBoxContainer(); v.AddChild(hb); var t = Lbl(title.ToUpper(), 24, UiTheme.Title, hb); t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        t.AddThemeColorOverride("font_outline_color", new Color(0.02f, 0.12f, 0.25f)); t.AddThemeConstantOverride("outline_size", 6);
        if (name != "title") { var cb = Btn("X", Close); cb.CustomMinimumSize = new Vector2(38, 34); hb.AddChild(cb); }
        // decoration overlay sized to the panel; the header rule sits just under the title row
        pc.Resized += () => { pc.HeaderY = 14 + hb.Size.Y + 5; pc.PivotOffset = pc.Size / 2; pc.QueueRedraw(); };
        AddChild(root); _panel = root;
        // system-window pop: fade in and grow from the centre
        root.Modulate = new Color(1, 1, 1, 0); pc.PivotOffset = pc.Size / 2; pc.Scale = new Vector2(1f, 0.92f);
        var tw = root.CreateTween().SetParallel(); tw.TweenProperty(root, "modulate:a", 1f, UiTheme.OpenTime); tw.TweenProperty(pc, "scale", Vector2.One, UiTheme.OpenTime).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);        if (name != "title") { GetTree().Paused = !Net.Active; /* a shared world never pauses */ Input.MouseMode = Input.MouseModeEnum.Visible; }
        return v;
    }
    public void Close() { _confirmQuit = false; CloseNoUnpause(); if (InGame) { GetTree().Paused = false; Input.MouseMode = Input.MouseModeEnum.Captured; } }
    void CloseNoUnpause() { HideTip(); if (_panel != null) { _panel.QueueFree(); _panel = null; _panelName = ""; _rebuild = null; } }

    static Button Btn(string t, Action a, bool small = false)
    {
        var b = new Button { Text = t, FocusMode = Control.FocusModeEnum.None }; if (small) b.AddThemeFontSizeOverride("font_size", 14);
        b.Pressed += () => { Sfx.Play("click"); a(); }; return b;
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
        var v = Frame("dialog", n.NpcName, new Vector2(620, 220), () => { });
        var d = Game.D;
        Row(v, n.Role == "arena" ? $"Welcome to the Crimson Arena. Best wave: {d.BestWave}. Best duel streak: {d.BestDuel}. Waves pay valor (first at wave {Valor.NextMilestone(0)}), spent with Sera in Oakhaven; leave by the gate whenever you like." : n.NextLine(), Colors.White, 18);
        foreach (var id in n.QuestIds)
        {
            var q = Quests.ById(id); if (q == null) continue; var s = Game.Q(id);
            if (s.S == 2) v.AddChild(Btn("Report: " + q.Name, () => Say(n, q.Done, ("Claim reward", () => { Quests.Reward(q); Talk(n); }))));
            else if (s.S == 1) v.AddChild(Btn(q.Name + " (in progress)", () => Say(n, q.Goal + (q.Need > 1 ? $" ({s.N}/{q.Need})" : "") + ".", ("Back", () => Talk(n)))));
            else if (Quests.Available(q)) v.AddChild(Btn("Quest: " + q.Name + (q.Lvl > 0 ? $" (lvl {q.Lvl}+)" : ""), () => Say(n, q.Offer, ("Accept", () => { Quests.Accept(q); Close(); }), ("Not now", () => Talk(n)))));
        }
        if (n.Role == "arena")
        {
            v.AddChild(Btn("Wave Survival", () => { Close(); Arenas.Start("waves"); }));
            v.AddChild(Btn("Shadow Duel (fight your own double)", () => { Close(); Arenas.Start("duel"); }));
        }
        v.AddChild(Btn("Farewell", Close));
    }
    void Say(Npc n, string text, params (string, Action)[] opts)
    {
        var v = Frame("dialog", n.NpcName, new Vector2(620, 220), () => { }); Row(v, text, Colors.White, 18);
        foreach (var (l, a) in opts) v.AddChild(Btn(l, a));
    }

    // ---------------------------------------------------------------- shop and smith
    static int _stockLvl = -1; static List<Item> _stock = new();
    static int BuyPrice(Item it) => it.Value * 3 + 12;
    public void OpenShop(string tab)
    {
        var d = Game.D; if (_stockLvl != d.Level) { _stockLvl = d.Level; _stock.Clear(); for (int i = 0; i < 6; i++) _stock.Add(Items.Roll(d.Level + 1, .2f)); }
        var v = Frame("shop", "Brynn's Wares", new Vector2(720, 560), () => OpenShop(tab));
        var top = new HBoxContainer(); v.AddChild(top); Tabs(top, tab, ("buy", "Buy", () => OpenShop("buy")), ("sell", "Sell", () => OpenShop("sell"))); Lbl($"   {d.Gold} gold", 20, Gold, top);
        var sc = Scroll(v, 420); var list = Inner(sc);
        if (tab == "buy")
        {
            AddRow(list, "Health potion  (have " + d.HpPot + ")", Colors.White, "Buy 30g", () => { if (d.Gold < 30) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= 30; d.HpPot++; Sfx.Play("coin"); OpenShop(tab); });
            AddRow(list, "Mana potion  (have " + d.MpPot + ")", Colors.White, "Buy 35g", () => { if (d.Gold < 35) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= 35; d.MpPot++; Sfx.Play("coin"); OpenShop(tab); });
            if (!d.Horse) AddRow(list, "Horse (ride with T)", Colors.White, "Buy 350g", () => { if (d.Gold < 350) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= 350; d.Horse = true; Sfx.Play("coin"); Note("You bought a horse. Press T to ride.", Gold); OpenShop(tab); });
            for (int i = 0; i < _stock.Count; i++) { int k = i; var it = _stock[i]; ItemRow(list, it, BuyPrice(it) + "g", () => { int c = BuyPrice(it); if (d.Gold < c) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Gold -= c; d.Inv.Add(it); _stock.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }); }
        }
        else
        {
            if (d.Inv.Count == 0) Row(list, "Your pack is empty.");
            // gathered materials sell by the stack
            int matGold = 0; foreach (var kv in d.Mats) matGold += kv.Value * GatherDb.Mat(kv.Key).Value;
            if (matGold > 0) list.AddChild(Btn($"Sell all materials ({matGold}g)", () => { d.Gold += matGold; d.Mats.Clear(); Sfx.Play("coin"); Game.Notify(); OpenShop(tab); }, true));
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
    void ItemRow(Node parent, Item it, string btn, Action? a)
    {
        var d = Game.D; var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); parent.AddChild(h);
        var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(46, 46) }; h.AddChild(cell);
        cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
        var l = Lbl(it.Describe(), 16, it.Color, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.VerticalAlignment = VerticalAlignment.Center;
        if (a != null) { var b = Btn(btn, () => { HideTip(); a(); }, true); b.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; h.AddChild(b); }
    }
    /// <summary>A row of tab buttons with the current one marked.</summary>
    void Tabs(Node parent, string cur, params (string id, string label, Action open)[] tabs)
    {
        foreach (var (id, label, open) in tabs) { var b = Btn(label, open); Mark(b, id == cur); parent.AddChild(b); }
    }

    void AddRow(Node parent, string text, Color col, string btn, Action a)
    {
        var h = new HBoxContainer(); parent.AddChild(h); var l = Lbl(text, 16, col, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; h.AddChild(Btn(btn, a, true));
    }
    // ---------------------------------------------------------------- valor quartermaster (data/valor.json)
    static readonly List<Item> _lastPulls = new();
    public void OpenValor(string tab)
    {
        var d = Game.D; var v = Frame("valor", "Sera, Valor Quartermaster", new Vector2(760, 580), () => OpenValor(tab));
        var top = new HBoxContainer(); v.AddChild(top);
        Tabs(top, tab, ("gacha", "Pulls", () => OpenValor("gacha")), ("gear", "Gear", () => OpenValor("gear")), ("gold", "Gold", () => OpenValor("gold")));
        Lbl($"   {d.Valor} valor", 20, new Color(1f, .78f, .35f), top); Lbl($"   {d.Gold} gold", 18, Gold, top);
        var sc = Scroll(v, 430); var list = Inner(sc);
        bool Pay(int c) { if (d.Valor < c) { Note("Not enough valor", new Color(1, .5f, .5f)); return false; } d.Valor -= c; return true; }
        bool Room(int n) { if (d.Inv.Count + n > 28) { Note("Pack is full", new Color(1, .5f, .5f)); return false; } return true; }
        if (tab == "gacha")
        {
            Row(list, $"A sealed cache of arena spoils: one random item at your level. Every {Valor.Pity} pulls without an epic, the next is at least epic ({d.GachaPity}/{Valor.Pity}).", Colors.White, 16);
            AddRow(list, "One pull", Colors.White, $"{Valor.PullCost} valor", () => { if (!Room(1) || !Pay(Valor.PullCost)) return; _lastPulls.Clear(); var it = Valor.Pull(); d.Inv.Add(it); _lastPulls.Add(it); Sfx.Play("level"); Game.Notify(); OpenValor(tab); });
            AddRow(list, "Ten pulls", Colors.White, $"{Valor.PullTenCost} valor", () => { if (!Room(10) || !Pay(Valor.PullTenCost)) return; _lastPulls.Clear(); for (int i = 0; i < 10; i++) { var it = Valor.Pull(); d.Inv.Add(it); _lastPulls.Add(it); } Sfx.Play("level"); Game.Notify(); OpenValor(tab); });
            if (_lastPulls.Count > 0) { Row(list, "You received:", Dim, 15); foreach (var it in _lastPulls) ItemRow(list, it, "", null); }
        }
        else if (tab == "gear")
        {
            Row(list, "Pick a slot and a rarity: a new item at your level.", Colors.White, 16);
            foreach (var slot in Valor.GearSlots)
                for (int rar = 2; rar <= 4; rar++)
                {
                    int c = Valor.GearCost(rar); if (c <= 0) continue; string s = slot; int r = rar;
                    AddRow(list, $"{Items.RarityName(rar)} {slot}", Items.RarityColor(rar), $"{c} valor", () => { if (!Room(1) || !Pay(c)) return; var it = Valor.Gear(s, r); d.Inv.Add(it); Note("Received " + it.Name, it.Color); Sfx.Play("coin"); Game.Notify(); OpenValor(tab); });
                }
        }
        else
            foreach (var (val, gold) in Valor.GoldTrades())
                AddRow(list, $"{gold} gold", Gold, $"{val} valor", () => { if (!Pay(val)) return; d.Gold += gold; Sfx.Play("coin"); Game.Notify(); OpenValor(tab); });
        v.AddChild(Btn("Leave", Close));
    }

    void OpenSmith()
    {
        var d = Game.D; var w = d.Weapon; int up = w?.Up ?? 0; int cost = 60 * (up + 1) + (w != null ? w.Lvl * 10 : 0);
        var v = Frame("smith", "Dorrin the Smith", new Vector2(560, 260), OpenSmith); Row(v, $"{d.Gold} gold", Gold, 20);
        if (w == null) Row(v, "You have no weapon to sharpen.", Colors.White, 18);
        else if (up >= 5) Row(v, $"That {w.Name} cannot be honed any further.", Colors.White, 18);
        else
        {
            int add = Mathf.CeilToInt(2 + w.Lvl * .6f); Row(v, $"I can hone your {w.Name} (+{up}/5) for {cost}g. It will bite harder.", w.Color, 18);
            v.AddChild(Btn($"Hone (+{add} dmg) for {cost}g", () =>
            {
                if (d.Gold < cost) { Note("Not enough gold", new Color(1, .5f, .5f)); return; }
                d.Gold -= cost; w.Dmg += add; w.Up = up + 1; w.Name = System.Text.RegularExpressions.Regex.Replace(w.Name, @" \+\d$", "") + " +" + w.Up; Sfx.Play("hit"); Sfx.Play("coin"); Game.Notify(); OpenSmith();
            }));
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
        var grid = new GridContainer { Columns = 7 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 6); right.AddChild(grid);
        for (int i = 0; i < 28; i++)
        {
            int k = i; var it = i < d.Inv.Count ? d.Inv[i] : null;
            var cell = new ItemSlot { Item = it, CustomMinimumSize = new Vector2(66, 66) }; grid.AddChild(cell);
            if (it == null) continue;
            cell.Hover += on => ShowTip(on ? it : null, cell, d.Eq.GetValueOrDefault(it.Slot));
            cell.Clicked += rightBtn => { HideTip(); if (rightBtn) { d.Inv.RemoveAt(k); Sfx.Play("click"); OpenPack(); } else Equip(k); };
        }
        Lbl("Click: equip     Right-click: drop     Hover: compare with what you wear", 13, Dim, right);
    }
    void ShowTip(Item? it, Control anchor, Item? worn)
    {
        if (it == null) { HideTip(); return; }
        if (_tip == null || !IsInstanceValid(_tip)) { _tip = new ItemTip(); AddChild(_tip); }
        _tip.Show(it, worn, anchor.GetGlobalRect(), GetViewport().GetVisibleRect().Size);
    }
    void HideTip() { if (_tip != null && IsInstanceValid(_tip)) _tip.Visible = false; }
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
    void OpenCharacterPanel()
    {
        var d = Game.D; var v = Frame("char", $"{d.Name}  -  Level {d.Level} {Races.Get(d.Race).Name} {Classes.Get(d.Class).Name}", new Vector2(640, 600), OpenCharacterPanel);
        Row(v, $"XP {d.Xp} / {d.XpNeed()}     Kills {d.Kills}     Gold {d.Gold}     Valor {d.Valor}", Gold);
        Row(v, $"Attribute points: {d.AttrPts}      Skill points: {d.RankPts} (L)      Skill tree points: {d.SkillPts} (K)", d.AttrPts > 0 ? Gold : Dim, 18);
        void Attr(string n, string desc, int val, Action up) { var h = new HBoxContainer(); v.AddChild(h); var l = Lbl($"{n}  {val}   - {desc}", 17, Colors.White, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; if (d.AttrPts > 0) h.AddChild(Btn("+", () => { d.AttrPts--; up(); Game.Notify(); Sfx.Play("click"); }, true)); }
        Attr("Strength", "melee damage", d.Str, () => d.Str++); Attr("Dexterity", "bow, speed, crit, stamina", d.Dex, () => d.Dex++); Attr("Vitality", "health, defense", d.Vit, () => d.Vit++); Attr("Magic", "mana, spell power", d.Mag, () => d.Mag++);
        Row(v, $"\nHealth {d.MaxHp()}   Mana {d.MaxMp()}   Stamina {d.MaxSt()}\nWeapon damage {d.WpnDmg():0}   Spell power x{d.SpellPow():0.00}   Defense {d.Defense()}\nCrit {d.CritChance() * 100:0}% (x{d.CritMult():0.0})   Attack speed x{d.AtkSpeed():0.00}");
        var sl = new List<string>(); foreach (var n in TreeDb.Nodes) if (d.Tree.Contains(n.Id)) sl.Add(n.Name);
        var TS = Game.TS; string R(string k) => $"{TS["res_" + k]:0}%";
        Row(v, $"Armor {d.Defense()}   Magic resist {TS["mr"] + d.Defense() * .5f:0}   Evade {TS["evade"]:0}%   Tenacity {TS["tenacity"]:0}%", new Color(.8f, .9f, 1f), 15);
        Row(v, $"Resist: fire {R("fire")}  frost {R("frost")}  lightning {R("lightning")}  poison {R("poison")}  shadow {R("shadow")}  holy {R("holy")}", new Color(.8f, .9f, 1f), 14);
        Row(v, $"Penetration: armor {TS["armorPen"]:0} + {TS["armorPenP"]:0}%   magic {TS["magicPen"]:0} + {TS["magicPenP"]:0}%   Status duration +{TS["statusP"]:0}%", new Color(.8f, .9f, 1f), 14);
        Row(v, $"Skill tree nodes: {d.Tree.Count}   Skill ranks to spend: {d.RankPts} (L)", Dim);
        v.AddChild(Btn("All bonuses", OpenTotals, true));
    }

    // ---------------------------------------------------------------- journal
    public void OpenJournal()
    {
        var v = Frame("journal", "Journal", new Vector2(700, 560), OpenJournal); var d = Game.D;
        Row(v, $"Best wave {d.BestWave}     Best duel streak {d.BestDuel}     Deepest dungeon depth {d.Deepest}     Kills {d.Kills}     Valor {d.Valor}", Gold);
        var sc = Scroll(v, 440); var list = Inner(sc); var act = Quests.Active();
        if (act.Count == 0) Row(list, "No active quests. Speak to Elder Maren, Guard Tomas or Kael.", Dim);
        foreach (var q in act) { var s = Game.Q(q.Id); Row(list, q.Name + (s.S == 2 ? "  (ready to turn in)" : ""), s.S == 2 ? new Color(.5f, 1f, .5f) : Gold, 19); Row(list, q.Goal + (q.Need > 1 && s.S == 1 ? $"  ({s.N}/{q.Need})" : "") + (s.S == 2 ? "\nReturn to " + Quests.GiverName(q.Giver) + "." : ""), Colors.White); Row(list, "", Colors.White, 6); }
        int done = 0; foreach (var q in Quests.All) if (Game.Q(q.Id).S == 3) done++;
        Row(list, $"Completed: {done}", Dim);
    }

    // ---------------------------------------------------------------- skill tree
    public void OpenTree()
    {
        TreeDb.Load(); var v = Frame("tree", "Skill Tree", new Vector2(1000, 680), OpenTree);
        var hb = new HBoxContainer(); v.AddChild(hb); hb.AddThemeConstantOverride("separation", 14);
        Lbl($"Skill points: {Game.D.SkillPts}", 16, Game.D.SkillPts > 0 ? UiTheme.Highlight : Gold, hb);
        Lbl("Left click: take   Right click: refund   Wheel: zoom   Drag: pan", 14, Dim, hb);
        TreeView? tvRef = null;
        hb.AddChild(Btn("My start", () => tvRef?.CenterOn(TreeDb.Get(TreeDb.StartFor(Game.D.Class))!, 3.2f), true)); hb.AddChild(Btn("Whole tree", () => tvRef?.CenterOn(TreeDb.Nodes[0], 1f), true));
        hb.AddChild(Btn("Totals", OpenTotals, true));
        // tree canvas takes what is left of the window after title, help row and a 3-line tip (fits 648 px tall windows)
        var vp = GetViewport().GetVisibleRect().Size;
        var tv = new TreeView { CustomMinimumSize = new Vector2(Mathf.Min(960, vp.X - 64), Mathf.Clamp(vp.Y - 250, 240, 560)), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; v.AddChild(tv); tvRef = tv;
        var tip = Row(v, " ", Colors.White, 14); tip.CustomMinimumSize = new Vector2(0, 60); tip.ClipContents = true; tv.Tip = tip; tv.Changed = () => { Game.RecalcTree(); Game.Notify(); };
    }

    /// <summary>Panel listing every bonus you currently have, with where the numbers come from.</summary>
    public void OpenTotals()
    {
        var v = Frame("totals", "All bonuses", new Vector2(620, 600), OpenTotals);
        Row(v, $"{Races.Get(Game.D.Race).Name} {Classes.Get(Game.D.Class).Name}: skill tree ({Game.D.Tree.Count} nodes) + race + class + active buffs", Dim, 14);
        var sc = Scroll(v, 440); Row(Inner(sc), TreeTotals(), Colors.White, 15);
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
    public void OpenMap()
    {
        // the map is square; size it to the window height left after the title and hint rows
        var vp = GetViewport().GetVisibleRect().Size; float side = Mathf.Clamp(vp.Y - 150, 240, 680);
        var v = Frame("map", "Map", new Vector2(side + 40, side + 110), OpenMap); var d = Game.D;
        Row(v, "Click a discovered place to fast travel (not inside dungeons or the arena).", Dim, 15);
        var mv = new MapView { CustomMinimumSize = new Vector2(side, side), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter }; v.AddChild(mv);
        mv.Travel = poi => { if (Dungeon.Active || Arenas.Active) { Note("You cannot travel from here", new Color(1, .6f, .5f)); return; } Close(); World.ForceRealm(0); Player.Instance?.Teleport(World.OnGround(poi.X, poi.Z + (poi.Id == "village" ? 9 : 6), 0.6f), 0f); Ui.Flash(Colors.White); Ui.Note("Travelled to " + poi.Name, Gold); };
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
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 12);
        Lbl("Two worlds. One hero. Press R to cross between Light and Shadow.", 16, Dim, top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(Btn("Multiplayer", OpenMultiplayer));
        top.AddChild(Btn("Settings", OpenSettings));
        top.AddChild(Btn("Quit game", () => GetTree().Quit()));
        Lbl("Version " + Game.Version, 13, Dim, v);
        if (Net.LastNote != "") Row(v, Net.LastNote, new Color(1f, .7f, .5f), 15);
        Lbl("CHOOSE YOUR HERO", 18, UiTheme.Accent, v);
        Row(v, "Three heroes share this save. Click a hero to select it (Multiplayer brings the selected hero), Play to enter the world, or create a hero in an empty slot.", Dim, 14);
        var cards = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cards.AddThemeConstantOverride("separation", 16); v.AddChild(cards);
        for (int i = 0; i < Game.MaxSlots; i++)
        {
            int slot = i; var p = Game.Peek(slot); bool sel = slot == Game.Slot;
            var card = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(300, 400), MouseFilter = Control.MouseFilterEnum.Stop };
            card.AddThemeStyleboxOverride("panel", UiTheme.Box(sel ? UiTheme.ButtonHover : UiTheme.Button, sel ? UiTheme.Highlight : UiTheme.AccentSoft, sel ? 2 : 1, 16, 8, sel ? 8 : 0));
            card.GuiInput += e => { if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left && Game.Slot != slot) { Game.Slot = slot; _confirmDelete = -1; Sfx.Play("click"); ShowTitle(); } };
            cards.AddChild(card);
            var c = new VBoxContainer(); c.AddThemeConstantOverride("separation", 6); card.AddChild(c);
            Lbl($"SLOT {slot + 1}" + (sel ? "   - selected" : ""), 14, sel ? UiTheme.Highlight : Dim, c);
            if (p == null)
            {
                Lbl("Empty", 26, Dim, c);
                Row(c, "No hero here yet.", Dim, 14);
                c.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
                var nb = Btn("Create a hero", () => { Game.Slot = slot; _confirmDelete = -1; ShowCreate(); }); nb.CustomMinimumSize = new Vector2(0, 46); c.AddChild(nb);
                continue;
            }
            Lbl(p.Name, 28, UiTheme.Title, c);
            Lbl($"Level {p.Level} {Races.Get(p.Race).Name} {Classes.Get(p.Class).Name}", 17, Colors.White, c);
            var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, p.XpNeed()), Value = Math.Min(p.Xp, p.XpNeed()), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 6), MouseFilter = Control.MouseFilterEnum.Ignore }; c.AddChild(bar);
            Row(c, $"XP {p.Xp} / {p.XpNeed()}", Dim, 13);
            Row(c, $"Gold {p.Gold}     Kills {p.Kills}", Colors.White, 15);
            Row(c, $"Deepest dungeon floor {p.Deepest}     Best wave {p.BestWave}", Dim, 14);
            Row(c, $"Weapon: {p.Weapon?.Name ?? "none"}", Dim, 14);
            Row(c, $"Played {Game.Played(p)}", Dim, 14);
            c.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
            var play = Btn("Play", () => { Game.Slot = slot; _confirmDelete = -1; CloseNoUnpause(); OnContinue?.Invoke(); }); play.CustomMinimumSize = new Vector2(0, 46); c.AddChild(play);
            var del = Btn(_confirmDelete == slot ? "Really delete " + p.Name + "?" : "Delete", () =>
            {
                if (_confirmDelete != slot) { _confirmDelete = slot; ShowTitle(); return; }
                _confirmDelete = -1; Game.DeleteSlot(slot); Note(p.Name + " was deleted", Dim); ShowTitle();
            }, true);
            if (_confirmDelete == slot) del.AddThemeColorOverride("font_color", new Color(1f, .5f, .45f));
            c.AddChild(del);
        }
    }

    /// <summary>New hero for the selected slot: race, class, name.</summary>
    public void ShowCreate()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        var v = Frame("title", $"NEW HERO - SLOT {Game.Slot + 1}", new Vector2(1060, 640), ShowCreate);
        var top = new HBoxContainer(); v.AddChild(top); top.AddThemeConstantOverride("separation", 12);
        Lbl("Pick a race and a class, name your hero, and begin.", 16, Dim, top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        top.AddChild(Btn("Back to heroes", ShowTitle));
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 16); v.AddChild(cols);
        // ---- races
        var rc = new VBoxContainer { CustomMinimumSize = new Vector2(190, 0) }; cols.AddChild(rc); Lbl("RACE", 15, UiTheme.Accent, rc);
        foreach (var r in Races.All) { string id = r.Id; var b = Btn(r.Name, () => { _pickRace = id; ShowCreate(); }); if (id == _pickRace) b.AddThemeColorOverride("font_color", UiTheme.Highlight); rc.AddChild(b); }
        Lbl("BODY", 15, UiTheme.Accent, rc);
        var bodies = new HBoxContainer(); rc.AddChild(bodies);
        foreach (var (id, label) in new[] { ("male", "Male"), ("female", "Female") }) { var b = Btn(label, () => { _pickBody = id; ShowCreate(); }); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; if (id == _pickBody) b.AddThemeColorOverride("font_color", UiTheme.Highlight); bodies.AddChild(b); }
        // ---- classes
        var cc = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; cols.AddChild(cc); Lbl("CLASS", 15, UiTheme.Accent, cc);
        var grid = new GridContainer { Columns = 2 }; grid.AddThemeConstantOverride("h_separation", 6); grid.AddThemeConstantOverride("v_separation", 4); cc.AddChild(grid);
        foreach (var c in Classes.All)
        {
            string id = c.Id; var b = Btn(c.Name, () => { _pickClass = id; ShowCreate(); }); b.CustomMinimumSize = new Vector2(160, 34);
            if (id == _pickClass) b.AddThemeColorOverride("font_color", UiTheme.Highlight); grid.AddChild(b);
        }
        // ---- details and start
        var dc = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; dc.AddThemeConstantOverride("separation", 5); cols.AddChild(dc);
        var rd = Races.Get(_pickRace); var cd = Classes.Get(_pickClass);
        Lbl($"{rd.Name.ToUpper()} {cd.Name.ToUpper()}", 22, UiTheme.Title, dc);
        Row(dc, rd.Desc, Colors.White, 14).CustomMinimumSize = new Vector2(420, 0);
        string Attr(Dictionary<string, int> a) { var l = new List<string>(); foreach (var kv in a) l.Add($"{(kv.Value > 0 ? "+" : "")}{kv.Value} {kv.Key}"); return string.Join(", ", l); }
        string Mods(Dictionary<string, float> m) { var l = new List<string>(); foreach (var kv in m) l.Add(TreeDb.StatText(kv.Key, kv.Value)); return string.Join("; ", l); }
        Row(dc, "Race: " + Attr(rd.Attr) + (rd.Pts > 0 ? $", +{rd.Pts} skill point" : "") + ". " + Mods(rd.Mods), new Color(.75f, .9f, 1f), 13).CustomMinimumSize = new Vector2(420, 0);
        Row(dc, cd.Desc, Colors.White, 14).CustomMinimumSize = new Vector2(420, 0);
        var skills = new List<string>(); foreach (var s in cd.Skills) skills.Add(Skills.Get(s)?.Name ?? s);
        Row(dc, $"Class: {Attr(cd.Attr)}. Weapon: {cd.WName} ({Weapons.Get(cd.Wt).Name}{(cd.Shield ? " and shield" : "")}). {Mods(cd.Mods)}", new Color(.75f, .9f, 1f), 13).CustomMinimumSize = new Vector2(420, 0);
        Row(dc, "Starting skills: " + string.Join(", ", skills), UiTheme.Highlight, 13).CustomMinimumSize = new Vector2(420, 0);
        Row(dc, "Every class starts on its own part of the skill tree, between its two neighbours; bridges lead into their paths for hybrid builds.", Dim, 13).CustomMinimumSize = new Vector2(420, 0);
        var name = new LineEdit { Text = _pickName, PlaceholderText = "Name", CustomMinimumSize = new Vector2(0, 34) }; dc.AddChild(name); name.TextChanged += s => _pickName = s;
        var go = Btn("Begin the journey", () => { CloseNoUnpause(); OnNew?.Invoke(_pickClass, name.Text, _pickRace); }); go.CustomMinimumSize = new Vector2(0, 44); dc.AddChild(go);
    }
}

/// <summary>
/// Interactive passive tree (about 420 nodes). Opens centred on your class start. Wheel zooms toward the cursor, drag pans,
/// left click takes a node next to your path, right click refunds one that nothing else depends on. Class starts and keystones are labelled.
/// </summary>
public partial class TreeView : Control
{
    public Label? Tip; public Action? Changed;
    float _zoom = 1f; Vector2 _pan = Vector2.Zero; bool _drag; int _hover = -1; float _scale = 1f; int _start;
    static Font F => ThemeDB.FallbackFont;
    Vector2 ToScreen(TreeNode n) => Size / 2 + _pan + new Vector2(n.X, n.Y) * _scale * _zoom;
    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; TreeDb.Load(); _start = TreeDb.StartFor(Game.D.Class);
        float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f; foreach (var n in TreeDb.Nodes) { minx = Mathf.Min(minx, n.X); maxx = Mathf.Max(maxx, n.X); miny = Mathf.Min(miny, n.Y); maxy = Mathf.Max(maxy, n.Y); }
        var box = CustomMinimumSize == Vector2.Zero ? new Vector2(960, 560) : CustomMinimumSize;
        _scale = Mathf.Min((box.X - 30) / (maxx - minx + 60), (box.Y - 20) / (maxy - miny + 60));
        CenterOn(TreeDb.Get(_start) ?? TreeDb.Nodes[0], 3.2f);
    }
    public void CenterOn(TreeNode n, float zoom) { _zoom = zoom; _pan = -new Vector2(n.X, n.Y) * _scale * _zoom; QueueRedraw(); }
    float Rad(TreeNode n) => (n.Type == "start" ? 15f : n.Type == "hub" ? 13f : n.Type == "key" ? 12f : n.Type == "notable" ? 8.5f : 5f) * Mathf.Clamp(_zoom / 2.5f, 0.55f, 1.5f);
    public override void _Draw()
    {
        var d = Game.D; var alloc = new HashSet<int>(d.Tree); alloc.Add(_start); var set = new HashSet<int>(d.Tree);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(.02f, .03f, .06f));
        // faint rings to show the structure
        var c0 = Size / 2 + _pan; foreach (var r in new[] { 400f, 900f, 1520f }) DrawArc(c0, r * _scale * _zoom, 0, Mathf.Tau, 96, new Color(.15f, .22f, .32f, .35f), 1f);
        foreach (var n in TreeDb.Nodes) foreach (var a in n.Adj) if (a > n.Id)
        {
            var m = TreeDb.Nodes[a]; bool on = alloc.Contains(n.Id) && alloc.Contains(a); var p0 = ToScreen(n); var p1 = ToScreen(m);
            if (!GetRect().Grow(400).HasPoint(p0 + Position) && !GetRect().Grow(400).HasPoint(p1 + Position)) continue;
            DrawLine(p0, p1, on ? new Color(.95f, .8f, .4f) : new Color(.25f, .27f, .34f), on ? 3f : 1.4f);
        }
        foreach (var n in TreeDb.Nodes)
        {
            var p = ToScreen(n); if (p.X < -40 || p.Y < -40 || p.X > Size.X + 40 || p.Y > Size.Y + 40) continue;
            bool on = alloc.Contains(n.Id); bool can = TreeDb.CanAlloc(n, set, d.SkillPts, _start); float r = Rad(n);
            if (n.Type == "key") DrawColoredPolygon(new[] { p + new Vector2(0, -r - 4), p + new Vector2(r + 4, 0), p + new Vector2(0, r + 4), p + new Vector2(-r - 4, 0) }, on ? Colors.White : can ? new Color(.9f, .9f, .5f) : new Color(.25f, .25f, .3f));
            else DrawCircle(p, r + 2, on ? Colors.White : can ? new Color(.9f, .9f, .5f) : n.Type == "start" ? n.Col.Darkened(.3f) : new Color(.2f, .2f, .26f));
            DrawCircle(p, r, on ? n.Col : n.Col.Darkened(n.Type == "start" ? .3f : .65f));
            if (n.Type == "start" && n.Id == _start) DrawArc(p, r + 7, 0, Mathf.Tau, 32, UiTheme.Highlight, 2.5f);
            if (_hover == n.Id) DrawArc(p, r + 5, 0, Mathf.Tau, 24, Colors.White, 2f);
        }
        // labels in a second pass so no circle is drawn over them: class starts always, keystones and notables when zoomed in.
        // Each sits on a dark tag under its node and is kept inside the view.
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
        float z2 = Mathf.Clamp(_zoom * factor, 0.7f, 9f); var c = Size / 2; _pan = mouse - c - (mouse - c - _pan) * (z2 / _zoom); _zoom = z2; QueueRedraw();
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            if (_drag) { _pan += mm.Relative; QueueRedraw(); }
            int h = Pick(mm.Position); if (h != _hover) { _hover = h; UpdateTip(); QueueRedraw(); }
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
                var n = TreeDb.Nodes[id]; var set = new HashSet<int>(d.Tree);
                if (TreeDb.CanAlloc(n, set, d.SkillPts, _start)) { d.Tree.Add(id); d.SkillPts--; Sfx.Play("level"); Changed?.Invoke(); }
                else Sfx.Play("click");
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed) _drag = false;
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                int id = Pick(mb.Position); if (id < 0) return; var n = TreeDb.Nodes[id]; var set = new HashSet<int>(Game.D.Tree);
                if (TreeDb.CanDealloc(n, set, _start)) { Game.D.Tree.Remove(id); Game.D.SkillPts++; Sfx.Play("click"); Changed?.Invoke(); }
            }
        }
    }
    void UpdateTip()
    {
        if (Tip == null) return; if (_hover < 0) { Tip.Text = " "; return; }
        var n = TreeDb.Nodes[_hover]; string kind = n.Type switch { "key" => "KEYSTONE", "notable" => "notable", "start" => "class start", "hub" => "centre", _ => "" };
        var s = n.Name + (kind != "" ? "  [" + kind + "]" : "") + (n.Arm == "hybrid" ? "  [hybrid path]" : "") + (n.Desc != "" ? "  " + n.Desc : "");
        if (n.Type == "start") s += n.Id == _start ? "   (you start here)" : $"   ({Classes.Get(n.Arm).Name} start; reach it through the ring or the bridges)";
        foreach (var kv in n.Mods) s += "\n  " + TreeDb.StatText(kv.Key, kv.Value);
        Tip.Text = s;
    }
}
/// <summary>World map with discovered points of interest.</summary>
public partial class MapView : Control
{
    public Action<Poi>? Travel;
    Vector2 W2S(float x, float z) => new Vector2((x + 256f) / 512f * Size.X, (z + 256f) / 512f * Size.Y);
    public override void _Ready() { MouseFilter = MouseFilterEnum.Stop; }
    public override void _Draw()
    {
        if (World.MapTex != null) DrawTextureRect(World.MapTex, new Rect2(Vector2.Zero, Size), false); else DrawRect(new Rect2(Vector2.Zero, Size), new Color(.2f, .3f, .2f));
        foreach (var p in World.Pois)
        {
            bool known = Game.D.Disc.Contains(p.Id); var s = W2S(p.X, p.Z);
            DrawCircle(s, known ? 7 : 4, known ? new Color(1f, .82f, .29f) : new Color(.3f, .3f, .3f));
            if (known) DrawString(ThemeDB.FallbackFont, s + new Vector2(10, 5), p.Name, HorizontalAlignment.Left, -1, 15, Colors.White);
        }
        if (Player.Instance != null) { var pp = Player.Instance.GlobalPosition; if (!Dungeon.Active) DrawCircle(W2S(pp.X, pp.Z), 6, new Color(1f, .2f, .2f)); }
    }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            foreach (var p in World.Pois) if (Game.D.Disc.Contains(p.Id) && W2S(p.X, p.Z).DistanceTo(mb.Position) < 14) { Travel?.Invoke(p); return; }
    }
}
