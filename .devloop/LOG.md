# Dev loop log (newest first; max 20 detailed entries, older ones rolled into History)

## #4 (2026-11-24) feel, animation, code, lighting, combat, graphics
- Feel.Impact (Feel.cs): hit-stop 45 ms light / 90 ms heavy, x0.8-1.4 by dmg/maxHp, crit x1.5, kill x1.6, cap 140 ms; CameraRig.Kick: directional punch + -2 deg FOV pulse on heavy/crit (respects Shake). Player.ResolveMelee calls it; Enemy.TakeHit's old hit-stop only for non-melee. Numbers: data/feel.json "impact".
- Landing: Player tracks peak height; falls >1.2 m -> Fx.Dust (surface-coloured), small shake, low thud (step_stone pitched down), longer crouch.
- Code: World.cs 958 -> 714 lines; environment/sky/water/look moved verbatim to WorldEnv.cs (partial).
- Night: lamp posts (1,.62,.32) r10 e2.2, distance fade 100-120 m; up to 4 door lanterns; windows Night*2.2 -> 3.4 (WorldEnv.cs).
- Enemies: wind-up glint at head height; slam/boss/dragon ground ring (lead changed the solid disc to a thin torus ring: the disc hid the ground); stagger on hits >=20% max HP or crit (bosses every 3rd); tuning.json game.monsters.stagger*.
- Kit: env.json lookup "<folder>/<material>" first (Kit.Shared folder-aware); TR_Matte darkening only for village/ and castle/; plaster f0e8dc, planks 6a5c52.
- Fixes by lead: OmniLight3D has no VisibilityRangeEnd (use DistanceFade*).
- FPS: village 56.6 -> 49.1 (-13%, more lights), night 60.9 -> 54.9 (-10%); within the 15% rule. Watch next cycle.
- Scores: combat feel 6.5->7, player feel 6->6.5, world code 6->7, enemies visuals 3->4.

## #3 (2026-11-24) world, menus, shaders, vfx, ui, mechanics
- Settlements: env.json "materials" maps kit TR_Tex_* (plaster, planks, thatch, 4 roof colours, stone/dungeon bricks, slabs) to G_* gothic sets; TR_Matte/Metal vertex colours tinted down; Kit.Shared honours "tint". Hollowfrost now dark gothic timber (a bit too dark in shade).
- Title: Frame() skips the full-screen dark layers for the title; panel 620 px, left, alpha .72, 44 px letter-spaced gold title; hero slots stacked; TitleBackdrop camera MakeCurrent + yawed. Skyline now visible.
- Sea shader (World.cs WaterShader inline): roughness .08 glints, fresnel toward sky_tint, 2nd rotated noise layer, calmer far water.
- Sword trail: new assets/shaders/sword_trail.gdshader (steel edge, smoky tail, element tint via vertex colour), tip sparks above 8 m/s (Vfx.cs SwordTrail).
- Inventory: ItemSlot.Inset framed wells, rarity frame + glow (rare+), hover ring.
- Mechanics: "torch" block (post shape, craft 1 planks + 1 coal -> 4), BlockWorld lights the 24 nearest with Props.Torch.
- Capture: "biome" lookup, new views settlement + terrace_close, wild foes within 30 m cleared before each view.
- Scores: world visuals 5->5.5, menus/ui visuals 6->6.5, skills_vfx visuals 4->4.5. FPS: views unchanged (settlement 38 = old foliage spot).

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
