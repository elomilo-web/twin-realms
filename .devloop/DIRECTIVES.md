# My directives (highest priority, read every cycle)

## 2026-11-25 FOCUS LIST (governs all work from now on)
The user's work list .devloop/FOCUS.txt (copy of Downloads\TwinRealms_FOCUS.txt) replaces the free-running loop:
work through it top to bottom (2-4 items of one phase together if they touch different files), check every DONE WHEN
line in game (captures), mark [x] with a NOTE (date, what, files) or [!] BLOCKED, one commit per item
"focus F-XX: <title>". Art style per FOCUS.txt: anime fantasy, block-built world: anything placed in the world must
match the block style. (This supersedes the 2026-11-24 gothic-realism decision in PROCESS.md for NEW work.)
Older directives below still apply where they don't conflict.

<!-- Write your instructions below. The loop reads this file at the start of every cycle; your directives override everything else. -->
<!-- To stop the loop: create the file .devloop/STOP (or type "stop"). -->

- 2026-11-25 (user, in chat): "ensure you are optimising all menus as well". Every menu and window must get the gothic
  treatment and a capture view, not only the HUD/pause/inventory/spellcraft/character ones. Menu coverage is tracked in
  BACKLOG ("[menus] coverage" line); keep at least one menu item in every cycle until all are done.
- 2026-11-25 (user, in chat, cycle 19+):
  1. Spend more time on PLAYER MODELS, ARMOR and WEAPONS: add more of each, make sure every armor/weapon fits the player
     models (no clipping/floating). Zoom in close-up on the player and study how to make the model better.
  2. BUG: wand and staff attacks don't shoot projectiles. Fix it.
  3. Dungeons must be brighter.
  4. More scrutiny before fixes: don't re-fix the same thing cycle after cycle. Observe first, find the root cause, decide
     the best possible fix, then change it once. Check LOG for earlier attempts at the same thing before starting.
  5. Re-check ALL menus: they get cut off and shift around when the inventory has more items. Make the inventory layout
     compact and modern (fixed slot grid that scrolls instead of growing; nothing moves when items are added).
- 2026-11-25 (user, in chat): native controller support for Xbox and PlayStation controllers; a gamepad keybind section
  in the Settings menu; make the game work well when played on a gamepad (camera, combat, menus navigable with the pad,
  button prompts/glyphs that match the controller).
- 2026-11-25 (user, in chat): "create loading screens where needed".
- 2026-11-25 (user, in chat): "add a pick axe and ability to place blocks and build things like minecraft".
- 2026-11-25 (user, in chat): monster catching with cubes (Pokemon-like: original design, no Pokemon names/art), a pets
  menu to manage pets, level them, give them gear, have them follow or mount them, pet aggro settings and an auto-loot
  setting, and an arena mode where pets battle turn-based using their skills. Design: .devloop/systems/pets.md.
- 2026-11-25 (user, in chat): overworld mobs: better grouping (packs), REMOVE level matching to the player, create level
  zones (higher zone level = more xp, more gold, better drops), higher overworld spawn rates, shorter aggro distance,
  mobs stop chasing when too far from their spawn (leash).
- 2026-11-25 (user, in chat): give mobs UNIQUE drops (per species materials/trophies/items) and REALLY expand crafting.
  Custom spells and skills system needs 3 slots, not two; make that layout easier to use.
- 2026-11-25 (user, in chat): menus get cut off as more skills/descriptions fill them: RULE: menus can never stretch.
  Implemented in Ui.Frame (#20): body in a ScrollContainer, window fixed at its requested size clamped to the screen.
  Every new/changed window must keep this (no window builds its own unbounded panel); check with the *_full views.
- 2026-11-25 (user, in chat): review the user's MANUAL SCREENSHOTS (taken in game with T) and fix everything seen: models,
  wings, characters standing through the ground, rework structures if needed. Controller support does not work in most
  menus. Helmets still don't fit the player models. Islands not rendering. Recalibrate for frame rate: lower and compress
  anything to make the game run better. Swinging with no weapon must be an unarmed strike (no sword sound/anim). Animation
  frame rates are not in sync with the world. Dungeon scaling is way too extreme. Make Scarletta's lair shorter.
