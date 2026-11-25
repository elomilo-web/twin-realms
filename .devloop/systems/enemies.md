# Enemies
Purpose: Data-driven enemies (melee, ranged, summoner, dragon, wildlife), bespoke bosses, aggro/taunt, lock-on targeting and overworld threat ambushes.
## Files
- `scripts/Enemy.cs` (920 lines): core enemy FSM, stats from enemies.json, hit/status handling, net sync. Key: `Rescale()` L29, `_Ready()` L94, `ModelPose()` L310, `Stun()` L430, `Wake()` L433, `Incoming()` L442, `TakeHit()` L502, `_PhysicsProcess()` L599, `NetWrite()` L865
- `scripts/EnemyAi.cs` (64 lines, `partial class Enemy`): `Notices`/`Alert` (aggro), `ClearTele`/`Telegraph`/`TeleTick` (attack telegraph), `SummonAdds`. The state switch is inline in `_PhysicsProcess`, not separate handlers.
- `scripts/EnemyDb.cs` (88 lines): EnemyDef loader/registry. Key: `Clone()` L51, `Get()` L64, `Load()` L70
- `scripts/EnemyAggro.cs` (80 lines): who a monster/boss fights (hero or remote peer), taunt. Key: `HeroOf()` L27, `PeerOf()` L33, `Taunt()` L49
- `scripts/EnemyDuelist.cs` (453 lines): Crimson Arena Shadow Duel boss, round scaling. Key: `DuelStats()` L45, `DuelDef()` L58, `ClampArena()` L440
- `scripts/EnemyScarletta.cs` (673 lines): Rotting Hollow raid boss. Key: `RotMotes()` L85, `ScarIntroPose()` L644
- `scripts/EnemyShade.cs` (77 lines): black mirror-image shades of party members. Key: `ShadeModels()` L19
- `scripts/Targeting.cs` (109 lines): Tab/middle-mouse lock-on, ring marker, auto-target. Key: `Toggle()` L23, `Set()` L31, `Pick()` L50, `PickAuto()` L69, `Tick()` L90
- `scripts/Threat.cs` (95 lines): playtime-driven ambush escalation. Key: `Level` L22, `SpawnFor()` L45, `Tick()` L47, `Ambush()` L74
- `scripts/Wildlife.cs` (36 lines): herd placement (deer, rabbits, boars, shade stags). Key: `Spawn()` L9
## Scenes / nodes
Built in code: Enemy nodes (CharacterBody3D) with model or primitive body, name label (e.g. "Wolf Lv20"), lock-on ring (Targeting.Ring), boss arena/lair nodes created by Builder/DungeonLair.
## Data
data/enemies.json, data/wildlife.json, data/progression.json ("threat"), tunables via Tune ("aggro.*", "duel.*", "raid.*").
## Links
[[combat]] [[animation]] [[world]] [[dungeons]] [[arena]] [[network]] [[progression]] [[skills_vfx]] [[loot]] [[ui]] [[audio]]
## Scores
- visuals 3/10: Obsidian Golem is a saturated flat-red lumpy blob; wolf/boar are tiny with floating text; nothing menacing or Elden Ring-grade.
- feel 5/10: stagger, lock-on and aggro exist, but little visual telegraph or hit reaction readable in captures.
- code 5/10: Enemy.cs is 947 lines with 270-line _PhysicsProcess and dense one-liners; bosses split into partials helps.
## Ideas
- Gothic-grade enemy materials: replace flat red golem with cracked obsidian + ember emissive seams via GothicMats in `Enemy._Ready()`.
- Split `Enemy._PhysicsProcess()` L611 into per-state methods (Chase/Attack/Flee/Stagger).
- Attack telegraph: red ground decal and wind-up glow in `Enemy` windup code using `Vfx.GroundMark()`.
- Scale name labels with distance and fade; use gothic font/outline in the label builder in `Enemy._Ready()`.
- Hit reactions: flash emissive + brief flinch pose in `Enemy.TakeHit()`.
- Lock-on ring: replace with thin gold diamond reticle in `Targeting.Ring()`.
- Threat ambush: spawn from fog with howl audio and screen vignette in `Threat.Ambush()`.
- Boss intro cinematic with named health bar (Elden Ring style) in `EnemyScarletta.ScarIntroPose()`.
