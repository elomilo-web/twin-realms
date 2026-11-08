# Handoff from the cloud chat to Claude Code on the user's PC

The user is new to terminals and git. Explain things in plain, beginner-friendly steps. Keep it simple and efficient.

## State
- Full game ported from the browser prototype (docs/spec/) to Godot 4.x .NET (C#). Latest push: loot-freeze fix, player arms, HUD and menu restyle.
- The port was written without a compiler. It builds and runs for the user now, but expect more bugs.
- Visuals are placeholder boxes and capsules. The user will add real assets slowly, once all mechanics are in place.

## First tasks
1. Run tools/smoke_test.sh. Fix every build error or script error.
2. Add a bot mode: the game plays itself for about 60 seconds (walk, fight wolves, loot, enter a dungeon, open each menu), writes a log and screenshots to a folder. Use it to catch crashes.
3. Compare the HUD and menus against the browser prototype (docs/spec/browser_prototype.html, CSS at the top) and match them.
4. Check: arms and weapons per class, combat feel, arena waves and duel, dungeons spawn mobs, quests, shops, save and load, realm shift.
5. Then polish toward AAA mechanics. Art comes later.

## Rules
- Data lives in data/*.json. Never hard-code stats in C#.
- Commit small and often. Commit trailers: Co-Authored-By line for the model in use.
