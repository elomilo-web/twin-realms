# Arena
Purpose: Wave-based Crimson Arena: start a mode, spawn waves of enemies (no drops), best-wave tracking, exit gate.
## Files
- `scripts/Arenas.cs` (126): gate + wave logic. Key: `Interact()` L20, `Leave()` L28, `Start()` L38, `End()` L49, `OnPlayerDeath()` L59, `OnEnemyDown()` L62, `Tick()` L67, `NoDrops()` L112, `SpawnOne()` L114
- (Arena building itself is in `scripts/Builder.cs`, see [[world]])
## Scenes / nodes
Built in code: arena bowl and EXIT gate under Main/World (Builder); enemies spawned as Enemy nodes.
## Data
data/valor.json
## Links
[[world]] [[enemies]] [[combat]] [[progression]] [[ui]]
## Scores
- visuals 5/10: gate and stone stands are decent, red band reads; but sand floor is a flat featureless beige, "EXIT" label overlaps the wave banner, bright flat lighting.
- feel 6/10: clear wave loop with revive-in-place on death.
- code 6/10: compact but static-state heavy; OnPlayerDeath is one dense line.
## Ideas
- Textured sand with footprints/blood decals in Builder arena floor.
- Fix HUD overlap of EXIT sign and banner (`Arenas._Process()` L19).
- Crowd silhouettes and braziers in stands (Builder).
- Wave intro banner + gate slam in `Arenas.Start()`.
- Spawn telegraphs (dust/portals) in `SpawnOne()`.
- Refactor `OnPlayerDeath()` into readable steps.
