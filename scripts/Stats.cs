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
    public List<Macro> Macros = new() { new Macro(), new Macro() };   // skill chains on the macro keys (PlayerMacros.cs); older saves load two empty ones
    /// <summary>Macro slot i (0 or 1), created if an older save has fewer.</summary>
    public Macro MacroAt(int i) { Macros ??= new(); while (Macros.Count <= i) Macros.Add(new Macro()); return Macros[i] ??= new Macro(); }
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
    public Dictionary<string, int> Gems = new();   // loose gems by key "type:tier" (Gems.cs, focus F-10); older saves load with an empty bag
    public int Pick = 1;                                // pickaxe tier (data/blocks.json "pickaxes": 1 Wooden .. 4 Runed); older saves load with the wooden one
    public List<string> BuildBar = new();                 // build-mode hotbar: up to 9 block keys (PlayerBlocks.cs), filled from the blocks owned
    // pickup filter (Pack > Pickup filter): items below PickRar or of a slot in PickSkip are left on the ground (PickScrap: disassembled instead). Set pieces are always picked up.
    public int PickRar = 0; public List<string> PickSkip = new(); public bool PickScrap = false;
    public List<string> Titles = new(); public string Title = "";   // earned titles and the one worn (scripts/Titles.cs); older saves load with none
    public List<string> Forgot = new();   // skills unlearned in the skills menu (class skills are not handed back on load)
    // Spellcraft (scripts/Spellcraft.cs, data/spellcards.json): spell cards owned, the spells built from them, whether the starter kit was given.
    // Older saves load with none and get their class kit once.
    public Dictionary<string, int> Cards = new();
    public List<CraftedSpell> Spells = new();
    public bool CardsGiven = false; public int SpellSeq = 0;
    public Dictionary<string, int> CraftTree = new();   // spell tree ranks (Spellcraft menu > Spell tree): node id -> rank
    public Dictionary<string, string> BlockDiffs = new();   // block world changes per area ("overworld", "dun:...") as packed diffs (BlockArea.Encode); older saves have none
    // Placed blocks with state (BlockUse.cs): chest contents and who placed a door or chest (locks), keyed "area|x,y,z". Older saves have none.
    public Dictionary<string, List<Item>> Chests = new();
    public Dictionary<string, string> BlockOwners = new();
    // Pets (Bound Souls, scripts/PetDb.cs): every bound soul (party + Soul Box), the one out in the world, Soul Cubes by tier and the tier thrown. Older saves load with none.
    public List<PetState> Pets = new(); public string ActivePet = "";
    public Dictionary<string, int> Cubes = new(); public string CubeSel = "iron";
    // Soul Duels (scripts/SoulDuel.cs): the ladder rank fought next (index into pets.json duel.ranks), duels won and lost. Pet trading escrow
    // (PetTrade.cs): pets taken out for a trade in flight (put back if it falls through). Older saves load with none.
    public int DuelRank = 0, SoulWins = 0, SoulLosses = 0;
    public List<PetState> TradeEscrow = new(); public int TradeEscrowGold = 0;

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
    public string WType() => Weapon?.Wt ?? "unarmed";   // no weapon: fists (data/weapons.json "unarmed", attacks.json style "unarmed")
    // base numbers in data/tuning.json "game.player" (the defaults here are the old built-in values)
    // Every derived stat comes from the one stat pipeline (StatPipeline.cs: base > race > class > level > gear > gems > passives > buffs,
    // caps in data/stats.json, formulas in .devloop/systems/stats.md). Cached per hero; "/stats dump" (F9) prints every step.
    public int MaxHp() => (int)StatPipeline.Get(this, "maxHp");
    public int MaxMp() => (int)StatPipeline.Get(this, "maxMp");
    public int MaxSt() => (int)StatPipeline.Get(this, "maxSt");
    public int Defense() => (int)StatPipeline.Get(this, "defense");
    public int XpNeed() => Progression.XpNeed(Level);   // curve in data/progression.json
    public float AtkSpeed() => StatPipeline.Get(this, "atkSpeed");
    public float CritChance() => StatPipeline.Get(this, "critChance");
    public float CritMult() => StatPipeline.Get(this, "critMult");
    public float SpellPow() => StatPipeline.Get(this, "spellPow");
    /// <summary>
    /// Weapon hit damage before crits: base 5 (wands/staves 4) plus attribute scaling from data/weapons.json plus item damage, times the
    /// weapon's damage factor, times the matching bonus groups (melee / bow / spell + gear spell power), the weapon type (swordP, bowP...) and all-damage.
    /// </summary>
    public float WpnDmg() => StatPipeline.Get(this, "wpnDmg");
    [JsonIgnore] public float ShieldDef => Eq.GetValueOrDefault("shield")?.Def ?? 0;

    /// <summary>Adds XP, returns how many levels were gained.</summary>
    public int GainXp(int n)
    {
        // never loses experience: a negative gain or an xpP below -100% used to subtract (F-02); xpP is capped at -90% (data/stats.json "*P")
        Xp = Math.Max(0, Xp) + Math.Max(0, (int)Math.Round(n * (1 + StatPipeline.Key("xpP") / 100f))); int up = 0;
        // per level (data/progression.json): attribute points, skill points (Skills menu, RankPts) and skill tree points (SkillPts)
        while (Xp >= XpNeed()) { Xp -= XpNeed(); Level++; AttrPts += Progression.AttrPerLevel; SkillPts += Progression.TreePerLevel; RankPts += Progression.RankPerLevel; up++; }
        return up;
    }
}

public static class Scaling
{
    // curves in data/progression.json "scaling"; Enemy.Rescale adds the hero-power floor and the party, threat and dungeon tier multipliers
    public static float EnemyHp(float baseHp, int lvl) => Progression.EnemyHp(baseHp, lvl);
    public static float EnemyDmg(float baseDmg, int lvl) => Progression.EnemyDmg(baseDmg, lvl);
}
