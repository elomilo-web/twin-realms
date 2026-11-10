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
    // wildlife
    public string Look { get; set; } = "";        // extra body parts for quads: deer, rabbit, boar
    public bool Flee { get; set; }                // runs from the player instead of fighting
    public bool NoLoot { get; set; }              // no gold or gear, only Drops
    public bool Emissive { get; set; }            // glowing body (shadow creatures); "glow" is already a colour field on some enemies
    public Dictionary<string, int[]>? Drops { get; set; }   // materials given on death: id -> [min, max]
    // defenses (League of Legends style): armor reduces physical damage, mr (magic resist) reduces elemental damage, res adds per-type % resistance
    public float Armor { get; set; } = 10;
    public float Mr { get; set; } = 10;
    public Dictionary<string, float>? Res { get; set; }
    public string DmgType { get; set; } = "physical";
    public Dictionary<string, float>? OnHit { get; set; }   // status id -> chance to apply when this enemy hits the player

    /// <summary>A private copy (the definitions from EnemyDb are shared by every enemy of that type).</summary>
    public EnemyDef Clone()
    {
        var c = (EnemyDef)MemberwiseClone();
        if (Res != null) c.Res = new(Res); if (OnHit != null) c.OnHit = new(OnHit); if (Drops != null) c.Drops = new(Drops);
        return c;
    }
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
