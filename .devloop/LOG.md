# Dev loop log (newest first; max 20 detailed entries, older ones rolled into History)

## #11 (2026-11-25) feel, tools, shaders, ui, mechanics, perf
- Camera (CameraRig.cs): look-ahead lead 0.8 m run / 1.2 m sprint (tau .4, off when locked), collision pull-in instant + .35 s ease-out, shoulder offset .35 (off sprinting/locked). Untested by eye (stills).
- Capture: "spot": "auto:n" (FindSpot: dry, flat, >25 m from POIs, no collider in a 3.5 m sphere 5 m up); cast views auto:0-5; holy (smite), shadow (shadowbolt), poison (thornvolley) views; hero faces the foe. Trees seem to have no colliders: lightning/holy views still occluded.
- Water: block water already shared the sea shader + shore foam; falls/side faces now lighter scrolling streaks (COLOR.b=0 marks them).
- HUD: buff/debuff row (8 icons, gold/red frames, drain bar, stacks) under the bars; quest tracker top-right (gold caps title, objective progress, fades after 10 s, pulses on progress). Not visible in captures (no buffs/quests on the capture hero).
- Chests: lid ease-out-back, gold light swell, motes; loot pops out in arcs (Loot.Pop), rare+ beams 3 m fading after 6 s.
- Perf: Kit.Add props < 4.5 m get range 110, < 1.8 m no shadow: settlement 40 -> 61 fps.
- Note: capture fps depends on the views run before (closeup 47 in a full set, 90 alone): compare fps only between runs of the same view set.

## #10 (2026-11-24) full sweep + process review #2; vfx, combat, world, code, tools
- Full sweep: 28 views, 61.9 fps avg, 0 engine errors. Process review #2 in PROCESS.md; PLAYTEST.md checklist for the user.
- INFRA: 4 of 6 subagents could not edit (auto-mode safety classifier unavailable, "no verdict"); lead applied the small ones from their plans. Camera look-ahead (#10c) and the auto-spot capture helper were NOT done.
- VFX (Vfx.ElementImpact via SpellFx.Skill): holy light column + gold motes + sigil decal; shadow collapsing sphere + inward wisps + crack decal; poison bubbling puddle + bubbles + mist. Lead fix: System.Func.
- Combat (lead): riposte also on ranged/wand shots (FireWeapon); connecting heavy blows kick dust + stronger shake (tuning heavy.shake/dust).
- World (lead): Oakhaven wayshrine at the plaza's south edge (3,-12); shrines walk outward off lakes/shores.
- Code (lead): removed unused UiSystem.Meter. water_stylized.gdshader proven unused but kept for now (docs/kit README lists it).
- Capture (lead): spell views on separate spots; meteor wait 1.75 s (fire impact explosion now visible); lightning view still lands in a tree.

## #9 (2026-11-24) tools, animation, world, graphics, lighting, menus
- Capture: cast:<skill> action (nova, zap, meteor), freeze_foes, views frost_cast, lightning_cast, fire_impact, wayshrine, character (29 views total).
- Enemies: directional flinch (0.18 s, 4-14 deg), HitReact clip unless winding up; death tip-over ~80 deg with bounce + dust for big bodies; white kill-flash overlay (cached material).
- Village: Builder.DressVillage: 3 market stalls with awnings/goods (2 with night lanterns), house banners, firewood stacks, handcart, puddles, straw (Rng(777) so the world seed is unchanged).
- Roads: OverworldGen.WornRoad: gravel/cobble patches (more near places), grass and cobble kerbs at edges, cart ruts (new block path_rut appended to blocks.json).
- Dungeon: volumetric fog (quality >= 2) density .025 + ground mist, torches LightVolumetricFogEnergy 2.5; overworld fog values saved/restored.
- Character sheet (Ui.OpenCharacterPanel): 3 columns: identity + level + XP bar; 4 big attribute rows with pulsing +1/+5 and hover deltas; offense/defense/other/resistances.
- Seen in capture: frost shard ring looks great; lit wayshrine at night; character sheet clean. Spell views share a spot -> crater + ice from earlier casts; lightning/fire timing misses the bolt/impact (backlog).
- FPS: steady (running 57, combat 55, closeup 47.7).

## #8 (2026-11-24) perf, shaders, vfx, mechanics, ui, audio
- Perf: tufts 1-2 per block (the grass mesh is a 24-blade clump), full density within 2.5 chunks, half beyond, cap 600/chunk, range 45 (log: 78,947 instances / 480 chunk builds); HeadLook does nothing for non-local avatars beyond the turn lean and nothing beyond 25 m. FPS back: running 51 -> 56.5, combat 49.3 -> 55, terrace 45.7 -> 51.1, closeup 45.7 -> 47.7.
- Sky (WorldEnv SkyShader): warped 3-octave fbm cloud plane, blue-grey bases, silver lining to the sun, warm at golden hour (gold uniform), near-black violet-edged in the shadow realm, horizon fade; cloudCover .72 -> .55.
- VFX (Vfx.cs): FrostAftermath (ice shard ring, frost decal, mist); StormStrike (forked 12 m bolt, flash light, scorch, sparks) from Vfx.Lightning when time >= .2.
- Mechanics: scripts/Wayshrine.cs: plinth + obelisk + rune band + sigil at every POI; lit (gold rune, light, motes) when discovered; Hud discovery hook flashes it. Lead fix: missing namespace TwinRealms + GeometryInstance3D.ShadowCastingSetting.
- UI: Enemy nameplates only for target / hunting / bosses / within 12 m, fade 0.25 s; level badge coloured by level gap; health bar when damaged.
- Audio: Ambient soundscape (biome/water/settlement/altitude layers, spatial one-shots) written but DORMANT: data/ambient.json "sound": false was already set (left as is); stand-in one-shots (hurt pitched as crow) need real sounds before enabling.
- Scores: skills_vfx visuals 5->5.5, world visuals 6.5->7, enemies visuals 4->4.5, perf recovered.

## #7 (2026-11-24) combat, feel, lighting, graphics, code, menus
- Guard (existed): block now sparks + "block" clang + 40 ms hit-stop + push-back; parry window 0.15 s -> enemy Stun 0.8, "Parry!", riposte x2 within 1.5 s (main melee path only); tuning game.guard.*.
- Sprint: CameraRig +6 deg FOV, +0.4 m arm, footstep-synced bob (Player.Stepped -> Rig.StepBob), dust + "swing" whoosh at start, skid + dust on hard stop.
- Golden hour (WorldEnv.UpdateVisuals): gold band over low sun: amber sun up to x1.45, sun 30% lower (long shadows), rose-amber horizon, cooler ambient, warm FogSunScatter, glow +0.35; 10 look.json keys gold*.
- Grass tufts on grass/forest_floor block tops (BlockMesher worker -> BlockWorld.Show MultiMesh, 60 m range, Game.Set.Grass). Lead fix: grass shader hid them inside vox_rect; added uniform vox_cut (Flora.cs) and BlockWorld.TuftMat copy with vox_cut 0. Ground near the hero is now grassy, a big win.
- Code: EnemyAi.cs (Notices/Alert, telegraph helpers, SummonAdds); Enemy.cs 973 -> 920 (state switch is inline in _PhysicsProcess; moving it needs a method extraction).
- Pause: Ui.FrameSlim left column 360 px + desaturate/darken screen shader; 15 text entries (lead: 38->31 px rows, 22->19 font so the footer fits 720 px).
- FPS: near-hero views -10..-13% (tufts + sprint/idle); e.g. running 59 -> 51, combat 54.5 -> 49.3. Next cycle: perf first.
- Scores: world visuals 6->6.5 (tufts, dusk), combat feel 7->7.5, menus visuals 6.5->7, player feel 6.5->7.

## #6 (2026-11-24) audio, animation, mechanics, world, ui, tools
- Audio: tools/kit_gen/sfx_synth.py synthesizes land_thud_1-3, splash_1-4, step_water_1-4 (ours; ASSETS.md); landing uses land_thud, wading uses step_water (Sfx.BlockStep FluidAt), splash on entering water (PlayerBlocks _wasWater).
- Animation: HeadLook adds idle breathing (spine_03 + clavicles), weight shift every 6-10 s, idle glance (public Glance) every 8-14 s; fades on move/Busy/lock.
- Mechanics: fast travel already existed (Game.D.Disc + map Travel); added 30 s cooldown and no travel while foes hunt within 20 m (Ui.OpenMap, tuning game.fastTravel). Small gain.
- Shadow realm: Ambient.cs ash (225) + glowing motes (25) around the camera, cold back rim DirectionalLight 0.35*RealmT; off in Light/dungeons. Lead fix: CpuParticles3D has no Turbulence* (GPU only).
- UI: FloatText smaller parchment numbers, amber crit pop, dark red hero damage, element tints (Status.Dmg.TypeColor), random x offset; nameplates 36->26 px, bosses gold.
- Capture: foes cleared within 60 m.
- Scores: audio feel 6->6.5, animation visuals 3->4, world (shadow) visuals +0.5, ui visuals 6.5->7. FPS ~unchanged.

## #5 (2026-11-24) full sweep + process review; world, shaders, ui, vfx, tools, perf
- Shadow realm (WorldEnv.cs + look.json): cold blue-grey/violet palette, pale violet sun at ~1.05x, ambient >= 0.6x day, violet-grey fog .003, eclipse halo. Readable now, still dark.
- Sea: rotated/irrational noise layers, normals fade to 0 by ~1200 m, macro albedo/roughness variation: grid pattern gone. Lead fix: inline shader `mat2 R1/R2` must be `const` (shader failed to compile).
- Arena (Builder.cs ArenaMats): ochre-grey rock floor + worn circle + stains, dark brick base, G_Stone stands, 18 crimson banners, 8 braziers.
- Spellcraft (UiSpellcraft.cs SpellCardTile, Header): portrait cards with family/element band, count badge, dashed "+" slots, gold forge button. Lead fix: Header must be an instance method (Lbl).
- Fire impacts (Vfx.Aftermath): scorch Decal (cap 12, 6 s fade), heat shimmer quad (assets/shaders/heat_shimmer.gdshader), embers + smoke.
- Perf: lamps Visible only when lit, writes only on change, distance fade; torches fade 60-75 m.
- Capture: my #2 teleport bug (code after `//` on the same line) fixed; lake has its own camera; actions dodge, swing3, place:<block>, foe_side; views torch_night, dodge, head_look, stagger (22 views); realm resets to Light per view; full sweeps saved to screenshots/full.
- FPS: full sweep avg 65.6 over 22 views (new views are heavier); foliage 55->62, vista 59->70 (before the shader fix), village 51.
- Scores: arena visuals 5->6.5, spellcraft visuals 4->5.5, world visuals 5.5->6, skills_vfx visuals 4.5->5.

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
