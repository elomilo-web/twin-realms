# Blockworld
Purpose: Minecraft-style voxel ground around the player (overworld and dungeons): generation, meshing, fluids/fire sim, build/dig, multiplayer sync.
## Files
- `scripts/BlockWorld.cs` (255): chunk streaming node. Key: `ReadyAround()` L29, `UseOverworld()` L79, `UseDungeon()` L80, `UseNone()` L96, `Store()` L108, `ReloadOverworld()` L122
- `scripts/BlockArea.cs` (170): 16x16 columns, ids + meta. Key: `Get()` L66, `GetMeta()` L72, `ChunkAt()` L62
- `scripts/Blocks.cs` (175): block defs from json. Key: `Load()` L35, `Def()` L67, `DropItem()` L89, `Tint()` L101, `DigTime()` L110
- `scripts/BlockGen.cs` (157): `OverworldGen` L23 (from heightfield), `DungeonGen` L95, `Fill()` L55
- `scripts/BlockMesher.cs` (114): per-material surfaces + collision faces. Key: `Build()` L40
- `scripts/BlockSim.cs` (194): water/lava flow, fire, freeze. Key: `Change()` L25, `Ignite()` L108, `Freeze()` L119
- `scripts/BlockFx.cs` (146): skill hits alter blocks. Key: `Hit()` L31, `Dig()` L111, `CanPlace()` L125, `Place()` L139
- `scripts/PlayerBlocks.cs` (185): build mode (N), crosshair, hold-dig. Key: `CycleBlock()` L85
- `scripts/NetBlocks.cs` (99): host-authoritative edits. Key: `SendBlockSync()` L44, `ApplyRemote()` L66, `SyncFromHost()` L73
## Scenes / nodes
Built in code: BlockWorld node with chunk MeshInstance3D/StaticBody3D children under Main/World.
## Data
data/blocks.json, gather.json (drops), dungeons.json (dungeon gen)
## Links
[[world]] [[dungeons]] [[player]] [[combat]] [[network]] [[spellcraft]] [[skills_vfx]]
## Scores
- visuals 4/10: block look sits awkwardly against gothic realism; flat colored cubes lack texture/AO; not screenshot-covered, so unverified.
- feel 6/10: dig/place/fire/flow give systemic interaction.
- code 7/10: well split by role, small files, clear sim/mesh/gen separation.
## Ideas
- Per-vertex ambient occlusion in `BlockMesher.Build()`.
- Gothic triplanar PBR textures per block via `Blocks.Def()` materials (ambientCG sets).
- Blend block surface to heightfield terrain at the edge in `BlockGen.OverworldGen`.
- Dig crack-overlay and debris particles in `BlockFx.Dig()`.
- Greedy meshing to cut verts in `BlockMesher.Build()`.
- Fire light + smoke emission in `BlockSim.Ignite()`.
- Persist edits per save via `BlockWorld.Store()`.
