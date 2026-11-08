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
