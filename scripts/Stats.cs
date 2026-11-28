using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TwinRealms;

public class QuestState { public int S; public int N; }   // S: 0 none, 1 active, 2 ready to turn in, 3 done

/// <summary>Everything that is saved. Plain public fields (serialised with IncludeFields).</summary>
public class PlayerStats
{
    public string Name = "Hero", Class = "warrior", Race = "human", Body = "male";   // Body: base model (male/female)
    public HeroLook Look = new();   // the character creator's choices (AvatarLook.cs, focus F-36): body, face, hair, skin, markings, voice. Older saves load with the plain race look.
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
    // A-14: Wisdom (healing, mana regeneration, buff duration), Agility (move and attack speed, roll cost, evasion), Luck (crits, drops,
    // status resistance), Endurance (stamina, block, gear wear). What a point gives is data (data/stats.json "attributes"); older saves load with 5.
    public int Wis = 5, Agi = 5, Lck = 5, End = 5;
    /// <summary>Points in an attribute by id ("str", "dex", "vit", "mag", "wis", "agi", "lck", "end").</summary>
    public int AttrOf(string id) => id switch { "str" => Str, "dex" => Dex, "vit" => Vit, "mag" => Mag, "wis" => Wis, "agi" => Agi, "lck" => Lck, "end" => End, _ => 0 };
    /// <summary>Adds points to an attribute by id (race and class starting bonuses, the character window).</summary>
    public void AddAttr(string id, int n)
    {
        switch (id) { case "str": Str += n; break; case "dex": Dex += n; break; case "vit": Vit += n; break; case "mag": Mag += n; break; case "wis": Wis += n; break; case "agi": Agi += n; break; case "lck": Lck += n; break; case "end": End += n; break; }
    }
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
    public int Rod = 0;                                 // fishing rod owned (Fishing.cs, F-23): 0 none, 1 rod; older saves load without one
    public Dictionary<string, int> Life = new();        // life skill XP by id ("fishing"; LifeSkills.cs, F-23/F-24); older saves load with none
    public List<string> Recipes = new();                // cooking recipes learned (data/cooking.json; Cooking.cs, F-24); older saves load with the starting ones
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
    public TradeOffer? TradeHold;   // A-30 (Trade.cs): items, stacks, pets and gold taken out for a player trade in a friend's hosted world; put back if it falls through. Null in older saves and almost always.
    // Auction house (Auction.cs, focus F-37): the offline book (NPC listings, the hero's listings and collection box; null until first used and for
    // online heroes, whose book is the server's) and the number of the last server change this save contains (AhDelta.Seq). Older saves load with none.
    public AhBook? Ah; public long AhSeq = 0;
    // NPC memory and standing (NpcMemory.cs, focus F-28): what each NPC of data/npcs.json remembers of this hero, and standing per town. Older saves load with none.
    public Dictionary<string, NpcMem> Npcs = new();
    public Dictionary<string, int> TownRep = new();
    // Story quests (Story.cs, data/story.json, focus F-31): state per quest, and world flags set by their choices (also "npc:<id>:gone|schedule|line"). Older saves load with none.
    public Dictionary<string, StoryState> Story = new();
    public Dictionary<string, string> Flags = new();
    public Dictionary<string, int> Bounty = new();   // gold on the hero's head per town (Crime.cs, F-32); falls by the hour. Older saves load with none.
    public Dictionary<string, long> Broken = new();  // A-36 (Breakables.cs): broken crates, barrels, pots and opened chests by prop id -> unix time broken (they come back after their respawn time). Older saves load with none.
    public List<string> Instruments = new(); public string Instrument = "";   // instruments owned (data/instruments.json ids) and the one taken out with the instrument key (Instruments.cs, F-40). Older saves load with none.
    public SteadSave Stead = new();   // A-24..A-26 (Stead.cs): planted crops, forge contents, ranch animals. Older saves load with none.

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
    /// <summary>A-12: the off-hand weapon's hit while dual wielding (its own class and item damage, x dual.offDamageMul); 0 when not.</summary>
    public float OffDmg() => StatPipeline.Get(this, "offDmg");
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
