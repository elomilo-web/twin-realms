# Skills & VFX
Purpose: Hotbar skills (data-driven kinds), projectiles, and all spell/combat visuals (code particles, flipbook sprites, gear glow).
## Files
- `scripts/PlayerSkills.cs` (618 lines): hotbar keys 1-6, cost/cooldown, cast/charge, buffs. Key: `CdOf()` L18, `CancelCast()` L135, `AutoFire()` L217, `BeamFx()` L430, `_Process()` L580
- `scripts/PlayerSkillsExtra.cs` (470 lines): newer kinds (wall, trap, turret, vortex, blinkto...) as runtime nodes. Key: `ApplyEnchant()` L206, `Near()` L226, `Hit()` L232, `Glow()` L237, `Spawn()` L248
- `scripts/Skills.cs` (321 lines): SkillDef/SupportDef/SkillCalc + registry, ranks, sockets. Key: `Load()` L88, `Unlock()` L141, `RankUp()` L169, `Socket()` L179, `Compute()` L192
- `scripts/Projectile.cs` (176 lines): arrows, bolts, fireballs; owner 'p' or 'e'. Key: `Spawn()` L26, `_Ready()` L38, `_PhysicsProcess()` L70
- `scripts/SpellFx.cs` (212 lines): 4x4 flipbook sprite spells from assets/vfx/flipbooks. Key: `For()` L78, `Skill()` L87, `SkillAttach()` L95, `Play()` L110, `Beam()` L128, `Stretch()` L159
- `scripts/Vfx.cs` (277 lines): code particles, trails, explosions, frost nova, heal pillar, lightning. Key: `SpriteMat()` L52, `GroundMark()` L70, `Sparks()` L107, `Explosion()` L128, `FrostNova()` L150, `Lightning()` L202
- `scripts/Fx.cs` (44 lines): cheap burst/ring/line helpers. Key: `Burst()` L9, `Ring()` L16, `Line()` L31
- `scripts/GearFx.cs` (136 lines): elemental/rarity edge glow + sparks on gear. Key: `Roll()` L40, `ColorOf()` L42, `Apply()` L45, `EdgeMat()` L95
## Scenes / nodes
Built in code: CpuParticles3D/GpuParticles3D added to Game.Root with timer-based QueueFree (`Vfx.Later`); SpellFxSprite billboards; Projectile Node3D; per-kind skill helper nodes.
## Data
data/skills.json, data/skilltree.json, data/spellfx.json, data/gear_fx.json, data/attacks.json (basic-attack chain statuses). Flipbook PNGs in assets/vfx/flipbooks.
## Links
[[combat]] [[spellcraft]] [[player]] [[animation]] [[enemies]] [[loot]] [[ui]] [[audio]]
## Scores
- visuals 4/10: spell_cast shows a flat cream/blue crescent decal on the ground; bright cartoon sweep arcs clash with muted gothic grade, no embers/smoke/lighting weight.
- feel 5/10: readable swing arcs and hit effects, but no hit-stop/shake evidence and effects lack impact punch.
- code 6/10: data-driven and well factored, but PlayerSkills/Extra are large with many kinds in one class; lots of dense one-liners.
## Ideas
- Grade all flipbook sprites to gothic palette (desaturate, ember orange/ash) via `SpellFx.Make()` tint from data/spellfx.json.
- Replace the flat sword-arc with a thin dark-steel ribbon trail plus sparks in `Vfx` trail code; add motion-blur streak.
- Add hit-stop and camera shake on `PlayerSkillsExtra.Hit()` / `Projectile._PhysicsProcess()` impacts.
- Add dynamic OmniLight flicker + smoke/ember particles in `Vfx.Explosion()` and `Vfx.Flash()`.
- Spawn ground scorch/crack decals via `Vfx.GroundMark()` after fire/strike skills.
- Soft-particle depth fade in `Vfx.SpriteMat()` to remove hard billboard edges.
- Split `PlayerSkillsExtra.cs` per kind into separate files; share a `SkillNode` base for `_Ready/_Process`.
- Projectile trails with blackened smoke for 'e' owner in `Projectile._Ready()` so enemy shots read as threats.
