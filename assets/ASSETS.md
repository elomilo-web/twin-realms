# Assets (CC0 only; record source and license for each)

All files below are CC0 1.0 (public domain). No attribution is required; credits are kept here anyway.

## Characters (`models/`), used by `scripts/Avatar.cs` via `data/avatar.json`
All by Quaternius (quaternius.com), CC0 1.0 (licence text kept next to the files). Free "Standard" tiers.
| Path | Pack | Notes |
|---|---|---|
| `models/char/` | Universal Base Characters [Standard] | Superhero male/female bodies (base colour + eye textures only; normal/roughness maps removed, the toon look does not use them) |
| `models/hair/` | Universal Base Characters [Standard] — hairstyles rigged to the head bone | |
| `models/outfit/` | Modular Character Outfits – Fantasy [Standard] | Peasant + Ranger parts (body, legs, arms, feet, hood, pauldron), male/female |
| `models/weapons/` | Medieval Weapons Pack (FBX) — CC0 per quaternius.com/packs/medievalweapons.html | swords, axes, hammers, daggers, spear, bows, shields |
| `models/anim/clips.res` | baked from Universal Animation Library 1 (`AL_Standard.fbx`) + 2 (`UAL2_Standard.glb`), both CC0, from OpenGameArt | only the clips the game plays; the two source files stay in the folder for rebuilding (`-- --bake-anims`) and are excluded from exports |

Not used (licence): Bestiary – Dungeon Monsters Kit is under the Quaternius Asset License (no redistribution of the raw files), so it is not in the repo. Farm Animals had no licence file and could not be verified.

| Path | Source | License |
|---|---|---|
| `Characters/GLB format/*` | Kenney mini character pack, added by hand | CC0 (Kenney) — not used in game, excluded from exports |

## Music (`audio/music/`), played by `Sfx.cs` with long quiet gaps between songs
| File | Title / author | Source |
|---|---|---|
| `day/jrpg_piano.mp3` | "JRPG Piano", Joth | https://opengameart.org/content/jrpg-piano |
| `day/a_new_town.mp3` | "A New Town", The Cynic Project / cynicmusic | https://opengameart.org/content/a-new-town-rpg-theme |
| `day/champ_de_tournesol.mp3` | "Champ de tournesol", Komiku | https://opengameart.org/content/champ-de-tournesol |
| `night/contemplation.mp3` | "Contemplation" | https://opengameart.org/content/contemplation-0 |
| `night/deep_blue_piano.wav` | "Yoiyami Core Theme – Deep Blue Ambient Piano", Yoiyami (converted to mono 32 kHz) | https://opengameart.org/content/yoiyami-core-theme-%E2%80%93-deep-blue-ambient-piano |
| `night/budding_of_consciousness.wav` | "The Budding of Consciousness", Yoiyami (mono 32 kHz) | https://opengameart.org/content/the-budding-of-consciousness-%E2%80%93-cc0-ambient-minimalist-theme-yoiyami-blue-series-%E2%80%93-no4 |

## Ambience (`audio/ambient/`), looping layers mixed by time of day / realm / dungeon
| File | Source |
|---|---|
| `day_birds.wav`, `breeze.wav` | "Park ambiences" (75 s / 50 s excerpts, mono 22 kHz, loop-crossfaded) — https://opengameart.org/content/park-ambiences |
| `night_crickets.mp3` | "Crickets Ambient Noise - loopable" — https://opengameart.org/content/crickets-ambient-noise-loopable |
| `shadow_wind.ogg` | "wind whoosh loop" — https://opengameart.org/content/wind-whoosh-loop |
| `dungeon.ogg` | "Loopable Dungeon Ambience" — https://opengameart.org/content/loopable-dungeon-ambience |

## Sound effects (`audio/sfx/`, `<id>_<n>` = random variants of one sound id)
| Files | Source |
|---|---|
| `swing_1..4.wav` | "Swishes Sound Pack" (light swishes) — https://opengameart.org/content/swishes-sound-pack |
| `coin_*.wav`, `block_*.wav`, `roar_*.wav` | "RPG Sound Pack", artisticdude — https://opengameart.org/content/rpg-sound-pack |
| `pickup_*.ogg`, `click_*.ogg`, `ice_*.ogg` | Kenney "Interface Sounds" — https://opengameart.org/content/interface-sounds |
| `heal_1.wav` | "Magic Words + Healing Sound Effect" (mono 44 kHz) — https://opengameart.org/content/magic-words-healing-sound-effect |

Sounds with no file (hit, hurt, fire, zap, level, portal, shift, die) use soft synthesised bells in `Sfx.cs`.

## Shader
`scripts/Toon.cs` — toon lighting + screen-space outline, written for this project (no third-party code).
