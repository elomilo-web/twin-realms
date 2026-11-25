# Dev loop log (newest first; max 20 detailed entries, older ones rolled into History)

## #2 (2026-11-24) menus, combat, animation, audio, lighting, perf
- Title: new scripts/TitleBackdrop.cs (dusk ProceduralSky, gothic skyline silhouettes, embers, drifting camera), Main.cs adds it as "TitleEnv". NOT yet visible (opaque title panel; see BACKLOG).
- Perfect dodge: hit within 0.18 s of roll start -> 0.35x slow-mo 0.35 s (off in multiplayer), flash, "Perfect dodge!", 1.2 s x1.5 counter, 2 s cooldown (Player.cs PerfectDodge/TakeHit, Feel.SlowMo/Tick, tuning.json game.perfectDodge).
- Animation: Avatar.cs HeadLook SkeletonModifier3D: head 60/neck 30/chest 10 toward lock target / nearest foe / camera, fades during one-shots; spine roll into turns.
- Audio: footsteps by block under the foot (blocks.json "step", Blocks.BlockDef.Step, Sfx.BlockStep): grass/dirt/stone/wood/sand/snow/gravel/ice.
- Dungeon: entry room always gets 3 lit torches, lit torches capped 20/floor, warm (1,.55,.25) r9 e2; cold ambient fill (Dungeon.Generate L117; World resets ambient per frame after exit).
- Perf: hay/campfire stones no shadow + range. Sky-island grass range+Self fade REVERTED (foliage 35->29 fps: Self fade = transparent pass).
- Water "bug" was a bad viewpoint (camera under the lake surface); diagnostics in DevCapture.
- Fix by lead: CPUParticles3D -> CpuParticles3D (C# name).
- Scores: dungeons visuals 6->6.5, combat feel 6->6.5, audio feel 5->6, animation feel 5->5.5. FPS avg 70.7 (unchanged; foliage 34.6).

## #1 (2026-11-24) lighting, graphics, ui, vfx, world, code
- Fog: look.json fogDay .0028->.0009, skyAffect .6->.2, volFog .0042->.0018, horizon beige->blue-grey, new fogHeight 30 / fogHeightDensity .0035 (World.cs ~193), day fog light 40% cooler (World.cs ~829). Vista now readable: island, sea, roads, far lands.
- White beam = Lands.cs Updraft column (~831): alpha .07->.03, fades out beyond 140 m.
- Blocks: per-block colour jitter, grass/forest_floor side lip band, darker side bodies, stronger AO (BlockMesher.cs; blocks.json "lip").
- HUD: framed gothic bars with dark inset, top highlight, damage ghost (0.5 s hold, drain) on HP/MP/ST; low-HP (<30%) dark-red pulse (Hud.cs SlantBar/GhostStep).
- Dungeon exit: rune circle (torus + 12 ticks + inner sigil, counter-rotating, motes, pulsing light) replaces the flat ellipse (Dungeon.cs MakePad, Pad._Process).
- Code: 10 -> 1 compiler warnings. DevCapture: arena left before dungeon; hero keeps starting kit.
- BLOCKED/partial: Frostmere water still dry (rounding fix kept, harmless; root cause unknown, see BACKLOG top).
- Scores: world visuals 4->5, ui visuals 5->6, dungeons visuals 5->6, code 6.1->6.2. FPS avg 70.7 -> 70.6 (unchanged).

## #0 setup (2026-11-24)
- Built the info web: INDEX, 18 system cards, files.json (147 files), BACKLOG (45 items), PROCESS, STATE, DIRECTIVES.
- Eyes: scripts/dev/DevCapture.cs autoload (--devcapture), 16 viewpoints, tools/capture|verify .bat/.ps1, sheet.ps1, reindex.ps1.
- First capture: 16/16 views, avg 70.7 fps; baseline scores in STATE.json.
- Decision: art direction stays dark gothic realism + block world (see PROCESS.md), not anime cel-shading.

## History
(none yet)
