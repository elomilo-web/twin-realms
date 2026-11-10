# Port status

**Art + audio overhaul (2026-11-09), in progress.** Goal: one cohesive Genshin-like look, gear as separate assets on plain base bodies, calm audio.
Backup before the overhaul: `../twin-realms-backup-2026-11-09.zip` (git is not installed on this PC).
- Done: `scripts/Toon.cs` (toon lighting + rim on every StandardMaterial3D, screen-space outlines; tune in `data/look.json` "toon"). Brighter grading in look.json.
- Done: audio rewrite in `scripts/Sfx.cs`: file-based sfx with variants (`assets/audio/sfx/<id>_<n>`), soft synthesised bells as fallback, ambience layers (birds/crickets/breeze/shadow wind/dungeon), Minecraft-style music with quiet gaps, new Music volume slider. Old drones removed. Sources in `assets/ASSETS.md`.
- Done: characters (`scripts/Avatar.cs`, `data/avatar.json`). Hero, remote heroes, villagers and humanoid foes (bandit, archer, brute, warchief) are Quaternius base bodies + separate outfit parts bound to the same skeleton; weapons/shields are separate models in the hand bones; cape is a back piece on the spine. New heroes start in simple peasant clothes. Body triangles under clothing are removed per outfit (the free outfits were made for a slimmer body). Helmets (helm/greathelm/cap/circlet), wand, staff and race features (ears, tusks, horns) are procedural until models exist. HeroRig still runs invisibly for hand targets. 27 clips baked into `assets/models/anim/clips.res` (rebuild with `-- --bake-anims` after editing "clips").
- Done: gear glow (`scripts/GearFx.cs`, `data/gear_fx.json`): items roll an element by rarity (fire/frost/storm/venom/holy/shadow); colour = element or rarity; edge glow grows with rarity, epic/legendary weapons add a light and sparks; elemental weapons can inflict their status and swing in their colour. Trinkets are never drawn.
- New: body choice (male/female) on the create screen (`PlayerStats.Body`, sent to friends). `--observe --quick` = character shots only (about a minute).
- Size: release zip measured at 96.3 MB (limit 100). Texture import limit 1024 px, WAV → QOA. The environment packs will need budget: drop the 4.7 MB Champ de tournesol mp3 or split the download if needed.
- Next: environment (Stylized Nature + Medieval Village + Fantasy Props MegaKits are downloaded in ~/Downloads/Quaternius, all CC0). Then models for helmets/staff/wand/cape, and goblins/skeletons/animals (no CC0 models yet; still procedural).
- Verified: build OK, observer (full and quick) OK with no engine errors, bot 79/79.

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
