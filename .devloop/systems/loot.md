# Loot
Purpose: Items, rarity, affixes-on-drop, ground pickups, storage chests, smith upgrades, pack extras and crafting from disassembly scraps; weapon hand poses and model kits.
## Files
- `scripts/Items.cs` (131 lines): Item class, rarity, rolling. Key: `Describe()` L34, `RarityColor()` L55, `Load()` L58, `Roll()` L73, `BaseName()` L124
- `scripts/ItemSlot.cs` (131 lines): inventory cell with rarity frame and hover. Key: `_Draw()` L28, `_GuiInput()` L23, `Show()` L88
- `scripts/Loot.cs` (101 lines): world pickups (gold, potions, items). Key: `Drop()` L15, `DropFromPack()` L23, `Wanted()` L33, `DropFrom()` L39, `_PhysicsProcess()` L63
- `scripts/LootTables.cs` (181 lines): tables, uniques, dungeon sets. Key: `Load()` L33, `RollRarity()` L72, `DepthBonus()` L80, `Roll()` L90, `MakeUnique()` L104, `SetForDungeon()` L112
- `scripts/Home.cs` (71 lines): Oakhaven storage chest. Key: `Interact()` L34, `OpenStash()` L45
- `scripts/UiCraft.cs` (176 lines): pickup filter, crafting. Key: `OpenPickFilter()` L14, `CanPay()` L48, `OpenCraft()` L52
- `scripts/UiSmith.cs` (167 lines): Dorrin smith, reroll/hone gear. Key: `RerollCost()` L129, `Hone()` L132
- `scripts/Weapons.cs` (300 lines): hand poses, weapon kit models. Key: `Get()` L45, `KitModels()` L78, `KitModelFor()` L95, `ScaleOf()` L111, `Load()` L118
## Scenes / nodes
Pickups are Area/node instances added to Main at drop point; chests (Party Chest, Your Home) built in code in Oakhaven; windows under Main/Ui.
## Data
data/items.json, data/loot.json (tables, uniques, "crafting"), data/weapons.json, data/progression.json (tier scaling).
## Links
[[ui]] [[progression]] [[player]] [[enemies]] [[dungeons]] [[combat]] [[core]] [[network]]
## Scores
- visuals 4/10: pickups are plain; inventory slots are empty navy squares, item icons generic vector glyphs, chests are blocky; no rarity glow or ground beams.
- feel 6/10: pickup filter and disassemble are good QoL; no loot drop fanfare, sort exists.
- code 6/10: data-driven tables; Weapons.cs mixes poses and model lookup.
## Ideas
- Rarity light beam + sound on drops in `Loot.Drop()` L15 / `_Ready()` L49.
- Item comparison tooltip with delta arrows in `ItemSlot.Show()` L88.
- Engraved item icons with rarity-tinted backdrop in `ItemSlot._Draw()` L28.
- Auto-pickup magnet + gold fly-to-HUD in `Loot._PhysicsProcess()` L63.
- Weighted loot pity timer for legendaries in `LootTables.RollRarity()` L72.
- Item set bonus display in `Items.Describe()` L34.
- Split Weapons.cs poses from kit model lookup.
- Drag-and-drop between pack and equip slots in `ItemSlot._GuiInput()` L23.
