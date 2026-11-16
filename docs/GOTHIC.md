# Gothic realism pass (branch `gothic-realism`)

Goal: move every asset from the cartoon rough draft to one finished look: dark gothic realism in the spirit of Elden Ring.
Rules: CC0 only (record in `assets/ASSETS.md`); one shared material library; build in Blender by code where no fitting CC0 asset exists.

## How it works
- `data/look.json`: toon and Genshin shaders off, muted grade, haze, gothic sky, water and terrain tints.
- `assets/materials/gothic/G_*.tres` + `assets/textures/gothic/`: scanned CC0 ambientCG sets, triplanar (no UVs needed).
  Weapons/armor use object space; scenery uses world space so walls run on across modules.
- `scripts/GothicMats.cs`: a model material named `G_<x>` gets the shared material (Weapons.BuildKit, Avatar, Kit).
- `data/env.json` "materials": `"gothic": "G_Stone"` maps any Quaternius material to a gothic one (+ optional `tint`).
- `scripts/Gfx.cs`: the procedural surfaces (plaster, stone, planks, grain, shingle, bark, rock, cobble, cloth) use the scanned sets.
- `tools/blender_gothic/`: `gothic.py` (library), `weapons.py`, `armor.py`, `build.py` (run in Blender; see its header).

## Status
| Area | State |
|---|---|
| Lighting / grade / sky / water / terrain tint | done (tune in look.json) |
| Kit weapons (51) + long katana | done, rebuilt in Blender |
| Kit armor (10) | done, rebuilt in Blender |
| Hero shields | done (kit shields, avatar.json kitShields) |
| Oakhaven houses, props, rocks, tree bark | done via material swap (env.json) |
| Procedural buildings, dungeons, lands, plaza | done via scanned textures (Gfx.cs) |
| Trees / leaves / bushes / flowers | done: tools/blender_gothic/nature.py (oaks, firs, dead tree, bushes, shrubs; CC0 ambientCG leaf atlases via foliage_tex.ps1) |
| Hero bodies | male done: TR_Male_Body.glb from tools/blender_gothic/humans.py (MPFB, CC0 base mesh, on the game skeleton; baked skin x race colour, SSS). female done (TR_Female_Body.glb); hair done: 7 strand-card styles (assets/models/hair/TR_Hair_*.glb, build_hair); outfits done: 9 garments per body lifted off the skin (tunic, robe, gambeson, cuirass+belt, pauldrons, trousers, boots, gloves, hood), skinned like the body, body-UV mapped |
| Scarletta (golden armour, deep red hair, one angel wing, giant katana) | done: realistic female body, flowing deep-red hair + strand-card locks, polished gold cuirass/pauldrons/gauntlets/greaves over a crimson gown, feathered angel wing (wing_angel.glb), katana_long with a rot-green edge. Showcase shots 05/06 |
| Monsters (25 Quaternius) | todo |
| Kit castle / dungeon / spell models | todo (castle/dungeon kit not placed in game yet) |
| Spell flipbooks, particles | todo |
| Sound effects, ambience, music | todo |
| UI | todo |
