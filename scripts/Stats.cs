using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwinRealms;

public class QuestState { public int S; public int N; }   // S: 0 none, 1 active, 2 ready to turn in, 3 done

/// <summary>Everything that is saved. Plain public fields (serialised with IncludeFields).</summary>
public class PlayerStats
{
    public string Name = "Hero", Class = "warrior", Race = "human", Body = "male";   // Body: base model (male/female)
    public static readonly string[] Slots = { "weapon", "shield", "head", "armor", "hands", "feet", "trinket", "cape" };
    public List<Item> Stash = new();                    // storage chest at home; older saves load with an empty chest
    public Dictionary<string, SkillState> Skills = new(); // learned active skills with rank and socketed supports
    public List<string> Supports = new();                 // unlocked support modifiers
    public Dictionary<string, AttackBuild> Attacks = new(); // basic attack per weapon class: rank, chain of steps, socketed modifiers (data/attacks.json)
    public List<string> AtkSteps = new();                 // unlocked attack steps, as "class.step"
    public List<string> AtkMods = new();                  // unlocked attack modifiers
    public string[] Bar = new string[6];                  // hotbar keys 1-6
    public int RankPts = 2;                               // skill rank points (1 per level)
    public int TreeVer = 0;                               // skill tree layout version; older trees are refunded on load
    public int Level = 1, Xp = 0, Gold = 60, AttrPts = 0, SkillPts = 3, Kills = 0;
    public int Str = 5, Dex = 5, Vit = 5, Mag = 5;
    public int HpPot = 3, MpPot = 2, BestWave = 0, BestDuel = 0, Deepest = 0;
    public int Valor = 0, GachaPity = 0;   // arena valor points (data/valor.json) and pulls since the last epic
    public int DuelWins = 0;                              // duels won against friends
    public float Played = 0;                              // seconds played with this hero
    public bool Horse = false;
    public float PosX = 0, PosZ = 6, Time = 0.33f;
    public Dictionary<string, Item?> Eq = new() { ["weapon"] = null, ["armor"] = null, ["shield"] = null, ["trinket"] = null };
    public List<Item> Inv = new();
    public List<int> Tree = new();
    public Dictionary<string, QuestState> Quests = new();
    public List<string> Disc = new() { "village" };
    public Dictionary<string, int> Mats = new();   // gathered materials and disassembly scraps by id (data/gather.json); older saves load with an empty bag
    // pickup filter (Pack > Pickup filter): items below PickRar or of a slot in PickSkip are left on the ground (PickScrap: disassembled instead). Set pieces are always picked up.
    public int PickRar = 0; public List<string> PickSkip = new(); public bool PickScrap = false;
    public List<string> Titles = new(); public string Title = "";   // earned titles and the one worn (scripts/Titles.cs); older saves load with none

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
    // base numbers in data/tuning.json "game.player" (the defaults here are the old built-in values)
    public int MaxHp() => Math.Max(30, (int)Math.Round((Tune.F("player.hpBase", 100) + Level * Tune.F("player.hpPerLevel", 8) + Vit * Tune.F("player.hpPerVit", 7) + EqStat("hp") + TS["hp"]) * (1 + TS["hpP"] / 100f)));
    public int MaxMp() => (int)Math.Round((Tune.F("player.mpBase", 50) + Mag * Tune.F("player.mpPerMag", 6) + Level * Tune.F("player.mpPerLevel", 2) + EqStat("mp") + TS["mp"]) * (1 + TS["mpP"] / 100f));
    public int MaxSt() => (int)Math.Round(Tune.F("player.staminaBase", 90) + Dex * Tune.F("player.staminaPerDex", 3) + TS["st"]);
    public int Defense() => (int)(EqStat("def") + Vit * Tune.F("player.defensePerVit", .5f) + TS["def"]);
    public int XpNeed() => Progression.XpNeed(Level);   // curve in data/progression.json
    public float AtkSpeed() => (1 + Dex * Tune.F("player.attackSpeedPerDex", .012f)) * (1 + TS["aspd"] / 100f);
    public float CritChance() => Tune.F("player.critBase", .05f) + Dex * Tune.F("player.critPerDex", .005f) + TS["crit"] / 100f;
    public float CritMult() => Tune.F("player.critDamage", 1.8f) + TS["critD"] / 100f;
    public float SpellPow() => (1 + Mag * Tune.F("player.spellPerMag", .08f) + Level * Tune.F("player.spellPerLevel", .02f)) * (1 + TS["spellP"] / 100f) * (1 + EqStat("sp")) * (1 + TS["allP"] / 100f);
    /// <summary>
    /// Weapon hit damage before crits: base 5 plus attribute scaling from data/weapons.json (str/dex/mag per point) plus item damage,
    /// times the weapon's damage factor, times the matching passive bonus (melee, bow, or spell for wands and staves) and all-damage.
    /// </summary>
    public float WpnDmg()
    {
        string wt = WType(); var w = Weapons.Get(wt); float wd = Weapon?.Dmg ?? 0, all = 1 + TS["allP"] / 100f;
        float b = Tune.F("player.weaponBase", 5);
        if (wt == "bow") return (b + Dex * w.Dex + Str * w.Str + wd + Str * TS["strToBow"] + Mag * TS["magToBow"]) * w.Dmg * (1 + TS["rangedP"] / 100f) * all;
        if (wt == "wand" || wt == "staff") return (Tune.F("player.magicWeaponBase", 4) + Mag * w.Mag + wd) * w.Dmg * (1 + TS["spellP"] / 100f) * (1 + EqStat("sp")) * all;
        float wpnType = 1 + TS[wt + "P"] / 100f;                                    // e.g. "axeP": +% axe damage
        return (b + Str * w.Str + Dex * w.Dex + Mag * w.Mag + wd + Mag * TS["magToDmg"]) * w.Dmg * (1 + TS["meleeP"] / 100f) * wpnType * all;
    }
    [JsonIgnore] public float ShieldDef => Eq.GetValueOrDefault("shield")?.Def ?? 0;

    /// <summary>Adds XP, returns how many levels were gained.</summary>
    public int GainXp(int n)
    {
        Xp += (int)Math.Round(n * (1 + TS["xpP"] / 100f)); int up = 0;
        // per level (data/progression.json): attribute points, skill points (Skills menu, RankPts) and skill tree points (SkillPts)
        while (Xp >= XpNeed()) { Xp -= XpNeed(); Level++; AttrPts += Progression.AttrPerLevel; SkillPts += Progression.TreePerLevel; RankPts += Progression.RankPerLevel; up++; }
        return up;
    }
}

public static class Scaling
{
    // curves in data/progression.json "scaling"; Enemy.Rescale adds the hero-power floor and the party, threat and dungeon tier multipliers
    public static float EnemyHp(float baseHp, int lvl) => baseHp * Progression.HpCurve(lvl);
    public static float EnemyDmg(float baseDmg, int lvl) => baseDmg * Progression.DmgCurve(lvl);
}
