# RESUME (updated 2026-11-26)

## Where things stand
- Work list: .devloop/FOCUS.txt. Done: F-01..F-27 (Phases 0-5 and F-27), F-34, F-35.
- Next: F-28 (NPC memory and reputation), then F-29..F-33, F-36..F-45.
- Ask the user before multiplayer tests (F-43/F-44). No bot / mp runs unless asked.

## How to resume
1. `git status`; `dotnet build`.
2. First [ ] item in FOCUS.txt. Verify DONE WHEN with captures (`tools\capture.ps1 <views>`), mark [x] + NOTE,
   commit "focus F-XX: <title>", push.

## Notes for Phase 6
- NPC data: data/npcs.json (NpcDb.Get(id), NpcRec.Idx is a stable index). Nodes: NpcSchedule.NodeOf(id).
- Dev commands: `/npcs [town|road|wild]`, `/npc <id> [goto]` (goto's camera faces away from the NPC: fix when next touched).
- Capture views npc_* in .devloop/viewpoints.json.

## Open notes
- Capture tool shares the user's settings file: isolate it before changing settings in captures.
- Backlog extras: shadow-realm pink patches on sky islands, arena interior dim, leather cap grey under epic rim,
  unique mob drops + crafting expansion.
