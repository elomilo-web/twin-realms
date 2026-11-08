using System;
using System.Collections.Generic;
using System.Text.Json;
using Godot;

namespace TwinRealms;

public class EnemyDef
{
    public string N { get; set; } = "Enemy";
    public string Kind { get; set; } = "hum";
    public float Hp { get; set; } = 40;
    public float Dmg { get; set; } = 6;
    public float Spd { get; set; } = 4;
    public float Aggro { get; set; } = 20;
    public float Reach { get; set; } = 2.2f;
    public float Wind { get; set; } = 0.5f;
    public float Cd { get; set; } = 1.2f;
    public int Xp { get; set; } = 10;
    public float R { get; set; } = 0.45f;
    public float Scale { get; set; } = 1f;
    public int Color { get; set; } = 0x888888;
    public bool Boss { get; set; }
    public bool Slam { get; set; }
    public int Realm { get; set; }
    public string? Ranged { get; set; }
    public float Range { get; set; } = 0f;
    public float Keep { get; set; } = 0f;
    public string? Summon { get; set; }
    public bool Breath { get; set; }
}

/// <summary>Loads res://data/enemies.json (extracted from the prototype). Stats are never hard-coded in C#.</summary>
public static class EnemyDb
{
    static Dictionary<string, EnemyDef>? _defs;

    public static EnemyDef Get(string type)
    {
        Load();
        return _defs!.TryGetValue(type, out var d) ? d : new EnemyDef();
    }

    static void Load()
    {
        if (_defs != null) return;
        _defs = new Dictionary<string, EnemyDef>();
        using var f = Godot.FileAccess.Open("res://data/enemies.json", Godot.FileAccess.ModeFlags.Read);
        if (f == null) { GD.PushError("Could not open res://data/enemies.json"); return; }
        try
        {
            using var doc = JsonDocument.Parse(f.GetAsText());
            var opt = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var p in doc.RootElement.GetProperty("enemies").EnumerateObject())
            {
                try { _defs[p.Name] = JsonSerializer.Deserialize<EnemyDef>(p.Value.GetRawText(), opt) ?? new EnemyDef(); }
                catch (Exception e) { GD.PushError($"enemy '{p.Name}': {e.Message}"); }
            }
        }
        catch (Exception e) { GD.PushError("enemies.json: " + e.Message); }
    }
}
