# Improvement log

Newest at the bottom. The /improve command reads the last entries first and appends a new one every iteration, so each run continues where the last one stopped.

## Iteration 0 (baseline, written by the cloud chat)
- State: full port exists; HUD and menus restyled to match the prototype; arms added; loot freeze fixed.
- Known weak spots (never measured yet): placeholder box models, stiff animation, plain inventory list, no icons, no hit feedback beyond damage, unverified hit boxes.
- Scores: not yet scored. First run of /improve should score every rubric area.

## Baseline scores (2026-11-08, first /improve run)
Bot 42/42 pass. Observer at 1152x648.
| # | Area | Score | Evidence |
|---|------|------:|----------|
| 1 | Combat feel | 3 | Camera shake, damage numbers and hit bursts exist (Player.cs, Enemy.cs). No hit-stop, no swing trail, no enemy flash or recoil; anim_light_attack shows a thin stick swing with no impact read. |
| 2 | Hit boxes | 5 | metrics hitboxes: humanoids match (r .45 vs w .9); wraith hurtbox 1.8 tall vs visual 2.6; melee is a distance and cone check. |
| 3 | Spells and skills | 3 | anim_spell_fire: small orange ball, a faint ring on impact; no cast pose, trail or explosion read. |
| 4 | Animation | 2 | anim_walk_sprint: body glides and leans, no visible leg swing; idle static. |
| 5 | Icons | 1 | No icons anywhere; hotbar is text only (h01_hud). |
| 6 | Inventory and menus | 3 | m_pack is a plain text list with Equip/Drop buttons; m_tree and m_map panels are taller than the 648 px window (title and Close cut off, ui_issues). |
| 7 | HUD | 3 | h01_hud: quest tracker drawn off the right edge (only a stray "W" visible); compass mirrored (E on the left) and "NW" shows as "N"; hotbar names spill out of their slots. |
| 8 | World look | 4 | w01/w05: clean low-poly with fog and shadows, but flat colors, no vegetation motion, no clouds; w09 Shadow is one flat purple. |
| 9 | World life | 2 | NPCs stand still; no animals, birds or weather in any w* shot. |
| 10 | Game systems | 5 | Quests, skill tree, shop, smith, arena, 3 dungeons, fast travel, saves all work (bot). No crafting, bounties, achievements. |
| 11 | Performance | 9 | 125-144 fps in every shot; 370-1790 draw calls. |
| 12 | Stability | 9 | bot.sh 42/42, no engine errors. |

## Iteration 1 (2026-11-08): HUD and menu layout fixes
- Changed: `Hud.cs` quest tracker now draws inside a right-aligned box (it was drawn from the right edge outward, so off-screen); compass sign fixed (was mirrored) and labels get a 40 px box so "NW" is not cut; hotbar names are drawn from the slot's left edge and wrap to two lines or shrink to fit (`SlotName`). `Ui.cs` panels are clamped to the window; skill tree and map size themselves to the window height; tree zoom fits its canvas. `Observer.cs` no longer flags rows clipped by any ScrollContainer ancestor (false positives in the pack).
- Evidence: observer ui_issues 33 -> 0. h01_hud now shows "Wolves at the Gate" with its goal top-right, compass W NW N NE in order, all six hotbar names inside their slots. m_tree and m_map show title and Close button inside the window. hud fps 144 -> 144, draw calls 1394 -> 1398. Bot 42/42.
- Scores: HUD 3 -> 5, Inventory and menus 3 -> 4.
- Next candidates: (1) hit-stop plus enemy hit flash and recoil (combat feel 3), (2) procedural walk cycle with leg and arm swing (animation 2), (3) vector icons for hotbar spells and potions (icons 1).

## Iteration 2 (2026-11-08): hit-stop and enemy recoil
- Changed: new `scripts/Feel.cs` + `data/feel.json`. Hits the player lands freeze the game briefly (light 35 ms, combo 55, power 85, spells 25; +30 for a crit, +30 for a kill; capped at 120 ms; time scale 0.05). Taking a hit freezes 50 ms. Enemies now jolt away from the blow and squash (scaled by knockback; bosses and the dragon at 40%), then spring back over 0.2 s. `HitInfo.Kind` tells light/combo/power apart. Ui (always processing) ends the freeze in real time so it can never get stuck. Observer gained an `anim_hit_enemy` strip (power attack on a stunned brute).
- Evidence: anim_hit_enemy frames 13-14 show the brute white, squashed flat and wide, then knocked back with the number; before this change there was only a 0.12 s flash. The strip is captured in game time, so the freeze itself does not add frames. Bot 42/42 (arena waves and duel still finish, so time scale always recovers). fps 126-144 unchanged; draw calls unchanged in the world shots.
- Scores: Combat feel 3 -> 4.
- Next candidates: (1) procedural walk cycle with leg and arm swing (animation 2), (2) vector icons for hotbar spells and potions (icons 1), (3) death dissolve and stagger pose for enemies (combat feel).

## Iteration 3 (2026-11-08): procedural walk and run cycle
- Changed: `Player.cs` legs now hang from hip pivots and swing with a stride phase driven by ground speed (so feet keep pace with distance); arms swing against the legs when the hands are free; body bobs twice per stride and leans forward with speed; idle breathing lifts body and hands; jump tucks one knee; mounted legs sit astride. Upper arms are re-aimed every frame from shoulder to hand/shield (`AimLimb`), which also fixes the upper arm staying behind when the shield is raised to block. Tuning in `data/feel.json` "walk".
- Evidence: anim_walk_sprint before vs after (zoomed comparison): before, legs locked together and the body glides; after, wide stride in frame 1, knee driving through in frame 3, forward lean while sprinting. anim_block shows the arm following the raised shield. anim_idle unchanged in pose (breathing is 1 cm, by design). fps 125-145 unchanged. Bot 42/42.
- Scores: Animation 2 -> 4.
- Next candidates: (1) vector icons for hotbar spells and potions (icons 1), (2) death dissolve and stagger pose for enemies (combat feel 4), (3) enemy humanoids reuse the hip-pivot legs (animation 4).

## Run 2 focus (user request, 2026-11-08)
Menus in a Sword Art Online / isekai "system window" style (translucent deep blue, cyan, techy sci-fi). Furnish the land: plants and wildlife, and most things interactable (collect, kill, loot, gather); small grass can be scenery only.

## Iteration 4 (2026-11-08): SAO-style system-window menus
- Changed: new `data/ui_theme.json` and `scripts/UiTheme.cs`. Every menu panel is now translucent deep-blue glass with a cyan glow edge, bright corner brackets, a header band with a fading cyan rule and a diamond marker, tick marks, uppercase titles, an "X" close button, and a 0.16 s pop-in. Behind menus, a faint cyan grid over the dimmed game. Buttons, line edits, scrollbars and sliders are restyled to match (cyan hover glow, bright pressed state). Notes and banners use the same palette.
- Evidence: m_pack, m_pause, m_npc_quest (before: flat dark purple boxes with gold text; after: blue glass windows with cyan frame and glow). Observer ui_issues 0 -> 0. hud fps 144 -> 145. Bot 42/42.
- Scores: Inventory and menus 4 -> 5 (style is there; layout is still a list, not a grid).
- Next candidates: (1) wind-swept grass, ferns, bushes (world look 4), (2) gatherable herbs, mushrooms, berries, ore (world life 2), (3) wildlife that flees or fights (world life 2).
