# Twin Realms dev loop: INDEX (root map; pointers only)

Godot 4.7 .NET (C#), third-person open-world action RPG. Repo root = this folder's parent. Everything is built in code
from `data/*.json` (one scene: scenes/Main.tscn). Art direction: dark gothic realism + Minecraft-style block ground
near the player (see PROCESS.md "Decisions"). Project rules: ../CLAUDE.md. Design: docs/DESIGN.md. Status: docs/PORT.md, docs/GOTHIC.md.

## Loop files
- DIRECTIVES.md (user, read first) Â· STATE.json Â· PROCESS.md Â· BACKLOG.md Â· LOG.md Â· files.json (every file: lines, hash8, system, summary)
- viewpoints.json (capture views) Â· godot_path.txt Â· screenshots/{current,previous}/ (+ report.json: fps per view, engine errors)

## Tools
- `tools\verify.bat [views]`: dotnet build (errors/warnings only) + capture. Exit 1 on failure.
- `tools\capture.bat [views]`: game with `--devcapture[-only=a,b]` (scripts/dev/DevCapture.cs autoload), ~2.5 min full sweep.
- `powershell -File tools/sheet.ps1 [-names a,b] [-compare]` -> .devloop/sheet.png (contact sheet; -compare = previous|current).
- `powershell -File tools/reindex.ps1` -> refresh files.json, prints changed files.
- `tools/run.ps1 smoke|showcase|shots|bot` (older bots; bot/mp only when the user asks).

## Folder map
- scripts/ (~120 .cs, namespace TwinRealms; one system per file) Â· scripts/dev/ (loop tools)
- data/ (all numbers/content; tuning.json "files" overrides others!) Â· assets/ (models, materials/gothic G_*.tres, textures, audio)
- tools/ (scripts, blender_gothic/, kit_gen/) Â· docs/ Â· installer/ Â· scenes/Main.tscn

## Systems (card -> main files)
- [[core]] systems/core.md: Main.cs boot/Begin, Game.cs (data, save/load, settings), JsonUtil, Stats
- [[player]] systems/player.md: Player.cs (792), Inputs, PlayerDowned, PlayerMacros
- [[camera]] systems/camera.md: CameraRig.cs
- [[combat]] systems/combat.md: Combat, Feel (hit-stop/shake), Attacks, Status
- [[animation]] systems/animation.md: Avatar.cs (755), HeroRig, FootIK, Kit, Wings
- [[skills_vfx]] systems/skills_vfx.md: Skills, PlayerSkills(+Extra), Projectile, Vfx, SpellFx, GearFx, Fx
- [[spellcraft]] systems/spellcraft.md: Spellcraft, UiSpellcraft, PlayerSpellcraft
- [[enemies]] systems/enemies.md: Enemy.cs (947), EnemyAggro, EnemyDuelist, EnemyScarletta, Targeting, Threat, Wildlife
- [[ui]] systems/ui.md: Ui.cs (827, partials Ui*.cs), Hud, Icons, UiTheme, UiWidgets, MapMarks
- [[loot]] systems/loot.md: Items, ItemSlot, Loot, LootTables, Weapons, UiCraft, UiSmith, Home
- [[progression]] systems/progression.md: Tree, ModTree, Progression, Quests, Valor, Titles, Affixes, Classes, Races
- [[world]] systems/world.md: World.cs (949: env, sky, light, terrain), Lands.cs (922), Builder (village/arena), Flora, Gfx, GothicMats, Ambient, shaders
- [[blockworld]] systems/blockworld.md: Blocks, BlockGen, BlockMesher, BlockWorld, BlockSim, BlockFx, BlockArea, PlayerBlocks, NetBlocks
- [[dungeons]] systems/dungeons.md: Dungeon.cs, DungeonLair, LairIntro, NetDungeon
- [[arena]] systems/arena.md: Arenas.cs
- [[audio]] systems/audio.md: Sfx.cs, Ambient
- [[network]] systems/network.md: Net*, Updater, Accounts, RemoteHero (Railway server deploys main)
- [[devtools]] systems/devtools.md: Bot, Shots, Showcase, Observer, FitBot, Perf, DevCapture

## Hot paths (per-frame)
- Player._PhysicsProcess (movement, combat), CameraRig, Enemy._PhysicsProcess (AI, x many), BlockWorld streaming + BlockSim tick (0.25 s),
  Flora/grass, World._Process (time of day, realm blend), Hud._Process.

## Known problems (from capture 2026-11-24)
- Frostmere shore: no visible water, dry sand blocks (water view).
- Block terraces still read stair-steppy at distance (lip/jitter added #1). Dungeon entry room near-black.
- FPS: foliage view min 14, close-up min 20 (spikes); avg ~55-70 elsewhere at 1280x720 Ultra defaults.
- Compiler warnings: 1. Engine teardown on quit logs "Handle is not initialized" (harmless, filtered).
