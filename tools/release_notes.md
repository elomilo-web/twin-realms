## Twin Realms 1.2 beta (Windows)

**Download `TwinRealms-1.2-beta-Windows.zip` below, extract it and run `TwinRealms.exe`.** If Windows says "Windows protected your PC", click **More info** > **Run anyway**. Needs Windows 10 or 11 (64-bit) and a DirectX 12 graphics card. `HOW_TO_PLAY.txt` is inside the zip. Your heroes from 1.1 carry over.

Multiplayer: both players need 1.2 (1.1 cannot join 1.2 games). **Multiplayer works again**: in 1.1 a joining friend was dropped while their (much bigger) world loaded, so their moves and hits never reached the host.

### Monsters and difficulty
- **Monsters stay a threat.** Health and damage follow a curve that keeps rising with level, with a floor tied to your own power, so gear never makes them trivial. Armor is measured against the attacker's level instead of stacking to near-immunity. Levels give 2 attribute points (was 3).
- **The world keeps up with you.** Overworld monsters rise to your level (in multiplayer: everyone's average), and are 1.5x tougher with 2+ players.
- **The longer you play, the harder it gets.** Every 8 minutes out in the world monsters get stronger, respawn faster, and ambush packs come for you (shared with everyone in multiplayer).
- **Monsters don't know where you are.** They wander until you come close (in dungeons they must see you); their pack joins in when one notices you.
- **25 new monsters** with animated models: slimes, giant rats, spiders, wasps and crabs, vipers, flame skulls, orc warriors, hobgoblins, shadow ninjas, dark mages, yetis, demons, oni, ghosts, myconids, golems, cactus fiends, raptors, kitsune, and two world bosses: the Wyvern and the Myconid King.
- **The five new lands have monsters now** (178 of them), fitting each land, with a world boss in Emberwild and Elderwood.

### Dungeons and loot
- **3x the monsters** per floor, and a big difficulty jump every 5 floors.
- **Deeper floors drop rarer loot.** Each dungeon has its own drop table and named uniques.
- **Set gear:** each dungeon has a 5-piece set that drops from floor 10 down, with bonuses at 2 to 5 pieces worn.
- **Arena:** Wave Survival and the Shadow Duel give no experience or loot; duel rounds pay valor. Waves are bigger.

### Pack and crafting
- **Right-click an item to drop it** on the ground (a friend can pick it up, or you can).
- **Disassemble** items into 13 kinds of scraps, and **craft** gear, elemental gear, set pieces and potions from them.
- **Pickup filter:** choose the rarity and slots you pick up; leave the rest or scrap it automatically.

### Combat feel
- **Every attack chain step has its own animation** (O menu): 20 new swings, and friends see them too.
- **Heavy attacks have weight:** a pause after heavy swings and finishers before the next one (longer with two-handed weapons). Rolling cancels it.
- **Auto target** (Settings > Game): off, monsters that hit you, nearest, or lowest health.
- Faster running and sprinting that match the leg animation; sprinting costs far less stamina.
- The hero stands relaxed out of combat (the guarded stance only while fighting), and feet stay on the ground on slopes.

### Multiplayer
- See your friends' spell effects, damage and healing numbers, and status icons (each can be turned off in Settings > Game).

### For tinkering and testing
- **data/tuning.json:** one file with the game's balance numbers (base stats, scaling, drops, spawns, prices...).
- **F8 flags a moment** for review: a screenshot plus a report in %APPDATA%\Godot\app_userdata\TwinRealms\playtest\.

### Known limits
- No swimming yet: use the bridges.
- Cave mouths have no inside yet.
