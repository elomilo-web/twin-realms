# Camera
Purpose: Free third-person orbit camera with spring-arm collision, mouse look, scroll zoom, shake and mouse capture toggling.
## Files
- `scripts/CameraRig.cs` (78 lines): Node3D rig (Pivot -> SpringArm3D "Arm" -> Camera3D FOV 70, Far 4500). Key: `Shake()` L19, `_Ready()` L25, `ExcludeFromCollision()` L37, `_UnhandledInput()` L39, `_Process()` L56
## Scenes / nodes
Built in code: CameraRig/Pivot/Arm (SpringArm3D, margin 0.3, mask 1)/Camera (Current = true).
## Data
Shake scale from Game.Set (settings); no json.
## Links
[[player]] [[combat]] [[core]] [[ui]] [[devtools]]
## Scores
- visuals 4/10: combat_swing shot is cramped and high-angle, tree trunks and bushes occlude the hero; no DOF, vignette or grade from the camera.
- feel 5/10: basic orbit and shake only, no lock-on, no hit kick, no FOV change when sprinting.
- code 8/10: tiny, clear, single purpose.
## Ideas
- Soft lock-on target in `CameraRig._Process()` L56 using `Player.AimPoint()`.
- Sprint/roll FOV kick (70 to 76) lerped in `_Process()`.
- Directional hit shake from `Feel.ForHit()` into `Shake()` L19 with a direction vector.
- Shoulder offset and smoothing (critically damped follow) on Pivot.
- Fade or dither occluding foliage when SpringArm shrinks below a threshold.
- Subtle low-pass camera sway/breathing in idle for a handheld cinematic feel.
- Dark vignette plus slight chromatic aberration at low HP via Environment/CameraAttributes.
