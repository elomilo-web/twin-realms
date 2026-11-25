# Backlog (prioritized; one item per line)
Format: [impact 1-5][effort S/M/L][category] description -> target files. Top = next. Remove when done (log it in LOG.md).
Categories: graphics shaders lighting animation feel combat mechanics ui menus world vfx audio perf code tools

[5][M][world] Frostmere shore STILL dry after #1 (rounding fix in BlockGen.Column did not help; whole basin is sand terraces, sky looks smeared there too). Next: log hero pos, Lands.WaterAt/GroundAt, lake Level at the view from DevCapture; check the view really is at Frostmere (land offset Cx/Cz) and whether the lake bed is carved -> DevCapture.cs, BlockGen.cs, Lands.cs
[4][S][tools] Foliage viewpoint lands in Hollowfrost village, not a forest: point it at a forest biome region -> .devloop/viewpoints.json, DevCapture.cs (add "biome" lookup)
[4][M][lighting] Dungeon entry room still near-black although Dungeon.Dress places torches (L295-335, every 2nd lit, Range 13): check torches near the entry, warm them, add ambient fill/volumetric wisps -> Dungeon.cs, Torch class (grep)
[4][S][graphics] Verify block lip/jitter look up close (#1 added it; not visible at capture distance): add a close terrace viewpoint, tune data/blocks.json "lip" colours -> viewpoints.json, data/blocks.json
[3][S][feel] A wild wolf wanders into the "running" view and hits the hero (-40): DevCapture clears foes near the hero before each world view -> DevCapture.cs
[4][M][feel] Hit-stop + camera kick scaled by damage and weapon weight; heavier for crits -> Feel.cs, CameraRig.cs
[4][M][animation] Running pose stiff at distance: add lean into turns and upper-body counter-rotation -> HeroRig.cs / Avatar.cs
[3][S][ui] HUD ghost/vignette colours are hard-coded in Hud.SlantBar/GhostStep: move to data/ui_theme.json; add a "low_hp" viewpoint (hero at 20% HP) to judge the pulse -> Hud.cs, viewpoints.json
[3][S][lighting] Vista now clear: sea reads flat grey-blue; add sun glint/specular and horizon gradient on far water -> World.cs water material
[4][M][ui] Inventory grid is empty dark squares: add slot frame textures, rarity border glow, hovered-slot highlight, paper-doll silhouette behind equipment -> Ui.cs OpenPack, ItemSlot.cs
[4][M][menus] Main menu is a flat panel over a black void: title backdrop scene (slow camera over Oakhaven at dusk) behind a translucent gothic panel, serif title -> Main.cs title cam, Ui.cs ShowTitle
[4][M][world] Village plaza is sparse: add market stalls, banners, crates, lanterns on posts, scattered hay/leaves decals -> Builder.cs, data/env.json
[4][L][graphics] Sky in water view shows a smeared, tiled cloud texture: rebuild sky clouds as a procedural sky shader with layered fbm, sun disk and gothic overcast -> World.cs sky, new shaders/sky_gothic.gdshader
[4][M][vfx] Sword swing trail is a flat pale-blue quad: gothic trail with dark core, ember/steel spark edge, fade by speed -> SwordTrail (grep), Vfx.cs
[4][M][combat] Enemy telegraph wind-up poses + ground decals for heavy attacks -> Enemy.cs, EnemyAggro.cs
[4][M][combat] Perfect dodge: roll just before a hit gives 0.4 s slow-mo + counter window -> Player.cs, Feel.cs
[3][M][vfx] Fire spell impact: add heat distortion shader quad + ember particles + scorch decal that fades -> SpellFx.cs, Vfx.cs
[3][M][world] Grass hidden over block area (port limitation): scatter grass tufts on grass-top blocks in mesher -> BlockMesher.cs, Flora.cs
[3][M][shaders] Water: add depth-based color, shoreline foam and refraction to block-world water faces -> BlockMesher.cs, assets water shader
[3][S][lighting] Night village: lamps light pools too small; add warm omni lights on lamp posts with shadow off, window emissive bloom -> Builder.cs, World.cs night
[3][M][world] Shadow realm reads as purple murk: add drifting ash particles, desaturated cold rim light, distorted silhouette trees -> World.cs realm blend, Ambient.cs
[3][M][animation] Head look-at toward nearest enemy/NPC -> HeroRig.cs
[3][S][animation] Landing squash/dust after jumps and falls -> Player.cs, Fx.cs
[3][M][audio] Footstep surface types on blocks (stone/wood/sand/snow/water splash) -> Sfx.cs, PlayerBlocks.cs, data/blocks.json
[3][S][audio] Normalise SFX/music levels (GOTHIC.md: levels not normalised) -> tools script + Sfx.cs volume table
[3][M][ui] Floating damage numbers: gothic serif, crit scale-pop, color by element -> Hud.cs / Fx.cs
[3][M][ui] Target lock reticle and enemy nameplate restyle (gothic frame, HP bar with level badge) -> Targeting.cs, Hud.cs
[3][M][menus] Spellcraft screen: card art per carrier/effect (painted emblem icons), drag preview of the spell shape -> UiSpellcraft.cs, Icons.cs
[3][M][mechanics] Block world: ladders/doors/torches as placeable blocks with light -> data/blocks.json, BlockFx.cs, BlockWorld.cs
[3][M][mechanics] Fast travel between discovered wayshrines -> MapMarks.cs, Ui.cs OpenMap
[3][L][world] Trees as block-aware (fire spreads to trees): convert nearby trees to burnable wood/leaves blocks on hit -> Flora.cs, BlockSim.cs
[3][M][perf] Foliage view drops to 14 fps min: profile draw calls, add LOD/visibility ranges to props and trees -> Flora.cs, Builder.cs
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
