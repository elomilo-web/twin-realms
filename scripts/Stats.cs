using System;

namespace TwinRealms;

/// <summary>Player attributes and derived stats. Formulas ported from the prototype (docs/spec/reference_logic.js).
/// The skill tree and equipment will plug into the "bonus" values later.</summary>
public class PlayerStats
{
    public string Name = "Hero";
    public int Level = 1, Xp = 0, Gold = 60, AttrPts = 0, SkillPts = 3, Kills = 0;
    public int Str = 5, Dex = 5, Vit = 5, Mag = 5;
    public float WeaponDmg = 4f;   // Iron Sword
    public float ShieldDef = 2f;   // Oak Buckler

    public int MaxHp => 100 + Level * 8 + Vit * 7;
    public int MaxSt => 90 + Dex * 3;
    public int MaxMp => 50 + Mag * 6 + Level * 2;
    public int Defense => (int)ShieldDef + Vit / 2;
    public float SwordDamage => 5f + Str * 1.8f + WeaponDmg;
    public int XpNeed => 80 + (Level - 1) * 55 + (Level - 1) * (Level - 1) * 8;

    /// <summary>Adds XP, returns how many levels were gained.</summary>
    public int GainXp(int n)
    {
        Xp += n; int up = 0;
        while (Xp >= XpNeed) { Xp -= XpNeed; Level++; AttrPts += 2; SkillPts += 2; up++; }
        return up;
    }
}

public static class Scaling
{
    public static float EnemyHp(float baseHp, int lvl) => baseHp * (1f + 0.2f * (lvl - 1));
    public static float EnemyDmg(float baseDmg, int lvl) => baseDmg * (1f + 0.13f * (lvl - 1));
}
