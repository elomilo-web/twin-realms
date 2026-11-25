[4][S][tools] Spell capture views share one spot: spell_cast's meteor crater + frost ice ruin the later views; give each cast view its own player_at (>= 40 m apart); lightning_cast cast_wait .12 misses the bolt (try .05 after the cast lands / target a foe in range), fire_impact 1.2 s shows only the meteor targeting runes (meteor falls later: wait ~2.2 s) -> viewpoints.json
# Backlog (prioritized; one item per line)
Format: [impact 1-5][effort S/M/L][category] description -> target files. Top = next. Remove when done (log it in LOG.md).
Categories: graphics shaders lighting animation feel combat mechanics ui menus world vfx audio perf code tools

[4][S][world] Settlement walls still dark timber after #4 tint lift (plaster f0e8dc not visible in Hollowfrost): check which kit houses Hollowfrost uses and whether their walls are TR_Tex_planks; maybe swap some to plaster-walled models -> Lands.cs Cottages/Townhouses, data/env.json
[4][S][menus] Title backdrop now visible (#3) but the dusk is a flat saturated red; add sky gradient depth (violet top -> ember horizon), a moon, layered fog between skyline rows -> TitleBackdrop.cs
[3][M][perf] Foliage view still 35 fps / min 13 (Hollowfrost): flora already culled (#2 check). Profile lights/shadows/terrain: try shadow distance per view, omni shadow count, draw calls in report.json -> World.cs, Lands.cs
[4][S][graphics] Verify block lip/jitter look up close (#1 added it; not visible at capture distance): add a close terrace viewpoint, tune data/blocks.json "lip" colours -> viewpoints.json, data/blocks.json
[3][S][feel] A wild wolf wanders into the "running" view and hits the hero (-40): DevCapture clears foes near the hero before each world view -> DevCapture.cs
[4][M][animation] Running pose stiff at distance: add lean into turns and upper-body counter-rotation -> HeroRig.cs / Avatar.cs
[3][S][ui] HUD ghost/vignette colours are hard-coded in Hud.SlantBar/GhostStep: move to data/ui_theme.json; add a "low_hp" viewpoint (hero at 20% HP) to judge the pulse -> Hud.cs, viewpoints.json
[3][M][shaders] Water: add depth-based color, shoreline foam and refraction to block-world water faces -> BlockMesher.cs, assets water shader
[3][S][audio] Normalise SFX/music levels (GOTHIC.md: levels not normalised) -> tools script + Sfx.cs volume table
[3][M][ui] Target lock reticle and enemy nameplate restyle (gothic frame, HP bar with level badge) -> Targeting.cs, Hud.cs
[3][L][world] Trees as block-aware (fire spreads to trees): convert nearby trees to burnable wood/leaves blocks on hit -> Flora.cs, BlockSim.cs
[3][M][perf] Greedy meshing for block chunks (fewer verts, faster remesh) -> BlockMesher.cs
[3][S][perf] Player close-up min 20 fps: find first-frame spikes (shader compile?) -> report.json, Main.cs warmup
[2][S][code] Last compiler warning: EnemyScarletta.cs(164,29) CS8602 -> EnemyScarletta.cs
[2][S][ui] Serif font for titles (CC0, e.g. a free-license blackletter/serif) -> data/ui_theme.json, UiTheme.cs
[2][M][world] Weather: rain/storm cycle with wet darkening on blocks and puddle reflections -> World.cs, Ambient.cs
[2][S][tools] DevCapture: add viewpoints for block build mode (N) and a crater after Meteor -> .devloop/viewpoints.json, DevCapture.cs actions
[3][S][ui] Inventory slots framed (#3); equipment paper-doll silhouette still missing; slot colours hard-coded in ItemSlot.Inset -> ItemSlot.cs, Ui.cs OpenPack
[3][S][vfx] Sword trail restyled (#3): judge in a swing close-up; tune spark threshold 8 m/s -> Vfx.cs SwordTrail, assets/shaders/sword_trail.gdshader
[2][M][mechanics] Ladders and doors as placeable blocks -> data/blocks.json, BlockMesher.cs shapes
[3][S][shaders] Frostmere lake shows white floe-like blotches (sea macro/foam on lakes): fine for frost, but check temperate lakes; gate by land biome -> WorldEnv.cs WaterShader
[3][S][world] Arena floor reads like planks (G_Rock tint stretched?): check triplanar scale; add a real sand/soil set -> Builder.cs ArenaMats
[3][S][feel] Dodge/head-look/stagger views exist now: judge perfect-dodge flash, lean sign, stagger lean next cycle
[3][S][tools] stagger view: the wolf is not in frame; spawn foe at 3 m and frame both -> viewpoints.json, DevCapture.cs foes distance
[2][S][audio] Listen pass needed: synthesized thud/splash levels vs step_* (user ears) -> Sfx.cs volumes
[3][S][combat] Riposte x2 only on the main melee path (Player.cs ~385); second damage site ~421 unknown: check (skills? heavy attack?) -> Player.cs
[3][M][code] Enemy._PhysicsProcess 255 lines with inline state switch: extract per-state methods into EnemyAi.cs (logic-preserving refactor; needs a bot check if the user allows) -> Enemy.cs, EnemyAi.cs
[3][S][menus] Pause menu dropped the stats grid (gold, valor, kills, played): consider a small stats strip at the bottom -> UiSystem.cs OpenPause
[3][S][mechanics] Wayshrines at lake/peak POIs may stand in water or on cliffs; fixed offset ignores buildings: check each POI shrine in a capture sweep -> Wayshrine.cs
[3][S][audio] Soundscape is dormant (ambient.json sound:false): needs day_birds, breeze, bird/owl/crow one-shots (CC0 or synthesized) before enabling; replace 'hurt' crow stand-in -> assets/audio, data/ambient.json
[2][S][ui] Nameplate badge offset estimated from text length; check alignment in a close capture -> Enemy.cs
[3][S][animation] Enemies with a Death clip get the clip AND the tip-over (double); skip tip for bodies with a Death clip or blend -> Enemy.cs Die
[3][S][world] Wayshrine stands in the middle of Oakhaven plaza (POI centre + 4.5,4) next to the well: move village shrine to the plaza edge -> Wayshrine.cs offset per POI (data/world.json shrine offset)
[2][S][animation] Puppets (multiplayer clients) get no flinch/death fall: drive visuals from replicated hit/death events -> Enemy.cs, NetAggro
