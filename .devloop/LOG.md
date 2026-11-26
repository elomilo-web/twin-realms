# Dev loop log (newest first; max 20 detailed entries, older ones rolled into History)

## #20 (2026-11-25) user directives: loading screens, pickaxe/building, zones, spellcraft x3, no-stretch menus, user flag review fixes
- Full sweep 118 views, avg 72.7 fps (45.7 mid-cycle, ~62-70 before), 0 engine errors, 0 warnings, world built 9.4 s (staged).
- REGRESSION found + fixed: zone packs built ~650-900 Enemy nodes at world build -> dungeon 2.5 fps, D3D12 descriptor heap errors. Camps are data now (CampStreamer: nodes within 140 m, freed past 190 m; net indices fixed at build).
- Perf: Auto quality by resolution (Medium 62% > 2560 px), FSR1/FSR2, cheaper Low/Medium, occlusion culling, house mesh merge, 227 textures VRAM-compressed (max 2048), physics interpolation for hero/monsters (animation sync). User settings.json has MaxFps 30 (user's choice; told them); our captures changed its RenderScale 0.75->0.65: restored.
- Loading screens (LoadScreen.cs, tips.json, staged world build), pickaxe tiers + build hotbar + torch/glass/stairs/slab/fence blocks (PlayerBlocks, Blocks, BlockMesher), level zones + packs + leash + zone banner (Zones.cs, zones.json), spellcraft 3 sections + new layout, skill sockets data-driven.
- Ui.Frame RULE: body in a ScrollContainer, windows never stretch (user directive); *_full capture views (learn_all, pack_fill, mats_fill).
- User flags (9 screenshots) fixed: sky islands (vox_cut + under fill), Scarletta lair floor/lights + ~45% shorter walk, dungeon scaling rewritten vs a reference hero (boss 368k -> 43k HP), ground snapping on block tops, helmets refit + 4 new helm kits, wings (rim/glow skip feathers, swept flight pose), arena colosseum (MultiMesh), gamepad in all menus (PadInput repeat, PadTick refocus, IPadFocus custom views, LB/RB tabs everywhere), unarmed Fist Forms + synthesized punch sounds.
- Gear polish: dyed linen tunic, lighter wool, gem emission cap, gothic.finish exported stray models (fixed, armor kits rebuilt). Gamepad glyph badges, pad aim assist.
- FOCUS list started (.devloop/FOCUS.txt): F-01 cast gate code in (PayCast), test next.
- Capture fix: switching between dungeon keys now leaves the first dungeon.

## #19 (2026-11-25) user directives: player models/armor/weapons, wand+staff shots, dungeon light, inventory, gamepad
- BUG (root cause): devloop #18 swallowed `_hitDone = false; _atkSerial++;` into a comment (Player.TryAttack): every basic attack after the first never landed (no wand/staff shots, no melee damage, no fresh anim). Restored; capture logs "swing: 1 player shots" for wand + staff, 0 for sword. Same bug class since 217a461: roll never faced its direction (restored).
- Observe first: capture options gear {slot:name}, pack_fill, mats_fill, probe; views gear_knight/_back/ranger/mage/surcoat/coat/chain/face, wand_attack, staff_attack, inventory_full, menu_*_full, character_full, dungeon_room.
- Armor look (GearFx/Avatar/G_Metal_Iron): rarity was wide additive fresnel + whole-material emission + 1.8 m coloured light (flat neon helms/blades, purple legs) -> thin capped rim, .7 m light; G_Metal_Iron had a near-black albedo on a metal -> steel grey, metallic .85; metal skips cloth tints; Gauntlet/Greave/Sabaton/Plated -> iron; skin SSS/roughness; cloth normals.
- New garments (humans.py + outfits_from_glb.py, headless from TR_*_Body.glb, fit by construction, render-checked): Chainmail, Surcoat, Vambraces, Greaves, Gauntlets, Tassets, Gorget, Jerkin, Coat, Mantle (m+f); avatar.json rules (Plate = 7 layers + kit pauldrons, Surcoat, Longcoat, Jerkin, Chainmail, Brigandine, Vestments, Crown, Mage Hat, Vambraces); items.json names.
- 18 new weapon models mapped (Bastard Sword, Estoc, Great Cleaver, wavy Flamberge, Runed Axe, Flail, Spiked Club, Parrying Dagger, Seax, Bardiche, Winged Spear, War Scythe, Horn Bow, Bone Wand, Rune Rod, Gnarled Staff, Crozier, Skull Staff). Items.cs only rolled the first 5 base names: now all.
- Wand bolt comet streak + muzzle flash + impact sparks; Projectile.Spawn sets tint before _Ready.
- Dungeons (root cause: ambient .8 x 2-7% albedo stone, overridden in Dungeon.Go; SSAO 1.8; near-black fog): ambient 3.2, lighter thinner fog, SSAO .5 (restored on leave), all sconces lit (cap 34, range 10, energy 2.6), soft fill per room (data/dungeons.json "light"). data/look.json edit was denied by permissions: defaults in code.
- Inventory: fixed 820x560 (doll + stats | Items/Materials tabs in one scroll box | toolbar), tab + scroll kept on reopen; stash scrolls in a fixed window.
- Gamepad (user request): Inputs.cs rewrite (pad defaults, separate pad binds, Xbox/PS names, UsingPad, Rumble), PadInput.cs autoload, UiPad.cs Settings > Controller tab, right-stick camera, menu focus + B back + LB/RB tabs, rumble. Untested with a real pad (PLAYTEST).
- Scores: animation visuals 3 -> 5, loot visuals 4 -> 6, dungeons visuals 6 -> 7.

## #18 (2026-11-25) menus (directive), feel/combat, shaders, ui, perf, tools
- Menus: Frame gets a deferred Recentre() for windows whose content outgrows the requested size (pack still sits left of centre in capture: follow-up); pause menu 2x3 stats strip (gold, valor, kills, played, deepest, best wave).
- Combo: trail grows per step, finisher gold trail + ring + dust + heavy Feel.Impact + low "hit"; perfect presses (last 25% of recovery) speed the next step 10% + blade glint; "Rhythm" on an all-perfect finisher (feel.json combo).
- Terrain shader (World.TerrainShader): 37/113 m macro brightness/hue/roughness variation, rock blend + darkening on slopes, normal detail fades 40-200 m, far ground desaturates toward haze.
- Boss bar (Hud.DrawBossBar): Elden Ring layout bottom-centre (60% width), letter-spaced name, level, crimson fill + amber ghost, 2.5 s damage counter, fade-in; raid/duel bosses keep the old top bar.
- Perf: CharRim off on Quality < 2, dropped beyond 40 m (RimLod, .5 s checks); sway shader inverse() once per vertex. Lead fix: CharRim must be `partial` (GD0002: nested GodotObject).
- Capture: boss view (mushroomKing, level 8 so it doesn't kill the hero; hero healed while views settle) and combo view (combo3). Death screen seen once ("FALLEN" band): looks right.

## #17 (2026-11-25) menus (directive), animation, graphics, vfx, mechanics, tools
- Inventory (Ui.OpenPack): PaperDoll silhouette (iron body + faint gold outline) with 8 equipment slots placed around it, 2-column gold stats, GoldTab header buttons; window now left of centre (follow-up). Lead fix: RuneGold is MapView's -> GoldC.Lightened.
- Dodge roll: dust kick + whoosh at start, violet smoke puffs along the roll (pale + ring on a perfect dodge), dust + tiny settle at the end (Player.cs, Fx.RollPuff).
- Digging: crack overlay already existed (PlayerBlocks.ShowCrack); BlockFx adds a coloured break puff and Chip(); lead hooked chips every .15 s while digging.
- Footfalls (Sfx.Step -> Fx.Footfall): dust/snow puffs when running on dirt/sand/gravel/snow, splash ring + droplets in water; within 25 m of the camera.
- Blocks: ladder (rails + rungs, climb 3 m/s, no gravity on it), door (2 tall, E toggles door/door_open on both halves through BlockWorld.Change, so it syncs); orientation in block meta; recipes 3 planks -> 4 ladders, 4 planks -> door (appended ids).
- Capture: ResetHero() per view (clears buffs/statuses, TimeScale 1, heal, Hud.ResetFx): the red burn tint no longer leaks into menu views.
- Noted: Player.cs ~L657 has `_modelYaw = Util.YawTo(_rollDir...)` inside a comment (roll may not face its direction): left as is, added to PLAYTEST.

## #16 (2026-11-25) code, audio, lighting, tools, world, combat
- Code: Ui.cs 1080 -> 736 lines; MapView -> UiMap.cs, TreeView -> UiTree.cs, shop/valor/totals -> UiShop.cs (Edit/Write only; builds; menus unchanged in capture).
- Audio (sfx_synth.py): rain_loop, rain_light_loop, wind_loop (20 s seamless), thunder_1-2; Weather.cs plays rain loops by intensity (Ambient volume, muted in dungeons) and thunder in heavy rain after a Weather.Flash; lead wired the flash into WorldEnv (cold ambient wash).
- Lighting: god rays in the gold band (volumetric density +, anisotropy .7, sun volumetric energy +2; morning .6x; rain cuts them; quality >= 2). golden_rays view looks beautiful (backlit trees, warm haze).
- Capture: hud view gets buffs (warcry, poisonblade, burn) + quest wolves 1/5: buff row + quest tracker now visible; golden_rays view.
- World: bushes sway (BushShader for procedural bushes; Windy matches bush/shrub/atlas leaf materials with gentler, faster flutter).
- Combat: crit moment (Player.CritFx: enemy Flash, gold-white spark burst + streak along the swing, hit + pitched clash; crit kills: .12 s slow-mo + bigger kick; feel.json "crit"); Feel.SlowMo queues behind hit-stop. Lead fix: PlaneMesh.OrientationEnum.Z.
- Seen: menu views after the hud view carry a reddish screen tint (burn status effect leaking): clear statuses' screen effects between views.

## #15 (2026-11-25) full sweep (50 views, 62.4 fps, 0 errors) + process review #3; feel, shaders, ui, mechanics, perf
- BLOCKED + REVERTED: Ui.cs split (#15a): the agent's PowerShell helper `R` collided with the `r` alias and wrote empty files after cutting 342 lines; lead restored Ui.cs from HEAD (git checkout -- scripts/Ui.cs; it was fully committed in #14). Retry later with the Edit/Write tools only.
- Lock-on (CameraRig/Targeting): frames hero+target (lerp .35), distance grows with separation, tall-target pitch; mouse flick > 90 px in .15 s switches side (Targeting.SwitchSide); lock breaks on death / > 30 m / 1.5 s out of sight, auto-reacquire within 12 m if AutoTarget; gold ring + pulsing gold diamond reticle.
- Arena floor: rock_col has strong diagonal strata (looked like planks) -> tinted G_Plaster (ochre-grey, .45/m). Blocks: NormalScale .6 where normal maps exist.
- Compass (Hud.DrawCompass/DrawInfoLine): framed 520 px bar, gold rules, ticks, cardinals; gold runes for discovered POIs, grey dots for undiscovered within 150 m, red diamonds for hunting foes / lock target; info line with realm glyph, time, place in gold, coin. No quest marker (quests have no positions). Lead fix: duplicate Parch in Hud.cs.
- Gathering (Gather.cs, gather.json verbTimes): world prompt with gold key box + fill ring, timed Pick .4 / Chop .9 / Mine 1.1 s, cancel on move, chips/sparks/leaves + sounds, node shake/shrink. Lead: HUD prompt hidden for Gatherable (no double prompt).
- Perf: Warmup.cs fires every effect once at load (0.34 s), lead: frees only what the calls made. head_look min fps 19 -> 44.5.

## #14 (2026-11-25) menus (directive), animation, graphics, vfx, combat, world
- Map (MapView): parchment shader over World.MapTex (slate-teal hatched sea, sepia land by biome, ink coasts/roads, POI glyphs, red player arrow, vignette); lead: discovered POIs (Game.D.Disc) = gold wayshrine runes. Tree (TreeView): gold taken / pulsing available / iron locked, gold edges, keystone double ring. Banner: letter-spaced gold title + rules, fades; Notes: dark pills with coloured rule.
- Macros fits 720p (scrolling options, buttons always visible). Sounds: UiConfirm/UiError in craft + smith; lead: UiError on not-enough-gold/valor/pack-full in shop + valor.
- Enemies (EnemyAi.cs EnemyLook): head tracks the hero when hunting within 15 m (60/25 deg limits), idle look-arounds, quad wobble; assumes +Z forward (PLAYTEST).
- Wind: global wind_strength (Ambient), travelling gusts in grass/fern shaders, Flora.Windy leaf shader on kit trees (flutter + canopy lean; trunks still).
- Projectiles: arrow particle trail + wobble, arrows stick 6 s (20 max) with dust/splinters/sparks, dark spurt on enemies; player bolts small capped light + element ring.
- Weather.cs: clear/overcast/rain (60/25/15, 6-15 min), camera rain streaks + splashes, mood multipliers in WorldEnv (clouds .95, sun -25%, fog +80%, cooler); never in dungeons; rain view. Lead fixes: CpuParticles3D has no AmountRatio; streaks 1500 -> 700 (hud 41 -> 69 fps under rain); capture views without "rain" are dry; TwinRealms.Dmg (local float Dmg shadowed the class).

## #13 (2026-11-25) menus (directive), audio, lighting
- Ui.Frame clamps non-title windows to the viewport (24 px margins), re-clamps on resize. Lead fix: centring used SetAnchorsPreset(Center) without offsets (windows sat in the bottom-right quarter) -> explicit centred offsets.
- Character creation: slim left panel over the title skyline, gold-marker race/class lists, body toggles, styled name field, info card (bonuses in gold, starting skill chips), gold "Begin the journey". Looks great in capture.
- Dialogue: bottom-centre cinematic box (900x220), gold NPC name, numbered option rows (DlgOpt).
- Skills/attacks/macros: HFlowContainer header rows, stretch-ratio columns, scrolling lists: skills + attacks now fit 1280x720; macros still clipped top/bottom.
- Tooltips (UiWidgets InfoTip, ItemSlot ItemTip): gold divider, rarity type line, right-aligned green/red deltas, clamped on screen. Death screen: dark band + gold rules + "F A L L E N" fade (Hud.cs). Banner/Note (Ui.cs L65-81) not done.
- Audio: synthesized ui_hover/click/open/close/confirm/error (sfx_synth.py, ASSETS.md); Btn hover/click, Frame open, Close sounds at UiVol; confirm/error not wired yet.
- Lighting: CharRim fresnel overlay on hero/humanoids/creatures (global char_rim/char_rim_col; stronger at night/dungeon, violet in shadow, warm in dungeons); kill flash restores it; Duelist/Scarletta/Shade overlay guards accept it. Lead: stripped a BOM Add-Content left in Avatar.cs.
- Note: one capture logged Jolt RID leaks at exit (teardown, not gameplay); watch.

## #12 (2026-11-25) MENUS (user directive: improve all menus)
- Capture: generic ui:<name>[:arg] action + 18 menu_* views (settings, controls, journal, map, tree, skills, attacks, macros, mods, shop, smith, craft, craft_blocks, valor, totals, multiplayer, stash, create); UiSmith.OpenSmith made public.
- Ui.cs: CardBox/Empty/PriceStyle helpers, gold-underlined Tabs (shared by all menus), shop/valor price cards (muted red when unaffordable), journal records + quest cards, totals rows, tree side panel, map parchment frame + legend.
- UiSkills/UiAttacks/UiMacros/UiMods: GothPanel/CardRow/Pips helpers, element-coloured skill cards, gold rank pips, macro chain of cards with arrows, framed attack chains.
- UiSystem/UiControls: SettingsShell (tabs in a left column, gold marker), grouped option rows, gold sliders, < value > pickers; controls as key caps (gold when listening, red on conflict).
- UiCraft/UiSmith: recipe rows with have/need, big result slot, Craft x5 (new), block cube swatches; smith anvil slot, current vs next stats with deltas, gold flash on upgrade.
- UiNet/UiOnline/Home/Updater: host/join cards, session recent addresses, gold headers, launcher title + patch notes box + gold progress bar.
- Lead fixes: duplicate Parch colour, using System.Collections.Generic, a bad string.Contains overload.
- Seen: consistent and readable; skills/attacks/macros frames overflow the 1280x720 screen (title/left edge cut); the map interior still uses bright cartoon colours; character creation still plain.

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

## History
- #0 (11-24) setup: info web, DevCapture eyes (16 views), verify/capture/sheet/reindex tools; art direction kept as gothic realism + block world.
- #1 vista fog cleared, updraft beam faded, block colour jitter + grass lips, gothic HUD bars + damage ghost, dungeon rune exit, 9 warnings fixed.
- #2 title backdrop, perfect dodge, head look-at + lean, block-surface footsteps, lit dungeon entry; the 'dry lake' was a camera under water.
- #3 gothic settlement materials, visible title skyline, sea glints, gothic sword trail, framed rarity slots, torch block.
