using System;
using Godot;

namespace TwinRealms;

/// <summary>System menus: the pause menu (hero summary, quick links, save, quit with a confirm step)
/// and the tabbed Settings window (Audio, Display, Game, Controls). Every change is saved at once.</summary>
public partial class Ui
{
    static string _setTab = "audio";
    bool _confirmQuit; string _savedAt = "";
    static readonly string[] OffOn = { "Off", "On" };
    static readonly int[] FpsCaps = { 0, 30, 60, 120, 144 };

    // ---------------------------------------------------------------- pause
    public void OpenPause()
    {
        var d = Game.D; var p = Player.Instance; string K(string a) => Inputs.KeyOf(a);
        var v = FrameSlim("pause", OpenPause);
        var parch = new Color(.86f, .80f, .66f); var gold = new Color(.78f, .62f, .30f);

        // ---- top: small game title and a thin rule
        var tt = Lbl("TWIN REALMS", 15, gold, v); tt.AddThemeFontOverride("font", new FontVariation { BaseFont = ThemeDB.FallbackFont, SpacingGlyph = 4 });
        v.AddChild(new ColorRect { Color = gold with { A = .5f }, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        v.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10), MouseFilter = Control.MouseFilterEnum.Ignore });

        // ---- entries: large text buttons, gold with a left marker on hover
        Button Big(string t, Action a)
        {
            var b = Btn(t, a); b.Alignment = HorizontalAlignment.Left; b.CustomMinimumSize = new Vector2(0, 31);   // 15 entries + header + footer must fit 720 px (devloop #7)
            b.AddThemeFontSizeOverride("font_size", 19);
            b.AddThemeColorOverride("font_color", parch); b.AddThemeColorOverride("font_hover_color", gold); b.AddThemeColorOverride("font_pressed_color", UiTheme.Highlight); b.AddThemeColorOverride("font_disabled_color", new Color(.45f, .42f, .36f));
            StyleBoxFlat Sb(Color mark, Color bg) => new() { BgColor = bg, BorderColor = mark, BorderWidthLeft = 2, ContentMarginLeft = 16, ContentMarginRight = 8, ContentMarginTop = 2, ContentMarginBottom = 2 };
            var none = Sb(Colors.Transparent, Colors.Transparent); var hov = Sb(gold, new Color(gold, .10f));
            b.AddThemeStyleboxOverride("normal", none); b.AddThemeStyleboxOverride("disabled", none); b.AddThemeStyleboxOverride("focus", none);
            b.AddThemeStyleboxOverride("hover", hov); b.AddThemeStyleboxOverride("pressed", hov);
            v.AddChild(b); return b;
        }
        void Note13(string t, Color? c = null) { var l = Row(v, t, c ?? Dim, 13); l.CustomMinimumSize = new Vector2(0, 0); l.AddThemeConstantOverride("line_spacing", 0); l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; }
        Big("Resume", Close);
        Big("Save game", () => { Game.Save(); _savedAt = DateTime.Now.ToString("HH:mm"); Note("Game saved", Gold); OpenPause(); });
        if (_savedAt != "") Note13("   Saved at " + _savedAt);
        Big("Multiplayer" + (Net.Active ? $"  ({Net.I!.Players.Count} online)" : ""), OpenMultiplayer);
        foreach (var (label, key, open) in new (string, string, Action)[] { ("Character", "", OpenCharacterPanel), ("Pack", K(Inputs.Pack), OpenPack), ("Journal", K(Inputs.Journal), OpenJournal),
                                                                             ("Map", K(Inputs.Map), OpenMap), ("Skill tree", K(Inputs.Tree), OpenTree), ("Skills", K(Inputs.SkillsUi), OpenSkills),
                                                                             ("Attacks", K(Inputs.AttacksUi), () => OpenAttacks(null)), ("Modifiers", K(Inputs.ModsUi), OpenMods), ("Pets", K(Inputs.PetsUi), () => OpenPets(null)), ("Soul Duels", "", OpenSoulTeam) })
            Big(key == "" ? label : $"{label}  [{key}]", open);
        Big("Settings", OpenSettings);
        // Unstuck: back to the world spawn point (Oakhaven). Asks once, then a short cooldown so it is not a free escape every few seconds
        float wait = UnstuckWait();
        var un = Big(wait > 0 ? $"Unstuck (ready in {Mathf.CeilToInt(wait)} s)" : _confirmUnstuck ? "Really go back to Oakhaven?" : "Unstuck", () =>
        {
            if (UnstuckWait() > 0) { OpenPause(); return; }
            if (!_confirmUnstuck) { _confirmUnstuck = true; _confirmQuit = false; OpenPause(); return; }
            Unstuck();
        });
        un.Disabled = wait > 0 || p == null;
        un.TooltipText = "Stuck in a wall or out of reach? Takes you back to the world spawn point in Oakhaven" + (Dungeon.Active || Arenas.Active ? " (leaves this dungeon or arena run)." : ".");
        if (_confirmUnstuck) { un.AddThemeColorOverride("font_color", UiTheme.Highlight); Note13(Dungeon.Active || Arenas.Active ? "   This leaves the dungeon or arena run. Click again to go." : "   Click again to go back to Oakhaven."); }
        var quit = Big(_confirmQuit ? "Really quit to title?" : "Quit to title", () =>
        {
            if (!_confirmQuit) { _confirmQuit = true; _confirmUnstuck = false; OpenPause(); return; }
            QuitToTitle();
        });
        if (_confirmQuit)
        {
            quit.AddThemeColorOverride("font_color", new Color(1f, .55f, .5f));
            Note13("   Your hero is saved first. Click again to quit, or Resume to stay.");
        }
        v.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });

        // ---- bottom: hero name, level, location
        string place = Dungeon.Active ? $"Dungeon, floor {Dungeon.Depth}" : Arenas.Active ? "Crimson Arena" : p != null ? HudCanvas.PlaceName(p.GlobalPosition) : "";
        v.AddChild(new ColorRect { Color = gold with { A = .5f }, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore });
        var sg = new GridContainer { Columns = 2 }; sg.AddThemeConstantOverride("h_separation", 14); sg.AddThemeConstantOverride("v_separation", 0); v.AddChild(sg);
        foreach (var (sk, sv) in new[] { ("Gold", $"{d.Gold}"), ("Valor", $"{d.Valor}"), ("Kills", $"{d.Kills}"), ("Played", Game.Played(d)), ("Deepest floor", $"{d.Deepest}"), ("Best wave", $"{d.BestWave}") })
        { var sr = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; sg.AddChild(sr); var sl = Lbl(sk, 12, gold, sr); sl.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; Lbl(sv, 12, parch, sr); }
        Lbl(d.Name, 22, parch, v);
        Lbl($"Level {d.Level} {Races.Get(d.Race).Name} {Classes.Get(d.Class).Name}", 14, Dim, v);
        if (place != "") Lbl(place + (World.Realm == 1 ? "  (Shadow realm)" : ""), 14, World.Realm == 1 ? new Color(.78f, .62f, 1f) : gold, v);
        if (d.AttrPts + d.RankPts + d.SkillPts > 0) Row(v, $"Unspent: {d.AttrPts} attribute, {d.RankPts} skill, {d.SkillPts} skill tree", UiTheme.Highlight, 13);
        Row(v, $"{K(Inputs.Pause)} resumes", Dim, 12);
    }

    // ---- Unstuck
    bool _confirmUnstuck; static ulong _unstuckAt;
    const float UnstuckCooldown = 60f;
    static float UnstuckWait() => _unstuckAt == 0 ? 0f : Mathf.Max(0f, UnstuckCooldown - (Time.GetTicksMsec() - _unstuckAt) / 1000f);
    /// <summary>The world spawn point: Oakhaven, where new heroes start (also where the online server places newcomers).</summary>
    public static Vector3 SpawnPoint => World.OnGround(0, 9, 0.6f);

    /// <summary>Leaves any dungeon, raid lair or arena run and puts the hero back at the world spawn point.</summary>
    void Unstuck()
    {
        var p = Player.Instance; if (p == null) return;
        _confirmUnstuck = false; _unstuckAt = Time.GetTicksMsec();
        GD.Print("[UI] unstuck");
        if (Dungeon.Active) Dungeon.Leave(false);
        if (Arenas.Active) Arenas.Leave();
        if (p.Mounted) p.ToggleMount(true);
        Close();
        p.Teleport(SpawnPoint, 0f);
        Game.D.PosX = 0; Game.D.PosZ = 9; Game.Save();
        Flash(Colors.White); Banner(World.GetPoi("village")?.Name ?? "Oakhaven", "back at the world spawn point");
    }

    /// <summary>Saves, leaves multiplayer and reloads the scene, which starts on the title screen.</summary>
    public void QuitToTitle()
    {
        GD.Print("[UI] quit to title"); _confirmQuit = false; Game.Save(); Net.I?.Shutdown(); GetTree().Paused = false; InGame = false; GetTree().ReloadCurrentScene();
    }

    // ---------------------------------------------------------------- settings
    public void OpenSettings(string tab) { _setTab = tab; OpenSettings(); }
    public void OpenSettings()
    {
        _confirmQuit = false;
        if (_setTab is "controls" or "pad") _setTab = "audio";
        var v = Frame("settings", "Settings", new Vector2(940, 600), OpenSettings); var s = Game.Set;
        v = SettingsShell(v, _setTab);
        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) }; v.AddChild(sc);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 10); sc.AddChild(list);
        static string Pct(float x) => $"{x * 100:0}%";
        switch (_setTab)
        {
            case "display":
                Header("Window", list);
                Choice(list, "Window", "Fullscreen covers the screen; exclusive can run smoother on some PCs.", new[] { "Windowed", "Fullscreen", "Exclusive" }, s.WindowMode, i => s.WindowMode = i, true);
                Choice(list, "VSync", "Matches the screen's refresh rate and stops tearing.", OffOn, s.VSync ? 1 : 0, i => s.VSync = i == 1, true);
                Choice(list, "Frame limit", "Caps frames per second to save power.", new[] { "None", "30", "60", "120", "144" }, Math.Max(0, Array.IndexOf(FpsCaps, s.MaxFps)), i => s.MaxFps = FpsCaps[i], true);
                Header("Quality", list);
                SliderRow(list, "3D resolution", "Lower it for more speed (upscaled with AMD FSR). Menus and text stay sharp. Moving it leaves Auto quality.", s.RenderScale, .5f, 1f, .05f, Pct, x => { s.RenderScale = x; s.AutoQuality = false; }, true);
                Choice(list, "Graphics quality", "Lighting, shadows, smoothing and detail. Lower it for more frames per second. Auto picks the preset and 3D resolution for your screen size.", new[] { "Low", "Medium", "High", "Ultra", "Auto" }, s.AutoQuality ? 4 : s.Quality, i => { s.AutoQuality = i == 4; if (i < 4) s.Quality = i; }, true);
                Choice(list, "Outlines", "Ink lines around shapes (a cartoon look).", OffOn, s.Outlines ? 1 : 0, i => s.Outlines = i == 1, true);
                Header("Distance", list);
                SliderRow(list, "View distance", "How far you can see. Trees and scenery fade out before it. Shorter is faster.", s.ViewDist, 300f, 3000f, 50f, x => $"{x:0} m", x => s.ViewDist = x, true);
                SliderRow(list, "Grass", "How much grass grows around you. Lower it for more speed.", s.Grass, 0f, 1f, .05f, Pct, x => s.Grass = x, true);
                Header("Interface", list);
                Choice(list, "Show FPS", "Frames per second in the bottom-right corner.", OffOn, s.ShowFps ? 1 : 0, i => s.ShowFps = i == 1, true);
                break;
            case "game":
                Header("Camera", list);
                SliderRow(list, "Mouse sensitivity", "How fast the camera turns.", s.Sens, .2f, 3f, .05f, x => $"x{x:0.00}", x => s.Sens = x);
                Choice(list, "Invert mouse Y", "Push the mouse forward to look down.", OffOn, s.InvertY ? 1 : 0, i => s.InvertY = i == 1);
                SliderRow(list, "Field of view", "How wide the camera sees.", s.Fov, 55f, 100f, 1f, x => $"{x:0}°", x => s.Fov = x);
                SliderRow(list, "Camera shake", "Shake on heavy hits and spells.", s.Shake, 0f, 1.5f, .05f, Pct, x => s.Shake = x);
                Header("Combat", list);
                Choice(list, "Auto target", $"With nothing locked on ({Inputs.KeyOf(Inputs.Lock)}), attacks and spells aim at: the monster that hit you or that you hit, the nearest monster, or the one with the least health. A thin gold ring marks it; the camera stays free.", new[] { "Off", "Hit you", "Nearest", "Lowest HP" }, Math.Clamp(s.AutoTarget, 0, 3), i => s.AutoTarget = i);
                static string PotPct(float x) => x <= 0 ? "Off" : $"{x * 100:0}%";
                SliderRow(list, "Auto health potion", "Drink a health potion by itself when health falls below this. Off at 0.", s.AutoHp, 0f, .9f, .05f, PotPct, x => s.AutoHp = x);
                SliderRow(list, "Auto mana potion", "Drink a mana potion by itself when mana falls below this. Off at 0.", s.AutoMp, 0f, .9f, .05f, PotPct, x => s.AutoMp = x);
                SliderRow(list, "Auto potion delay", "Shortest time between two automatic potions of the same kind, so one bad moment does not empty your bag.", s.AutoPotDelay, 1f, 15f, .5f, x => $"{x:0.0} s", x => s.AutoPotDelay = x);
                Header("Multiplayer", list);
                Choice(list, "Friends' spell effects", "Multiplayer: see the spell and skill effects other players cast.", OffOn, s.OthersFx ? 1 : 0, i => s.OthersFx = i == 1);
                Choice(list, "Friends' damage and healing", "Multiplayer: see the damage and healing numbers of other players (their hits, the hits they take, their heals).", OffOn, s.OthersNumbers ? 1 : 0, i => s.OthersNumbers = i == 1);
                Choice(list, "Friends' status icons", "Multiplayer: see the statuses on other players (burning, poisoned, chilled...) above their names.", OffOn, s.OthersStatus ? 1 : 0, i => s.OthersStatus = i == 1);
                break;
            default:
                Header("Volumes", list);
                SliderRow(list, "Master volume", "Everything at once.", s.Master, 0, 1, .05f, Pct, x => s.Master = x);
                SliderRow(list, "Music", "Calm songs with long quiet gaps between them.", s.Music, 0, 1, .05f, Pct, x => s.Music = x);
                if (Sfx.AmbienceOn) SliderRow(list, "Ambience", "Birds, crickets, wind and dungeon air.", s.Ambient, 0, 1, .05f, Pct, x => s.Ambient = x);
                SliderRow(list, "Combat and spells", "Swings, hits, spells, arrows and monsters. Let go of the slider to hear a sample.", s.Sfx, 0, 1, .05f, Pct, x => s.Sfx = x, false, "fire");
                SliderRow(list, "Footsteps", "Your steps on grass, stone, wood, sand and snow.", s.Steps, 0, 1, .05f, Pct, x => s.Steps = x, false, "step_soft");
                SliderRow(list, "Interface", "Menu clicks, level-ups, coins and pickups.", s.UiVol, 0, 1, .05f, Pct, x => s.UiVol = x, false, "coin");
                break;
        }
        var bot = new HBoxContainer(); bot.AddThemeConstantOverride("separation", 10); v.AddChild(bot);
        bot.AddChild(Btn("Reset this page", () =>
        {
            var def = new Settings();
            switch (_setTab)
            {
                case "display": s.WindowMode = def.WindowMode; s.VSync = def.VSync; s.MaxFps = def.MaxFps; s.RenderScale = def.RenderScale; s.Outlines = def.Outlines; s.ShowFps = def.ShowFps; s.Quality = def.Quality; s.ViewDist = def.ViewDist; s.Grass = def.Grass; break;
                case "game": s.Sens = def.Sens; s.InvertY = def.InvertY; s.Fov = def.Fov; s.Shake = def.Shake; s.AutoTarget = def.AutoTarget; s.OthersFx = def.OthersFx; s.OthersNumbers = def.OthersNumbers; s.OthersStatus = def.OthersStatus; s.AutoHp = def.AutoHp; s.AutoMp = def.AutoMp; s.AutoPotDelay = def.AutoPotDelay; break;
                default: s.Master = def.Master; s.Music = def.Music; s.Ambient = def.Ambient; s.Sfx = def.Sfx; s.Steps = def.Steps; s.UiVol = def.UiVol; break;
            }
            Game.SaveSettings(); Game.ApplyDisplay(); OpenSettings();
        }, true));
        bot.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var back = Btn(InGame ? "Back" : "Done", () => { if (InGame) OpenPause(); else ShowTitle(); }); back.CustomMinimumSize = new Vector2(140, 36); bot.AddChild(back);
    }

    static readonly Color GoldC = new(.78f, .62f, .30f);   // (Parch is shared, declared in Ui.cs)

    /// <summary>Left column of tabs (gold marker on the active one) shared by Settings and Controls; returns the right-hand content column.</summary>
    VBoxContainer SettingsShell(VBoxContainer v, string cur)
    {
        var h = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; h.AddThemeConstantOverride("separation", 18); v.AddChild(h);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(170, 0) }; left.AddThemeConstantOverride("separation", 2); h.AddChild(left);
        foreach (var (id, label) in new[] { ("audio", "Audio"), ("display", "Display"), ("game", "Game"), ("controls", "Controls"), ("pad", "Controller") })
        {
            string t = id; bool on = id == cur;
            var b = Btn(label, () => { if (t == cur) return; OpenSetTab(t); });
            b.Alignment = HorizontalAlignment.Left; b.CustomMinimumSize = new Vector2(0, 34); b.AddThemeFontSizeOverride("font_size", 18);
            b.AddThemeColorOverride("font_color", on ? GoldC : Parch); b.AddThemeColorOverride("font_hover_color", GoldC); b.AddThemeColorOverride("font_pressed_color", UiTheme.Highlight);
            StyleBoxFlat Sb(Color mark, Color bg) => new() { BgColor = bg, BorderColor = mark, BorderWidthLeft = 2, ContentMarginLeft = 14, ContentMarginRight = 8, ContentMarginTop = 2, ContentMarginBottom = 2 };
            var act = Sb(GoldC, new Color(GoldC, .10f)); var none = Sb(Colors.Transparent, Colors.Transparent);
            b.AddThemeStyleboxOverride("normal", on ? act : none); b.AddThemeStyleboxOverride("hover", act); b.AddThemeStyleboxOverride("pressed", act);
            left.AddChild(b);
        }
        h.AddChild(new ColorRect { Color = UiTheme.AccentSoft, CustomMinimumSize = new Vector2(1, 0), MouseFilter = Control.MouseFilterEnum.Ignore });
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; right.AddThemeConstantOverride("separation", 8); h.AddChild(right);
        return right;
    }

    /// <summary>Thin gold-filled slider with a dark track.</summary>
    static void StyleSlider(HSlider sl)
    {
        sl.AddThemeStyleboxOverride("slider", new StyleBoxFlat { BgColor = new Color(.12f, .10f, .08f), BorderColor = UiTheme.AccentSoft, BorderWidthBottom = 1, BorderWidthTop = 1, BorderWidthLeft = 1, BorderWidthRight = 1, ContentMarginTop = 2, ContentMarginBottom = 2 });
        var fill = new StyleBoxFlat { BgColor = GoldC with { A = .85f }, ContentMarginTop = 2, ContentMarginBottom = 2 };
        sl.AddThemeStyleboxOverride("grabber_area", fill); sl.AddThemeStyleboxOverride("grabber_area_highlight", fill);
    }
    /// <summary>Selected look for tabs and choice buttons.</summary>
    static void Mark(Button b, bool on)
    {
        if (on) { b.AddThemeStyleboxOverride("normal", UiTheme.Box(UiTheme.ButtonHover, UiTheme.Accent, 1, 14, 5, 4)); b.AddThemeColorOverride("font_color", UiTheme.Highlight); }
        else { b.RemoveThemeStyleboxOverride("normal"); b.RemoveThemeColorOverride("font_color"); }
    }

    /// <summary>Settings row: name and a short hint on the left, the control on the right.</summary>
    HBoxContainer OptRow(Node parent, string name, string hint)
    {
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 14); parent.AddChild(h);
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(290, 0) }; left.AddThemeConstantOverride("separation", 0); h.AddChild(left);
        Lbl(name, 16, Parch, left);
        if (hint != "") { var l = Lbl(hint, 13, Dim, left); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(290, 0); }
        return h;
    }

    void SliderRow(Node parent, string name, string hint, float val, float min, float max, float step, Func<float, string> fmt, Action<float> set, bool display = false, string sample = "")
    {
        var h = OptRow(parent, name, hint);
        var sl = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = val, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(200, 24), FocusMode = Control.FocusModeEnum.None };
        StyleSlider(sl); h.AddChild(sl);
        var vl = Lbl(fmt(val), 15, GoldC, h); vl.CustomMinimumSize = new Vector2(64, 0); vl.HorizontalAlignment = HorizontalAlignment.Right;
        sl.ValueChanged += x => { set((float)x); vl.Text = fmt((float)x); Game.SaveSettings(); if (display) Game.ApplyDisplay(); };
        if (sample != "") sl.DragEnded += _ => Sfx.Play(sample);
    }

    /// <summary>Option picker: a check switch for Off/On, otherwise "< value >" arrows that wrap around.</summary>
    void Choice(Node parent, string name, string hint, string[] labels, int cur, Action<int> set, bool display = false)
    {
        var h = OptRow(parent, name, hint);
        void Commit(int k) { set(k); Game.SaveSettings(); if (display) Game.ApplyDisplay(); }
        if (labels.Length == 2 && labels[0] == "Off" && labels[1] == "On")
        {
            h.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
            var cb = new CheckButton { ButtonPressed = cur == 1, FocusMode = Control.FocusModeEnum.None, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, Text = cur == 1 ? "On" : "Off" };
            cb.AddThemeColorOverride("font_color", Parch); cb.AddThemeFontSizeOverride("font_size", 15);
            cb.Toggled += on => { cb.Text = on ? "On" : "Off"; Sfx.Play("click"); Commit(on ? 1 : 0); };
            h.AddChild(cb); return;
        }
        h.AddChild(new Control { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var box = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; box.AddThemeConstantOverride("separation", 6); h.AddChild(box);
        int idx = Math.Clamp(cur, 0, labels.Length - 1);
        var val = new Label { Text = labels[idx], HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, CustomMinimumSize = new Vector2(130, 0) };
        val.AddThemeFontSizeOverride("font_size", 16); val.AddThemeColorOverride("font_color", GoldC);
        void Step(int d) { idx = (idx + d + labels.Length) % labels.Length; val.Text = labels[idx]; Commit(idx); }
        var l = Btn("<", () => Step(-1), true); l.CustomMinimumSize = new Vector2(32, 30); var r = Btn(">", () => Step(1), true); r.CustomMinimumSize = new Vector2(32, 30);
        box.AddChild(l); box.AddChild(val); box.AddChild(r);
    }
}