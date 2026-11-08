# Port status (browser prototype to Godot C#)

## Current focus
Step 0 runs (confirmed by user). Step 2 combat (sword, block/parry, stamina, melee enemies from JSON, HUD) written but UNTESTED. Next: run it, fix errors, tune feel, then bow/wand/staff, spells, loot.

## Checklist
- [x] 0. Project builds and runs (user confirmed). Main, Player, CameraRig, Inputs
- [~] 1. Player controller, free third-person camera, jump, roll, sprint (basic version in step 0 code; needs animation, stamina, tuning)
- [~] 2. Combat: sword combo (3 hits + power attack), block and parry, stamina: written, untested. Still to do: bow, wand, staff, spells, mana
- [~] 3. Enemy AI: melee Idle/Chase/Wind/Recover/Stagger/Dead from `data/enemies.json`: written, untested. Still to do: ranged (archer), wander/leash home, boss specials
- [ ] 4. Loot, inventory, equipment, shops, blacksmith (`data/items.json`)
- [~] 5. Stats, XP, levels: PlayerStats written (no attribute-spending UI yet)
- [ ] 6. Skill tree: data load, allocate and deallocate rules, aggregated stats, UI
- [ ] 7. Terrain (Terrain3D or heightmap), village, roads, vegetation
- [ ] 8. Light/Shadow realm shift: materials, lighting, enemy sets
- [ ] 9. Quests, NPCs, dialogue, journal
- [ ] 10. Dungeons: procedural generation, three themes, bosses, descent
- [ ] 11. Crimson Arena: wave and duel modes
- [ ] 12. Dragon boss Vorthrax
- [ ] 13. Horse, map, fast travel
- [ ] 14. Save and load
- [ ] 15. Audio (music, ambience, sfx, volume settings)
- [ ] 16. Art pass: real models, PBR, lighting, post-processing

## Known gaps in the prototype (fix in the port, do not copy)
- Visuals were procedural boxes and spheres. Replace with real assets.
- Enemy streaming was by distance only. Use proper scene instancing.
- Skill tree layout was force-relaxed at runtime. Bake the positions from `data/skilltree.json` instead.

## Session log
(add a dated line at the end of each session: what was done, what was verified, what is next)
- 2026-11-08: Claude (cloud chat) connection test, push works.
- 2026-11-08: Wrote Main/Player/CameraRig/Inputs for step 0 from the cloud chat. NOT compiled or run. Controls: WASD, Space jump, Shift sprint, C roll, mouse look, wheel zoom, Esc frees mouse.
- 2026-11-08: Added Combat.cs (HitInfo, IDamageable, FloatText), Stats.cs, EnemyDb.cs, Enemy.cs, Hud.cs; rewrote Player.cs with combat; CameraRig shake. 9 test enemies in Main. NOT compiled or run.
