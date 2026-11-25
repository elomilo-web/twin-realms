# Core
Purpose: Global game state, save/load (slots, online cloud save), settings, the Main boot sequence (title, world, player, HUD, server mode), JSON loading with tuning overrides, and the saved stats model.
## Files
- `scripts/Game.cs` (215 lines): static Game + Settings. Key: `Settings` L8, `RecalcTree()` L52, `Notify()` L63, `File()` L72, `HasSave()` L91, `DeleteSlot()` L92, `UseOnline()` L98, `LeaveOnline()` L107, `WriteFile()` L113, `Save()` L124, `Load()` L136, `NewGame()` L153, `LoadSettings()` L168, `SetBackground()` L185, `ApplyDisplay()` L187, `SaveSettings()` L203, `Q()` L210, `LevelAt()` L213
- `scripts/Main.cs` (151 lines): game root, bot/tool mode dispatch. Key: `_Ready()` L10, `Hero()` L40, `JoinWorld()` L77, `ServerWanted()` L87, `RunServer()` L88, `Begin()` L114, `_Notification()` L137, `_Process()` L143
- `scripts/JsonUtil.cs` (81 lines): data loader with data/tuning.json merge, helpers. Key: `Load()` L16, `Merge()` L46, `F()` L56, `S()` L57, `Hex()` L58, `Tune.F()` L69, `Tune.I()` L79
- `scripts/Stats.cs` (107 lines): saved PlayerStats and derived stats. Key: `PlayerStats` L10, `MaxHp()` L67, `MaxMp()` L68, `Defense()` L70, `CritChance()` L73, `SpellPow()` L75, `WpnDmg()` L80, `GainXp()` L92, `Scaling` L101
- `scenes/Main.tscn` (7 lines): root scene with Main.cs.
- `installer/Setup.cs` (286 lines): per-user Windows installer to %LOCALAPPDATA%\Programs\Twin Realms (built via `tools/run.ps1 setup`).
## Scenes / nodes
Main (Node3D) creates children in code: Sfx, Flag, Net, Ui, TitleCam, then World/Player/Hud via `Begin()` (L114). Bot/tool modes add their own node.
## Data
user://save.json (SaveFile, 3 slots), user://settings.json; bots use test paths (test_save, mp_*_save, perf_settings); data/tuning.json (master override via JsonUtil.Load/Tune), data/build.json.
## Links
[[network]] [[player]] [[ui]] [[progression]] [[world]] [[devtools]] [[loot]] [[skills]]
## Scores
- visuals n/a: no rendering of its own
- feel 7/10: quick boot, background FPS cap, settings applied live
- code 6/10: Main._Ready is a long chain of mode ifs; Game is a static god-object, but tuning override via Tune is neat
## Ideas
- Extract test/tool mode dispatch from `Main._Ready()` into a `Modes` registry.
- Add save versioning + migration in `Game.Load()` and backup rotation in `Game.WriteFile()`.
- Atomic write (tmp + move) in `Game.WriteFile()` as Accounts already does.
- Cache parsed docs and watch for tuning reload in `JsonUtil.Load()`/`Tune.F()` (hot reload in dev).
- Log unknown tuning paths in `Tune.F()` to catch typos.
- Autosave on interval/area change in `Main._Process()`.
- Move Settings validation/clamping into `Game.LoadSettings()`.
- Break `PlayerStats` derived-stat math into a testable StatCalc (`Stats.cs` MaxHp..SpellPow).
