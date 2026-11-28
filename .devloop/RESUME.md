# RESUME (updated 2026-11-26)

## Where things stand (2026-11-27)
- FOCUS.txt: every item is coded. F-01..F-32, F-34, F-35 were verified in game ([x]). F-33, F-36..F-44 are [~]: coded and compiling but NOT run in game
  (the owner said: write the code, do not run Godot or bots; he play tests). F-45 (full playthrough) is the owner's.
- AFTER_FOCUS.txt (the owner's second list, A-01..A-33): every item is coded and marked [x] with a note, NONE run in game.
- Next: wait for the owner's play-test findings and fix what he reports. Each note lists what to look at.
- Open follow-ups named in the notes: no bear / bird models for forms, thrown weapons fly as arrows, no left-handed swing clips,
  no dance clip or tavern music track, hoe tool, seeds in chests, snakes / scorpions / swimming animals, the two-client tests.
- No bot / mp / capture / Godot runs unless the owner asks. New music .wav files need one editor open to import. Net protocol is 16.

- 2026-11-27 later: third list A-34..A-38 in AFTER_FOCUS.txt (faces / hair, Elden Ring weapons, breakable props with online respawn,
  world filling, Souls-like combat agent). Owner rule: near 90% usage -> pause, commit + push, update this file, publish a release
  with the next version (tools/run.ps1 publish, $env:GODOT from .devloop/godot_path.txt).

## How to resume
1. `git status`; `dotnet build`.
2. First [ ] item in FOCUS.txt. Verify DONE WHEN with captures (`tools\capture.ps1 <views>`), mark [x] + NOTE,
   commit "focus F-XX: <title>", push.

## Notes for Phase 6
- NPC data: data/npcs.json (NpcDb.Get(id), NpcRec.Idx is a stable index). Nodes: NpcSchedule.NodeOf(id).
- Dev commands: `/npcs [town|road|wild]`, `/npc <id> [goto]` (goto's camera faces away from the NPC: fix when next touched).
- Capture views npc_* and rep_* in .devloop/viewpoints.json; actions "ui:talk:<id>[/cmd;cmd]", "ui:npcshop:<id>[/cmd;cmd]".
- Social: NpcSocial (chats, greetings, weather / time lines), bubbles via Npc.Say(text); dev command `/social [chat|stage|greet|log]`.
  `social stage <kind>` stands two NPCs in front of the camera for pictures. Capture views social_*.
- Story quests: Story.cs + data/story.json (format in its "_help"); dev command `/story [check|flags|resetall|<id> start|goto|pick|objs|late|done|fail]`.
  World consequences are flags "npc:<id>:gone|schedule|line" (Story.NpcGone / NpcDays / NpcLine). Capture views story_*;
  actions "ui:journal:<tab>[/cmds]", "ui:storytalk:<npc>[/cmds]".
- Crime: Crime.cs (Struck, Pickpocket, Alert, Pay, Resist, Run, Bounty, Essential); dev command `/crime`; capture views crime_*.
- Memory / standing: NpcMemory (Of, Greet, Gift, Insult, QuestDone, Trade, Crime, BuyMul, SellMul, Refuses); dev command `/rep`.

## Open notes
- Capture tool shares the user's settings file: isolate it before changing settings in captures.
- Backlog extras: shadow-realm pink patches on sky islands, arena interior dim, leather cap grey under epic rim,
  unique mob drops + crafting expansion.
