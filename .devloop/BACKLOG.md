[5][M][ui] Loading screens (user request): world boot, dungeon enter/leave, arena, lair, realm travel/teleport; gothic art + tips + progress -> Main.cs, Dungeon.cs, Arenas.cs, new LoadScreen.cs
[4][S][graphics] Default linen tunic reads as bare skin at distance (wand_attack/staff_attack): darker default outfit tint or wool tunic -> data/avatar.json default
[4][M][graphics] Hero legs under plate still near-black trousers; Crown kit shows an orange flame-like glow -> GearFx.cs, kit crown.glb
[3][S][models] Kit armor glbs carry a stray circlet_winged mesh ~4 m aside (armor.py export): verify with probe, fix armor.py, rebuild -> tools/blender_gothic/armor.py
[3][S][models] Female Gambeson/Cuirass/Tunic show nipples: rebuild with fill on outer layers -> humans.py
[3][S][ui] Gamepad: pack slots/custom controls not focusable; hotbar shows "LT+Up"; hard-coded "Esc"/"L" hints -> ItemSlot.cs, Hud.cs
[3][S][lighting] Dungeon ceilings/far end still black; look.json dungeon keys need user OK (edit denied) -> WorldEnv.cs
[3][S][menus] Stash scroll resets each click -> Home.cs
[2][S][perf] CritFx re-reads feel.json each crit via Cf(): cache the crit block -> Player.cs
[3][S][ui] Compass quest marker: quests have no target positions; add optional x/z to data/quests.json objectives and show a gold diamond -> Quests.cs, data/quests.json, Hud.cs
[2][S][world] Arena floor still faintly stripy (plaster tint): add a real CC0 soil/sand scanned set (ambientCG Ground/Soil) to assets/textures/gothic -> tools/blender_gothic or foliage_tex.ps1-like fetch, ASSETS.md
[4][S][menus] coverage (user directive): ALL listed menus have had a gothic pass (#12-#14). LEFT: inventory window sits right of centre; party/lobby panel (UiNet) check in a capture; keep re-checking every menu_* view each full sweep
[2][S][tools] Capture logs Jolt RID leaks at exit sometimes: ignore in capture.ps1 (teardown) -> tools/capture.ps1
[3][S][tools] Auto-spot tree check misses trees (no colliders?): also reject spots within 6 m of Flora tree instances (query FloraField/Lands tree positions) so lightning/holy views aren't occluded -> DevCapture.cs FindSpot
# Backlog (prioritized; one item per line)
Format: [impact 1-5][effort S/M/L][category] description -> target files. Top = next. Remove when done (log it in LOG.md).
Categories: graphics shaders lighting animation feel combat mechanics ui menus world vfx audio perf code tools

[4][S][world] Settlement walls still dark timber after #4 tint lift (plaster f0e8dc not visible in Hollowfrost): check which kit houses Hollowfrost uses and whether their walls are TR_Tex_planks; maybe swap some to plaster-walled models -> Lands.cs Cottages/Townhouses, data/env.json
[4][S][menus] Title backdrop now visible (#3) but the dusk is a flat saturated red; add sky gradient depth (violet top -> ember horizon), a moon, layered fog between skyline rows -> TitleBackdrop.cs
[4][S][graphics] Verify block lip/jitter look up close (#1 added it; not visible at capture distance): add a close terrace viewpoint, tune data/blocks.json "lip" colours -> viewpoints.json, data/blocks.json
[3][S][feel] A wild wolf wanders into the "running" view and hits the hero (-40): DevCapture clears foes near the hero before each world view -> DevCapture.cs
[4][M][animation] Running pose stiff at distance: add lean into turns and upper-body counter-rotation -> HeroRig.cs / Avatar.cs
[3][S][ui] HUD ghost/vignette colours are hard-coded in Hud.SlantBar/GhostStep: move to data/ui_theme.json; add a "low_hp" viewpoint (hero at 20% HP) to judge the pulse -> Hud.cs, viewpoints.json
[3][S][audio] Normalise SFX/music levels (GOTHIC.md: levels not normalised) -> tools script + Sfx.cs volume table
[3][M][ui] Target lock reticle and enemy nameplate restyle (gothic frame, HP bar with level badge) -> Targeting.cs, Hud.cs
[3][L][world] Trees as block-aware (fire spreads to trees): convert nearby trees to burnable wood/leaves blocks on hit -> Flora.cs, BlockSim.cs
[3][M][perf] Greedy meshing for block chunks (fewer verts, faster remesh) -> BlockMesher.cs
[2][S][code] Last compiler warning: EnemyScarletta.cs(164,29) CS8602 -> EnemyScarletta.cs
[2][S][ui] Serif font for titles (CC0, e.g. a free-license blackletter/serif) -> data/ui_theme.json, UiTheme.cs
[2][S][tools] DevCapture: add viewpoints for block build mode (N) and a crater after Meteor -> .devloop/viewpoints.json, DevCapture.cs actions
[3][S][ui] Inventory slots framed (#3); equipment paper-doll silhouette still missing; slot colours hard-coded in ItemSlot.Inset -> ItemSlot.cs, Ui.cs OpenPack
[3][S][vfx] Sword trail restyled (#3): judge in a swing close-up; tune spark threshold 8 m/s -> Vfx.cs SwordTrail, assets/shaders/sword_trail.gdshader
[3][S][shaders] Frostmere lake shows white floe-like blotches (sea macro/foam on lakes): fine for frost, but check temperate lakes; gate by land biome -> WorldEnv.cs WaterShader
[3][S][feel] Dodge/head-look/stagger views exist now: judge perfect-dodge flash, lean sign, stagger lean next cycle
[3][S][tools] stagger view: the wolf is not in frame; spawn foe at 3 m and frame both -> viewpoints.json, DevCapture.cs foes distance
[2][S][audio] Listen pass needed: synthesized thud/splash levels vs step_* (user ears) -> Sfx.cs volumes
[3][M][code] Enemy._PhysicsProcess 255 lines with inline state switch: extract per-state methods into EnemyAi.cs (logic-preserving refactor; needs a bot check if the user allows) -> Enemy.cs, EnemyAi.cs
[3][S][menus] Pause menu dropped the stats grid (gold, valor, kills, played): consider a small stats strip at the bottom -> UiSystem.cs OpenPause
[3][S][audio] Soundscape is dormant (ambient.json sound:false): needs day_birds, breeze, bird/owl/crow one-shots (CC0 or synthesized) before enabling; replace 'hurt' crow stand-in -> assets/audio, data/ambient.json
[2][S][ui] Nameplate badge offset estimated from text length; check alignment in a close capture -> Enemy.cs
[3][S][animation] Enemies with a Death clip get the clip AND the tip-over (double); skip tip for bodies with a Death clip or blend -> Enemy.cs Die
[2][S][animation] Puppets (multiplayer clients) get no flinch/death fall: drive visuals from replicated hit/death events -> Enemy.cs, NetAggro
[2][S][code] water_stylized.gdshader unused (proof in LOG #10): delete together with its docs/kit/README.md row -> assets/shaders, docs/kit/README.md
[3][S][feel] Camera look-ahead uses a guessed 6 m/s run speed and writes _cam.Position.Z beside the SpringArm: check for jitter in play (PLAYTEST) -> CameraRig.cs
