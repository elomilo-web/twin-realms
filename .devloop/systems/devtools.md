# Devtools
Purpose: Automated self-test bots, screenshot/capture tools and the dev-loop scripts (capture, verify, reindex, run modes) that let the game be checked without a human.
## Files
- `scripts/dev/DevCapture.cs` (221 lines): `--devcapture` autoload, shoots viewpoints from .devloop/viewpoints.json. Key: `Wanted()` L21, `_Ready()` L30, `RotateFolders()` L49, `LoadViews()` L61, `FreeCam()` L104, `WriteReport()` L210
- `scripts/Bot.cs` (932 lines): `--bot` gameplay self-test, step-based checks. Key: `Wanted()` L17, `_Ready()` L21, `Check()` L30, `Step("walk")` L104, `Step("combat and loot")` L111, `Dummy()` L178
- `scripts/BotBlocks.cs` (141 lines): block-world checks (dig, build, water, fire). Key: `TopOf()` L18, `Reach()` L117
- `scripts/MpBot.cs` (290 lines): two-window host/join multiplayer test (`--mp`). Key: `_Ready()` L33, `Write()` L40, `Finish()` L46
- `scripts/OnlineBot.cs` (103 lines): online server test (tools/online_test.sh). Key: `_Ready()` L25, `Finish()` L92
- `scripts/FitBot.cs` (659 lines): gear fit checks on mannequins. Key: `Wanted()` L24, `Studio()` L47, `Cases()` L80, `MeasureBody()` L125, `Helm()` L240
- `scripts/Shots.cs` (543 lines): screenshot tour (`--shots`). Key: `_Ready()` L21, `Cam()` L48, `Free()` L52
- `scripts/Showcase.cs` (183 lines): store-page pictures (`--showcase`). Key: `_Ready()` L20, `Free()` L45
- `scripts/Observer.cs` (346 lines): look-and-feel/perf/UI audit (`--observe`). Key: `AuditUi()` L70, `Sky()` L44
- `scripts/Perf.cs` (44 lines): `--perf` probe with default settings. Key: `_Process()` L18
- `scripts/AnimTool.cs` (113 lines): `--measure-attacks` impact timing. Key: `Wanted()` L16
- `scripts/Flag.cs` (94 lines): F8 playtest flag (screenshot + report). Key: `Event()` L23, `_Input()` L32, `Report()` L59
- `tools/run.ps1`: modes bot, fit, fit-tune, shots, showcase, quit, mp, export, smoke, setup, release, publish, upload, observe.
- `tools/capture.ps1`: runs DevCapture, rotates .devloop/screenshots/current to previous, writes report.json.
- `tools/verify.ps1`: dotnet build (errors/warnings) then capture; prints BUILD_OK/BUILD_FAIL.
- `tools/reindex.ps1`: refreshes .devloop/files.json (hash8, system, summary); `-check` lists changes only.
## Scenes / nodes
Built in code: Main adds the bot node by command-line user arg (Main.cs L50-58). DevCapture runs as autoload.
## Data
.devloop/viewpoints.json, .devloop/godot_path.txt, .devloop/screenshots/{current,previous}, report.json, bot_out/, screenshots/, user://playtest/flags.log (test runs use playtest_test/), test save/settings files in user://.
## Links
[[core]] [[network]] [[player]] [[camera]] [[ui]] [[world]] [[blockworld]] [[combat]]
## Scores
- visuals n/a: tools only (capture quality drives review)
- feel n/a: tools only
- code 6/10: broad coverage; Bot.cs is huge (932 lines) and the bots duplicate helpers (Say/Check/Heal/Mouse/Press/Free/Cam)
## Ideas
- Extract shared helpers (Say, Check, Heal, Press, Mouse, Hud, ClearFoes) from bots into `scripts/dev/BotBase.cs`.
- Split `Bot.cs` steps into files per system like BotBlocks.
- Add pixel-diff of current vs previous in `tools/capture.ps1` and flag changes in report.json.
- Add per-viewpoint perf numbers (fps, draw calls) in `DevCapture.WriteReport()`.
- Make `tools/verify.ps1` also run a fast `bot` subset.
- Add exit-code summary JSON for all `tools/run.ps1` modes.
- Reuse DevCapture viewpoints in `Showcase.cs` to avoid duplicate camera code.
