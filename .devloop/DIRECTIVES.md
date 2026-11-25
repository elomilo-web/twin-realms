# My directives (highest priority, read every cycle)

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
