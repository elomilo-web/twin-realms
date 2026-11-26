using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class Settings
{
    public float Master = 0.8f, Sfx = 0.8f, Ambient = 0.6f, Music = 0.5f, Sens = 1f;
    public float Steps = 0.7f, UiVol = 0.7f;   // footsteps and interface sounds (older settings files get these defaults)
    public bool InvertY = false;
    public bool FirstPerson = false;       // camera view (toggle_view key, default F5); older settings files get third person
    // display and game options (Settings > Display / Game); older settings files get these defaults
    public int WindowMode = 0;            // 0 windowed, 1 fullscreen (borderless), 2 exclusive fullscreen
    public bool VSync = true;
    public int MaxFps = 144;              // 0 = no limit; a cap by default, in case a graphics driver overrides VSync (an uncapped game pins the CPU)
    public float RenderScale = 1f;        // 3D resolution; the menus and HUD stay sharp
    public float Fov = 70f;
    public bool Outlines = false;          // the ink lines around shapes (off: too cartoony; settings files from before version 1 are switched off once)
    public int Quality = 3;                // graphics preset: 0 low, 1 medium, 2 high, 3 ultra (lighting, shadows, anti-aliasing, detail)
    public bool AutoQuality = true;        // "Auto": the preset and 3D resolution are picked from the screen size (Game.PickAuto)
    public float ViewDist = 1200f;         // metres: how far the camera draws, trees and scenery fade before it
    public float Grass = 1f;               // grass amount 0..1 (0 = none)
    public int SetVer = 0;
    public bool ShowFps = false;
    public float Shake = 1f;              // camera shake strength
    public int AutoTarget = 0;            // auto target when nothing is locked: 0 off, 1 monsters that hit you (or you hit), 2 nearest, 3 lowest health
    // multiplayer: what we see of other players (Settings > Game); all on by default, older settings files get true
    public bool OthersFx = true, OthersNumbers = true, OthersStatus = true;
    // auto potions (Settings > Game): drink when health / mana falls below this share (0 = off), at most once per AutoPotDelay seconds each
    public float AutoHp = 0f, AutoMp = 0f, AutoPotDelay = 4f;
    public Dictionary<string, string> Binds = new();   // rebound controls: action -> "k:keycode" / "m:button" (older settings files: empty = defaults)
    // controller (Settings > Controller): its own bindings, action -> "b:button" / "a:trigger axis", "L+" = with the left trigger held
    public Dictionary<string, string> PadBinds = new();
    public float PadSens = 1f, PadDeadzone = .18f, VibStrength = .8f;
    public bool Vibration = true;
    public int PadGlyphs = 0;              // button names: 0 auto (from the connected pad), 1 Xbox, 2 PlayStation
}

/// <summary>Global game state, saving and cross-system helpers.</summary>
public static class Game
{
    public static PlayerStats D = new();
    public static TreeStats TS = new();
    public static Settings Set = new();
    public static Node3D? Root;                 // Main scene root; projectiles, loot and floating text are added here
    public static float PlayTime;
    public static bool TreeRefunded;            // set when an old save had its skill tree points refunded
    public static event Action? Changed;        // fired when saved data changes (UI refresh)

    /// <summary>The game version (project.godot application/config/version), shown on the title screen.</summary>
    public static string Version => ProjectSettings.GetSetting("application/config/version", "dev").AsString();
    public static string SavePath = "user://save.json";   // the self-test bots use their own files so they never touch your hero
    public static string SetPath = "user://settings.json";   // the test bots use their own file so they never change your controls
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true, WriteIndented = false };

    /// <summary>
    /// All modifiers into the stat table through the one stat pipeline (StatPipeline.Rebuild, focus F-02): race, class, gear affixes and
    /// set bonuses, gems, skill tree and title, timed skill buffs; then the caps of data/stats.json. Everything reads them through Game.TS.
    /// Called on Notify and whenever a buff starts or ends; the derived stats (MaxHp, WpnDmg...) rebuild from it on their next read.
    /// </summary>
    public static void RecalcTree() { TreeDb.Load(); StatPipeline.Rebuild(D, TS); }
    /// <summary>Saved data changed: refresh the stat table (equipment may have changed, and with it set bonuses), then the UI.</summary>
    public static void Notify() { RecalcTree(); Changed?.Invoke(); }

    // ---- save file: one file holds up to three heroes (character slots). Older single-hero saves load into slot 1.
    public const int MaxSlots = 3;
    public static int Slot;                     // the slot being played (kept across "Quit to title", which reloads the scene)
    public class SaveFile { public PlayerStats?[] Slots = new PlayerStats?[MaxSlots]; public int Last; }
    static SaveFile? _file; static string _filePath = "";

    /// <summary>The whole save file (all slots), read once and kept in memory; we are its only writer.</summary>
    public static SaveFile File()
    {
        if (_file != null && _filePath == SavePath) return _file;
        _filePath = SavePath; _file = new SaveFile();
        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            if (f == null) return _file;
            string txt = f.GetAsText(); using var doc = JsonDocument.Parse(txt);
            if (doc.RootElement.TryGetProperty("Slots", out _)) _file = JsonSerializer.Deserialize<SaveFile>(txt, Opt) ?? new SaveFile();
            else _file.Slots[0] = JsonSerializer.Deserialize<PlayerStats>(txt, Opt);   // a save from before character slots
            if (_file.Slots.Length != MaxSlots) Array.Resize(ref _file.Slots, MaxSlots);
            _file.Last = Math.Clamp(_file.Last, 0, MaxSlots - 1);
        }
        catch (Exception e) { GD.PushError("save file unreadable: " + e.Message); }
        return _file;
    }
    public static PlayerStats? Peek(int slot) => slot >= 0 && slot < MaxSlots ? File().Slots[slot] : null;
    /// <summary>True when the current slot holds a hero.</summary>
    public static bool HasSave() => Peek(Slot) != null;
    public static void DeleteSlot(int slot) { var f = File(); f.Slots[slot] = null; WriteFile(f); }

    // ---- online: while logged in to the online server the save file comes from the server and every write is uploaded to it
    public static bool Online; public static string OnlineEmail = "";
    static string _offlinePath = "";
    /// <summary>Logged in: use the account's save file from the server (empty for a new account).</summary>
    public static void UseOnline(string email, string saveJson)
    {
        if (!Online) _offlinePath = SavePath;
        Online = true; OnlineEmail = email; SavePath = "online://" + email; _filePath = SavePath; _file = new SaveFile();
        try { if (saveJson != "") _file = JsonSerializer.Deserialize<SaveFile>(saveJson, Opt) ?? new SaveFile(); } catch (Exception e) { GD.PushError("online save unreadable: " + e.Message); }
        if (_file.Slots == null || _file.Slots.Length != MaxSlots) Array.Resize(ref _file.Slots, MaxSlots);
        _file.Last = Math.Clamp(_file.Last, 0, MaxSlots - 1); Slot = _file.Last;
    }
    /// <summary>Logged out: back to the save file on this computer.</summary>
    public static void LeaveOnline()
    {
        if (!Online) return;
        Online = false; OnlineEmail = ""; SavePath = _offlinePath != "" ? _offlinePath : "user://save.json"; _file = null; Slot = File().Last;
    }

    static void WriteFile(SaveFile file)
    {
        if (Online) { Net.I?.UploadSave(JsonSerializer.Serialize(file, Opt)); return; }
        // write to a temporary file first, then swap it in, so a crash mid-write never loses all three heroes
        string tmp = SavePath + ".tmp";
        using (var f = Godot.FileAccess.Open(tmp, Godot.FileAccess.ModeFlags.Write)) { if (f == null) throw new Exception("cannot write " + tmp); f.StoreString(JsonSerializer.Serialize(file, Opt)); }
        string a = ProjectSettings.GlobalizePath(tmp), b = ProjectSettings.GlobalizePath(SavePath);
        if (System.IO.File.Exists(b)) System.IO.File.Copy(b, b + ".bak", true);
        System.IO.File.Move(a, b, true);
    }

    public static void Save()
    {
        try
        {
            if (Player.Instance != null && !Dungeon.Active) { D.PosX = Player.Instance.GlobalPosition.X; D.PosZ = Player.Instance.GlobalPosition.Z; }
            D.Time = World.TimeOfDay;
            BlockWorld.Store(D);   // the block world's changes (dug, built, burnt) go with the hero
            var file = File(); file.Slots[Slot] = D; file.Last = Slot; WriteFile(file);
        }
        catch (Exception e) { GD.PushError("save failed: " + e.Message); }
    }

    public static bool Load()
    {
        try
        {
            // a fresh copy from disk so the in-memory slot and the hero being played are never the same object by accident
            if (!Online) _file = null;   // online, the server's copy in memory is the latest
            var d0 = Peek(Slot); if (d0 == null) return false;
            var d = JsonSerializer.Deserialize<PlayerStats>(JsonSerializer.Serialize(d0, Opt), Opt);
            if (d == null) return false;
            D = d; TreeDb.Load();
            D.GainXp(0);   // the levelling curve changed: settle any experience already past the next level
            if (D.TreeVer < TreeDb.Version) { D.SkillPts += D.Tree.Count; D.Tree.Clear(); D.TreeVer = TreeDb.Version; TreeRefunded = true; }   // the tree was rebuilt: refund every point
            Skills.GrantStarting(D); RecalcTree(); return true;
        }
        catch (Exception e) { GD.PushError("load failed: " + e.Message); return false; }
    }

    public static void NewGame(string cls, string name, string race = "human")
    {
        D = new PlayerStats { Name = string.IsNullOrWhiteSpace(name) ? "Hero" : name, Class = cls, Race = race };
        var c = Classes.Get(cls); var r = Races.Get(race);
        void Attr(Dictionary<string, int> a) { foreach (var kv in a) switch (kv.Key) { case "str": D.Str += kv.Value; break; case "dex": D.Dex += kv.Value; break; case "vit": D.Vit += kv.Value; break; case "mag": D.Mag += kv.Value; break; } }
        Attr(c.Attr); Attr(r.Attr); D.SkillPts += r.Pts;
        foreach (var kv in Classes.StartGear(c)) D.Eq[kv.Key] = kv.Value;   // weapon, shield and the class gear set (F-34)
        Skills.GrantStarting(D); TreeDb.Load(); D.TreeVer = TreeDb.Version;
        PlayTime = 0; RecalcTree();
    }

    /// <summary>Game time is saved per hero (shown on the character select screen).</summary>
    public static string Played(PlayerStats p) { int m = (int)(p.Played / 60); return m < 60 ? $"{m} min" : $"{m / 60} h {m % 60} min"; }

    public static void LoadSettings()
    {
        try
        {
            using var f = Godot.FileAccess.Open(SetPath, Godot.FileAccess.ModeFlags.Read);
            if (f != null) Set = JsonSerializer.Deserialize<Settings>(f.GetAsText(), Opt) ?? new Settings();
        }
        catch { Set = new Settings(); }
        Set.Binds ??= new(); Set.PadBinds ??= new();
        if (Set.SetVer < 1) { Set.Outlines = false; Set.SetVer = 1; }   // the outline look was retired (too cartoony)
        if (Set.SetVer < 2) { if (Set.MaxFps == 0) Set.MaxFps = 144; Set.SetVer = 2; }   // older files: the old "no limit" default becomes a cap once
        if (Set.SetVer < 3) { Set.AutoQuality = true; Set.SetVer = 3; }   // the recalibrated presets: everyone starts on Auto once (pick a preset to leave it)
        Sfx.ApplyVolumes(); Inputs.ApplyBinds(); ApplyDisplay();
    }

    /// <summary>The window is out of focus or minimised: the game keeps running (multiplayer, timers) but draws only a few frames a second.</summary>
    public static bool Background;
    public const int BackgroundFps = 15;
    public static void SetBackground(bool bg) { if (bg == Background) return; Background = bg; Engine.MaxFps = bg ? BackgroundFps : Math.Max(0, Set.MaxFps); }
    /// <summary>
    /// "Auto" quality: the preset and 3D resolution from the screen width (the pixel count is what costs): above 2560 px Medium at
    /// 62% (FSR 1, about a 1440p load on a 4K screen), above 1920 px High at 77% (FSR 2), else High at full resolution.
    /// </summary>
    public static void PickAuto()
    {
        int w = 1920;
        if (DisplayServer.GetName() != "headless")
        {
            var win = DisplayServer.WindowGetSize(); var scr = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
            w = Math.Max(win.X, Set.WindowMode != 0 ? scr.X : 0); if (w <= 0) w = scr.X;
        }
        (Set.Quality, Set.RenderScale) = w > 2560 ? (1, .62f) : w > 1920 ? (2, .77f) : (2, 1f);
    }
    /// <summary>Window mode, vsync, frame cap, 3D resolution and outlines from the settings (field of view and shake are read live by CameraRig).</summary>
    public static void ApplyDisplay()
    {
        if (Set.AutoQuality) PickAuto();
        Set.RenderScale = Mathf.Clamp(Set.RenderScale, .5f, 1f); Set.Fov = Mathf.Clamp(Set.Fov, 55f, 100f);
        Engine.MaxFps = Background ? BackgroundFps : Math.Max(0, Set.MaxFps);
        if (Engine.GetMainLoop() is SceneTree tree)
        {
            // below full resolution the picture is upscaled with AMD FSR: FSR 2 (temporal, replaces TAA) on High/Ultra, the cheaper FSR 1 below
            var root = tree.Root; root.Scaling3DScale = Set.RenderScale;
            root.Scaling3DMode = Set.RenderScale > .99f ? Viewport.Scaling3DModeEnum.Bilinear : Set.Quality >= 2 ? Viewport.Scaling3DModeEnum.Fsr2 : Viewport.Scaling3DModeEnum.Fsr;
            root.FsrSharpness = .25f;
        }
        Toon.SetOutlines(Set.Outlines);
        Set.Quality = Math.Clamp(Set.Quality, 0, 3); Set.ViewDist = Mathf.Clamp(Set.ViewDist, 300f, 3000f); Set.Grass = Mathf.Clamp(Set.Grass, 0f, 1f);
        World.I?.ApplyQuality(); Flora.ApplyQuality();
        if (DisplayServer.GetName() == "headless") return;
        DisplayServer.WindowSetVsyncMode(Set.VSync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        var want = Set.WindowMode switch { 1 => DisplayServer.WindowMode.Fullscreen, 2 => DisplayServer.WindowMode.ExclusiveFullscreen, _ => DisplayServer.WindowMode.Windowed };
        var cur = DisplayServer.WindowGetMode();
        bool curFull = cur == DisplayServer.WindowMode.Fullscreen || cur == DisplayServer.WindowMode.ExclusiveFullscreen;
        // leaving a maximised window alone when "windowed" is chosen
        if (cur != want && (want != DisplayServer.WindowMode.Windowed || curFull)) DisplayServer.WindowSetMode(want);
    }
    public static void SaveSettings()
    {
        try { using var f = Godot.FileAccess.Open(SetPath, Godot.FileAccess.ModeFlags.Write); f.StoreString(JsonSerializer.Serialize(Set, Opt)); } catch { }
        Sfx.ApplyVolumes();
    }

    // ---- quest helpers
    public static QuestState Q(string id) { if (!D.Quests.TryGetValue(id, out var s)) D.Quests[id] = s = new QuestState(); return s; }

    // ---- level helpers
    /// <summary>Area level: the level zone's fixed level there (data/zones.json); outside every zone the old distance formula.</summary>
    public static int LevelAt(float x, float z, int realm) => Zones.At(x, z) is { } zn ? Zones.Level(zn, x, z, realm) : Mathf.Clamp(1 + (int)(new Vector2(x, z).Length() / Tune.F("world.metresPerAreaLevel", 38)) + realm * Tune.I("world.shadowAreaBonus", 1), 1, Tune.I("world.maxAreaLevel", 12));   // data/tuning.json "game.world"
}
