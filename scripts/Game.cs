using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class Settings
{
    public float Master = 0.8f, Sfx = 0.8f, Ambient = 0.6f, Sens = 1f;
    public bool InvertY = false;
    public Dictionary<string, string> Binds = new();   // rebound controls: action -> "k:keycode" / "m:button" (older settings files: empty = defaults)
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

    public static string SavePath = "user://save.json";   // the self-test bots use their own files so they never touch your hero
    public static string SetPath = "user://settings.json";   // the test bots use their own file so they never change your controls
    static readonly JsonSerializerOptions Opt = new() { IncludeFields = true, WriteIndented = false };

    /// <summary>All permanent modifiers: skill tree nodes + race bonuses + class passive. Everything reads them through Game.TS.</summary>
    public static void RecalcTree()
    {
        TreeDb.Load(); TreeDb.Recalc(new HashSet<int>(D.Tree), TS);
        foreach (var kv in Races.Get(D.Race).Mods) TS[kv.Key] += kv.Value;
        foreach (var kv in Classes.Get(D.Class).Mods) TS[kv.Key] += kv.Value;
        if (Player.Instance != null) foreach (var b in Player.Instance.Buffs) foreach (var kv in b.mods) TS[kv.Key] += kv.Value;   // timed skill buffs (War Cry, Venom Blade)
    }
    public static void Notify() => Changed?.Invoke();

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

    static void WriteFile(SaveFile file)
    {
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
            var file = File(); file.Slots[Slot] = D; file.Last = Slot; WriteFile(file);
        }
        catch (Exception e) { GD.PushError("save failed: " + e.Message); }
    }

    public static bool Load()
    {
        try
        {
            // a fresh copy from disk so the in-memory slot and the hero being played are never the same object by accident
            _file = null; var d0 = Peek(Slot); if (d0 == null) return false;
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
        D.Eq["weapon"] = new Item { Name = c.WName, Slot = "weapon", Wt = c.Wt, Dmg = c.WDmg, Sp = c.WSp, Two = Weapons.Get(c.Wt).Hands > 1 };
        D.Eq["shield"] = c.Shield ? new Item { Name = "Oak Buckler", Slot = "shield", Def = 2 } : null;
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
        Set.Binds ??= new();
        Sfx.ApplyVolumes(); Inputs.ApplyBinds();
    }
    public static void SaveSettings()
    {
        try { using var f = Godot.FileAccess.Open(SetPath, Godot.FileAccess.ModeFlags.Write); f.StoreString(JsonSerializer.Serialize(Set, Opt)); } catch { }
        Sfx.ApplyVolumes();
    }

    // ---- quest helpers
    public static QuestState Q(string id) { if (!D.Quests.TryGetValue(id, out var s)) D.Quests[id] = s = new QuestState(); return s; }

    // ---- level helpers
    public static int LevelAt(float x, float z, int realm) => Mathf.Clamp(1 + (int)(new Vector2(x, z).Length() / 38f) + realm, 1, 12);
}
