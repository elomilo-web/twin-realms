---
description: Self-improvement loop. Observe the game, find the weakest area, fix it, verify, log it, repeat.
argument-hint: "[number of iterations, default 3]"
---

You are running the Twin Realms self-improvement loop on this Godot C# project. The goal: make the game feel closer to a AAA title using only what can be made in code (no outside assets). Iterations to run: $ARGUMENTS (default 3 if empty).

## Before every step
- If a file named `STOP` exists in the project root, stop immediately. Finish writing the log, tell the user what was done and what is next, and do not start new work. (The user can also press Esc to interrupt you at any time.)
- Use a task list so the user can see progress.

## Start of the run
1. Read `CLAUDE.md`, `docs/IMPROVE_RUBRIC.md`, `docs/backlog.md`, and the last 3 entries of `docs/improvement_log.md`.
2. Run `tools/bot.sh`. It must pass. If it fails, fixing that is the first iteration.
3. Run `tools/observe.sh`. Read `observe_out/metrics.json` and look at the images in `observe_out/` with the Read tool: the `w*` world shots, `h01_hud`, the `m_*` menus, and the `anim_*` strips (frames run left to right, top to bottom). Be honest about what looks bad.
4. If this is the first run or the log has no scores, score all 12 rubric areas from 1 to 10 with evidence and write them into the log.

## Each iteration
1. **Diagnose.** Add any newly discovered problems and ideas to `docs/backlog.md` (impact, effort). Invent new ideas too: menus, systems, features, polish. Think about what a player would notice first.
2. **Pick ONE item**: highest impact for the effort, preferably the lowest rubric score. Keep the change small enough to finish in this iteration.
3. **Implement it** in code, data-driven (`data/*.json`), no outside assets. Keep saves compatible (new fields get defaults).
4. **Verify.**
   - `dotnet build` must have no errors.
   - `tools/bot.sh` must pass.
   - Run `tools/observe.sh` again and compare the before and after images and metrics (fps must stay at or above 60; draw calls should not jump).
   - Judge the result honestly. If it is not clearly better, or anything fails, run `git checkout .` and `git clean -fd scripts data` to undo, mark the backlog item `[!]`, and write the reason in the log so it is not repeated blindly.
5. **Save.** Commit with a clear message, tag it `iter-N`, push. Append an entry to `docs/improvement_log.md`: iteration number, date, what changed, evidence (images or metrics, before to after), updated scores for the areas touched, and the next 3 candidate items.
6. Every 5th iteration: re-score all 12 areas, prune the backlog, and brainstorm at least 5 fresh ideas.

## Rules
- Never edit files outside this project. Never add outside art, sound or model files.
- Do not leave the game broken between iterations; the last commit must always build and pass the bot.
- Do not add stats to C#; use `data/*.json`.
- Keep the user informed in plain language: after each iteration give 2 or 3 sentences (what changed, what looks better, what is next). The user is new to programming.
- At the end give a short summary and tell the user how to continue (`/improve` again) or stop.
