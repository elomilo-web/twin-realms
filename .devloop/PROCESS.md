# Dev loop process (living; changed every 5th cycle when progress is slow)

## Fixed rules (never remove)
- Stop check first: .devloop/STOP or the user saying "stop".
- One commit per cycle ("devloop #N: ..."), pushed (the user wants finished work pushed without asking).
- Revert-on-failure: an item that fails verify 3 times has only its own files reverted and is logged as blocked.

## Decisions
- 2026-11-24 ART DIRECTION: the setup prompt asked for "anime cel-shaded fantasy", but the project already committed to
  dark gothic realism (Elden Ring-like; docs/GOTHIC.md, data/look.json toon shaders off) and the Minecraft-style block world
  merged into main today. The loop keeps gothic realism + block world: muted grade, scanned triplanar materials, warm
  torchlight vs cold shadow, readable silhouettes, rim light. Write "anime" (or anything else) in DIRECTIVES.md to switch.
- Verification = tools/verify.bat (build + DevCapture). Bot and multiplayer tests are NOT run unless the user asks
  (standing user preference); capture screenshots + report.json are the check.
- Subagents: one per item, sonnet model for S/M items, briefed with card line ranges. They must not run verify or git
  (the lead does that once per cycle, so parallel builds do not collide).

## How a cycle runs (short)
1 stop check; 2 DIRECTIVES, STATE, INDEX, top of BACKLOG/LOG, tools/reindex.ps1; 3 capture (full sweep every 5th
cycle, else relevant views + 2 random) and contact sheet via tools/sheet.ps1; 4 pick 4-6 items, 3+ categories, 2+ visible;
5 subagents in parallel; 6 verify, compare previous vs current; 7 commit + push; 8 update web; 9 every 5th: review here.
