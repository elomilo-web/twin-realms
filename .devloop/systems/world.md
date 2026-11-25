# World
Purpose: Overworld terrain, sky/day-night, Light/Shadow realm shift, hand-built Oakhaven, flora, gathering, far lands, shared gothic/toon materials.
## Files
- `scripts/World.cs` (949 lines): heightfield terrain, sky, day/night, realm shift, shared mats. Key: `GetPoi()` L26, `Mat()` L53, `Fbm()` L96, `RoadDist()` L102, `GroundAt()` L124, `OnGround()` L132, `_Ready()` L153
- `scripts/Builder.cs` (471): hand-placed Oakhaven, arena, dungeon mouths, lair, stones. Key: `BuildAll()` L18, `Box()` L37, `Solid()` L42, `SetGate()` L262
- `scripts/Lands.cs` (922): data-driven heightfield islands. Key: `Load()` L45, `WaterAt()` L193, `GroundAt()` L224, `CoverAt()` L249, `RegionAt()` L275, `Build()` L299
- `scripts/Flora.cs` (325): wind grass/ferns/bushes per chunk. Key: `Build()` L190, `ApplyQuality()` L220, `BuildChunk()` L233, `_Process()` L294
- `scripts/Gather.cs` (177): gather nodes + scatter. Key: `Load()` L16, `Spawn()` L42, `Interact()` L165
- `scripts/Gfx.cs` (357): procedural tileable textures/meshes. Key: `Tex()` L181, `GableRoof()` L208, `Boulder()` L323, `Pine()` L350
- `scripts/GothicMats.cs` (28): G_* material lookup. Key: `For()` L17
- `scripts/Toon.cs` (114) / `Genshin.cs` (147): legacy anime look. Key: `Toon.Apply()` L69, `Genshin.From()` L83, `SetNight()` L135
- `scripts/Ambient.cs` (115): butterflies/fireflies. Key: `_Ready()` L19, `_Process()` L80
- `scripts/Props.cs` (199): spinners, interactable props. `scripts/Interact.cs` (12): E-use base.
- `assets/shaders/water_stylized.gdshader` (81), `genshin_toon.gdshader` (149), `genshin_outline.gdshader` (48)
## Scenes / nodes
Built in code: Main/World (terrain chunks, sky, lights), Oakhaven under World, Flora chunk MultiMeshes.
## Data
data/look.json, lands.json, flora.json, gather.json, ambient.json, env.json, quests.json
## Links
[[blockworld]] [[dungeons]] [[arena]] [[enemies]] [[loot]] [[core]] [[audio]]
## Scores
- visuals 4/10: vista is a flat beige-sepia fog wash, no depth, no light shafts or silhouette contrast; floating island and sky beams look placeholder; far from Elden Ring.
- feel 5/10: readable layout and roads, but empty-feeling terrain and uniform haze.
- code 6/10: World.cs 949 and Lands.cs 922 lines are monoliths; legacy Toon/Genshin kept beside Gothic.
## Ideas
- Layered height fog + colored distant haze gradient in `World._Ready()` env (look.json fog_*) to give depth.
- Add god-ray/volumetric sun shaft and warm/cool grade split in World sky setup.
- Split `Lands.Build()` into terrain/cover/water files to shrink monolith.
- Retire Toon/Genshin code paths when look.json disables them.
- Darker, desaturated terrain tint + moss/dirt triplanar blend in the terrain material (`World.Mat()`).
- Add ruin silhouettes and dead trees on horizon in `Builder.BuildAll()` for landmarks.
- Water: add foam/shore depth fade to `water_stylized.gdshader`.
- Flora: density falloff + wind variation in `Flora.BuildChunk()`.
