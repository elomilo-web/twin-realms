# Port status

The whole browser game has been ported to Godot C# in one pass. It builds cleanly and runs on Godot 4.7.2 .NET (Windows).

**Self-test bot (2026-11-08):** `tools/bot.sh` passes 42/42 checks, 10 runs in a row, with no engine errors. Fixes this session (all in `scripts/Bot.cs` plus one HUD line; no gameplay logic changed):
- The bot held LMB down, but attacks fire on *release* (tap = light, hold = power), so it never attacked. It now clicks.
- The bot could teleport behind a dungeon wall when starting a fight. It now picks a spot with line of sight to the target.
- Rare crash on exit (Godot .NET GC finalizers racing engine teardown). The bot now runs GC and drains finalizers before `Quit`.
- HUD showed "[E] [E] ..." because both `Player.Prompt` and `Hud` added the key prefix. Removed the one in `Hud`.

Cosmetic issues seen in bot screenshots, not yet fixed: hotbar spell names overflow their slots; the compass strip shows "N" twice and a stray "W" at the top-right screen edge.

Note: Windows has no bash here; the bot steps were run directly in PowerShell with the Godot console exe.

Written: terrain (same noise as the browser version), day/night, Light/Shadow realms, village Oakhaven, Crimson Arena (waves + shadow duel), three procedural dungeons with bosses and descent, dragon lair, standing stones, obelisks, chests, enemy camps, NPCs, quests, shops, smith, loot, skill tree, pack/character/journal/map/settings/pause/title, save and load, synthesized sound.

Data lives in `data/*.json`. The browser version is in `docs/spec/` as the reference.

Deferred: real art assets, real textures, audio polish.
