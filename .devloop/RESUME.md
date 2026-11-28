# RESUME (updated 2026-11-27)

## AnimeTool + animation pass (2026-11-27, run in game through the tool)
- New dev tool scripts/dev/AnimeTool.cs: white studio floating above the village with the REAL hero (Player, gear, trail, cape).
  Interactive: tools/run.ps1 animetool (` toggles tool/play mode; F1-F7 cameras; [ ] \ = speed/pause/step; buttons for every move,
  sparring foe, bones, blade trace). Batch: tools/run.ps1 animetool-shots [light5,roll,...] -> bot_out/animetool/<move>_<side|front>.png
  + report.txt (clip, blade turn per frame, hip/head height, arm lift, cape, position). --animetool-clips=A,B poses raw clips and scans them.
  --animetool-slow=1 for true per-frame numbers (slow-motion numbers are inflated by the attack time warp).
- Fixed: swing step piled up in _walkV (each light swing threw the hero ~7 m at 16 m/s, so Sprint/Jog clips played mid-combo);
  parry sound / riposte timer were inside a comment; jump arms (T-pose) lowered in the air (HeroPose.AirArms); roll is a procedural
  tucked roll over Crouch_Idle (HeroPose.TuckRoll; both library rolls are dive rolls); block holds its guard while held + recoils on a
  blocked blow; heavy wind-up starts during the press gather; light follow-through clips removed (they snapped the blade upright);
  swings faster than rates.strikeMax skip into their wind-up instead of playing at 2-2.5x; heavy knockdown gets up with LayToIdle
  (stagger knockDown+getUp, untouchable while rising); sword arm swings less in the run when armed; trail sampled at FramePreDraw and
  smoothed with Catmull-Rom; cape streams less. Tunables: data/tuning.json game.anim.*, poise.knockDown/getUp, life.armedSwingDamp.
- Re-bake clips before a release (new clips: Crouch_Idle as rollBase, LayToIdle as getUp): godot --headless --path . -- --bake-anims
- Not done: the cape is still one rigid piece (no cloth); attack clips themselves are the library's (fast swings ~60-115 deg/frame).

## Where things stand (2026-11-27)
- FOCUS.txt: every item is coded. F-01..F-32, F-34, F-35 were verified in game ([x]). F-33, F-36..F-44 are [~]: coded and compiling but NOT run in game
  (the owner said: write the code, do not run Godot or bots; he play tests). F-45 (full playthrough) is the owner's.
- AFTER_FOCUS.txt (the owner's second list, A-01..A-33): every item is coded and marked [x] with a note, NONE run in game.
- Next: wait for the owner's play-test findings and fix what he reports. Each note lists what to look at.
- Open follow-ups named in the notes: no bear / bird models for forms, thrown weapons fly as arrows, no left-handed swing clips,
  no dance clip or tavern music track, hoe tool, seeds in chests, snakes / scorpions / swimming animals, the two-client tests.
- No bot / mp / capture / Godot runs unless the owner asks. New music .wav files need one editor open to import. Net protocol is 16.

- 2026-11-27 later: third list A-34..A-38 all coded and pushed (not run in game). Before a release: open the editor once to import
  the new .glb/.png files, and re-bake the clip library (godot --headless --path . -- --bake-anims). Net protocol is 17.
- (was) third list A-34..A-38 in AFTER_FOCUS.txt (faces / hair, Elden Ring weapons, breakable props with online respawn,
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
