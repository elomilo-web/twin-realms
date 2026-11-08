# Twin Realms: design

## Pitch
You wake in Oakhaven, a village on the edge of two overlapping worlds. Light is the living world. Shadow is its hungry reflection. Cross at the Whispering Stones, hunt in the arena, delve into barrows, and challenge the dragon Vorthrax.

## Camera and controls
Third-person, free camera (mouse look, orbit, over-the-shoulder). WASD move, mouse look, LMB strike (hold for power attack), RMB guard (or aim with bow, wand or staff), Space jump, C roll, Shift sprint, 1-4 spells, R cross realms, E use, H health potion, G mana potion, T whistle for the horse, V first/third person, I pack/character/skill tree, J journal, M map, Esc pause.

## Realms
| | Light | Shadow |
|---|---|---|
| Palette | warm greens, blue sky, golden light | violet and teal, dark sky, glowing flora |
| Enemies | wolves, bandits, goblins, ogres | wraiths, skeletons, hounds, revenants |
| Audio | birds, wind | low drones, whispers |

Same terrain in both. Realm shift has a cooldown and a short visual transition. Some quests and chests are only available in one realm.

## Combat
- **Sword:** combo chain, power attack on hold. Shield blocks.
- **Bow:** hold to aim, release to fire. Two-handed. Pierce and multishot from the tree.
- **Wand:** fast low-cost bolts, spends mana.
- **Staff:** slow area orb. Two-handed.
- Stamina for attacks, rolls and blocking. Mana for spells. Spells: Firebolt, Frost Nova, Healing Light, Chain Lightning.
- Numbers live in `docs/spec/reference_logic.js` and `data/`.

## Progression
XP to levels. Each level gives 2 attribute points and 2 skill points. Attributes: STR, DEX, VIT, MAG. The passive tree has a central hub, three class arms, bridge rings between arms for hybrids, and keystone nodes. Allocation rule: a node can be taken only if connected to the hub through taken nodes. A node can be removed only if nothing taken depends on it.

## World
512 m square island, 2 m heightfield cells. Oakhaven plaza and houses at the centre, the arena east, the dragon lair north-east in the mountains, dungeon entrances at the edges. Positions in `data/world.json`.

## Dungeons
Procedural tile grid (40x40, 4 m tiles, 6 m walls), rooms joined by corridors. The entry room has an exit pad. The farthest room holds the boss. Dungeons scale with depth, and killing the boss opens a descent. Themes in `data/dungeons.json`.

## Arena
Gate opens when you step in. Wave Survival: escalating waves with streak multiplier. Shadow Duel: fight a clone of your own hero with your gear and skills.

## Art direction (target)
Stylised-realistic, like Two Worlds 2 and Skyrim's softer palettes, not photoreal. PBR materials, volumetric fog, soft shadows, day/night cycle, strong silhouette readability. Buildings: timber frame, plaster, shingle roofs, stone bases. Dungeons: carved stone, braziers, rubble, banners.

## Not in scope (yet)
Multiplayer, voice acting, full-body IK, large cities.
