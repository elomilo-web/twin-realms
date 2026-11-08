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

    public static void RecalcTree() { TreeDb.Load(); TreeDb.Recalc(new HashSet<int>(D.Tree), TS); }
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
            D = d; RecalcTree(); return true;
        }
        catch (Exception e) { GD.PushError("load failed: " + e.Message); return false; }
    }

    public static void NewGame(string cls, string name)
    {
        D = new PlayerStats { Name = string.IsNullOrWhiteSpace(name) ? "Hero" : name, Class = cls };
        D.Eq["weapon"] = new Item { Name = "Iron Sword", Slot = "weapon", Wt = "sword", Dmg = 4 };
        D.Eq["shield"] = new Item { Name = "Oak Buckler", Slot = "shield", Def = 2 };
        using var doc = JsonUtil.Load("res://data/classes.json");
        if (doc.RootElement.GetProperty("classes").TryGetProperty(cls, out var c))
        {
            if (c.TryGetProperty("a", out var a))
            {
                if (a.TryGetProperty("str", out var s)) D.Str += s.GetInt32();
                if (a.TryGetProperty("dex", out var x)) D.Dex += x.GetInt32();
                if (a.TryGetProperty("vit", out var v)) D.Vit += v.GetInt32();
                if (a.TryGetProperty("mag", out var m)) D.Mag += m.GetInt32();
            }
            if (c.TryGetProperty("w", out var w))
            {
                D.Eq["weapon"] = new Item { Name = JsonUtil.S(w, "name", "Weapon"), Slot = "weapon", Wt = JsonUtil.S(w, "wt", "sword"), Dmg = (int)JsonUtil.F(w, "dmg"), Sp = JsonUtil.F(w, "sp"), Two = w.TryGetProperty("two", out var t) && t.GetBoolean() };
            }
            if (c.TryGetProperty("noShield", out var ns) && ns.GetBoolean()) D.Eq["shield"] = null;
        }
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
