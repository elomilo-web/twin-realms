# Twin Realms: beta download

A third-person open-world action RPG. Play it on Windows without installing anything.

## Download and play
1. Open the **[Releases page](https://github.com/elomilo-web/twin-realms/releases)** and download the newest **`TwinRealms-...-Windows.zip`** under *Assets*. (The game is too big to keep in the repository: GitHub allows files up to 100 MB.)
2. Right-click the downloaded zip > **Extract All...**
3. Open the extracted `TwinRealms` folder and double-click **`TwinRealms.exe`**.
   - Windows may show "Windows protected your PC" because the game is not signed: click **More info** > **Run anyway**.
   - Keep the files in the folder together.

Needs: Windows 10 or 11, 64-bit, a graphics card with DirectX 12 support. No .NET or other install needed.

Full instructions (controls, playing with a friend, saves) are in `HOW_TO_PLAY.txt`, also inside the zip.

## Playing with a friend
Both players need the **same version**. The host shares a **join code** from the Multiplayer menu (Copy); the friend pastes it (Paste > Join). See `HOW_TO_PLAY.txt`.

## What's in Beta 1.0
- Open world with two realms (Light and Shadow), a village, quests, shops, a blacksmith, gathering, wildlife and a horse.
- 8 races, 13 classes, a 417-node skill tree, 26 active skills with supports, 9 weapon types with combos.
- Three procedural dungeons with bosses. Monsters scale to you (your level + 2, +1 per floor) and wear better gear and roll upgrades (Frenzied, Venomous, Searing, Stalwart...) the deeper you go.
- 3 hero slots per save.
- Co-op for up to 8 players: shared world, parties with shared dungeons (average party level + 3), 90% of the experience split between party members, better drops in a party, a party chest and friendly duels.
- Join codes for internet play; the game opens the router port by itself when the router allows it (UPnP).
- Rebindable controls.

## Known limits
- Windows only for now.
- Friends do not see each other's spell area effects (novas, ground zones); arrows and bolts do show.
- Dungeon chests are per player.
- If your internet provider shares your address (CGNAT), direct internet joining does not work: use Tailscale or Radmin VPN.

## For the developer: making a new release
1. Change `config/version` in `project.godot` (and `Net.Protocol` in `scripts/Net.cs` if old and new games cannot play together).
2. `powershell -File tools/run.ps1 bot` and `powershell -File tools/run.ps1 mp` must pass.
3. Write the notes in `tools/release_notes.md`, commit and push.
4. `powershell -File tools/run.ps1 publish` builds the game and uploads it as a GitHub Release `v<version>` (pre-release) on the current branch. The zip itself is never committed.
