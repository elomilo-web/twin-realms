# Twin Realms: instructions for Claude Code

## What this is
A third-person, free-camera open-world action RPG in the spirit of **Two Worlds 2 and Skyrim**. Not isometric.
Engine: **Godot 4.x (.NET build), C#**. Goal: **simple and efficient**. Prefer the smallest change that works.

## Core pillars
1. **Two realms.** Light and Shadow. The same map, re-skinned and repopulated. Press R to shift (cooldown). Shadow has its own enemies, palette and ambient audio.
2. **Open world.** Village Oakhaven, roads, mountains, the Crimson Arena, a dragon lair, standing stones, obelisks, chests, bandit camps.
3. **Dungeons.** Three procedural dungeons (Hollow Barrow, Goblin Warrens, Ashen Depths) with depth descent and a boss at the end of each.
4. **Arena.** Wave Survival and Shadow Duel (fight a clone of your own hero).
5. **Hybrid builds.** Weapons: sword, bow, wand, staff. A Path-of-Exile-style passive tree with three arms (Warrior, Ranger, Mage) and bridge nodes so players can mix styles.
6. **Loot.** 5 rarities, random affixes, shops, a blacksmith.

## Source of truth
- `docs/DESIGN.md`: what the game is. Update it when a design decision is made.
- `docs/PORT.md`: porting checklist and status. Update it at the end of every work session.
- `docs/spec/reference_logic.js`: the working **JavaScript prototype logic**. Port the behaviour, not the syntax.
- `docs/spec/browser_prototype.html`: the playable prototype. Open it in a browser to see how a thing is meant to feel.
- `data/*.json`: game data extracted from the prototype. **Load these at runtime. Never hard-code stats in C#.**
- `data/tuning.json`: the user's master tuning file. Its `files` section overrides values in the other data files (applied in `JsonUtil.Load`), so a value listed there wins: change it there, or the change in the other file has no effect. Its `game` section holds numbers that used to be in code, read with `Tune.F("path", oldDefault)`. New balance numbers go into `game` (or the relevant data file plus a copy in `files`).

## Conventions
- C# 12, nullable enabled. One system per file in `scripts/`. Namespace `TwinRealms`.
- Data-driven: enemies, items, quests, tree nodes and dungeon themes come from JSON. Balance changes must not need code changes.
- Signals for cross-system events (EnemyKilled, ItemPickedUp, QuestUpdated). Avoid singletons except `Game` (autoload) and `SaveSystem`.
- Keep scenes small. Prefer composition: `Player.tscn`, `Enemy.tscn`, `Dungeon.tscn`, `UI/`.
- Placeholders first, art later. Use CC0 assets only (Quaternius, Kenney, Poly Haven, ambientCG) and record each in `assets/ASSETS.md`.
- Units are metres. Forward is -Z (Godot convention). The prototype used a different yaw convention, so do not copy angle math from it.

## Commands
- Build: `dotnet build`
- Run headless smoke test: `tools/smoke_test.sh`  (must exit 0; create it if it is missing)
- Screenshots: `tools/shots.sh`  (writes to `screenshots/`; create it if it is missing)

## Working rules
- Make small commits with clear messages after each working step.
- After any change: build, run the smoke test, then say what you verified. If you could not verify something, say so.
- Do not rewrite working systems for style. Do not add dependencies without asking.
- Ask before big design changes. For small ambiguities, choose the simplest option and note it in `docs/PORT.md`.

## Current focus
See the top of `docs/PORT.md`.
