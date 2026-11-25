# Playtest checklist (things the dev loop built but could only check by screenshots)

Play a few minutes and tick what works; write problems into DIRECTIVES.md (or tell Claude). Newest first.

## Combat
- [ ] Perfect dodge (#2): roll just as an enemy blow lands -> brief slow motion, pale flash, "Perfect dodge!", next hit x1.5.
- [ ] Guard (F) (#7): blocked hits spark + clang + small push back; guard breaks at 0 stamina ("Guard broken").
- [ ] Parry (#7): press guard ~0.15 s before a blow -> "Parry!", enemy stunned, next hit within 1.5 s is a x2 "Riposte!".
- [ ] Hit weight (#4): heavy weapons stop longer on hit; camera punches along the swing; crits/kills feel bigger.
- [ ] Enemy telegraphs (#4): glint before attacks; red ring before slams/boss blows; big hits stagger enemies.
- [ ] Enemy flinch + death fall (#9): enemies twist away from hits and tip over when they die.

## Movement and feel
- [ ] Combo (#18): each step's trail grows; the finisher hits with a gold ring and dust; time presses near the end of each swing for 'Rhythm'.
- [ ] Boss bar (#18): bottom-centre bar with name, draining ghost and a damage counter.
- [ ] Lock-on (#15): camera frames you and the target; flick the mouse sideways to switch targets; lock drops when the target dies or hides.
- [ ] Gathering (#15): press E on herbs/logs/ore: short timed action with a ring, chips/sparks, node shrinks.
- [ ] Camera (#11): leads slightly where you run, eases out after walls/trees (no jitter?), slight over-the-shoulder offset.
- [ ] Chests (#11): lid swings open, golden light, loot pops out; rare items get a coloured light beam.
- [ ] Sprint (#7): wider view, dust kick at start, short skid when stopping.
- [ ] Landing (#4): falls over ~1.2 m puff dust, thud, small camera shake.
- [ ] Hero idle (#6): breathing, weight shift, glances around; head turns toward nearby foes (#2).

## Sound (levels are untested by ear)
- [ ] Rain + thunder (#16): rain loop follows the rain, thunder after a sky flash in heavy rain; menu sounds (#13).
- [ ] Critical hits (#16): flash, spark streak, sharp clash; crit kills slow time for a blink.
- [ ] Footsteps change with the block under your feet (#2): grass, dirt, stone, wood, sand, snow, gravel, ice, water (#6).
- [ ] Landing thud and water splash/wading (#6) are synthesized: are they too loud/soft?

## World and systems
- [ ] Ladders and doors (#17): craft in the Blocks tab, place with N; climb ladders (forward/jump up, back down); E opens/closes doors.
- [ ] Dodge roll (#17): dust + smoke trail; does the hero face the roll direction? (code for that is commented out)
- [ ] Rain (#14): every 6-15 min the weather may turn to rain (streaks, splashes, darker foggier sky); never in dungeons.
- [ ] Wind (#14): grass gusts and tree leaves sway; trunks stay still.
- [ ] Enemy heads (#14): hunting enemies turn their heads to you; idle ones look around (tell me if a creature looks the wrong way).
- [ ] Arrows (#14): stick in the ground for a few seconds; magic bolts glow in their element colour.
- [ ] Menus (#12-#14): every window fits the screen, buttons work, sounds on hover/click; map travel by clicking a gold rune.
- [ ] Torch block (#3): craft 4 torches from 1 planks + 1 coal in the Blocks tab, place with N (build mode), they light up.
- [ ] Fast travel (#6): blocked while foes hunt you and for 30 s after travelling.
- [ ] Wayshrines (#8): obelisks at each place light up when discovered.
- [ ] Pause menu (#7) and character sheet (#9): every button still works.
