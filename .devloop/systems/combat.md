# Combat
Purpose: Hit data, damage mitigation, status effects, hit-stop game feel and the weapon basic-attack chain builder.
## Files
- `scripts/Combat.cs` (74 lines): HitInfo, angle helpers, floating damage text. Key: `With()` L27, `AngDiff()` L39, `YawTo()` L41, `Spawn()` L48, `SpawnRemote()` L55
- `scripts/Status.cs` (97 lines): damage types, resist/penetration mitigation, status effects (DoT, root, stun). Key: `Mitigate()` L33, `Mask()` L45, `FromMask()` L47, `TypeColor()` L53, `Add()` L69, `Tick()` L90
- `scripts/Feel.cs` (67 lines): hit-stop and recoil tuning. Key: `Load()` L17, `ForHit()` L46, `HitStop()` L55, `Tick()` L65
- `scripts/Attacks.cs` (186 lines): attack chain styles per weapon, sockets, mods, resolve to AttackStep. Key: `Build()` L81, `Resolve()` L136, `SetChain()` L114, `UnlockStep()` L96, `RankUp()` L108, `Steps()` L73
- `scripts/UiAttacks.cs` (92 lines): basic-attacks menu to edit chains. Key: `OpenAttacks()` L16
## Scenes / nodes
Built in code; damage numbers spawned as world-space labels at hit position. Melee resolved in Player.ResolveMelee (see [[player]]).
## Data
data/attacks.json, data/status.json, data/feel.json.
## Links
[[player]] [[enemies]] [[skills_vfx]] [[spellcraft]] [[animation]] [[camera]] [[ui]] [[progression]]
## Scores
- visuals 4/10: combat_swing shows a giant translucent glass-blade smear and a purple sparkle; no blood, sparks, impact flash or decals; reads cheap.
- feel 6/10: hit-stop, recoil, statuses and combo chains are in; lacks impact audio/VFX sync and enemy hit reactions are untested here.
- code 7/10: data-driven, compact files, clear static APIs.
## Ideas
- Impact VFX burst (sparks on metal, dark blood mist on flesh) spawned in `Combat.Spawn()` L48 neighbours or a new Impact.cs.
- Replace swing smear mesh with a thin additive trail tied to weapon bone, dimmer and shorter.
- Stronger hit-stop scaling for heavy/killing blows in `Feel.ForHit()` L46.
- Poise/stagger meter on enemies, consumed in `Status.Add()` L69.
- Muted gothic damage numbers (desaturated, smaller, serif) in `Combat.Spawn()`.
- Parry riposte window data-driven via data/feel.json.
- Combo cancel windows per step in `Attacks.Resolve()` L136.
