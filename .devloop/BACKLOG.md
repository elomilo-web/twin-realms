# Backlog (prioritized; one item per line)
Format: [impact 1-5][effort S/M/L][category] description -> target files. Top = next. Remove when done (log it in LOG.md).
Categories: graphics shaders lighting animation feel combat mechanics ui menus world vfx audio perf code tools

[4][S][world] Settlements gothic since #3 but walls read near-black in shade, plaster barely visible: lift TR_Tex_planks/planks_dark tints, check TR_Tex_plaster actually maps (walls look like planks) -> data/env.json, Kit.cs Shared
[4][S][menus] Title backdrop now visible (#3) but the dusk is a flat saturated red; add sky gradient depth (violet top -> ember horizon), a moon, layered fog between skyline rows -> TitleBackdrop.cs
[3][S][world] Kit TR_Matte tint (#3, 8f8780) also dims nature/spell kit props: check forest props/crystals still read; split tint per folder if needed -> data/env.json, Kit.cs
[3][S][mechanics] Torch block (#3) untested in play: capture action "place:torch" near hero at night to see the light -> DevCapture.cs, viewpoints.json
[4][S][tools] Water viewpoint: lake was NOT dry (#2 diagnostics: level 12.7 over ground 10.3; old view camera was underwater). New shore spot teleports the hero to (44,-791) instead of (-138,-860): find why (Teleport into water? swim push?), then frame the lake from above the surface -> DevCapture.cs, viewpoints.json
[3][S][tools] "biome": "forest" view (#3) lands on a coast (first forest region centre is near sea): pick a region point that is on land and inside trees -> DevCapture.cs biome lookup
[3][M][lighting] Dungeon better after #2 (3 lit torches in the entry room, cold ambient fill) but floors still very dark between pools: volumetric fog for glowing torch pools (World.SetDungeonLook, restore on exit) -> World.cs SetDungeonLook
[3][S][animation] Head look-at + turn lean added #2 (Avatar.cs HeadLook): not visible in stills; add a viewpoint with a foe beside the hero to judge; flip lean sign if wrong -> viewpoints.json, Avatar.cs
[3][M][perf] Foliage view still 35 fps / min 13 (Hollowfrost): flora already culled (#2 check). Profile lights/shadows/terrain: try shadow distance per view, omni shadow count, draw calls in report.json -> World.cs, Lands.cs
[4][S][graphics] Verify block lip/jitter look up close (#1 added it; not visible at capture distance): add a close terrace viewpoint, tune data/blocks.json "lip" colours -> viewpoints.json, data/blocks.json
[3][S][feel] A wild wolf wanders into the "running" view and hits the hero (-40): DevCapture clears foes near the hero before each world view -> DevCapture.cs
[4][M][feel] Hit-stop + camera kick scaled by damage and weapon weight; heavier for crits -> Feel.cs, CameraRig.cs
[4][M][animation] Running pose stiff at distance: add lean into turns and upper-body counter-rotation -> HeroRig.cs / Avatar.cs
[3][S][ui] HUD ghost/vignette colours are hard-coded in Hud.SlantBar/GhostStep: move to data/ui_theme.json; add a "low_hp" viewpoint (hero at 20% HP) to judge the pulse -> Hud.cs, viewpoints.json
[4][M][world] Village plaza is sparse: add market stalls, banners, crates, lanterns on posts, scattered hay/leaves decals -> Builder.cs, data/env.json
[4][L][graphics] Sky in water view shows a smeared, tiled cloud texture: rebuild sky clouds as a procedural sky shader with layered fbm, sun disk and gothic overcast -> World.cs sky, new shaders/sky_gothic.gdshader
[4][M][combat] Enemy telegraph wind-up poses + ground decals for heavy attacks -> Enemy.cs, EnemyAggro.cs
[3][S][audio] No water splash sound exists: add a CC0 splash/wade set (record in assets/ASSETS.md) and map block "water" step to it -> assets/audio/sfx, Sfx.cs BlockStep
[3][S][combat] Perfect dodge (#2) untested in play: add a capture action "dodge" with a foe swinging to see the flash; maybe a bot step if the user asks -> DevCapture.cs
[3][M][vfx] Fire spell impact: add heat distortion shader quad + ember particles + scorch decal that fades -> SpellFx.cs, Vfx.cs
[3][M][world] Grass hidden over block area (port limitation): scatter grass tufts on grass-top blocks in mesher -> BlockMesher.cs, Flora.cs
[3][M][shaders] Water: add depth-based color, shoreline foam and refraction to block-world water faces -> BlockMesher.cs, assets water shader
[3][S][lighting] Night village: lamps light pools too small; add warm omni lights on lamp posts with shadow off, window emissive bloom -> Builder.cs, World.cs night
[3][M][world] Shadow realm reads as purple murk: add drifting ash particles, desaturated cold rim light, distorted silhouette trees -> World.cs realm blend, Ambient.cs
[3][S][animation] Landing squash/dust after jumps and falls -> Player.cs, Fx.cs
[3][S][audio] Normalise SFX/music levels (GOTHIC.md: levels not normalised) -> tools script + Sfx.cs volume table
[3][M][ui] Floating damage numbers: gothic serif, crit scale-pop, color by element -> Hud.cs / Fx.cs
[3][M][ui] Target lock reticle and enemy nameplate restyle (gothic frame, HP bar with level badge) -> Targeting.cs, Hud.cs
[3][M][menus] Spellcraft screen: card art per carrier/effect (painted emblem icons), drag preview of the spell shape -> UiSpellcraft.cs, Icons.cs
[3][M][mechanics] Fast travel between discovered wayshrines -> MapMarks.cs, Ui.cs OpenMap
[3][L][world] Trees as block-aware (fire spreads to trees): convert nearby trees to burnable wood/leaves blocks on hit -> Flora.cs, BlockSim.cs
[3][M][perf] Greedy meshing for block chunks (fewer verts, faster remesh) -> BlockMesher.cs
[3][S][perf] Player close-up min 20 fps: find first-frame spikes (shader compile?) -> report.json, Main.cs warmup
[3][M][code] Enemy.cs 947 lines: split AI states into EnemyAi.cs (no behavior change) -> Enemy.cs
[3][M][code] World.cs 949 lines: split environment/sky/light setup into WorldEnv.cs -> World.cs
[2][S][code] Last compiler warning: EnemyScarletta.cs(164,29) CS8602 -> EnemyScarletta.cs
[2][S][ui] Serif font for titles (CC0, e.g. a free-license blackletter/serif) -> data/ui_theme.json, UiTheme.cs
[2][M][world] Weather: rain/storm cycle with wet darkening on blocks and puddle reflections -> World.cs, Ambient.cs
[2][M][vfx] Ambient motes: fireflies at night near water, falling leaves in forests -> Ambient.cs
[2][S][tools] DevCapture: add viewpoints for block build mode (N) and a crater after Meteor -> .devloop/viewpoints.json, DevCapture.cs actions
[2][M][world] Roads on block terrain: path blocks with cart-rut variation and edge stones -> BlockGen.cs
[3][S][ui] Inventory slots framed (#3); equipment paper-doll silhouette still missing; slot colours hard-coded in ItemSlot.Inset -> ItemSlot.cs, Ui.cs OpenPack
[3][S][vfx] Sword trail restyled (#3): judge in a swing close-up; tune spark threshold 8 m/s -> Vfx.cs SwordTrail, assets/shaders/sword_trail.gdshader
[2][M][mechanics] Ladders and doors as placeable blocks -> data/blocks.json, BlockMesher.cs shapes
