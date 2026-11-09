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

## Iteration 5 (2026-11-08): wind-swept ground cover
- Changed: new `scripts/Flora.cs` + `data/flora.json`. The island is split into 32 m chunks; each chunk scatters grass clumps (9 blades, 5 greens), ferns (forests only, same noise as the trees), bushes (cast shadows) and pebbles, skipping roads, cliffs, the plaza and the arena floor. Each layer is one MultiMesh per chunk with a draw distance (grass 56 m, ferns 85, bushes 160) and fade. Grass, ferns and bushes share a sway shader (gusting wind, stronger at the tips, backlit); in Shadow they turn violet with glowing teal tips. Scenery only, no collisions.
- Evidence: w06_forest before (bare green ground) vs after (meadow with tufts, ferns, bushes, pebbles); w01_village_day shows ferns and grass around the houses; w10_shadow_wilds shows teal-tipped grass and glowing bushes. fps 125-144 before, 126-144 after; draw calls about +100 (1787 -> 1898 in w01). Bot 42/42.
- Scores: World look 4 -> 5.
- Next candidates: (1) gatherable herbs, mushrooms, berries, ore, crystals with a materials bag (world life 2, systems 5), (2) wildlife that flees or fights (world life 2), (3) HUD restyle to match the menus.

## Iteration 6 (2026-11-08): gatherables and a materials bag
- Changed: new `scripts/Gather.cs` + `data/gather.json`. 315 nodes scattered by terrain: Sunleaf herbs (meadows, Light), glowing Moonpetal (meadows, Shadow only, soft violet light), Redcap mushroom patches (forests), berry bushes (meadows), iron ore veins (rocky slopes, solid), Rift Crystals (mountains, glowing, solid), fallen logs (forests). Press E: "Gather / Pick / Mine / Harvest / Chop", burst in the material's colour, floating "+2 Sunleaf", note with the new total; the node shrinks away and regrows after 2.5-5 min with a pop. `PlayerStats.Mats` (new, defaults to empty so old saves load). Pack shows a Materials list; the merchant's Sell tab buys materials per stack or all at once (values in gather.json). Each node's parts are merged into one mesh per material (1-3 draw calls a node).
- Evidence: bot 42 -> 45 checks, all pass ("world has gatherables (315 nodes)", "gathering adds materials (Pick Redcap Mushroom: 0 -> 2)", "gathered node waits to regrow"). w12_ore shows veined rocks; w11_gatherables a red-berried bush; m_pack the Materials section. Draw calls w01 1898 -> 1970 (+4%; an unmerged first try was 2223 and was fixed in this iteration); fps 124-144.
- Scores: World life 2 -> 3, Game systems 5 -> 6.
- Next candidates: (1) wildlife: deer and rabbits that flee, boars that fight, hide and meat drops (world life 3), (2) HUD restyle to match the menus (HUD 5), (3) crafting at the smith using materials (systems 6).

## Iteration 7 (2026-11-08): wildlife
- Changed: new `data/wildlife.json` + `scripts/Wildlife.cs` place herds: deer and rabbits (meadows), wild boars (forests), Shade Stags (Shadow realm, glowing). They are Enemies with new data fields in enemies.json: `look` (deer antlers and long legs and white tail, rabbit ears and puff tail, boar tusks, snout and bristle ridge), `flee` (graze around home, bolt away when you come close or hit them, come back later), `noLoot`, `emissive`, `drops` (Hide, Raw Meat, Rift Crystal into the materials bag, sellable). Boars graze but fight back if you get close or hit them. All four-legged creatures, wolves and hounds included, now have legs on hip pivots with a diagonal gait that follows their speed. Enemy body parts are merged per material; wildlife is hidden beyond 100 m. Quad hurtboxes include the extra leg height.
- Mistake caught: the first version named the shadow flag "glow", which clashed with the existing glow colour on the Shade Hound and broke its loading (engine error in bot_log). Renamed to "emissive"; the error is gone.
- Evidence: bot 45 -> 48 checks, all pass ("wildlife flees from the player (Rabbit: 17.4 m away after 1.5 s)", "hunting gives hides or meat"). w13_deer shows a grazing herd; w14_animals a lineup (rabbit, deer, boar, legged wolf, Shade Stag). Draw calls w01 1970 -> 2093 (+6%; an unmerged first try was 2487 and was fixed); fps 123-144.
- Scores: World life 3 -> 5, Animation 4 -> 5 (quads walk).
- Next candidates: (1) HUD restyle to match the menus (HUD 5), (2) crafting: potions from herbs, smith upgrades costing ore and crystals (systems 6), (3) quad hurtboxes as a capsule along the body (hit boxes; wolf, hound, boar, deer flagged as narrower than the visual).

## Iteration 8 (2026-11-08): HUD in the system-window style
- Changed: `Hud.cs`. A glass status window top-left (name, level, slanted segmented HP bar that turns green > yellow > red, HP numbers, MP and stamina bars, XP line along the bottom edge), corner brackets like the menus. Realm, clock, place and gold moved under the compass; arena status under that. Compass in glass with cyan rules, gold N and a cyan pointer. Quest tracker with a diamond marker. Target bar slanted (green for wildlife, red for foes). Interaction prompt is a glass pill with a cyan [E] key chip. Hotbar slots in glass; the mana-cost numbers now sit inside the slot (they straddled the edge). Minimap ring cyan with side arcs.
- Investigated: moving the notes column (Ui `_notes.Position`) to (16,150) made every bot run print "RID allocations ... leaked at exit" (4 of 4 runs); with it left at (22,120) runs are clean (4 of 4), and the committed iteration 7 was clean (2 of 2). Bisected by swapping files. Cause not understood (an exit-time teardown race, see PORT.md); the notes stay at (22,120), which already sits below the new status window. Added to the backlog.
- Evidence: h01_hud before (white text, plain red/blue/green bars at the bottom) vs after (glass status window, info line under the compass, cyan hotbar, minimap ring); bot_out/16_arena_waves shows the arena counter in place. Observer ui_issues 0. hud fps 144, draw calls 1680 (1680 before the HUD change). Bot 48/48 twice, no engine errors.
- Scores: HUD 5 -> 7.

## Re-score (every 5th iteration; due at iteration 5 and missed then, done now)
| # | Area | Score | Evidence |
|---|------|------:|----------|
| 1 | Combat feel | 4 | Hit-stop, recoil and squash (iter 2), shake, numbers. Still no swing trails, stagger pose or death dissolve. |
| 2 | Hit boxes | 5 | Humanoids match. Quads (wolf, hound, boar, deer) flagged: round capsule narrower than the long body. |
| 3 | Spells and skills | 3 | Unchanged: small fireball, faint rings. |
| 4 | Animation | 5 | Player walk cycle (iter 3); all quads walk on legs (iter 7). Humanoid enemies still glide. |
| 5 | Icons | 1 | Still text only. |
| 6 | Inventory and menus | 5 | SAO system-window look everywhere (iter 4), materials list; still a text list, not a grid with tooltips. |
| 7 | HUD | 7 | Status window, slanted HP bar, glass hotbar, prompt pill (iter 8). |
| 8 | World look | 5 | Ground cover with wind (iter 5), glowing Shadow flora. Flat sky, no clouds, plain water. |
| 9 | World life | 5 | 315 gatherables, herds that graze and flee, boars that fight. No birds, insects, NPC routines. |
| 10 | Game systems | 6 | Materials economy (gather, hunt, sell). No crafting yet. |
| 11 | Performance | 9 | 123-144 fps everywhere; draw calls 1787 -> 2041 in w01 across 5 iterations. |
| 12 | Stability | 9 | Bot 48/48. One unexplained exit-time leak found and avoided (see iteration 8). |

Fresh ideas (added to the backlog): (1) crafting bench: potions from Sunleaf/Moonpetal, smith upgrades that cost Iron Ore and Rift Crystal; (2) birds that lift off when you approach and butterflies/fireflies over meadows (scenery); (3) hunting quests and a bounty board using the new animals and materials; (4) a Codex/collection tab in the journal that fills as you gather and hunt each kind; (5) cooking meat and berries at campfires for a short regen buff; (6) SAO-style grid inventory with item icons drawn in code.
- Next candidates: (1) crafting bench using materials (systems 6), (2) vector icons for items, materials and spells (icons 1), (3) grid inventory with tooltips (menus 5).

## Iteration 9 (2026-11-08, /improve run 3 round 1): sky, light, materials, roofs, water, trees, villagers, icons
- Changed (10 things, 6 areas):
  - World look: new sky shader (World.cs) with sun disc and halo, drifting lit clouds, stars at night, dusk glow on the sun side, and a Shadow-realm nebula and rift. ACES tonemap, SSAO, contrast and saturation grading, lighter fog with aerial perspective; all tuning in new `data/look.json`.
  - Day/night: long twilight (day fades over sun elevation +0.25 to -0.12), dusk peaks at the horizon, brighter moonlit ambient. Shadow realm gets lower saturation and more ambient so it is not one flat indigo.
  - Materials: new `scripts/Gfx.cs` generates tileable textures and normal maps in code (plaster, stone courses, planks, wood grain, shingles, bark, rock, cobbles, cloth, sand). Applied triplanar to houses, foundations, beams, plaza, well, arena, rocks, trunks.
  - Geometry: real gable roofs (thickness, overhang, fascia, plaster gables, ridge beam) instead of solid prisms; chimney caps; jagged four-tier pines with per-tree tint; oaks from five faceted noise blobs; noise-displaced boulders, dungeon-mouth rocks, menhirs and rift obelisks (with a glowing rune seam); a lintel on two standing stones.
  - Water: shader with moving waves, fresnel, depth-tinted shallows and shore foam.
  - Characters: villagers now have legs, boots, tunic or robe, belt, arms with hands, head with hair, eyes and nose, role gear (merchant apron, smith apron and hammer, guard helmet, breastplate and spear, elder staff and beard) and idle breathing and sway.
  - HUD/Icons: new `scripts/Icons.cs` vector icon library (spells, potions, weapons, armor, trinkets, materials, rarity frame). Hotbar shows icons with a radial cooldown sweep and seconds left.
  - Readability: enemy and NPC name labels are depth-tested (no names through roofs or hills) and fade out past 26 m (bosses 90 m). Lamp posts are lanterns on arms and dither out when the camera is close; the spawn-side lamp was moved off the camera line.
  - UI bug: a new banner kills the previous fade tween; observer clears banners before scene shots (they used to linger in w11 and w17).
  - Observer: w12 and w17 cameras fixed (w17 now shows the sea); new shots w19_sunset_hills, w20_npc_brynn, w21_rooftops.
- Evidence: w01 before (flat pale boxes, blank sky) vs after (shingled roofs, stone foundations, cobbles, clouds, deeper colour); w02 before (black night) vs after (red dusk sky with stars, lit windows, readable ground); w17 before (no water) vs after (blue sea with waves); w13/w20 villagers with faces and gear; h01 hotbar icons. Fps 115-144 in all shots (was 117-145); draw calls w01 2093 -> 2421, hud 1680 -> 1893. Bot 48/48, no engine errors.
- Mistakes caught: the first sky hash (fract(p*k) products) made straight-edged cloud artefacts; replaced with a better hash plus rotated octaves. Rock cracks were too thick; thinned.
- Scores: World look 5 -> 7, World life 5 -> 6 (villagers), Icons 1 -> 4, HUD 7 -> 8.
- Next candidates: (1) hero and enemy bodies: rounded, tapered limbs and faces instead of boxes and capsules (wolves are box piles, w14); (2) sword swing trails and spell VFX (fire trail and explosion, frost ring shards, lightning arcs); (3) grid inventory with icons, rarity frames and tooltip comparisons; (4) Shadow realm still dark and flat-blue on the ground; menhirs need more shape; (5) dungeon door is a flat black plane: animated portal shader.

## Iteration 10 (2026-11-08, run 3 round 2): hero and enemy bodies, sword trails, spell VFX, portal
- Changed (9 things, 5 areas):
  - Hero (Player.cs): tapered chest and hips, belt and gold buckle, tabard, steel pauldrons with gold trim, a cape that streams back with speed and in rolls, thigh/knee/shin/boot legs, neck, head with hair, eyes, brows, ears and nose. Diamond-section sword blade (Gfx.Blade) with crossguard, wrapped grip and pommel; heater shield (Gfx.ExtrudeX) with steel rim, painted face, gold cross and boss.
  - Sword trail (new SwordTrail in scripts/Vfx.cs): an additive ribbon follows the blade in the cutting part of each swing; cyan for light hits, orange for the combo finisher, gold for power attacks.
  - Enemy humanoids (Enemy.BuildHumanoid): walking legs on hip pivots (they used to glide), tapered torso, arms reaching a weapon, heads with glowing eyes; goblins get ears and a long nose, skeletons ribs and dark sockets, bandits a hood and face mask, archers a hood, bow and quiver, brutes a belly, horns and spiked club, bosses a crown and cape. Swords use the new blade mesh.
  - Four-legged foes and wildlife: rounded capsule bodies with a deeper chest, a neck, round skull, tapered snout and black nose, wolf ears, bushy tail and dark fur ridge, tapered thigh/shin/paw legs (they were stacks of boxes).
  - Spell VFX (new scripts/Vfx.cs): fireball has a hot core, soft glow and ember trail; impact is a flash, expanding fireball, embers, smoke and a ground ring. Frost Nova bursts ice shards out of the ground in a ring with a frost flash. Healing Light raises a green-gold beam with a gold ring and rising motes. Chain Lightning draws jagged, flickering bolts with sparks at each target. Arrows have a shaft, steel head, fletching and a faint trail.
  - Dungeon doors: animated swirling portal shader instead of a black slab.
  - Observer: new close-ups w22 bandit, w23 goblin, w24 skeleton (Shadow), w25 hero front; new strips anim_spell_heal and anim_spell_zap (with two stunned goblins as targets).
- Evidence: w25_hero_front (face, pauldrons, shield, cape, boots); w14 wolves (snouts, ears, tails vs box piles); w22/w23/w24 (hooded bandits, eared goblin, ribbed skeleton); anim_combo_3 shows the trail arcs; anim_spell_nova shows the shard ring; anim_spell_heal the beam; anim_spell_zap jagged bolts between the goblins; w08 the swirling portal. Bot 48/48. Fps 113-145 (was 115-144); draw calls w01 2421 -> 2787, hud 1893 -> 2226 (the hero and villagers are not merged yet).
- Mistakes caught: first frost disc and heal column were too opaque and washed out the screen (heal turned the hero and walls green); thinned to a faint shell plus a narrow core. First wolf fur ridge was spikes that looked like a dinosaur; changed to rounded tufts. The portal stayed black because an opaque backing box sat in front of it from the camera side; removed.
- Scores: Combat feel 4 -> 5 (trails), Spells and skills 3 -> 6, Animation 5 -> 6 (humanoid foes walk), World look 7 -> 7.
- Next candidates: (1) grid inventory with icons, rarity frames and tooltip comparisons (menus 5); (2) merge hero/NPC parts per material to win back draw calls; (3) death dissolve and stagger pose for enemies; (4) birds, butterflies, fireflies; (5) journal and character sheet redesign.
