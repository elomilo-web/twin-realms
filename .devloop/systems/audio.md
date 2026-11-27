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
## F-39 / F-40 (2026-11-27)
- Buses made in code (`Sfx.EnsureBuses`): Music, Effects, Ambient, Voice under Master; `Sfx.ApplyVolumes` maps Settings.Music / Sfx / Ambient / Voice to them. Footsteps and interface sounds stay on Master with their own sliders. `Sfx.Voice(id, vol, pitch)` plays on Voice.
- `scripts/Music.cs` + `data/music.json`: picker by place (boss > dungeon > home > town > night > explore), crossfade, on/off per category and song (Settings.MusicOff), dev command `/music`. `scripts/UiMusic.cs`: the Music part of Settings > Audio.
- `tools/music_gen.ps1` writes the seven generated songs to assets/audio/music/gen (no Godot needed); Music.ReadWav loads a .wav that has no .import yet.
- `scripts/Instruments.cs` + `data/instruments.json` + `scripts/NetMusic.cs`: playable instruments (runtime synth `InstrumentDb.Tone`), dev command `/play`.
