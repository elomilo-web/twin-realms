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
        var v = Frame("pause", Net.Active ? "Menu" : "Paused", new Vector2(780, 480), OpenPause);
        var cols = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill }; cols.AddThemeConstantOverride("separation", 18); v.AddChild(cols);

        // ---- left: hero card
        var card = new PanelContainer { CustomMinimumSize = new Vector2(350, 0) };
        card.AddThemeStyleboxOverride("panel", UiTheme.Box(UiTheme.Button, UiTheme.AccentSoft, 1, 14, 10)); cols.AddChild(card);
        var c = new VBoxContainer(); c.AddThemeConstantOverride("separation", 4); card.AddChild(c);
        Lbl(d.Name, 24, UiTheme.Title, c);
        Lbl($"Level {d.Level} {Races.Get(d.Race).Name} {Classes.Get(d.Class).Name}", 15, Colors.White, c);
        Meter(c, "XP", d.Xp, d.XpNeed(), UiTheme.Highlight);
        if (p != null) { Meter(c, "HP", p.Hp, d.MaxHp(), new Color("5be37d")); Meter(c, "MP", p.Mp, d.MaxMp(), new Color("4fb6ff")); }
        var info = new GridContainer { Columns = 2 }; info.AddThemeConstantOverride("h_separation", 14); info.AddThemeConstantOverride("v_separation", 1); c.AddChild(info);
        float hrs = World.TimeOfDay * 24f;
        string place = Dungeon.Active ? $"Dungeon, floor {Dungeon.Depth}" : Arenas.Active ? "Crimson Arena" : p != null ? HudCanvas.PlaceName(p.GlobalPosition) : "";
        foreach (var (k, val) in new[] { ("Place", place), ("Realm", World.Realm == 1 ? "Shadow" : "Light"), ("Time", $"{(int)hrs:00}:{(int)((hrs - (int)hrs) * 60):00}"), ("Gold", $"{d.Gold}"), ("Valor", $"{d.Valor}"), ("Kills", $"{d.Kills}"), ("Played", Game.Played(d)) })
        { Lbl(k, 14, Dim, info); Lbl(val, 14, k == "Realm" && World.Realm == 1 ? new Color(.78f, .62f, 1f) : Colors.White, info); }
        if (d.AttrPts + d.RankPts + d.SkillPts > 0) Row(c, $"Unspent: {d.AttrPts} attribute, {d.RankPts} skill, {d.SkillPts} skill tree", UiTheme.Highlight, 13);
        c.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var links = new GridContainer { Columns = 3 }; links.AddThemeConstantOverride("h_separation", 6); links.AddThemeConstantOverride("v_separation", 6); c.AddChild(links);
        foreach (var (label, key, open) in new (string, string, Action)[] { ("Character", "", OpenCharacterPanel), ("Pack", K(Inputs.Pack), OpenPack), ("Journal", K(Inputs.Journal), OpenJournal),
                                                                             ("Map", K(Inputs.Map), OpenMap), ("Skill tree", K(Inputs.Tree), OpenTree), ("Skills", K(Inputs.SkillsUi), OpenSkills),
                                                                             ("Attacks", K(Inputs.AttacksUi), () => OpenAttacks(null)), ("Modifiers", K(Inputs.ModsUi), OpenMods) })
        { var b = Btn(key == "" ? label : $"{label} [{key}]", open, true); b.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; b.CustomMinimumSize = new Vector2(104, 32); links.AddChild(b); }

        // ---- right: actions
        var r = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill }; r.AddThemeConstantOverride("separation", 8); cols.AddChild(r);
        Button Big(string t, Action a) { var b = Btn(t, a); b.CustomMinimumSize = new Vector2(0, 42); r.AddChild(b); return b; }
        Big("Resume", Close);
        Big("Save game", () => { Game.Save(); _savedAt = DateTime.Now.ToString("HH:mm"); Note("Game saved", Gold); OpenPause(); });
        if (_savedAt != "") { var l = Lbl("Saved at " + _savedAt, 13, Dim, r); l.HorizontalAlignment = HorizontalAlignment.Center; }
        Big("Multiplayer" + (Net.Active ? $"  ({Net.I!.Players.Count} online)" : ""), OpenMultiplayer);
        Big("Settings", OpenSettings);
        r.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill, MouseFilter = Control.MouseFilterEnum.Ignore });
        var quit = Big(_confirmQuit ? "Really quit to title?" : "Quit to title", () =>
        {
            if (!_confirmQuit) { _confirmQuit = true; OpenPause(); return; }
            QuitToTitle();
        });
        if (_confirmQuit)
        {
            quit.AddThemeColorOverride("font_color", new Color(1f, .55f, .5f));
            var l = Row(r, "Your hero is saved first. Click again to quit, or Resume to stay.", Dim, 13); l.HorizontalAlignment = HorizontalAlignment.Center;
        }
        var hint = Row(v, $"{K(Inputs.Pause)} resumes.   Every key can be changed in Settings > Controls.", Dim, 13); hint.HorizontalAlignment = HorizontalAlignment.Center;
    }

    /// <summary>Saves, leaves multiplayer and reloads the scene, which starts on the title screen.</summary>
    public void QuitToTitle()
    {
        GD.Print("[UI] quit to title"); _confirmQuit = false; Game.Save(); Net.I?.Shutdown(); GetTree().Paused = false; InGame = false; GetTree().ReloadCurrentScene();
    }

    /// <summary>Labelled bar with "value / max" on the right.</summary>
    void Meter(Node parent, string name, float val, float max, Color col)
    {
        var h = new HBoxContainer(); h.AddThemeConstantOverride("separation", 8); parent.AddChild(h);
        var n = Lbl(name, 13, Dim, h); n.CustomMinimumSize = new Vector2(26, 0);
        var bar = new ProgressBar { MinValue = 0, MaxValue = Math.Max(1f, max), Value = Mathf.Clamp(val, 0, max), ShowPercentage = false, CustomMinimumSize = new Vector2(0, 8), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, SizeFlagsVertical = Control.SizeFlags.ShrinkCenter, MouseFilter = Control.MouseFilterEnum.Ignore };
        bar.AddThemeStyleboxOverride("background", new StyleBoxFlat { BgColor = new Color(0, 0, 0, .45f) }); bar.AddThemeStyleboxOverride("fill", new StyleBoxFlat { BgColor = col });
        h.AddChild(bar);
        var t = Lbl($"{Mathf.CeilToInt(val)} / {Mathf.RoundToInt(max)}", 13, Colors.White, h); t.CustomMinimumSize = new Vector2(84, 0); t.HorizontalAlignment = HorizontalAlignment.Right;
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
                case "game": s.Sens = def.Sens; s.InvertY = def.InvertY; s.Fov = def.Fov; s.Shake = def.Shake; s.AutoTarget = def.AutoTarget; s.OthersFx = def.OthersFx; s.OthersNumbers = def.OthersNumbers; s.OthersStatus = def.OthersStatus; break;
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
