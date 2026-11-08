# Port status (browser prototype to Godot C#)

## Current focus
Step 0: project setup and a walking character on flat ground.

## Checklist
- [ ] 0. Godot project created, repo builds, `tools/smoke_test.sh` exits 0
- [ ] 1. Player controller, free third-person camera, jump, roll, sprint
- [ ] 2. Combat: sword combo, block, bow, wand, staff, spells, stamina and mana
- [ ] 3. Enemy scene and AI (idle, aggro, attack, ranged, flee, leash), enemy table loaded from `data/enemies.json`
- [ ] 4. Loot, inventory, equipment, shops, blacksmith (`data/items.json`)
- [ ] 5. Stats, XP, levels, attributes
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
