# Progression
Purpose: Character growth and content gating: races, classes, titles, skill tree stats, modifier tree, affixes, dungeon tiers, quests and wave valor.
## Files
- `scripts/Tree.cs` (133 lines): aggregated skill tree bonuses. Key: `Load()` L52, `Recalc()` L77, `CanAlloc()` L87, `CanDealloc()` L95, `StatText()` L108
- `scripts/ModTree.cs` (76 lines): skill modifier / basic attack modifier tree. Key: `Unlocked()` L58, `Unlock()` L69, `Count()` L74
- `scripts/Progression.cs` (165 lines): dungeon gear tiers, monster upgrades, XP curves. Key: `XpNeed()` L86, `DungeonLevel()` L93, `Tier()` L97, `HpCurve()` L103, `DmgCurve()` L104, `PartyShare()` L26
- `scripts/Affixes.cs` (85 lines): bonus mods on epic/legendary gear. Key: `Roll()` L36, `Reroll()` L38, `Bonus()` L83
- `scripts/Classes.cs` (40 lines): classes. Key: `Get()` L20, `Load()` L23
- `scripts/Races.cs` (49 lines): races. Key: `Get()` L18, `Look()` L42
- `scripts/Titles.cs` (55 lines): earned titles. Key: `Grant()` L23, `Wear()` L25, `Tick()` L37
- `scripts/Quests.cs` (69 lines): quest log and events. Key: `Available()` L29, `OnKill()` L39, `OnWave()` L43, `Accept()` L47, `Reward()` L48
- `scripts/Valor.cs` (53 lines): Wave Survival currency. Key: `ForWave()` L18, `ForDuel()` L29, `GearCost()` L35, `Pull()` L43
## Scenes / nodes
Pure static data/logic classes, no nodes. UIs live in [[ui]] (Skills L, Mods U, Character).
## Data
data/classes.json, data/races.json, data/modtree.json, data/progression.json, data/quests.json, data/valor.json, data/skills.json, data/loot.json.
## Links
[[ui]] [[loot]] [[skills_vfx]] [[enemies]] [[dungeons]] [[arena]] [[spellcraft]] [[core]] [[network]]
## Scores
- visuals 3/10: no screens of its own; surfaced only as text lists, no level-up fanfare or tree art.
- feel 6/10: broad systems (trees, tiers, affixes, titles); rewards feel invisible without feedback.
- code 7/10: small static data-driven classes with consistent Load()/Get().
## Ideas
- Level-up golden ring + banner in Ui via `Quests.Reward()` L48 / XP hook (`Progression.XpNeed()` L86).
- Skill tree as a constellation graph drawn in UiSkills `_Draw()`.
- Title unlock toasts in `Titles.Grant()` L23.
- Quest tracker markers on map via MapMarks.
- Respec cost curve in `Tree.Recalc()` L77.
- Affix tier colours and reroll lock in `Affixes.Reroll()` L38.
- Scale-validation tool printing HpCurve/DmgCurve per level.
