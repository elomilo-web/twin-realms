using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>All menus and overlays, built in code: notes, banners, dialogue, shop, smith, pack, character, skill tree, journal, map, settings, pause and title.</summary>
public partial class Ui : CanvasLayer
{
    public static Ui? I;
    public static bool AnyOpen => I != null && I._panel != null;
    public static Action<string, string>? OnNew; public static Action? OnContinue;
    public static bool InGame;

    VBoxContainer _notes = null!; Label _banner = null!, _bannerSub = null!; ColorRect _flash = null!;
    Control? _panel; string _panelName = ""; Action? _rebuild;
    public static readonly Color Gold = new Color("e3b04b"), Dim = new Color("a095bd"), PanelBg = new Color("15111f"), Line = new Color("3a2f57"), Panel2 = new Color("1f1830"), Fg = new Color("ece6f8");
    static Theme? _theme;
    static Theme MakeTheme()
    {
        if (_theme != null) return _theme; var t = new Theme();
        StyleBoxFlat Sb(Color bg, Color bd, int pad = 8) => new StyleBoxFlat { BgColor = bg, BorderColor = bd, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4, CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = pad - 2, ContentMarginBottom = pad - 2 };
        t.SetStylebox("normal", "Button", Sb(Panel2, Line)); t.SetStylebox("hover", "Button", Sb(Panel2, Gold)); t.SetStylebox("pressed", "Button", Sb(Gold, Gold)); t.SetStylebox("focus", "Button", Sb(Panel2, Gold));
        t.SetColor("font_color", "Button", Fg); t.SetColor("font_hover_color", "Button", Colors.White); t.SetColor("font_pressed_color", "Button", new Color("1a1205")); t.SetColor("font_color", "Label", Fg);
        t.SetStylebox("normal", "LineEdit", Sb(new Color("0b0910"), Line)); t.SetColor("font_color", "LineEdit", Fg);
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
        _banner = Lbl("", 52, new Color("f4e7c5")); _banner.HorizontalAlignment = HorizontalAlignment.Center; _banner.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _banner.OffsetTop = 130; _banner.OffsetLeft = -400; _banner.OffsetRight = 400; _banner.Modulate = new Color(1, 1, 1, 0);
        _bannerSub = Lbl("", 20, new Color("e3b04b")); _bannerSub.HorizontalAlignment = HorizontalAlignment.Center; _bannerSub.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.CenterTop); _bannerSub.OffsetTop = 200; _bannerSub.OffsetLeft = -400; _bannerSub.OffsetRight = 400; _bannerSub.Modulate = new Color(1, 1, 1, 0);
        _flash = new ColorRect { Color = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore }; _flash.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect); AddChild(_flash);
        Game.Changed += () => { if (_rebuild != null && _panel != null) _rebuild(); };
    }

    Label Lbl(string t, int size, Color? c = null, Node? parent = null)
    {
        var l = new Label { Text = t, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontSizeOverride("font_size", size); l.AddThemeColorOverride("font_color", c ?? Colors.White); l.AddThemeColorOverride("font_outline_color", Colors.Black); l.AddThemeConstantOverride("outline_size", 5);
        (parent ?? this).AddChild(l); return l;
    }

    void AddNote(string t, Color c)
    {
        var pc = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
        pc.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(.03f, .024f, .055f, .62f), BorderColor = new Color("e3b04b"), BorderWidthLeft = 3, ContentMarginLeft = 10, ContentMarginRight = 10, ContentMarginTop = 2, ContentMarginBottom = 2 });
        var l = Lbl(t, 16, c, pc); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(300, 0);
        while (_notes.GetChildCount() >= 6) { var old = _notes.GetChild(0); _notes.RemoveChild(old); old.QueueFree(); }
        _notes.AddChild(pc);
        var tw = pc.CreateTween(); tw.TweenInterval(3.2); tw.TweenProperty(pc, "modulate:a", 0f, 0.8); tw.TweenCallback(Callable.From(pc.QueueFree));
    }
    void ShowBanner(string t, string s)
    {
        _banner.Text = t; _bannerSub.Text = s.ToUpper(); _banner.Modulate = Colors.White; _bannerSub.Modulate = Colors.White;
        var tw = CreateTween().SetParallel(); tw.TweenProperty(_banner, "modulate:a", 0f, 1.2).SetDelay(2.0); tw.TweenProperty(_bannerSub, "modulate:a", 0f, 1.2).SetDelay(2.0);
    }
    void DoFlash(Color c) { _flash.Color = new Color(c, 0.85f); CreateTween().TweenProperty(_flash, "color:a", 0f, 0.7); }

    // ---------------------------------------------------------------- input
    public override void _UnhandledInput(InputEvent e)
    {
        if (!InGame) return;
        if (e.IsActionPressed(Inputs.Pause)) { if (_panel != null) Close(); else OpenPause(); GetViewport().SetInputAsHandled(); return; }
        if (e.IsActionPressed(Inputs.Pack)) { Toggle("pack", OpenPack); } else if (e.IsActionPressed(Inputs.Journal)) Toggle("journal", OpenJournal);
        else if (e.IsActionPressed(Inputs.Map)) Toggle("map", OpenMap); else if (e.IsActionPressed(Inputs.Tree)) Toggle("tree", OpenTree);
    }
    void Toggle(string name, Action open) { if (_panelName == name) Close(); else if (_panel == null && Player.Instance != null && !Player.Instance.Dead) open(); }

    // ---------------------------------------------------------------- panel frame
    VBoxContainer Frame(string name, string title, Vector2 size, Action rebuild)
    {
        CloseNoUnpause(); _panelName = name; _rebuild = rebuild;
        var root = new Control { MouseFilter = Control.MouseFilterEnum.Stop, Theme = MakeTheme() }; root.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(new ColorRect { Color = new Color(.027f, .02f, .047f, name == "title" ? 0.9f : 0.55f), MouseFilter = Control.MouseFilterEnum.Ignore, AnchorRight = 1, AnchorBottom = 1 });
        var pc = new PanelContainer(); pc.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = PanelBg, BorderColor = Line, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1, BorderWidthTop = 1, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 18, ContentMarginRight = 18, ContentMarginTop = 14, ContentMarginBottom = 14 });
        var vp = GetViewport().GetVisibleRect().Size; // never ask for more than the window has
        pc.CustomMinimumSize = new Vector2(Mathf.Min(size.X, vp.X - 24), Mathf.Min(size.Y, vp.Y - 24)); pc.SetAnchorsPreset(Control.LayoutPreset.Center); pc.GrowHorizontal = Control.GrowDirection.Both; pc.GrowVertical = Control.GrowDirection.Both; root.AddChild(pc);
        var v = new VBoxContainer(); v.AddThemeConstantOverride("separation", 8); pc.AddChild(v);
        var hb = new HBoxContainer(); v.AddChild(hb); var t = Lbl(title, 26, Gold, hb); t.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        if (name != "title") hb.AddChild(Btn("Close", Close));
        AddChild(root); _panel = root;
        if (name != "title") { GetTree().Paused = true; Input.MouseMode = Input.MouseModeEnum.Visible; }
        return v;
    }
    public void Close() { CloseNoUnpause(); if (InGame) { GetTree().Paused = false; Input.MouseMode = Input.MouseModeEnum.Captured; } }
    void CloseNoUnpause() { if (_panel != null) { _panel.QueueFree(); _panel = null; _panelName = ""; _rebuild = null; } }

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
        var v = Frame("dialog", n.NpcName, new Vector2(620, 220), () => { });
        var d = Game.D;
        Row(v, n.Role == "arena" ? $"Welcome to the Crimson Arena. Best wave: {d.BestWave}. Best duel streak: {d.BestDuel}." : n.NextLine(), Colors.White, 18);
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
    void OpenShop(string tab)
    {
        var d = Game.D; if (_stockLvl != d.Level) { _stockLvl = d.Level; _stock.Clear(); for (int i = 0; i < 6; i++) _stock.Add(Items.Roll(d.Level + 1, .2f)); }
        var v = Frame("shop", "Brynn's Wares", new Vector2(720, 560), () => OpenShop(tab));
        var top = new HBoxContainer(); v.AddChild(top); top.AddChild(Btn("Buy", () => OpenShop("buy"))); top.AddChild(Btn("Sell", () => OpenShop("sell"))); Lbl($"   {d.Gold} gold", 20, Gold, top);
        var sc = Scroll(v, 420); var list = Inner(sc);
        if (tab == "buy")
        {
            AddRow(list, "Health potion  (have " + d.HpPot + ")", Colors.White, "Buy 30g", () => { if (d.Gold < 30) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= 30; d.HpPot++; Sfx.Play("coin"); OpenShop(tab); });
            AddRow(list, "Mana potion  (have " + d.MpPot + ")", Colors.White, "Buy 35g", () => { if (d.Gold < 35) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= 35; d.MpPot++; Sfx.Play("coin"); OpenShop(tab); });
            if (!d.Horse) AddRow(list, "Horse (ride with T)", Colors.White, "Buy 350g", () => { if (d.Gold < 350) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } d.Gold -= 350; d.Horse = true; Sfx.Play("coin"); Note("You bought a horse. Press T to ride.", Gold); OpenShop(tab); });
            for (int i = 0; i < _stock.Count; i++) { int k = i; var it = _stock[i]; AddRow(list, it.Describe(), it.Color, BuyPrice(it) + "g", () => { int c = BuyPrice(it); if (d.Gold < c) { Note("Not enough gold", new Color(1, .5f, .5f)); return; } if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Gold -= c; d.Inv.Add(it); _stock.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }); }
        }
        else
        {
            if (d.Inv.Count == 0) Row(list, "Your pack is empty.");
            list.AddChild(Btn("Sell all Common", () => { int g = 0; d.Inv.RemoveAll(it => { if (it.Rar == 0) { g += it.Value; return true; } return false; }); d.Gold += g; if (g > 0) Sfx.Play("coin"); OpenShop(tab); }, true));
            for (int i = 0; i < d.Inv.Count; i++) { int k = i; var it = d.Inv[i]; AddRow(list, it.Describe(), it.Color, "Sell " + it.Value + "g", () => { d.Gold += it.Value; d.Inv.RemoveAt(k); Sfx.Play("coin"); OpenShop(tab); }); }
        }
    }
    void AddRow(Node parent, string text, Color col, string btn, Action a)
    {
        var h = new HBoxContainer(); parent.AddChild(h); var l = Lbl(text, 16, col, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart; h.AddChild(Btn(btn, a, true));
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
    public void OpenPack()
    {
        var d = Game.D; var v = Frame("pack", "Pack and Equipment", new Vector2(860, 620), OpenPack);
        Row(v, $"{d.Gold} gold    HP potions {d.HpPot} (H)    MP potions {d.MpPot} (G)    Pack {d.Inv.Count}/28", Gold);
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 20); v.AddChild(cols);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(330, 0) }; cols.AddChild(left); Row(left, "Equipped", Gold, 20);
        foreach (var slot in new[] { "weapon", "shield", "armor", "trinket" })
        {
            var it = d.Eq.GetValueOrDefault(slot);
            var h = new HBoxContainer(); left.AddChild(h); var l = Lbl(Cap(slot) + ": " + (it?.Describe() ?? "-"), 15, it?.Color ?? Dim, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            if (it != null) h.AddChild(Btn("Remove", () => { if (d.Inv.Count >= 28) { Note("Pack is full", new Color(1, .5f, .5f)); return; } d.Eq[slot] = null; d.Inv.Add(it); Game.Notify(); OpenPack(); }, true));
        }
        Row(left, $"\nDamage {d.WpnDmg():0}   Defense {d.Defense()}   Crit {d.CritChance() * 100:0}%", Colors.White);
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddChild(right); Row(right, "Items", Gold, 20);
        var sc = Scroll(right, 440); var list = Inner(sc);
        if (d.Inv.Count == 0) Row(list, "Empty. Go and slay something.", Dim);
        for (int i = 0; i < d.Inv.Count; i++)
        {
            int k = i; var it = d.Inv[i]; var h = new HBoxContainer(); list.AddChild(h); var l = Lbl(it.Describe(), 15, it.Color, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; l.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            h.AddChild(Btn("Equip", () => Equip(k), true)); h.AddChild(Btn("Drop", () => { d.Inv.RemoveAt(k); OpenPack(); }, true));
        }
    }
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
        var d = Game.D; var v = Frame("char", $"{d.Name}  -  Level {d.Level} {d.Class}", new Vector2(560, 560), OpenCharacterPanel);
        Row(v, $"XP {d.Xp} / {d.XpNeed()}     Kills {d.Kills}     Gold {d.Gold}", Gold);
        Row(v, $"Attribute points: {d.AttrPts}      Skill points: {d.SkillPts} (K)", d.AttrPts > 0 ? Gold : Dim, 18);
        void Attr(string n, string desc, int val, Action up) { var h = new HBoxContainer(); v.AddChild(h); var l = Lbl($"{n}  {val}   - {desc}", 17, Colors.White, h); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; if (d.AttrPts > 0) h.AddChild(Btn("+", () => { d.AttrPts--; up(); Game.Notify(); Sfx.Play("click"); }, true)); }
        Attr("Strength", "melee damage", d.Str, () => d.Str++); Attr("Dexterity", "bow, speed, crit, stamina", d.Dex, () => d.Dex++); Attr("Vitality", "health, defense", d.Vit, () => d.Vit++); Attr("Magic", "mana, spell power", d.Mag, () => d.Mag++);
        Row(v, $"\nHealth {d.MaxHp()}   Mana {d.MaxMp()}   Stamina {d.MaxSt()}\nWeapon damage {d.WpnDmg():0}   Spell power x{d.SpellPow():0.00}   Defense {d.Defense()}\nCrit {d.CritChance() * 100:0}% (x{d.CritMult():0.0})   Attack speed x{d.AtkSpeed():0.00}");
        var sl = new List<string>(); foreach (var n in TreeDb.Nodes) if (d.Tree.Contains(n.Id)) sl.Add(n.Name);
        Row(v, "\nSkill tree nodes: " + d.Tree.Count, Dim);
    }

    // ---------------------------------------------------------------- journal
    public void OpenJournal()
    {
        var v = Frame("journal", "Journal", new Vector2(700, 560), OpenJournal); var d = Game.D;
        Row(v, $"Best wave {d.BestWave}     Best duel streak {d.BestDuel}     Deepest dungeon depth {d.Deepest}     Kills {d.Kills}", Gold);
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
        Row(v, $"Skill points: {Game.D.SkillPts}      Left click: allocate      Right click: remove      Mouse wheel: zoom      Drag: pan", Gold, 15);
        // tree canvas takes what is left of the window after title, help row and a 3-line tip (fits 648 px tall windows)
        var vp = GetViewport().GetVisibleRect().Size;
        var tv = new TreeView { CustomMinimumSize = new Vector2(Mathf.Min(960, vp.X - 64), Mathf.Clamp(vp.Y - 250, 240, 560)), SizeFlagsVertical = Control.SizeFlags.ExpandFill }; v.AddChild(tv);
        var tip = Row(v, " ", Colors.White, 14); tip.CustomMinimumSize = new Vector2(0, 60); tip.ClipContents = true; tv.Tip = tip; tv.Changed = () => { Game.RecalcTree(); Game.Notify(); };
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

    // ---------------------------------------------------------------- settings, pause
    public void OpenPause()
    {
        var v = Frame("pause", "Paused", new Vector2(420, 360), OpenPause);
        v.AddChild(Btn("Resume", Close)); v.AddChild(Btn("Character (attributes)", OpenCharacterPanel)); v.AddChild(Btn("Save game", () => { Game.Save(); Note("Game saved", Gold); }));
        v.AddChild(Btn("Settings", OpenSettings)); v.AddChild(Btn("Quit to title", () => { Game.Save(); GetTree().Paused = false; InGame = false; GetTree().ReloadCurrentScene(); }));
        Row(v, "Controls: WASD move, Shift sprint, Space jump, C roll, LMB strike (hold for power), RMB guard, 1-4 spells, H/G potions, E interact, R shift realm, T horse, I pack, J journal, M map, K skill tree.", Dim, 14);
    }
    public void OpenSettings()
    {
        var v = Frame("settings", "Settings", new Vector2(520, 380), OpenSettings); var s = Game.Set;
        void Slider(string n, float val, float min, float max, Action<float> set) { var h = new HBoxContainer(); v.AddChild(h); var l = Lbl(n, 17, Colors.White, h); l.CustomMinimumSize = new Vector2(150, 0); var sl = new HSlider { MinValue = min, MaxValue = max, Step = 0.05, Value = val, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(250, 24) }; h.AddChild(sl); sl.ValueChanged += x => { set((float)x); Game.SaveSettings(); }; }
        Slider("Master volume", s.Master, 0, 1, x => s.Master = x); Slider("Effects", s.Sfx, 0, 1, x => s.Sfx = x); Slider("Ambient", s.Ambient, 0, 1, x => s.Ambient = x); Slider("Mouse sensitivity", s.Sens, 0.2f, 3, x => s.Sens = x);
        var cb = new CheckBox { Text = "Invert Y", ButtonPressed = s.InvertY, FocusMode = Control.FocusModeEnum.None }; cb.Toggled += on => { s.InvertY = on; Game.SaveSettings(); }; v.AddChild(cb);
        v.AddChild(Btn(InGame ? "Back" : "Done", () => { if (InGame) OpenPause(); else ShowTitle(); }));
    }

    // ---------------------------------------------------------------- title
    public void ShowTitle()
    {
        InGame = false; Input.MouseMode = Input.MouseModeEnum.Visible;
        var v = Frame("title", "TWIN REALMS", new Vector2(520, 460), ShowTitle);
        Row(v, "Two worlds. One hero. Press R to cross between Light and Shadow.", Dim, 16);
        if (Game.HasSave()) v.AddChild(Btn("Continue", () => { CloseNoUnpause(); OnContinue?.Invoke(); }));
        var name = new LineEdit { Text = "Hero", PlaceholderText = "Name", CustomMinimumSize = new Vector2(0, 34) }; v.AddChild(name);
        Row(v, "New game - choose a class:", Gold, 18);
        foreach (var (id, label) in new[] { ("warrior", "Warrior  -  sword and shield"), ("rogue", "Ranger  -  shortbow, quick feet"), ("mage", "Mage  -  wand and spells") })
            v.AddChild(Btn(label, () => { CloseNoUnpause(); OnNew?.Invoke(id, name.Text); }));
        v.AddChild(Btn("Settings", OpenSettings));
    }
}

/// <summary>Interactive skill-tree canvas.</summary>
public partial class TreeView : Control
{
    public Label? Tip; public Action? Changed;
    float _zoom = 1f; Vector2 _pan = Vector2.Zero; bool _drag; Vector2 _dragStart; int _hover = -1; float _scale = 1f;
    Vector2 ToScreen(TreeNode n) => Size / 2 + _pan + new Vector2(n.X, n.Y) * _scale * _zoom;
    public override void _Ready()
    {
        ClipContents = true; MouseFilter = MouseFilterEnum.Stop; TreeDb.Load();
        float minx = 1e9f, maxx = -1e9f, miny = 1e9f, maxy = -1e9f; foreach (var n in TreeDb.Nodes) { minx = Mathf.Min(minx, n.X); maxx = Mathf.Max(maxx, n.X); miny = Mathf.Min(miny, n.Y); maxy = Mathf.Max(maxy, n.Y); }
        var box = CustomMinimumSize == Vector2.Zero ? new Vector2(960, 560) : CustomMinimumSize; // fit the whole tree in the canvas
        _scale = Mathf.Min((box.X - 30) / (maxx - minx + 60), (box.Y - 20) / (maxy - miny + 60)); _pan = -new Vector2((minx + maxx) / 2, (miny + maxy) / 2) * _scale;
    }
    float Rad(TreeNode n) => (n.Type == "hub" ? 16f : n.Type == "key" ? 14f : n.Type == "notable" ? 10f : 6f) * Mathf.Clamp(_zoom, 0.8f, 1.5f);
    public override void _Draw()
    {
        var alloc = new HashSet<int>(Game.D.Tree); alloc.Add(0);
        DrawRect(new Rect2(Vector2.Zero, Size), new Color(.03f, .03f, .06f));
        foreach (var n in TreeDb.Nodes) foreach (var a in n.Adj) if (a > n.Id) { var m = TreeDb.Nodes[a]; bool on = alloc.Contains(n.Id) && alloc.Contains(a); DrawLine(ToScreen(n), ToScreen(m), on ? new Color(.95f, .8f, .4f) : new Color(.28f, .28f, .35f), on ? 3f : 1.5f); }
        var set = new HashSet<int>(Game.D.Tree);
        foreach (var n in TreeDb.Nodes)
        {
            var p = ToScreen(n); bool on = alloc.Contains(n.Id); bool can = TreeDb.CanAlloc(n, set, Game.D.SkillPts); float r = Rad(n);
            DrawCircle(p, r + 2, on ? Colors.White : can ? new Color(.9f, .9f, .5f) : new Color(.2f, .2f, .26f));
            DrawCircle(p, r, on ? n.Col : n.Col.Darkened(0.65f));
            if (_hover == n.Id) DrawArc(p, r + 5, 0, Mathf.Tau, 24, Colors.White, 2f);
        }
    }
    int Pick(Vector2 pos) { int best = -1; float bd = 1e9f; foreach (var n in TreeDb.Nodes) { float d = ToScreen(n).DistanceTo(pos); if (d < Rad(n) + 6 && d < bd) { bd = d; best = n.Id; } } return best; }
    public override void _GuiInput(InputEvent e)
    {
        if (e is InputEventMouseMotion mm)
        {
            if (_drag) { _pan += mm.Relative; QueueRedraw(); }
            int h = Pick(mm.Position); if (h != _hover) { _hover = h; UpdateTip(); QueueRedraw(); }
        }
        else if (e is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { _zoom = Mathf.Min(2.5f, _zoom * 1.12f); QueueRedraw(); }
            else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { _zoom = Mathf.Max(0.5f, _zoom / 1.12f); QueueRedraw(); }
            else if (mb.ButtonIndex == MouseButton.Middle) { _drag = mb.Pressed; }
            else if (mb.ButtonIndex == MouseButton.Left && mb.Pressed)
            {
                int id = Pick(mb.Position); var d = Game.D;
                if (id < 0) { _drag = true; return; }
                var n = TreeDb.Nodes[id]; var set = new HashSet<int>(d.Tree);
                if (TreeDb.CanAlloc(n, set, d.SkillPts)) { d.Tree.Add(id); d.SkillPts--; Sfx.Play("level"); Changed?.Invoke(); }
                else Sfx.Play("click");
            }
            else if (mb.ButtonIndex == MouseButton.Left && !mb.Pressed) _drag = false;
            else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
            {
                int id = Pick(mb.Position); if (id < 0) return; var n = TreeDb.Nodes[id]; var set = new HashSet<int>(Game.D.Tree);
                if (TreeDb.CanDealloc(n, set)) { Game.D.Tree.Remove(id); Game.D.SkillPts++; Sfx.Play("click"); Changed?.Invoke(); }
            }
        }
    }
    void UpdateTip()
    {
        if (Tip == null) return; if (_hover < 0) { Tip.Text = " "; return; }
        var n = TreeDb.Nodes[_hover]; var s = n.Name + "  [" + n.Type + "]  " + n.Desc; foreach (var kv in n.Mods) s += "\n  " + TreeDb.StatText(kv.Key, kv.Value);
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
