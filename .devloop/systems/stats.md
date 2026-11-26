# Stats (hero stat pipeline, focus F-02)

Code: `scripts/StatPipeline.cs` (pipeline, caps, dump), `scripts/Stats.cs` (getters), `scripts/Game.cs` RecalcTree,
`scripts/Skills.cs` Compute, `scripts/Progression.cs` (XP curve, armor), `scripts/Commands.cs` (`/stats dump`).
Data: `data/stats.json` (caps), `data/tuning.json` game.player (base numbers; tuning OVERRIDES other data files),
`data/races.json`, `data/classes.json`, `data/skilltree.json`, `data/weapons.json`, `data/progression.json`, `data/skills.json`.

## One pipeline
Stages, in order: **base > race > class > level > gear > gems > passives > life > buffs**.

| stage    | what it holds |
|----------|---------------|
| base     | tuning player.* bases (hpBase, critBase, weaponBase...), the 5 starting points of each attribute, the weapon type's damage factor |
| race     | race `mods` (races.json) and the race's start attributes |
| class    | class passive `mods` (classes.json) and the class's start attributes |
| level    | level x perLevel, attribute points bought on level-up |
| gear     | item base stats (Dmg/Def/Hp/Mp/Sp), item `Mods` (affixes, Golden Wings), set bonuses (loot.json sets) |
| gems     | `Item.Gems` (one stat table per gem) - empty until F-10 |
| passives | skill tree nodes, the worn title |
| life     | life and action skills (F-24/F-25, `scripts/LifeSkills.cs`, `data/life.json`): per skill, level (+ gem `life_<id>` levels from the stages above) x its `perLevel` keys for every level past 1 |
| buffs    | timed skill buffs (`Player.Buffs`: War Cry, Venom Blade...) and the food buff (id `food`, cooking F-24) |

1. `Game.RecalcTree()` -> `StatPipeline.Rebuild`: every stage's contributions to the stat-table keys (meleeP, cdr, res_fire...) are
   recorded, summed into `Game.TS`, then each key is clamped to its cap. Runs on `Game.Notify`, NewGame/Load and whenever a buff
   starts or expires. Nothing recomputes per frame.
2. Derived stats (`StatPipeline.Sheet(d)`) per hero: **flat** steps add up first, then each **percent group** multiplies by
   `max(pctFloor, 1 + sum/100)` (groups multiply each other, keys inside one group add), then **more** multipliers, then rounding,
   then the derived cap. Cached per PlayerStats; rebuilt only when the signature changes (TS version, level, attributes, worn
   items and their numbers). The dump shows how often a sheet was rebuilt (`[perf]`).
3. Readers of a key use `StatPipeline.Key(k)` (the capped total) or `Game.TS[k]` (already capped by Rebuild).

Attributes never count below 0. Attribute steps are split by origin: 5 base, race bonus, class bonus, level-up points.

## Formulas (A = attribute, L = level; defaults are tuning.json game.player)
- **Max health** = round((hpBase 100 + L x 8 + Vit x 7 + gear hp + key hp) x (1 + hpP%)), cap >= 30
- **Max mana** = round((mpBase 50 + Mag x 6 + L x 2 + gear mp + key mp) x (1 + mpP%)), >= 0
- **Max stamina** = round(90 + Dex x 3 + key st), >= 10
- **Defense (armor)** = trunc(gear def + Vit x 0.5 + key def), >= 0
- **Attack speed** = (1 + Dex x 0.012) x (1 + aspd%), cap [0.25, 3]
- **Crit chance** = 0.05 + Dex x 0.005 + crit/100, cap [0, 1]; a hit/skill adds its own crit bonus on top (weapon Crit, CritC)
- **Crit multiplier** = 1.8 + critD/100, cap [1, 6]
- **Spell power** = (1 + Mag x 0.08 + L x 0.02) x (1 + spellP%) x (1 + gear Sp) x (1 + allP%), >= 0
- **Weapon hit** (before crit and the 0.9-1.1 roll), w = weapons.json entry of the weapon type:
  - melee: (5 + Str x w.str + Dex x w.dex + Mag x w.mag + item dmg + Mag x magToDmg) x w.dmg x (1 + meleeP%) x (1 + <type>P%) x (1 + allP%)
  - bow: (5 + Dex x w.dex + Str x w.str + item dmg + Str x strToBow + Mag x magToBow) x w.dmg x (1 + rangedP%) x (1 + bowP%) x (1 + allP%)
  - wand/staff: (4 + Mag x w.mag + item dmg) x w.dmg x (1 + spellP%) x (1 + gear Sp) x (1 + <type>P%) x (1 + allP%)
- **Damage dealt to a monster** (Enemy.Incoming): raw x (1 + <damage type>P%) x statuses x execute x rage, then
  Dmg.Mitigate: eff = armor (or mr) x (1 - penP%) - pen; x 100/(100+eff) (eff < 0: 2 - 100/(100-eff)); x (1 - res%, res clamped [-100, 90]).
- **Damage taken by the hero** (Player.TakeHit): physical x K/(K + defense); elemental x K/(K + defense x 0.5 + mr) x (1 - res_<type>%);
  K = 14 + 6 x attacker level (progression.json scaling.armor); res_* capped at 75% (stats.json and Player.cs). Evade chance = evade% (cap 60).
  Block: reduction = clamp(0.55 + shield def x 0.012 + block%, 0.5, 0.92).
- **Healing**: heal x MaxHp x (1 + healP%) x HealIn, HealIn = max(0, 1 + healIn%). Regen/s = regenHp + Vit x 0.02 (health), 1.5 + regenMp + Mag x 0.05 (mana).
- **Move speed** multiplier = 1 + move% (move capped [-60, 100]).

## Skills (Skills.Compute)
- rank r is clamped to [1, maxRank 5]; rank factor = 1 + rankP 15% x (r - 1)
- spell base = (dmg + lvl x L) x spell power; weapon skill base = mult x weapon hit; heals/buffs with no damage: (8 + 1.6 L) x spell power
- damage = base x rank factor x product(1 + support dmgP%) x (1 + skillP%), >= 0
- **cooldown** = cd x max(0.2, 1 - cdr% + sum support cdP% ...) (cdr capped at 50%, so 0.2 only matters with supports); tick supports
  on non-zones scale it further (>= 0.2)
- **mana** = mp x max(0, 1 + mcost% + support costP%) x (1 - weapon Mc), Mc clamped [0, 0.9]; stamina likewise without Mc
- **cast time** = cast x max(0.3, 1 + support castP%) / (weapon skills: attack speed; spells: 1 + castSpd%, castSpd >= -50%)
- area = radius x max(0.3, 1 + aoeP% + support aoeP%)

### Cast rule (F-01)
Every ROOT cast (hotbar key, macro step, auto-cast support) goes through `PayCast()`: checks cooldown, pays mana/stamina, starts
the cooldown. Echo / Resonance repeats cost nothing but RESTART the cooldown (ready one full cooldown after the last repeat).
Spell-chain sections and weapon-skill impacts are covered by the root cast's cooldown. Channels (resurrect) start it at the end.
Lingering zones (F-13, PlayerSpellZones.cs): mana/stamina are paid ONCE by the root cast. Every zone tick (the zone section's own
hit again + every later section from the zone centre) is part of that root cast: no cost, no new cooldown, logged as
`CastPath.Chain` "zone tick n/N". Zone = section with an AoE carrier ("zones.carriers": area) + a duration card in the SAME section
("zones.durationCards": time; modifier cards are per section). tick = zones.tick x tickP mods (>= minTick .25 s); dur = zones.dur 4 s
x durP mods (Time: 6 s) x (1 + rankP 5% per spell rank past 1); radius = the section's radius. Limits: maxPerCaster 3 (oldest ends),
maxTicks 20, noNested (a zone's follow-ups go off once per tick instead of making zones, unless the section holds a "nestCards" card).

## XP curve (Progression.XpNeed, data/progression.json, overridden by tuning.json files > progression.json)
need(L) = max(10, round(40 + 20 n + 6 n^2 + 0.15 n^3)), n = L - 1. Gain = max(0, round(xp x (1 + xpP%))), xpP >= -90%.
Per level: 2 attribute points, 1 skill rank point, 2 skill tree points (levelUp). Monster xp = base x (1 + 0.12 (lvl-1)) x xpMul
(tuning game.monsters), x0.5 when the monster is more than 4 levels below you; parties share 90% split evenly.

## Life and action skills (life stage, F-24/F-25)
XP from level L to L+1 = round(base x L^growth) (data/life.json defaults 40, 1.35, max 50; fishing keeps data/fishing.json "skill").
XP from play goes through `LifeSkills.Award`: x soft / (soft + XP got in the last 60 s), fractions kept. Distance skills count real
displacement per physics frame (`Player.LifeTick`, PlayerLife.cs), never keys; a frame step over track.maxStep (teleport) counts nothing.
Jumps count only when they land >= 2.2 m away or >= 0.8 m higher, once per 1.5 s. Blocks you placed give no mining XP when dug.
Keys and their readers: digP / chopP (dig and gather time / (1 + k%)), mineDropChance / chopExtraChance / gatherExtraChance (one more),
mineGemChance (a gem from pickaxe blocks), fishEase (reel zone + k%), fishLuck (whole extra levels in the fishing tables), foodP
(heal and food buff x (1 + k%)), foodDurP, foodDoubleChance, craftSaveChance (one material back), smithCost (hone gold x (1 + k%), cap
[-40, 0]), lockW (lockpick sweet spot 12 deg x (1 + k%)), lockStrainP, jumpP (jump velocity x (1 + k%), clamped 0.5-1.6), move (walking),
stRegenP (stamina regen), sprintCost (sprint drain x (1 + k%), cap [-60, 0]), st (running), swimP, breathP (breath drains / (1 + k%)),
sneakP (sneak speed 0.5 x (1 + k%), max 0.85), stealthP (notice range while sneaking 0.5 / (1 + k%), min 0.15), climbP (ladder speed),
rideP (mount speed), block (blocking). The dump prints `[life skills]` (levels, xp, keys) and `[life bonuses]` (the resolved numbers).

## Caps (data/stats.json)
Keys: cdr [-50, 50], castSpd [-50, 200], aspd [-60, 300], move [-60, 100], evade [0, 60], block [-50, 100], mcost [-75, 200],
leech / mleech [0, 30], tenacity [0, 80], healIn [-100, 300], regenHp / regenMp >= 0, pen >= 0, penP [0, 100], thorns [0, 300],
reach [-50, 200], luck >= 0, res_* [-100, 75], *Chance [0, 100], every other *P >= -90. pctFloor 0.1.
Derived: maxHp >= 30, maxMp >= 0, maxSt >= 10, defense >= 0, atkSpeed [0.25, 3], critChance [0, 1], critMult [1, 6], spellPow >= 0, wpnDmg >= 0.

## Debug
`/stats dump` (Commands.Run("stats dump")): every derived stat step by step, every non-zero key with its stage sources and caps,
the hotbar skills' resolved numbers, the XP curve. Ways to run it now: **F9** in game (log lines `[STATS]` + `user://stats_dump.txt`),
dev capture view `test_stats_dump` (`tools\capture.bat test_stats_dump`, lines `[DEVCAP] cmd>` in .devloop/capture_log.txt).
The F-42 command bar should call `Commands.Run(line)`.
