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
                                                                             ("Attacks", K(Inputs.AttacksUi), () => OpenAttacks(null)), ("Modifiers", K(Inputs.ModsUi), OpenMods) })
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
        if (_setTab == "controls") _setTab = "audio";
        var v = Frame("settings", "Settings", new Vector2(760, 600), OpenSettings); var s = Game.Set;
        SettingsTabs(v, _setTab);
        var sc = new ScrollContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled, CustomMinimumSize = new Vector2(0, 300) }; v.AddChild(sc);
        var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; list.AddThemeConstantOverride("separation", 12); sc.AddChild(list);
        static string Pct(float x) => $"{x * 100:0}%";
        switch (_setTab)
        {
            case "display":
                Choice(list, "Window", "Fullscreen covers the screen; exclusive can run smoother on some PCs.", new[] { "Windowed", "Fullscreen", "Exclusive" }, s.WindowMode, i => s.WindowMode = i, true);
                Choice(list, "VSync", "Matches the screen's refresh rate and stops tearing.", OffOn, s.VSync ? 1 : 0, i => s.VSync = i == 1, true);
                Choice(list, "Frame limit", "Caps frames per second to save power.", new[] { "None", "30", "60", "120", "144" }, Math.Max(0, Array.IndexOf(FpsCaps, s.MaxFps)), i => s.MaxFps = FpsCaps[i], true);
                SliderRow(list, "3D resolution", "Lower it for more speed. Menus and text stay sharp.", s.RenderScale, .5f, 1f, .05f, Pct, x => s.RenderScale = x, true);
                Choice(list, "Graphics quality", "Lighting, shadows, smoothing and detail. Lower it for more frames per second.", new[] { "Low", "Medium", "High", "Ultra" }, s.Quality, i => s.Quality = i, true);
                SliderRow(list, "View distance", "How far you can see. Trees and scenery fade out before it. Shorter is faster.", s.ViewDist, 300f, 3000f, 50f, x => $"{x:0} m", x => s.ViewDist = x, true);
                SliderRow(list, "Grass", "How much grass grows around you. Lower it for more speed.", s.Grass, 0f, 1f, .05f, Pct, x => s.Grass = x, true);
                Choice(list, "Outlines", "Ink lines around shapes (a cartoon look).", OffOn, s.Outlines ? 1 : 0, i => s.Outlines = i == 1, true);
                Choice(list, "Show FPS", "Frames per second in the bottom-right corner.", OffOn, s.ShowFps ? 1 : 0, i => s.ShowFps = i == 1, true);
                break;
            case "game":
                SliderRow(list, "Mouse sensitivity", "How fast the camera turns.", s.Sens, .2f, 3f, .05f, x => $"x{x:0.00}", x => s.Sens = x);
                Choice(list, "Invert mouse Y", "Push the mouse forward to look down.", OffOn, s.InvertY ? 1 : 0, i => s.InvertY = i == 1);
                SliderRow(list, "Field of view", "How wide the camera sees.", s.Fov, 55f, 100f, 1f, x => $"{x:0}°", x => s.Fov = x);
                SliderRow(list, "Camera shake", "Shake on heavy hits and spells.", s.Shake, 0f, 1.5f, .05f, Pct, x => s.Shake = x);
                Choice(list, "Auto target", "With nothing locked on (Tab), attacks and spells aim at: the monster that hit you or that you hit, the nearest monster, or the one with the least health. A thin gold ring marks it; the camera stays free.", new[] { "Off", "Hit you", "Nearest", "Lowest HP" }, Math.Clamp(s.AutoTarget, 0, 3), i => s.AutoTarget = i);
                static string PotPct(float x) => x <= 0 ? "Off" : $"{x * 100:0}%";
                SliderRow(list, "Auto health potion", "Drink a health potion by itself when health falls below this. Off at 0.", s.AutoHp, 0f, .9f, .05f, PotPct, x => s.AutoHp = x);
                SliderRow(list, "Auto mana potion", "Drink a mana potion by itself when mana falls below this. Off at 0.", s.AutoMp, 0f, .9f, .05f, PotPct, x => s.AutoMp = x);
                SliderRow(list, "Auto potion delay", "Shortest time between two automatic potions of the same kind, so one bad moment does not empty your bag.", s.AutoPotDelay, 1f, 15f, .5f, x => $"{x:0.0} s", x => s.AutoPotDelay = x);
                Choice(list, "Friends' spell effects", "Multiplayer: see the spell and skill effects other players cast.", OffOn, s.OthersFx ? 1 : 0, i => s.OthersFx = i == 1);
                Choice(list, "Friends' damage and healing", "Multiplayer: see the damage and healing numbers of other players (their hits, the hits they take, their heals).", OffOn, s.OthersNumbers ? 1 : 0, i => s.OthersNumbers = i == 1);
                Choice(list, "Friends' status icons", "Multiplayer: see the statuses on other players (burning, poisoned, chilled...) above their names.", OffOn, s.OthersStatus ? 1 : 0, i => s.OthersStatus = i == 1);
                break;
            default:
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

    /// <summary>Tab row shared by Settings and Settings > Controls.</summary>
    void SettingsTabs(VBoxContainer v, string cur)
    {
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 6); v.AddChild(h);
        foreach (var (id, label) in new[] { ("audio", "Audio"), ("display", "Display"), ("game", "Game"), ("controls", "Controls") })
        {
            string t = id;
            var b = Btn(label, () => { if (t == cur) return; if (t == "controls") OpenControls(); else { _setTab = t; OpenSettings(); } });
            b.CustomMinimumSize = new Vector2(120, 36); Mark(b, id == cur); h.AddChild(b);
        }
        var rule = new ColorRect { Color = UiTheme.AccentSoft, CustomMinimumSize = new Vector2(0, 1), MouseFilter = Control.MouseFilterEnum.Ignore }; v.AddChild(rule);
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
        var left = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) }; left.AddThemeConstantOverride("separation", 0); h.AddChild(left);
        Lbl(name, 17, Colors.White, left);
        if (hint != "") { var l = Lbl(hint, 13, Dim, left); l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.CustomMinimumSize = new Vector2(300, 0); }
        return h;
    }

    void SliderRow(Node parent, string name, string hint, float val, float min, float max, float step, Func<float, string> fmt, Action<float> set, bool display = false, string sample = "")
    {
        var h = OptRow(parent, name, hint);
        var sl = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = val, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, CustomMinimumSize = new Vector2(200, 24), FocusMode = Control.FocusModeEnum.None };
        h.AddChild(sl);
        var vl = Lbl(fmt(val), 16, UiTheme.Accent, h); vl.CustomMinimumSize = new Vector2(64, 0); vl.HorizontalAlignment = HorizontalAlignment.Right;
        sl.ValueChanged += x => { set((float)x); vl.Text = fmt((float)x); Game.SaveSettings(); if (display) Game.ApplyDisplay(); };
        if (sample != "") sl.DragEnded += _ => Sfx.Play(sample);
    }

    void Choice(Node parent, string name, string hint, string[] labels, int cur, Action<int> set, bool display = false)
    {
        var h = OptRow(parent, name, hint);
        var seg = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter }; seg.AddThemeConstantOverride("separation", 4); h.AddChild(seg);
        var btns = new Button[labels.Length];
        for (int i = 0; i < labels.Length; i++)
        {
            int k = i;
            var b = Btn(labels[i], () => { for (int j = 0; j < btns.Length; j++) Mark(btns[j], j == k); set(k); Game.SaveSettings(); if (display) Game.ApplyDisplay(); }, true);
            b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.CustomMinimumSize = new Vector2(0, 32); Mark(b, i == cur); btns[i] = b; seg.AddChild(b);
        }
    }
}
