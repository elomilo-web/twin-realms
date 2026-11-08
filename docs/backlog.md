# Backlog (ideas, highest value first)

Format: `- [ ] (impact H/M/L, effort S/M/L) title: why / how`. Mark `[x]` when done and `[!]` if tried and failed (with the reason in the log). /improve adds new items it discovers.

## Combat feel
- [x] (H,S) Hit-stop and camera shake on hit: freeze 40-80 ms on impact, bigger for crits and power attacks.
- [ ] (H,M) Impact sparks and slash trails: a glowing arc mesh that follows the sword during swings (SAO-like skill trails).
- [ ] (H,M) Enemy telegraphs and hit reactions: wind-up flash, recoil, stagger pose, death dissolve instead of vanishing. (recoil and squash done in iter 2; stagger pose and death dissolve still open)
- [ ] (H,M) Target lock-on (Zelda style): a key to lock the nearest enemy, camera and strafing follow, target ring.
- [ ] (M,M) Named sword skills with a short chain (SAO): e.g. 3 skills unlocked via the skill tree, each with a distinct move.
- [ ] (M,S) Perfect dodge: roll just before a hit gives slow motion and a free counter.
## Hit boxes
- [ ] (H,M) Replace point/distance melee checks with a swept arc or capsule that follows the weapon; debug draw toggle (F3).
- [ ] (M,S) Align every enemy hurtbox with its visual using the hitboxes section of observe_out/metrics.json.
## Spells and skills
- [ ] (H,M) Distinct spell VFX: fire trail and explosion, ice shards and frost ring, healing pillar, chain lightning arcs; cast poses.
- [ ] (M,M) Spell upgrades/runes and a fifth and sixth spell slot (data-driven in classes.json).
## Animation
- [ ] (H,M) Procedural walk/run cycle: swinging legs and arms, torso lean, bobbing; idle breathing; landing squash; head turns toward targets.
- [ ] (M,M) Enemy walk and attack animations per kind (quad, humanoid, wraith float, dragon wings).
## Icons
- [ ] (H,M) Vector icon library drawn in code: sword, bow, wand, staff, shield, armor, trinket, potions, and one per spell; rarity frames with glow.
## Inventory and menus
- [ ] (H,L) Grid inventory with drag and drop, paper-doll equipment slots, hover tooltip with stat comparison arrows (green up, red down), sort and filter, item lock.
- [ ] (M,M) Character sheet with stat bars and a rotating 3D preview of the hero.
- [ ] (M,S) Quest log tabs (active, done) with tracked-quest toggle and map markers.
## HUD
- [x] (H,S) Layout bugs: quest tracker drawn off-screen (right-aligned with no width), compass mirrored and "NW" clipped, hotbar names offset by half a slot, skill tree and map panels taller than a 648 px window.
- [ ] (L,S) Dungeon title banner lingers faintly in the sky after leaving (w09_shadow_village shows "Hollow Barrow").
- [ ] (M,S) Camera can sit behind lamp posts and hide the hero (h01_hud): fade or ignore thin props in the spring arm.
- [ ] (M,S) Floating damage numbers by type (crit big and gold, heal green, taken red); buff and debuff icons with timers.
- [ ] (M,M) Quest markers on the compass and minimap.
## World look
- [ ] (H,M) Grass and tree sway shader, clouds with moving shadows, god rays at dusk, better water with foam.
- [ ] (M,M) Weather: rain and fog banks that change with time; puddle shine.
- [ ] (M,S) Color grading per realm and time of day; vignette when hurt.
## World life
- [ ] (H,M) NPC daily routines: walk between home, market and forge; sleep at night; idle animations.
- [ ] (M,S) Birds, butterflies by day, fireflies at night; footstep dust and sound by surface.
- [ ] (M,M) Roaming animals (deer, rabbits) that flee.
## Systems
- [ ] (H,M) Crafting and enchanting at the smith: break items into materials, add affixes.
- [ ] (M,M) Bounty board with repeatable kill, fetch and explore jobs.
- [ ] (M,M) Achievements and a statistics page.
- [ ] (M,M) Fast-travel shrines to discover; stamina-based climbing and gliding-free exploration rewards (hidden chests with puzzles).
- [ ] (M,L) Companion or horse upgrades; mounted combat.
## Audio
- [ ] (M,M) Footsteps by surface, combat music layer that fades in when enemies are near, per-spell sounds.
