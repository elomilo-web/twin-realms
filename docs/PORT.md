# Port status

The whole browser game has been ported to Godot C# in one pass. It builds cleanly and runs on Godot 4.7.2 .NET (Windows).

**Self-test bot (2026-11-08):** `tools/bot.sh` passes 42/42 checks, 10 runs in a row, with no engine errors. Fixes this session (all in `scripts/Bot.cs` plus one HUD line; no gameplay logic changed):
- The bot held LMB down, but attacks fire on *release* (tap = light, hold = power), so it never attacked. It now clicks.
- The bot could teleport behind a dungeon wall when starting a fight. It now picks a spot with line of sight to the target.
- Rare crash on exit (Godot .NET GC finalizers racing engine teardown). The bot now runs GC and drains finalizers before `Quit`.
- HUD showed "[E] [E] ..." because both `Player.Prompt` and `Hud` added the key prefix. Removed the one in `Hud`.

**/improve run 1 (2026-11-08), iterations 1-3** (details in `docs/improvement_log.md`):
1. HUD and menu layout fixed: quest tracker was drawn off-screen (that was the stray "W"), compass was mirrored, hotbar names overflowed, skill tree and map were taller than the window.
2. Hit-stop and enemy recoil/squash (`scripts/Feel.cs`, `data/feel.json`).
3. Procedural walk/run cycle for the player (legs, arm swing, bob, lean, breathing).
**/improve run 2 (2026-11-08), iterations 4-8:** SAO-style menus and HUD (data/ui_theme.json), wind-swept ground cover (data/flora.json), 315 gatherables and a materials bag (data/gather.json), wildlife herds with legs, fleeing and drops (data/wildlife.json, new enemies.json fields). Bot now 48 checks.
Note: git push from Claude Code's shell has no GitHub login on this PC; push from GitHub Desktop.

Note: Windows has no bash here; the bot steps were run directly in PowerShell with the Godot console exe.

Written: terrain (same noise as the browser version), day/night, Light/Shadow realms, village Oakhaven, Crimson Arena (waves + shadow duel), three procedural dungeons with bosses and descent, dragon lair, standing stones, obelisks, chests, enemy camps, NPCs, quests, shops, smith, loot, skill tree, pack/character/journal/map/settings/pause/title, save and load, synthesized sound.

Data lives in `data/*.json`. The browser version is in `docs/spec/` as the reference.

Deferred: real art assets, real textures, audio polish.

**/improve run 3 (2026-11-08), iterations 9-11** (details in `docs/improvement_log.md`):
- New shared code: `scripts/Gfx.cs` (procedural textures, roofs, rocks, trees, blades, mesh merging), `scripts/Icons.cs` (vector icons), `scripts/Vfx.cs` (spell effects, sword trail), `scripts/ItemSlot.cs` (grid pack cells and tooltips), `scripts/Ambient.cs` (butterflies, fireflies, birds, spores). New data: `data/look.json` (sky, grading, fog), `data/ambient.json`.
- Look: sky shader with clouds, stars and dusk; ACES, SSAO, grading; textured buildings with real gable roofs; water shader; faceted trees and rocks; villagers, hero and foes rebuilt with faces, gear and walking legs.
- Play: sword trails, spell VFX, death dissolve, hurt vignette, grid pack with comparison tooltips, icon hotbar with radial cooldowns.
- Windows note: no bash on this PC; `powershell -File tools/run.ps1 bot|observe` replaces `tools/bot.sh` and `tools/observe.sh`. Pushing from the terminal needs a GitHub login, so push with GitHub Desktop.
