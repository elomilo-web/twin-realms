---
description: Aggressive self-improvement loop. Critique every image, fix many things across many areas per round, verify, log, repeat.
argument-hint: "[number of rounds, default 3]"
---

You are running the Twin Realms self-improvement loop on this Godot C# project. The goal is to make the game look, feel and play like a AAA title using only what can be made in code (no outside assets). Rounds to run: $ARGUMENTS (default 3).

The user's complaint about earlier runs: too slow, one tiny change at a time, only fixing what was listed, and not noticing obvious problems (for example blocky buildings). So this loop is BIG and SELF-DRIVEN:

- **Each round changes at least 6 things across at least 4 different areas** (for example world look, character animation, enemy visuals, HUD, menus, effects, a new system).
- **Nothing is off limits.** The backlog is a starting point, not a boundary. Anything visible in any image is fair game.
- **The user never has to ask for the obvious.** If it looks blocky, flat, empty, repetitive, stiff, cramped, ugly, unclear or unfinished, it is your job to notice and fix it.

## Before every step
- If a file named `STOP` exists in the project root, stop immediately: write the log, summarize, do not start new work. (Esc also interrupts.)
- Keep a task list so the user can see progress.

## Start of the run
1. Read `CLAUDE.md`, `docs/IMPROVE_RUBRIC.md`, `docs/backlog.md`, and the last 3 entries of `docs/improvement_log.md`.
2. Run `tools/bot.sh` (must pass; if not, fix first) and `tools/observe.sh`. On Windows without bash use `powershell -File tools/run.ps1 bot` and `powershell -File tools/run.ps1 observe`. If `git` is not on PATH, GitHub Desktop's copy is at `%LOCALAPPDATA%\GitHubDesktop\app-*\resources\app\git\cmd\git.exe`; pushing may need the user (GitHub Desktop "Push origin") because the terminal cannot prompt for a login.
3. Read `observe_out/metrics.json` and **look at every image** in `observe_out/` with the Read tool: all `w*` world shots, `d*`, `h01_hud`, all `m_*` menus, all `anim_*` strips (frames run left to right, top to bottom).

## Critique every image (the key step)
For EACH image, write down at least 5 concrete flaws, as if you were a harsh art director. Use this checklist and do not skip categories:
- **Geometry**: blocky or boxy shapes, straight edges, no bevels, no detail, repeated identical objects, floating or intersecting parts, wrong scale.
- **Materials**: flat colors, no texture or noise, no roughness variation, no wear, no color variation between objects.
- **Light**: flat lighting, weak shadows, no depth, no atmosphere (fog, god rays, bloom, ambient occlusion, color grading), dull sky.
- **Composition**: empty areas, nothing to look at, no landmarks, bad framing, nothing living.
- **Characters and enemies**: stiff pose, no silhouette, no personality, no animation, no hit reaction.
- **Effects**: missing or weak particles, trails, impacts, spell visuals.
- **UI**: alignment, spacing, contrast, font sizes, clutter, missing icons, inconsistent style, text overflow, empty-looking panels, no tooltips, no feedback.
- **Feel**: anything that would make a player say "this feels cheap".
Turn every flaw into a backlog line (impact, effort), then pick the best batch. Prefer high-impact fixes that make a screenshot obviously better at a glance.

## Toolbox (you are allowed and expected to use these)
Godot gives a lot of AAA look for free. Use it:
- **Environment**: SSAO, SSIL, SSR, SDFGI or VoxelGI, volumetric fog, glow and bloom, filmic tonemap, color correction, depth of field, sky and cloud shaders, adjust per time of day and per realm.
- **Meshes in code**: `SurfaceTool`/`ArrayMesh` to build beveled boxes, rounded houses, thatched and tiled roofs, arched doors, beams, chimneys, fences, stone walls with irregular blocks, trees with real trunks and layered canopies, rocks with noise displacement, cloth and banners; `CSG` for quick shapes; MultiMesh for repetition with random scale and rotation.
- **Materials**: `ShaderMaterial` with procedural noise textures (stone, wood grain, plaster, grass, bark, cloth), triplanar mapping, normal maps from `NoiseTexture2D`, rim light, wind sway, water with foam and refraction, emissive windows.
- **Effects**: `GPUParticles3D`, `Decal`, trails via `ImmediateMesh` or `MeshInstance3D` ribbons, hit-stop, camera shake, slow motion, screen vignettes, damage numbers.
- **Animation**: procedural animation (sine-driven limbs, spring bones, lean, bob, breathing, landing squash, anticipation and follow-through), tweens.
- **UI**: code-drawn vector icons (`_Draw`), 9-slice-style panels, gradients, shadows, tooltips, drag and drop, animated bars, SAO-style segmented bars.
- **Systems**: crafting, enchanting, bounties, achievements, shrines, weather, NPC routines, companions, mini-games, anything that makes the world feel alive.
- Reuse code: shared builders live in `scripts/Gfx.cs` (procedural textures `Gfx.Tex`, `GableRoof`, `Lumpy`, `Boulder`, `Pine`, `Blade`, `ExtrudeX`, `MergeChildren`), `scripts/Icons.cs` (vector icons, rarity frames), `scripts/Vfx.cs` (sparks, explosions, frost nova, heal pillar, lightning, sword trail). Extend them instead of starting new ones.
- Watch for washed-out effects: additive or translucent full-screen-sized meshes (columns, discs) easily tint the whole frame. Check the strips after any VFX change.
- Merge static body parts per material (`Gfx.MergeChildren` on each pivot) so characters stay cheap.

## Each round
1. **Critique** (above) and update the backlog. Also add ideas nobody asked for.
2. **Pick a batch**: 6 or more changes, at least 4 areas, biggest visible wins first. You may use sub-agents for independent files (world, character, UI, enemies). Do not let two agents edit the same file.
3. **Implement** all of it. Data-driven where it is content (`data/*.json`). Saves stay compatible (new fields get defaults).
4. **Verify the batch.** `dotnet build` clean, `tools/bot.sh` passes, `tools/observe.sh` fps stays at or above 60. Re-read the new images and compare with the old ones.
   - If the bot or build fails, find which change broke it and revert ONLY that change. Keep the rest.
   - Keep a change if it is better or neutral-and-needed. Revert only what is clearly worse. Do not throw away a whole round because one part failed.
5. **Save**: commit, tag `iter-N`, push. Append to `docs/improvement_log.md`: round number, every change, before and after evidence (image names, metrics), rubric scores touched, and the next 5 candidate items.
6. **Evolve the loop itself** (do this every round, briefly):
   - If you could not see something you needed to judge (a new building, a spell effect, a menu, a boss, a place, a time of day), **add it to the observer**: edit `data/observe_scenes.json` to add camera views, and edit `scripts/Observer.cs` or `scripts/Bot.cs` to capture new subjects (new menus, new animations, new effects, new enemies, close-ups, fights in progress).
   - If a metric would help (triangle count, overdraw, text overflow, contrast, unused space), add it to `metrics.json`.
   - If this command or `docs/IMPROVE_RUBRIC.md` led you astray or is missing something, edit it. Note what you changed in the log.
7. Every 3rd round: re-score all 12 rubric areas, prune the backlog, and brainstorm at least 8 brand-new features or polish ideas, then act on the best of them next round.

## Pace
- Do not stop to ask the user for permission or direction. Decide and act. The user prefers speed and ambition.
- Do not spend a whole round on one tiny tweak. If a round has fewer than 6 real changes, you were too timid.
- A round should end with a visibly different game.

## Rules
- Stay inside this project. No outside art, sound or model files.
- Never leave the game broken: the last commit always builds and passes the bot.
- No stats hard-coded in C#.
- After each round, tell the user in 3 to 5 plain sentences what changed and what to look at when they play. They are new to programming.
- At the very end: summary, and say they can type `/improve` again to continue or create a file named `STOP` to end.
