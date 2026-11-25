# Dungeons
Purpose: Procedural 3-theme dungeons with depth, boss room, raid lairs (instanced boss fights), party-shared instances.
## Files
- `scripts/Dungeon.cs` (445): generation, floors, entry/exit. Key: `Reveal()` L39, `Enter()` L68, `EnterShared()` L74, `SetOwner()` L77, `Register()` L90, `SpawnPuppet()` L96, `Leave()` L128
- `scripts/DungeonLair.cs` (291): raid lairs. Key: `IsLair()` L17, `LairTick()` L42, `InBossRoom()` L60, `Interact()` L254/L277
- `scripts/LairIntro.cs` (81): boss room cinematic. Key: `Play()` L17, `_Process()` L39
- `scripts/NetDungeon.cs` (176): host party instances. Key: `DunRequest()` L20, `DunPeers()` L72, `HitPeerDun()` L122, `DunSpawn()` L139, `DunKill()` L148
## Scenes / nodes
Built in code: Main/Dungeon (rooms, torches, lights), exit portal; reuses BlockWorld in dungeon mode.
## Data
data/dungeons.json, progression.json
## Links
[[blockworld]] [[enemies]] [[network]] [[progression]] [[loot]] [[combat]]
## Scores
- visuals 5/10: dungeon.png is near-black with a flat cyan blob exit marker; cobble floor is decent but lighting is murky, walls unseen; no atmosphere (fog, dust, props).
- feel 6/10: depth/waves/boss loop works; minimap helps navigation.
- code 6/10: Dungeon.cs 445 lines mixes gen/entry/puppets.
## Ideas
- Replace the flat cyan exit disc with a gothic archway/light shaft in `Dungeon.Enter()` setup.
- Torch flicker + warm pools with volumetric fog per room in Dungeon generation.
- Rubble, bones, webs, pillars as room dressing (gen step in `Dungeon.cs`).
- Raise ambient floor slightly so walls read.
- Boss intro: slow camera push and title card in `LairIntro.Play()`.
- Split puppet/net code out of `Dungeon.cs` into NetDungeon.
