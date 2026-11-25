# Audio
Purpose: SFX playback (file-based with synthesized bell/whoosh fallback), footsteps by surface, ambience mix and music.
## Files
- `scripts/Sfx.cs` (251 lines): all audio. Key: `ScanSfx()` L47, `Make()` L58, `Bell()` L65, `Whoosh()` L75, `Gen()` L84, `Play()` L117/L122, `Step()` L138, `StepKind()` L151, `MusicSoon()` L240, `_Process()` L242, `ApplyVolumes()` L244, `SetRealmMix()` L249 (empty stub)
## Scenes / nodes
Built in code: singleton Sfx node (`Sfx.I`) with pooled AudioStreamPlayers, ambience layers and a music player.
## Data
data/ambient.json ("sound" toggle); files in res://assets/audio/sfx/id_N.(wav|ogg|mp3), assets/audio/ambient/, assets/audio/music/. Volumes from Game.Set (Sfx, Steps, UiVol).
## Links
[[core]] [[combat]] [[player]] [[enemies]] [[skills_vfx]] [[ui]] [[world]] [[progression]]
## Scores
- visuals: n/a
- feel 5/10: random variant selection and surface-aware steps are good; missing ids fall back to synth bells that may not fit gothic tone.
- code 6/10: compact and single-file, but music/ambience/sfx all in one class and `SetRealmMix` is a stub.
## Ideas
- Implement `SetRealmMix()` L249: low-pass/reverb bus swap between light realm and shadow realm.
- Per-id pitch/volume jitter in `Play()` L122 to avoid repetition.
- Positional 3D playback for enemy/skill sounds in `Play()` (AudioStreamPlayer3D overload).
- Add reverb bus by location (dungeon, cathedral) in `ApplyVolumes()`.
- Distance low-pass and occlusion on `Step()` for enemies.
- Dynamic combat music layer triggered by `Targeting`/`Threat` in `TickMusic()`.
- Replace synth fallback in `Gen()` L84 with dark drone/low gong tones.
- Split into SfxPlayer, Ambience, Music classes.
