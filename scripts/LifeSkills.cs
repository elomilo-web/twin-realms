using System;
using System.Collections.Generic;
using Godot;

namespace TwinRealms;

/// <summary>
/// Life skills (fishing now; F-24 adds mining and cooking, perks and a panel): XP per skill id in PlayerStats.Life, levels from a curve.
/// Curve per skill in data (fishing.json "skill": base, growth, max); unknown ids use the default curve.
/// </summary>
public static class LifeSkills
{
    public sealed class Curve { public float Base = 40, Growth = 1.35f; public int Max = 50; }
    static readonly Dictionary<string, Curve> Curves = new();
    static string Title(string id) => id.Length == 0 ? id : char.ToUpper(id[0]) + id[1..];

    /// <summary>Registers a skill's curve (called by the system that owns the skill, e.g. FishDb).</summary>
    public static void Define(string id, Curve c) => Curves[id] = c;
    static Curve C(string id) => Curves.TryGetValue(id, out var c) ? c : new Curve();

    /// <summary>XP needed to go from level lv to lv + 1.</summary>
    public static int Need(string id, int lv) { var c = C(id); return (int)Mathf.Round(c.Base * Mathf.Pow(lv, c.Growth)); }
    public static int Xp(PlayerStats d, string id) { d.Life ??= new(); return d.Life.GetValueOrDefault(id); }
    /// <summary>Level 1.. from the total XP.</summary>
    public static int Level(PlayerStats d, string id)
    {
        int xp = Xp(d, id), lv = 1, max = C(id).Max;
        while (lv < max) { int n = Need(id, lv); if (xp < n) break; xp -= n; lv++; }
        return lv;
    }
    public static int Level(string id) => Level(Game.D, id);
    /// <summary>0..1 progress into the current level.</summary>
    public static float Progress(PlayerStats d, string id)
    {
        int xp = Xp(d, id), lv = 1, max = C(id).Max;
        while (lv < max) { int n = Need(id, lv); if (xp < n) return n > 0 ? (float)xp / n : 0; xp -= n; lv++; }
        return 1;
    }
    /// <summary>Adds XP; on a level-up a note and a fanfare. Returns the new level.</summary>
    public static int Gain(string id, int xp, PlayerStats? d = null, bool quiet = false)
    {
        d ??= Game.D; d.Life ??= new(); if (xp <= 0) return Level(d, id);
        int before = Level(d, id); d.Life[id] = d.Life.GetValueOrDefault(id) + xp; int after = Level(d, id);
        if (!quiet && after > before) { Ui.Note($"{Title(id)} level {after}!", UiTheme.Highlight); Sfx.Play("level"); }
        return after;
    }
}
