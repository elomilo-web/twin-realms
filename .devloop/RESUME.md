# RESUME (paused by the user 2026-11-25, "remember where you're at but stop")

## Where things stand
- Last commits (pushed to main): 053f53f devloop #20 (all cycle-20 work + user flag fixes + perf), b67a38e focus F-01.
- Work list: .devloop/FOCUS.txt (user's TwinRealms_FOCUS.txt). F-01 [x]. Next: F-02..F-08 (Phase 0).
- Working tree has UNCOMMITTED, UNFINISHED edits from four agents that hit the usage limit mid-task (build state unknown):
  - F-02 stats audit (agent a2d0c73a615a26364): stopped at "Now look at Stats.cs" (early: maybe StatPipeline.cs/Commands.cs/stats.md started).
  - F-03/F-04 body seams + white flashing (agent a822292c5cd49c3ed): stopped early (no report).
  - F-05/F-06 weapon sounds + bow animation (agent a494a7f1b4065efad): stopped at "check avatar.json clips block and weapons section".
  - F-07/F-08 reticle + flying bottle (agent a455648c5d9f70293): stopped at "edit the JSON: drop the three travel entries and add a banned list" (data/spellfx.json mid-edit).

## How to resume
1. `git status` + `git diff --stat` to see the partial work; `dotnet build` to see if it compiles.
2. Resume each agent with SendMessage to its id ("continue from where you stopped") if still addressable; otherwise
   relaunch the same briefs (focus items F-02, F-03/04, F-05/06, F-07/08: read FOCUS.txt blocks; file ownership as before:
   F-02 Stats/Tree/Races/Classes/Affixes/Items/Skills.Compute/StatPipeline/Commands/data stats; F-03/04 Avatar.cs materials/
   MaskBody/hair, CharRim, hair .import; F-05/06 Sfx.cs, data/weapons.json, data/avatar.json, Player.cs attack/anim/MuzzlePos;
   F-07/08 PlayerBlocks.cs, BlockAim.cs, CameraRig aim, Hud reticle, SpellFx/Vfx/Projectile visuals, data/spellfx.json).
3. Verify each item's DONE WHEN with captures, mark [x] + NOTE in FOCUS.txt, commit "focus F-XX: <title>", push.
4. Then continue FOCUS.txt top to bottom (Phase 1: F-09 first person ...). Ask the user before multiplayer tests (F-43/F-44).

## Open notes
- User settings.json: MaxFps 30 is the user's own cap (told them). Capture tool shares the user's settings file: isolate
  it (DevCapture should load/save a separate settings path) before more captures.
- Backlog extras: shadow-realm pink patches on sky islands, arena interior dim, leather cap grey under epic rim,
  unique mob drops + crafting expansion, pets (design .devloop/systems/pets.md) — after FOCUS items or where they fit.
