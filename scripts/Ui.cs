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
        else if (e.IsActionPressed(Inputs.ModsUi)) Toggle("mods", OpenMods);
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
    static int BuyPrice(Item it) => Mathf.RoundToInt(it.Value * Tune.F("shop.buyMul", 3) + Tune.F("shop.buyAdd", 12));   // data/tuning.json "game.shop"
    public void OpenShop(string tab)
    {
        var d = Game.D; if (_stockLvl != d.Level) { _stockLvl = d.Level; _stock.Clear(); for (int i = 0; i < 6; i++) _stock.Add(Items.Roll(d.Level + 1, .2f)); }
        var v = Frame("shop", "Brynn's Wares", new Vector2(720, 560), () => OpenShop(tab));
        var top = new HBoxContainer(); v.AddChild(top); Tabs(top, tab, ("buy", "Buy", () => OpenShop("buy")), ("sell", "Sell", () => OpenShop("sell"))); Lbl($"   {d.Gold} gold", 20, Gold, top);
        var sc = Scroll(v, 420); var list = Inner(sc);
        if (tab == "buy")
        {
            int hpc = Tune.I("shop.healthPotion", 30), mpc = Tune.I("shop.manaPotion", 35), hc = Tune.I("shop.horse", 350);
            AddRow(list, "Health potion  (have " + d.HpPot + ")", Colors.White, $"Buy {hpc}g", () => { if (d.Gold < hpc) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hpc; d.HpPot++; Sfx.Play("coin"); OpenShop(tab); });
            AddRow(list, "Mana potion  (have " + d.MpPot + ")", Colors.White, $"Buy {mpc}g", () => { if (d.Gold < mpc) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= mpc; d.MpPot++; Sfx.Play("coin"); OpenShop(tab); });
            if (!d.Horse) AddRow(list, "Horse (ride with T)", Colors.White, $"Buy {hc}g", () => { if (d.Gold < hc) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= hc; d.Horse = true; Sfx.Play("coin"); Note("You bought a horse. Press T to ride.", Gold); OpenShop(tab); });
            for (int i = 0; i < _stock.Count; i++) { int k = i; var it = _stock[i]; ItemRow(list, it, BuyPrice(it) + "g", () => { int c = BuyPrice(it); if (d.Gold < c) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Gold -= c; d.Inv.Add(it); _stock.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }); }
        }
        else
        {
            if (d.Inv.Count == 0) Row(list, "Your pack is empty.");
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
    void OpenCharacterPanel()
    {
        var d = Game.D; var TS = Game.TS; var v = Frame("char", "Character", new Vector2(1000, 620), OpenCharacterPanel); HideInfo();
        // header: who, level, XP bar, wealth
        var head = new HBoxContainer(); head.AddThemeConstantOverride("separation", 14); v.AddChild(head);
        Lbl($"{d.Name}", 22, UiTheme.Title, head); Lbl($"Level {d.Level} {Races.Get(d.Race).Name} {Classes.Get(d.Class).Name}", 16, Colors.White, head);
        var xb = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; xb.AddThemeConstantOverride("separation", 0); head.AddChild(xb);
        xb.AddChild(new ProgressBar { MinValue = 0, MaxValue = Math.Max(1, d.XpNeed()), Value = Math.Min(d.Xp, d.XpNeed()), ShowPercentage = false, CustomMinimumSize = new Vector2(160, 8), MouseFilter = Control.MouseFilterEnum.Ignore });
        Lbl($"XP {d.Xp} / {d.XpNeed()}", 12, Dim, xb);
        Lbl($"Gold {d.Gold}   Valor {d.Valor}   Kills {d.Kills}", 14, Dim, head);
        var pts = new HBoxContainer(); pts.AddThemeConstantOverride("separation", 8); v.AddChild(pts);
        Chip(pts, $"Attribute points  {d.AttrPts}", d.AttrPts > 0); Chip(pts, $"Skill points  {d.RankPts}", d.RankPts > 0); Chip(pts, $"Tree points  {d.SkillPts}", d.SkillPts > 0);
        pts.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        pts.AddChild(Btn($"Skills ({Inputs.KeyOf(Inputs.SkillsUi)})", OpenSkills, true)); pts.AddChild(Btn($"Skill tree ({Inputs.KeyOf(Inputs.Tree)})", OpenTree, true)); pts.AddChild(Btn($"Modifiers ({Inputs.KeyOf(Inputs.ModsUi)})", OpenMods, true)); pts.AddChild(Btn("All bonuses", OpenTotals, true));

        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 18); v.AddChild(cols);
        // ---- left: attributes, each with what a point gives and +1 / +5
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(380, 0) }; left.AddThemeConstantOverride("separation", 6); cols.AddChild(left);
        Lbl("ATTRIBUTES", 15, UiTheme.Accent, left);
        void Attr(string name, string role, int val, Action<int> add, Color col)
        {
            var pc = new PanelContainer(); pc.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .06f, .12f, .7f), col with { A = .45f }, 1, 12, 6, 0)); left.AddChild(pc);
            var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 10); pc.AddChild(h);
            var nv = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; nv.AddThemeConstantOverride("separation", 0); h.AddChild(nv);
            var top = new HBoxContainer(); nv.AddChild(top); Lbl(name, 17, col.Lightened(.3f), top).SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; Lbl(role, 12, Dim, top);
            var pp = Row(nv, PerPoint(add), UiTheme.Text, 12); pp.CustomMinimumSize = new Vector2(220, 0);
            Lbl(val.ToString(), 26, Colors.White, h).CustomMinimumSize = new Vector2(44, 0);
            var bv = new VBoxContainer(); bv.AddThemeConstantOverride("separation", 2); h.AddChild(bv);
            foreach (int k in new[] { 1, 5 })
            {
                int kk = k; var b = Btn("+" + k, () => { int n = Math.Min(kk, d.AttrPts); if (n <= 0) return; d.AttrPts -= n; add(n); Game.Notify(); }, true);
                b.Disabled = d.AttrPts < 1 || (k == 5 && d.AttrPts < 5); b.CustomMinimumSize = new Vector2(46, 26); bv.AddChild(b);
            }
        }
        Attr("Strength", "melee", d.Str, n => d.Str += n, new Color("ff7a5a")); Attr("Dexterity", "bow · speed · crit", d.Dex, n => d.Dex += n, new Color("7ee08a"));
        Attr("Vitality", "health · armor", d.Vit, n => d.Vit += n, new Color("ffd36a")); Attr("Magic", "mana · spells", d.Mag, n => d.Mag += n, new Color("8fb0ff"));

        // ---- right: derived stats in groups
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 4); cols.AddChild(right);
        void Section(string title, (string n, string v, Color? c)[] rows)
        {
            Lbl(title, 14, UiTheme.Accent, right);
            var g = new GridContainer { Columns = 6 }; g.AddThemeConstantOverride("h_separation", 10); g.AddThemeConstantOverride("v_separation", 1); right.AddChild(g);
            foreach (var (n, val, c) in rows) { Lbl(n, 13, Dim, g).CustomMinimumSize = new Vector2(104, 0); Lbl(val, 15, c ?? Colors.White, g).CustomMinimumSize = new Vector2(62, 0); }
        }
        string P(float x) => $"{x:0.#}%";
        Section("OFFENSE", new (string, string, Color?)[] { ("Weapon damage", $"{d.WpnDmg():0}", null), ("Spell power", $"x{d.SpellPow():0.00}", null), ("Crit chance", P(d.CritChance() * 100), null),
            ("Crit damage", $"x{d.CritMult():0.0}", null), ("Attack speed", $"x{d.AtkSpeed():0.00}", null), ("Cast speed", "+" + P(TS["castSpd"]), null),
            ("Cooldowns", "-" + P(TS["cdr"]), null), ("Skill damage", "+" + P(TS["skillP"]), null), ("All damage", "+" + P(TS["allP"]), null) });
        Section("DEFENSE", new (string, string, Color?)[] { ("Health", $"{d.MaxHp()}", new Color("ff8a7a")), ("Mana", $"{d.MaxMp()}", new Color("8fb0ff")), ("Stamina", $"{d.MaxSt()}", new Color("7ee08a")),
            ("Armor", $"{d.Defense()}", null), ("Magic resist", $"{TS["mr"] + d.Defense() * .5f:0}", null), ("Evade", P(TS["evade"]), null),
            ("Tenacity", P(TS["tenacity"]), null), ("Block", P(TS["block"]), null), ("Life leech", P(TS["leech"]), null) });
        var res = new List<(string, string, Color?)>(); foreach (var t in new[] { "fire", "frost", "lightning", "poison", "shadow", "holy" }) res.Add((char.ToUpper(t[0]) + t[1..], P(TS["res_" + t]), Dmg.TypeColor(t)));
        Section("RESISTANCES", res.ToArray());
        Section("OTHER", new (string, string, Color?)[] { ("Armor pen", $"{TS["armorPen"]:0} + {TS["armorPenP"]:0}%", null), ("Magic pen", $"{TS["magicPen"]:0} + {TS["magicPenP"]:0}%", null), ("Status time", "+" + P(TS["statusP"]), null),
            ("Move speed", "+" + P(TS["move"]), null), ("XP bonus", "+" + P(TS["xpP"]), null), ("Tree nodes", $"{d.Tree.Count}", null) });
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
    static string _treeSearch = "";
    public void OpenTree()
    {
        TreeDb.Load(); if (_panelName != "tree") TreeView.HasSaved = false;   // a fresh open centres on your start; rebuilds after taking a node keep the view
        var v = Frame("tree", "Skill Tree", new Vector2(1120, 640), OpenTree); HideInfo(); var d = Game.D;
        var hb = new HBoxContainer(); v.AddChild(hb); hb.AddThemeConstantOverride("separation", 8);
        Chip(hb, $"Tree points  {d.SkillPts}", d.SkillPts > 0);
        var search = new LineEdit { Text = _treeSearch, PlaceholderText = "Find a bonus: crit, fire, mana...", CustomMinimumSize = new Vector2(240, 30), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; hb.AddChild(search);
        var found = Lbl("", 13, new Color(.35f, .95f, 1f), hb); found.CustomMinimumSize = new Vector2(70, 0);
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
        var side = new PanelContainer { CustomMinimumSize = new Vector2(250, 0) }; side.AddThemeStyleboxOverride("panel", UiTheme.Box(new Color(.02f, .06f, .12f, .7f), UiTheme.AccentSoft, 1, 10, 8, 0)); cols.AddChild(side);
        var sv = new VBoxContainer(); sv.AddThemeConstantOverride("separation", 4); side.AddChild(sv);
        Lbl($"YOUR BONUSES   {d.Tree.Count} nodes", 13, UiTheme.Accent, sv);
        var ss = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled }; sv.AddChild(ss);
        var sl = new VBoxContainer(); ss.AddChild(sl);
        var keys = new List<string>(); foreach (var k in TreeDb.Known) if (Mathf.Abs(Game.TS[k]) > .001f) keys.Add(k); keys.Sort();
        if (keys.Count == 0) Row(sl, "Nothing yet. Take nodes next to your start.", Dim, 13);
        foreach (var k in keys) { var l = Row(sl, TreeDb.StatText(k, Game.TS[k]), new Color(.8f, .92f, 1f), 13); l.CustomMinimumSize = new Vector2(220, 0); }
        Row(sv, "Click: take a node, or the whole path to it.  Right click: refund.  Drag: move.  Wheel: zoom.", Dim, 12).CustomMinimumSize = new Vector2(220, 0);
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
        Row(v, Dungeon.Active ? "This floor as far as you have explored it. Scroll to zoom, drag to pan." : "Click a discovered place to fast travel. Scroll to zoom, drag to pan.", Dim, 15);
        var mv = new MapView { CustomMinimumSize = new Vector2(side, side), SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter }; v.AddChild(mv);
        mv.Travel = poi => { if (Dungeon.Active || Arenas.Active) { Note("You cannot travel from here", new Color(1, .6f, .5f)); return; } Close(); World.ForceRealm(0); Player.Instance?.Teleport(World.TravelPoint(poi), 0f); Ui.Flash(Colors.White); Ui.Note("Travelled to " + poi.Name, Gold); };
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
