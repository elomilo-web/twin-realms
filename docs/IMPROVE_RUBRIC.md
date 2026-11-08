# What "better" means (the rubric)

Score each area 1-10 with evidence (name the screenshot or metric). Be harsh and specific; a 10 would be a shipped AAA game.

Inspirations to borrow from (ideas only, no copied art or names):
- **Sword Art Online**: named sword skills with glowing trails, slim segmented HP bars, floating damage numbers, clean glassy menus, floors and boss rooms with a door and a fanfare, a quest log with a tracked objective.
- **Zelda (Breath of the Wild)**: target lock-on, stamina wheel, readable silhouettes, weather and wind, a world that rewards looking around, shrines and fast travel.
- **Skyrim**: compass with markers, journal, perk tree, loot you can compare, villages with people going about their day, dungeons with a boss and a chest at the end.
- **Two Worlds 2**: freedom to mix magic and weapons, crafting and upgrading gear, a living open world.
- **World of Warcraft**: action bar, item tooltips with rarity and comparisons, quest givers with markers, zones with names and levels.
- **League of Legends**: instant readable spell effects, cooldown clarity, hit confirmation, strong spell icons.

## Areas
1. **Combat feel**: hit-stop, camera shake, impact sparks, enemy hit reactions, telegraphs, readable timing, combos, dodge and block reward.
2. **Hit boxes**: attacks hit what they visibly touch; hurtboxes match the visuals; reach feels fair (see hitboxes in metrics.json).
3. **Spells and skills**: each spell looks and sounds different, has a cast pose, a trail, an impact, and a clear cooldown. Skills flow into each other.
4. **Animation**: no stiff poses; idle breathing, walk and run cycles, landing, turning, sprint lean, enemy attack wind-ups, death.
5. **Icons**: every spell, item type, potion and status has a clear icon made in code (vector drawing); rarity is readable at a glance.
6. **Inventory and menus**: grid inventory with tooltips and comparison arrows, paper-doll equipment, sort and filter, consistent style, nothing off-screen or clipped, readable font sizes.
7. **HUD**: only what is needed, clear, stylish, never covers the action; damage numbers, buff icons, boss bar, quest tracker.
8. **World look**: lighting, color, fog, shadows, water, sky, clouds, vegetation motion, places that look different from each other, night and Shadow realm mood.
9. **World life**: NPC routines, animals, birds, fireflies, weather, footsteps and dust, ambient sound, things to discover.
10. **Game systems**: progression, crafting and upgrades, quests with variety, bounties, achievements, fast travel, difficulty curve, saves.
11. **Performance**: fps at or above 60 (fps and drawcalls in metrics.json), no stutter.
12. **Stability**: tools/bot.sh passes every time.

## Hard rules
- No outside art, sound or model files. Everything is made in code (shapes, shaders, particles, synthesized sound, vector icons).
- Stats and content live in data/*.json, never hard-coded.
- Never break saves: add new fields with defaults.
- One focused change per iteration. Keep it small enough to review.
