# Player
Purpose: The hero controller: movement, roll, weapons (sword/bow/wand/staff), block/parry, spells, potions, downed/raid rules, macros and input binding.
## Files
- `scripts/Player.cs` (792 lines): CharacterBody3D hero, god-class for locomotion + combat dispatch. Key: `_Ready()` L56, `Facing()` L134, `AddXp()` L145, `TakeHit()` L160, `Revive()` L245, `Teleport()` L256, `ToggleMount()` L264, `AimPoint()` L273, `ResolveMelee()` L354, `FireWeapon()` L401, `_PhysicsProcess()` L478
- `scripts/PlayerDowned.cs` (144 lines): partial; party-raid downed state, revive channel, boss grab. Key: `DownedAllies()` L28, `BeginRevive()` L35, `CancelChannel()` L57, `StartGrab()` L96, `ReviveHere()` L127
- `scripts/PlayerMacros.cs` (89 lines): partial; two scripted action macros per hero. Key: `MacroAction()` L22, `StopMacro()` L42
- `scripts/Inputs.cs` (139 lines): registers InputMap in code, rebinding, key naming. Key: `Setup()` L39, `Rebind()` L89, `ResetAll()` L103, `ApplyBinds()` L76, `KeyOf()` L131
## Scenes / nodes
Built in code. Player (Instance singleton) has children "Model" (HeroRig, L85) and "Avatar" (Avatar, L90); camera lives in [[camera]] CameraRig.
## Data
data/feel.json (run/sprint speeds via Feel), data/tuning.json, data/progression.json, data/races.json (via Races.Look).
## Links
[[camera]] [[combat]] [[animation]] [[skills_vfx]] [[spellcraft]] [[ui]] [[network]] [[progression]] [[loot]] [[core]]
## Scores
- visuals 3/10: closeup shows a flat grey-shirt peasant with a plastic black disc shield; no cloth detail, no armour read, nowhere near Elden Ring silhouette.
- feel 6/10: roll, block, parry, mount and macros exist and are data-tunable, but no readable weight or stamina-gated commitment.
- code 4/10: Player.cs is a 792-line god class with partials bolted on; hard to test.
## Ideas
- Split locomotion out of `Player.cs` `_PhysicsProcess()` L478 into PlayerMove.cs partial to make tuning isolated.
- Add stamina-gated roll/sprint with i-frame window data in data/feel.json, read in `Player.cs` roll code.
- Lock-on targeting using `AimPoint()` L273 plus CameraRig soft-lock (see [[camera]]).
- Heavy attack charge (hold) in `ResolveMelee()` L354 with distinct hit-stop via `Feel.ForHit()`.
- Footstep surface events (stone, grass, mud) from `_PhysicsProcess()` to the audio system.
- Gothic armour sets: swap `HeroRig.ApplyGear()` look for cloaks, hoods and worn leather instead of flat tunic.
- Add input buffering (0.15s) for attack/roll in `_PhysicsProcess()` to tighten responsiveness.
