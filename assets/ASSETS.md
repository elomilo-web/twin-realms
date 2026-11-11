# Assets (CC0 only; record source and license for each)

All files below are CC0 1.0 (public domain). No attribution is required; credits are kept here anyway.

## Characters (`models/`), used by `scripts/Avatar.cs` via `data/avatar.json`
All by Quaternius (quaternius.com), CC0 1.0 (licence text kept next to the files). Free "Standard" tiers.
| Path | Pack | Notes |
|---|---|---|
| `models/char/` | Universal Base Characters [Standard] | Superhero male/female bodies (base colour + eye textures only; normal/roughness maps removed, the toon look does not use them) |
| `models/hair/` | Universal Base Characters [Standard] â€” hairstyles rigged to the head bone | |
| `models/outfit/` | Modular Character Outfits â€“ Fantasy [Standard] | Peasant + Ranger parts (body, legs, arms, feet, hood, pauldron), male/female |
| `models/weapons/` | Medieval Weapons Pack (FBX) â€” CC0 per quaternius.com/packs/medievalweapons.html | swords, axes, hammers, daggers, spear, bows, shields |
| `models/anim/clips.res` | baked from Universal Animation Library 1 (`AL_Standard.fbx`) + 2 (`UAL2_Standard.glb`), both CC0, from OpenGameArt | only the clips the game plays; the two source files stay in the folder for rebuilding (`-- --bake-anims`) and are excluded from exports |

## Environment (`models/env/`), used by `scripts/Kit.cs` via `data/env.json`
All by Quaternius, CC0 1.0 (licence text in each folder). Free "Standard" tiers. Only the models the game places are kept, with base-colour textures only (normal/roughness/ORM maps removed; metallic set to 0 where the file left it unset). Nature and props textures import at 512 px to fit the download size.
| Path | Pack | Used for |
|---|---|---|
| `models/env/nature/` | Stylized Nature MegaKit [Standard] | leafy trees, pines, rocks, flowering bushes, flowers, mushrooms |
| `models/env/village/` | Medieval Village MegaKit [Standard] | Oakhaven houses (walls, windows, doors, corners, roofs, gables, chimneys), wagon, fences |
| `models/env/props/` | Fantasy Props MegaKit [Standard] | crates, barrels, market stall, anvil, workbench, weapon stand, training dummies, benches, table, cauldron, lanterns, sacks |

Not used (licence): Bestiary â€“ Dungeon Monsters Kit is under the Quaternius Asset License (no redistribution of the raw files), so it is not in the repo. Farm Animals had no licence file and could not be verified.

| Path | Source | License |
|---|---|---|
| `Characters/GLB format/*` | Kenney mini character pack, added by hand | CC0 (Kenney) â€” not used in game, excluded from exports |

## Ground textures (`textures/terrain/`), blended by the terrain shader in `scripts/World.cs` (settings: `data/look.json` "terrain")
All from ambientCG (ambientcg.com), CC0 1.0, 1K JPG. Colour map kept for each, the OpenGL normal map for four of them; the other maps were not used.
| File | ambientCG material | Used for |
|---|---|---|
| `grass_col.jpg`, `grass_nrm.jpg` | Grass004 | open grass |
| `meadow_col.jpg`, `meadow_nrm.jpg` | Ground037 | grass with dirt patches |
| `gravel_col.jpg`, `gravel_nrm.jpg` | Gravel022 | gravel patches, roads |
| `rock_col.jpg`, `rock_nrm.jpg` | Rock030 | steep slopes, mountains |
| `sand_col.jpg` | Ground054 | shore, packed dirt on roads |
| `forest_col.jpg` | Ground048 | forest floor under trees |

## Footsteps (`audio/sfx/step_*`), played by `Sfx.Step` on each footfall, picked by the ground under the foot
| Files | Source | License |
|---|---|---|
| `step_soft_1-6` (grass, sand, snow at lower pitch) and `step_stone_1-6` | "Fantozzi's Footsteps (Grass/Sand & Stone)", Fantozzi, https://opengameart.org/content/fantozzis-footsteps-grasssand-stone | CC0 |
| `step_wood_1-3`, `step_leaves_1-2`, `step_dirt_1`, `step_gravel_1` | "Different steps on wood, stone, leaves, gravel and mud", TinyWorlds (kdd), https://opengameart.org/content/different-steps-on-wood-stone-leaves-gravel-and-mud | CC0 |

## Music (`audio/music/`), played by `Sfx.cs` with long quiet gaps between songs
| File | Title / author | Source |
|---|---|---|
| `day/jrpg_piano.mp3` | "JRPG Piano", Joth | https://opengameart.org/content/jrpg-piano |
| `day/a_new_town.mp3` | "A New Town", The Cynic Project / cynicmusic | https://opengameart.org/content/a-new-town-rpg-theme |
| `night/contemplation.mp3` | "Contemplation" | https://opengameart.org/content/contemplation-0 |
| `night/deep_blue_piano.wav` | "Yoiyami Core Theme â€“ Deep Blue Ambient Piano", Yoiyami (converted to mono 22 kHz) | https://opengameart.org/content/yoiyami-core-theme-%E2%80%93-deep-blue-ambient-piano |
| `night/budding_of_consciousness.wav` | "The Budding of Consciousness", Yoiyami (mono 22 kHz) | https://opengameart.org/content/the-budding-of-consciousness-%E2%80%93-cc0-ambient-minimalist-theme-yoiyami-blue-series-%E2%80%93-no4 |

## Ambience (`audio/ambient/`), looping layers mixed by time of day / realm / dungeon (off: data/ambient.json "sound": false; the park birds/breeze recording was removed, it had traffic in it)
| File | Source |
|---|---|
| `night_crickets.mp3` | "Crickets Ambient Noise - loopable" â€” https://opengameart.org/content/crickets-ambient-noise-loopable |
| `shadow_wind.ogg` | "wind whoosh loop" â€” https://opengameart.org/content/wind-whoosh-loop |
| `dungeon.ogg` | "Loopable Dungeon Ambience" â€” https://opengameart.org/content/loopable-dungeon-ambience |

## Sound effects (`audio/sfx/`, `<id>_<n>` = random variants of one sound id)
| Files | Source |
|---|---|
| `swing_1..4.wav` | "Swishes Sound Pack" (light swishes) â€” https://opengameart.org/content/swishes-sound-pack |
| `coin_*.wav`, `block_*.wav`, `roar_*.wav` | "RPG Sound Pack", artisticdude â€” https://opengameart.org/content/rpg-sound-pack |
| `pickup_*.ogg`, `click_*.ogg`, `ice_*.ogg` | Kenney "Interface Sounds" â€” https://opengameart.org/content/interface-sounds |
| `heal_1.wav` | "Magic Words + Healing Sound Effect" (mono 44 kHz) â€” https://opengameart.org/content/magic-words-healing-sound-effect |

Sounds with no file (hit, hurt, fire, zap, level, portal, shift, die) use soft synthesised bells in `Sfx.cs`.

## Shader
`scripts/Toon.cs` â€” toon lighting + screen-space outline, written for this project (no third-party code).

## Twin Realms kit (`models/kit/`, `textures/kit/`, `shaders/`)
Original work made for this game by `tools/kit_gen/` (code-generated, CC0). 164 models, 36 spell animation sheets (`vfx/flipbooks/`), 8 eye textures (painted over Quaternius `T_Eye_Brown.png`, CC0), water and character shaders. See `docs/kit/README.md`.
