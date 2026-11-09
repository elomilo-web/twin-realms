using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class Settings
{
    public float Master = 0.8f, Sfx = 0.8f, Ambient = 0.6f, Sens = 1f;
    public bool InvertY = false;
}

/// <summary>Global game state, saving and cross-system helpers.</summary>
public static class Game
{
    public static PlayerStats D = new();
    public static TreeStats TS = new();
    public static Settings Set = new();
    public static Node3D? Root;                 // Main scene root; projectiles, loot and floating text are added here
    public static float PlayTime;
    public static event Action? Changed;        // fired when saved data changes (UI refresh)

    const string SavePath = "user://save.json", SetPath = "user://settings.json";
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

    public static bool HasSave() => Godot.FileAccess.FileExists(SavePath);

    public static void Save()
    {
        try
        {
            if (Player.Instance != null && !Dungeon.Active) { D.PosX = Player.Instance.GlobalPosition.X; D.PosZ = Player.Instance.GlobalPosition.Z; }
            D.Time = World.TimeOfDay;
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Write);
            f.StoreString(JsonSerializer.Serialize(D, Opt));
        }
        catch (Exception e) { GD.PushError("save failed: " + e.Message); }
    }

    public static bool Load()
    {
        try
        {
            using var f = Godot.FileAccess.Open(SavePath, Godot.FileAccess.ModeFlags.Read);
            if (f == null) return false;
            var d = JsonSerializer.Deserialize<PlayerStats>(f.GetAsText(), Opt);
            if (d == null) return false;
            D = d; Skills.GrantStarting(D); RecalcTree(); return true;
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
        Skills.GrantStarting(D);
        PlayTime = 0; RecalcTree();
    }

    public static void LoadSettings()
    {
        try
        {
            using var f = Godot.FileAccess.Open(SetPath, Godot.FileAccess.ModeFlags.Read);
            if (f != null) Set = JsonSerializer.Deserialize<Settings>(f.GetAsText(), Opt) ?? new Settings();
        }
        catch { Set = new Settings(); }
        Sfx.ApplyVolumes();
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
