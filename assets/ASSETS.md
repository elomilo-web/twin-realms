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

## Hair, ears and face skin (A-34, added 2026-11-27): MakeHuman CC0 assets
The realistic bodies (`models/char/TR_*_Body.glb`) are the MakeHuman base mesh, so MakeHuman's own assets fit them exactly.
Fitted and converted by `tools/blender_gothic/mh_head_assets.py` (headless Blender; MakeHuman's proxy fit on the game bodies;
hair textures made light grey so the hair colour tints them, skin neutralised to the old map's tone). Licence text (CC0 1.0 legal code
plus the source list) in `models/hair/mh/LICENSE_MakeHuman_CC0.txt` and `models/char/LICENSE_MakeHuman_CC0.txt`.
Rejected for licence: every hair01/bodyparts asset whose own file names AGPL3 / CC-BY (o4saken_long01 "CC BY 4.0", learning_anime_hair,
elvs_* braids, rehmanpolanski bun, culturalibre_hair_01/02), and the CC-BY packs (hair02/03, animal02-04, bodyparts02/03/06).
| Files | Source asset (author) | Page | Licence |
|---|---|---|---|
| `models/hair/mh/TR_MH_{short01,short02,short03,short04,long01,ponytail01,braid01,twinbraids,bob01,bob02,afro01}_*.glb` + `TR_MH_*_diffuse.png`, `TR_MH_afro_diffuse.png` | MakeHuman system hair short01-04, long01, ponytail01, braid01 (twinbraids = braid01 swung out and mirrored), bob01, bob02, afro01 (MakeHuman team: Data Collection AB, Joel Palmius, Jonas Hauquier) | https://static.makehumancommunity.org/assets/assetpacks/makehuman_system_assets.html (zip: https://files2.makehumancommunity.org/asset_packs/makehuman_system_assets/makehuman_system_assets_cc0.zip) | CC0 ("explicitly released as CC0 in September 2020", in each file) |
| `models/hair/mh/TR_MH_shaggy_*.glb`, `TR_MH_straightbangs_Female.glb`, `TR_MH_shaggy_green_diff.png`, `TR_MH_straight_bangs_diff.png` | cortu_shaggy_green_hair, cortu_straight_bangs (Cortu) | https://static.makehumancommunity.org/assets/assetpacks/hair01.html (zip: .../asset_packs/hair01/hair01_cc0.zip) | CC0 ("Cortu Johnstone - CC0" in the file) |
| `models/hair/mh/TR_MH_{bluntbob,curledbob,invertedbob}_Female.glb`, `TR_MH_AuburnHair.png`, `TR_MH_BlondHair.png`, `TR_MH_GoldenBlondHair.png` | toigo_blunt_bob_with_bangs, toigo_curled_under_bob_with_bangs, toigo_inverted_bob (MargaretToigo; subdivided once) | same hair01 pack | CC0 ("license CC0" in the file) |
| `../data/ear_morphs.json` | MakeHuman ear targets (pointed, scale-vert, rot-backward, trans-up, scale, wing incr/decr) from the MPFB 2 data folder; titleknown_catgirl_ears (titleknown); elvs_flap_ears_1 (Elvaerwyn) | https://static.makehumancommunity.org/mpfb.html ; https://static.makehumancommunity.org/assets/assetpacks/animal01.html ; .../ears01.html | CC0 |
| `models/char/TR_Male_skin_mh.png`, `TR_Female_skin_mh.png` | MakeHuman skins young_caucasian_male / young_caucasian_female (neutralised), with eyebrow001 / eyebrow010 and eyelashes01 / eyelashes02 painted in | makehuman_system_assets (above) | CC0 |

## Environment (`models/env/`), used by `scripts/Kit.cs` via `data/env.json`
All by Quaternius, CC0 1.0 (licence text in each folder). Free "Standard" tiers. Only the models the game places are kept, with base-colour textures only (normal/roughness/ORM maps removed; metallic set to 0 where the file left it unset). Nature and props textures import at 512 px to fit the download size.
| Path | Pack | Used for |
|---|---|---|
| `models/env/nature/` | Stylized Nature MegaKit [Standard] | leafy trees, pines, rocks, flowering bushes, flowers, mushrooms |
| `models/env/village/` | Medieval Village MegaKit [Standard] | Oakhaven houses (walls, windows, doors, corners, roofs, gables, chimneys), wagon, fences |
| `models/env/props/` | Fantasy Props MegaKit [Standard] | crates, barrels, market stall, anvil, workbench, weapon stand, training dummies, benches, table, cauldron, lanterns, sacks |

## Graveyard props (`models/kit/graveyard/`, added 2026-11-27, A-36), used by `scripts/Breakables.cs` and `scripts/WorldSites.cs`
"Graveyard Kit" 5.0 by Kenney (www.kenney.nl), https://kenney.nl/assets/graveyard-kit, CC0 1.0 (licence text kept next to the files: `models/kit/graveyard/License.txt`). Preview image checked before choosing. Only these 18 GLB models and their shared `Textures/colormap.png` are kept: urn-round, urn-square (breakable pots and burial urns), grave, grave-border, gravestone-cross / -round / -bevel / -decorative / -roof / -wide / -broken, cross-column, altar-stone, pillar-obelisk, column-large, debris, coffin-old, candle-multiple (graveyards, chapels, monuments). Their one material `colormap` is swapped for the gothic stone (`data/env.json` "graveyard/colormap"). Until Godot has imported them (open the editor once or run an import) the game draws simple stand-in shapes.
The crates, barrels, apple barrel, produce crate and sacks that break are the Quaternius Fantasy Props MegaKit models already listed above (`models/env/props/`); the chests are the Twin Realms kit chests (`models/kit/dungeon/chest_*.glb`).

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
| `day/lament_warriors_soul.mp3` | "Fantasy: Lament for a Warrior's Soul", RandomMind (CC0, added 2026-11-15) | https://opengameart.org/content/fantasy-lament-for-a-warriors-soul |
| `day/dark_forest.mp3` | "Dark Forest Theme", cynicmusic (CC0, added 2026-11-15) | https://opengameart.org/content/dark-forest-theme |
| `night/vampires_piano.mp3` | "Vampire's Piano", TAD (CC0, added 2026-11-15) | https://opengameart.org/content/vampires-piano |
| `night/forgotten_tombs.mp3` | "Forgoten Tomb Ambience", kindland (CC0, added 2026-11-15) | https://opengameart.org/content/forgoten-tomb-ambience |
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
Gothic realism (2026-11-15): replaced with realistic CC0 recordings, all from OpenGameArt:
| Files | Source |
|---|---|
| `swing_1..6.ogg` | "20 Sword Sound Effects (Attacks and Clashes)", StarNinjas: sword attacks — https://opengameart.org/content/20-sword-sound-effects-attacks-and-clashes |
| `block_1..4.ogg` | same pack: sword clashes |
| `hit_1..3.ogg` | "75 CC0 breaking / falling / hit sfx", rubberduck — https://opengameart.org/content/75-cc0-breaking-falling-hit-sfx |
| `hit_4..5.ogg`, `fire_1..4.ogg`, `zap_1..2.ogg`, `coin_1..4.ogg`, `pickup_1..3.ogg`, `roar_1..3.ogg`, `die_1.ogg` | "80 CC0 RPG SFX", rubberduck — https://opengameart.org/content/80-cc0-rpg-sfx |
| `roar_4..6.ogg` | "80 CC0 creature SFX", rubberduck — https://opengameart.org/content/80-cc0-creature-sfx |
| `hurt_1..3.ogg` | "100 CC0 SFX #2", rubberduck — https://opengameart.org/content/100-cc0-sfx-2 |
| `ice_1..2.wav` | "Ice spells" — https://opengameart.org/content/ice-spells |
| `heal_1..3.wav` | "Cure magic" — https://opengameart.org/content/cure-magic |
| `click_*.ogg` | Kenney "Interface Sounds" — https://opengameart.org/content/interface-sounds |
| `land_thud_1..3.wav`, `splash_1..4.wav`, `step_water_1..4.wav`, `ui_hover_1..3`, `ui_click_1..2`, `ui_open`, `ui_close`, `ui_confirm`, `ui_error`, `rain_loop`, `rain_light_loop`, `wind_loop` (20 s seamless loops), `thunder_1..2` (.wav) | procedurally generated, tools/kit_gen/sfx_synth.py |

Sounds with no file (level, portal, shift) use soft synthesised bells in `Sfx.cs`.

## Particle textures (`vfx/particles/`, added 2026-11-12)
Kenney "Particle Pack" 1.1 by Kenney Vleugels (www.kenney.nl), CC0 (license in `vfx/particles/LICENSE_Kenney.txt`). 43 of its 80 sprites, scaled to 256x256: flames, smoke, sparks, stars, magic wisps, twirls, light, scorch, dirt, circles, slashes. Taken from the Godot packaging at https://github.com/Calinou/kenney-particle-pack (original page: https://kenney.nl/assets/particle-pack). Used by `scripts/Vfx.cs` (`Vfx.Sets`).
Gothic realism (2026-11-15): smoke, fire, flame, spark, dirt, star, light and scorch sprites are now original, made by `tools/kit_gen/particles_gen.py` (CC0); the Kenney magic, twirl, circle, slash and trace sprites stay.

## Shader
`scripts/Toon.cs` â€” toon lighting + screen-space outline, written for this project (no third-party code).

## Twin Realms kit (`models/kit/`, `textures/kit/`, `shaders/`)
Original work made for this game by `tools/kit_gen/` (code-generated, CC0). 164 models, 36 spell animation sheets (`vfx/flipbooks/`), 8 eye textures (painted over Quaternius `T_Eye_Brown.png`, CC0), water and character shaders. See `docs/kit/README.md`.

`models/kit/weapons/katana_long.glb` (added 2026-11-15): original long nodachi modelled in Blender for this game (not from `kit_gen`). 2.87 m (2.3 m blade), grip centre at the origin, blade up +Y, 7 materials (`TR_Metal_*` take the element tint). Shown for "Odachi" and "Nodachi" (scale 0.85) via `data/weapons.json` kitModels.

## Monsters (assets/models/monsters/, added 2026-11-11)

Gothic realism (2026-11-15): 18 of these files are now original models made for this game by `tools/blender_gothic/creatures.py` (rigged, animated, CC0): giantRat, giantSpider, slime, toxicBlob, spikeSlime, viper, earthGolem, goleling, giantWasp, giantCrab, raptor, wyvern, kitsune, myconid, mushroomKing, cactusFiend (now a thorn fiend), flameSkull, ghost (a wraith). The humanoid ones (orcWarrior, hobgoblin, ninja, darkMage, oni, yeti, lesserDemon) are drawn with the realistic hero bodies instead; their Quaternius files stay in the folder, unused. The rest of this section describes the original Quaternius files (in git history for the replaced ones).

Animated low-poly monsters by **Quaternius** (Ultimate Monsters, Animated Enemies, Animated Animal packs), **CC0 / Public Domain**, downloaded from poly.pizza. Used as data/enemies.json "model". Sources (ssets/models/monsters/_sources.tsv):

| File | Original model | Page |
|---|---|---|
| `slime.glb` | Pink Slime | https://poly.pizza/m/AyP8sQmDLh |
| `toxicBlob.glb` | Green Blob | https://poly.pizza/m/y4kJh8EeYS |
| `spikeSlime.glb` | Green Spiky Blob | https://poly.pizza/m/IoWG5F9WUc |
| `giantRat.glb` | Rat | https://poly.pizza/m/iltq5bVNaV |
| `giantSpider.glb` | Spider | https://poly.pizza/m/yRYJiAJyiM |
| `viper.glb` | Snake | https://poly.pizza/m/x9x0viZs8V |
| `giantWasp.glb` | Wasp | https://poly.pizza/m/3aQgc75sUR |
| `giantCrab.glb` | Crab Enemy | https://poly.pizza/m/Gs3yfsV5lB |
| `flameSkull.glb` | Skull Enemy | https://poly.pizza/m/EHHwZ17lWD |
| `orcWarrior.glb` | Orc | https://poly.pizza/m/5vO2YJsPEf |
| `hobgoblin.glb` | Orc Enemy | https://poly.pizza/m/Q3z8ZX4kUy |
| `ninja.glb` | Ninja | https://poly.pizza/m/mCNoqcqpvC |
| `darkMage.glb` | Wizard | https://poly.pizza/m/o87Upt5uHX |
| `yeti.glb` | Yeti | https://poly.pizza/m/S1E7idPFhe |
| `lesserDemon.glb` | Demon | https://poly.pizza/m/Mo2ky6vkf8 |
| `oni.glb` | Blue Demon | https://poly.pizza/m/S7jYW6Amye |
| `ghost.glb` | Ghost | https://poly.pizza/m/Iip30bDHmu |
| `myconid.glb` | Mushnub | https://poly.pizza/m/LWKmS30Xxl |
| `mushroomKing.glb` | Mushroom King | https://poly.pizza/m/grnFTziU8u |
| `goleling.glb` | Goleling | https://poly.pizza/m/71gomWolax |
| `earthGolem.glb` | Goleling Evolved | https://poly.pizza/m/iHEuXiH6Aj |
| `cactusFiend.glb` | Cactoro | https://poly.pizza/m/66CYX5UhRK |
| `wyvern.glb` | Dragon | https://poly.pizza/m/3rUm1cN3yp |
| `raptor.glb` | Dino | https://poly.pizza/m/wuerCFCWNR |
| `dragon.glb` | Dragon (clips Dragon_Flying, Dragon_Attack, Dragon_Attack2, Dragon_Hit, Dragon_Death), the Vorthrax boss since 2026-11-27 | https://poly.pizza/m/VBvzjFIYws |
| `deer.glb` | Stag (Ultimate Animated Animal Pack, 13 clips incl. Attack_Headbutt, Attack_Kick, Gallop, Walk, Idle_HitReact_Left/Right, Death), deer and shade stag since 2026-11-27 | https://poly.pizza/m/tQdzbZ1Cmw |
| `wolf.glb` | Wolf (Ultimate Animated Animal Pack, 12 clips: Idle, Idle_2, Walk, Gallop, Gallop_Jump, Attack, Idle_HitReact_Left/Right, Death, Eating, Jump_ToIdle), replaced the older wolf on 2026-11-27 | https://poly.pizza/m/P1gU3Qkr9r |
| `kitsune.glb` | Fox | https://poly.pizza/m/Bc97C66HKi |


## Painted textures (`textures/terrain_painted/`, `textures/painted/`)
Original work made for this game by `tools/kit_gen/tex_gen.py` (code-generated, CC0). 20 seamless textures. See `docs/kit/README.md`.

## Icons and menu pieces (`ui/`)
Original work made for this game by `tools/kit_gen/ui_gen.py` (code-generated, CC0). 69 skill icons, 48 item icons, 22 frame pieces. See `docs/kit/README.md`.

- assets/audio/music/gen/*.wav (7 songs: town_hearthside, town_market_morning, explore_open_road, explore_far_hills, night_starlit, night_lantern_watch, home_by_the_fire): original, synthesised by tools/music_gen.ps1 (focus F-39). No third-party material.

## A-35 weapons (`models/kit/weapons/ph_*`, `odd_*` and the grand set), added 2026-11-27
All CC0 1.0. Licence notes and credits next to the files: `models/kit/weapons/LICENSE_A35_CC0.txt`. Downloaded files were re-oriented
(grip at the origin, head up +Y, striking side +X; shields centred, front +Z), scaled and re-exported by `tools/blender_gothic/import_props.py`
(Poly Haven textures scaled to 512 px JPEG; the Poly Pizza models re-skinned with the shared gothic G_* materials and baked AO).
Used through `data/weapons.json` kitModels.byName, `data/avatar.json` kitShields and `data/weapon_finds.json`.
| File | Source (licence) | Used as |
|---|---|---|
| `ph_estoc_cathedral.glb` | "Antique Estoc", Poly Haven, https://polyhaven.com/a/antique_estoc (CC0) | sword "Cathedral Estoc" |
| `ph_katana_mist.glb` | "Antique Katana 01", Poly Haven, https://polyhaven.com/a/antique_katana_01 (CC0) | katana "Ronin Blade", "Mist-Edge Uchigatana" |
| `ph_mace_bishop.glb` | "Ornate Medieval Mace", Poly Haven, https://polyhaven.com/a/ornate_medieval_mace (CC0) | mace "Bishop's Mace" |
| `ph_hammer_bell.glb` | "Ornate War Hammer", Poly Haven (Ulan Cabanilla, James Ray Cock), https://polyhaven.com/a/ornate_war_hammer (CC0) | mace "Bell Hammer", warhammer "Bellkeeper's Maul" (x1.75), colossal hammer "Bellkeeper's Colossus" (x2.3) |
| `ph_axe_woodsman.glb` | "Wooden Axe 02", Poly Haven, https://polyhaven.com/a/wooden_axe_02 (CC0) | axe "Woodsman's Axe", greataxe "Felling Axe" (x1.7), colossal axe "Woodcutter's Colossus" (x2.1) |
| `ph_greatshield_kite.glb` | "Kite Shield", Poly Haven, https://polyhaven.com/a/kite_shield (CC0) | shields named "...Greatshield" (drawn 1.15 m) |
| `odd_pan.glb` | "Brass Pan 01", Poly Haven, https://polyhaven.com/a/brass_pan_01 (CC0) | pan (frying pan) |
| `odd_spade.glb` | "Rusted Spade 01", Poly Haven, https://polyhaven.com/a/rusted_spade_01 (CC0) | spade |
| `odd_broom.glb` | "Wooden Broom", Poly Haven, https://polyhaven.com/a/wooden_broom (CC0) | broom |
| `odd_door.glb`, `odd_door_shield.glb` | "Large Castle Door" (left leaf), Poly Haven, https://polyhaven.com/a/large_castle_door (CC0) | door (weapon), shield "Cellar Door" (drawn 1.3 m) |
| `odd_lamppost.glb` | "Street Lamp 01", Poly Haven, https://polyhaven.com/a/street_lamp_01 (CC0) | lamppost |
| `odd_chair.glb` | "Wooden Chair 01", Poly Haven, https://polyhaven.com/a/WoodenChair_01 (CC0) | chair |
| `odd_cittern.glb` | "Ukulele 01", Poly Haven, https://polyhaven.com/a/Ukulele_01 (CC0) | cittern (lute) |
| `odd_anchor.glb` | "Anchor" by Nikdane12, Poly Pizza, https://poly.pizza/m/SxdMzDMKpS (CC0) | anchor |
| `odd_fish.glb` | "Fish" by Quaternius, Poly Pizza, https://poly.pizza/m/XWl86YFtpF (CC0) | fish |
| `odd_pitchfork.glb` | "Pitchfork" by CreativeTrio, Poly Pizza, https://poly.pizza/m/dtLT2Nj0KF (CC0) | pitchfork |
| `odd_torch.glb` | "Wooden Torch" by Quaternius, Poly Pizza, https://poly.pizza/m/pNsfJzhXiD (CC0) | torch |
| `colossal_sword_moon.glb`, `colossal_slab_ember.glb`, `dune_reaver_sun.glb`, `giant_axe_storm.glb`, `starfall_maul_void.glb`, `seal_hallowed.glb`, `siege_ladder.glb` | original work for this game, `tools/blender_gothic/weapons_grand.py` (CC0) | colossal sword / axe / hammer, curved greatsword, sacred seal, ladder |
