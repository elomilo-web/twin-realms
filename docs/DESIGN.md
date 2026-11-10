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
Changed 2026-11-09: an anime look in the spirit of Genshin Impact. Every mesh gets toon lighting (flat light/shadow bands), a soft rim light and screen-space outlines (`scripts/Toon.cs`, `data/look.json` "toon"), crisp shadows and a bright, saturated grade, so assets from different packs read as one project. Assets are CC0 only, preferring Quaternius for cohesion; anything without an asset stays procedural until one exists.
Characters: a plain base body (male or female, chosen at creation, skin coloured by race) and every piece of gear as a separate asset on the same skeleton: armor, gloves, boots, hood/helmet, weapons, shields and back pieces (cape). Trinkets, amulets and rings are not drawn. New heroes start in simple clothes.
Gear glow: few models, many looks. Items can carry an element (fire, frost, storm, venom, holy, shadow) and glow in its colour, or in their rarity colour; the glow grows from none (common) to a breathing edge glow with light and sparks (legendary). Elemental weapons can inflict their element's status.
Buildings: timber frame, plaster or uneven brick, round clay-tile roofs, stone plinths, built from 2 m kit modules (Quaternius Medieval Village); windows glow at night. Nature: leafy trees in the lowlands, pines on high ground, rocks, flowering bushes and flowers (Quaternius Stylized Nature). Props: crates, barrels, market stall, forge tools, training dummies (Quaternius Fantasy Props). Dungeons: carved stone, braziers, rubble, banners (still procedural).

## Audio
Calm and peaceful: soft piano/harp music (Minecraft-like, with long quiet gaps), nature ambience (birds, crickets, breeze, wind in the Shadow realm), gentle sound effects. Effects without a recording use soft synthesised bells in one key. Sources in `assets/ASSETS.md`.

## Not in scope (yet)
Multiplayer, voice acting, full-body IK, large cities.
