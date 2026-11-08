using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwinRealms;

public class QuestState { public int S; public int N; }   // S: 0 none, 1 active, 2 ready to turn in, 3 done

/// <summary>Everything that is saved. Plain public fields (serialised with IncludeFields).</summary>
public class PlayerStats
{
    public string Name = "Hero", Class = "warrior";
    public int Level = 1, Xp = 0, Gold = 60, AttrPts = 0, SkillPts = 3, Kills = 0;
    public int Str = 5, Dex = 5, Vit = 5, Mag = 5;
    public int HpPot = 3, MpPot = 2, BestWave = 0, BestDuel = 0, Deepest = 0;
    public bool Horse = false;
    public float PosX = 0, PosZ = 6, Time = 0.33f;
    public Dictionary<string, Item?> Eq = new() { ["weapon"] = null, ["armor"] = null, ["shield"] = null, ["trinket"] = null };
    public List<Item> Inv = new();
    public List<int> Tree = new();
    public Dictionary<string, QuestState> Quests = new();
    public List<string> Disc = new() { "village" };

    // ---- derived stats (methods so they are not serialised)
    static TreeStats TS => Game.TS;
    public float EqStat(string k)
    {
        float s = 0;
        foreach (var it in Eq.Values)
        {
            if (it == null) continue;
            s += k switch { "dmg" => it.Dmg, "def" => it.Def, "hp" => it.Hp, "mp" => it.Mp, "sp" => it.Sp, _ => 0 };
        }
        return s;
    }
    [JsonIgnore] public Item? Weapon => Eq.GetValueOrDefault("weapon");
    public string WType() => Weapon?.Wt ?? "sword";
    public int MaxHp() => Math.Max(30, (int)Math.Round((100 + Level * 8 + Vit * 7 + EqStat("hp") + TS["hp"]) * (1 + TS["hpP"] / 100f)));
    public int MaxMp() => (int)Math.Round((50 + Mag * 6 + Level * 2 + EqStat("mp") + TS["mp"]) * (1 + TS["mpP"] / 100f));
    public int MaxSt() => (int)Math.Round(90 + Dex * 3 + TS["st"]);
    public int Defense() => (int)(EqStat("def") + Vit / 2 + TS["def"]);
    public int XpNeed() => 80 + (Level - 1) * 55 + (Level - 1) * (Level - 1) * 8;
    public float AtkSpeed() => (1 + Dex * .012f) * (1 + TS["aspd"] / 100f);
    public float CritChance() => .05f + Dex * .005f + TS["crit"] / 100f;
    public float CritMult() => 1.8f + TS["critD"] / 100f;
    public float SpellPow() => (1 + Mag * .08f + Level * .02f) * (1 + TS["spellP"] / 100f) * (1 + EqStat("sp")) * (1 + TS["allP"] / 100f);
    public float WpnDmg()
    {
        string wt = WType(); float wd = Weapon?.Dmg ?? 0, all = 1 + TS["allP"] / 100f;
        if (wt == "bow") return (5 + Dex * 1.7f + wd + Str * TS["strToBow"] + Mag * TS["magToBow"]) * (1 + TS["rangedP"] / 100f) * all;
        if (wt == "wand" || wt == "staff") return (4 + Mag * 1.5f + wd) * (1 + TS["spellP"] / 100f) * (1 + EqStat("sp")) * all;
        return (5 + Str * 1.8f + wd + Mag * TS["magToDmg"]) * (1 + TS["meleeP"] / 100f) * all;
    }
    [JsonIgnore] public float ShieldDef => Eq.GetValueOrDefault("shield")?.Def ?? 0;

    /// <summary>Adds XP, returns how many levels were gained.</summary>
    public int GainXp(int n)
    {
        Xp += n; int up = 0;
        while (Xp >= XpNeed()) { Xp -= XpNeed(); Level++; AttrPts += 2; SkillPts += 2; up++; }
        return up;
    }
}

public static class Scaling
{
    public static float EnemyHp(float baseHp, int lvl) => baseHp * (1f + 0.2f * (lvl - 1));
    public static float EnemyDmg(float baseDmg, int lvl) => baseDmg * (1f + 0.13f * (lvl - 1));
}
