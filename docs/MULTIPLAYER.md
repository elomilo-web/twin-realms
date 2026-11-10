# Playing with friends

## Start a game
- **Host:** title screen > Multiplayer > Host a game. Or, already in the world: Esc > Multiplayer > Open my world to friends.
- **Join:** title screen > Multiplayer, type the host's address, then Join. You appear next to the host.
- Everyone brings their own hero: the one selected on the character select screen (3 slots), or a new one if that slot is empty. To share the game as an .exe see `docs/PLAYTEST.md`. Level, skills, skill tree, pack and **home storage** come with you and are saved on your own computer.

## Join codes`n- The host's Multiplayer menu shows join codes (like 4K7QM-2XH9D) with a Copy button: one for the internet, one per home network or VPN address. The friend pastes it under Join a friend. A code is just the address and port packed into 10 characters.`n- If the router does not tell the game its internet address, the game asks api.ipify.org (or icanhazip.com) once.`n- Fully address-free joining (no port forwarding at all) would need a relay server on the internet; the game does not have one. If UPnP fails and the router cannot be changed, use Tailscale or Radmin VPN.`n`n## Addresses
- Same house or Wi-Fi: the host's menu shows a home address (usually 192.168.x.x or 10.x.x.x). Friends type that.
- Over the internet: the host's game asks the router to open UDP port 24565 (UPnP) and shows the internet address if it worked. If not:
  - forward UDP port 24565 to the host's computer in the router settings, or
  - everyone installs a free virtual network (Tailscale, ZeroTier or Radmin VPN) and uses the address it gives the host.
- Windows may ask to allow Twin Realms through the firewall the first time you host: allow it.

## What is shared
| Thing | How it works |
|---|---|
| World | The host's world. Time of day and the Light/Shadow realm are shared: R shifts it for everyone. |
| Enemies | Run by the host. Everyone sees the same enemies and can fight them together; your hits are sent to the host. |
| Kills | The killer and party members within 90 m share the kill: 90% of the experience split evenly between them (data/progression.json "party"), kill count, quest progress. |
| Gold | Party kills: split evenly and paid straight to everyone nearby. |
| Items | Party leader picks: **Personal** (each member gets their own private roll), **Round robin**, **Leader takes items**, **Free for all**. Solo players own their drops. Reserved drops become free for all after 60 s. |
| Home storage | Your own chest ("Your Home", north of the plaza) holds your own items wherever you play. |
| Party chest | Next to your home chest. Everyone in the lobby uses the same chest; the host keeps it (user://party_chest.json). |
| Dungeons | Solo: private, monsters at your level + 2. In a party: one shared dungeon per party. Any dungeon door takes you to where your party is. Monsters are the average party level + 3 (+1 per floor). The first member in runs the monsters (the next member inside takes over if they leave). Kills pay everyone on the floor (90% of the experience split evenly), each member rolls their own loot with x1.15 drop chance. |`n| Arena | Private: each player gets their own copy. |`n| Drops | Party kills have a x1.15 item drop chance. |

## Parties and duels
- Walk up to a friend and press E, or use Esc > Multiplayer: **Invite to party** or **Challenge to a duel**.
- Duels: a glowing ring appears, a 3-second countdown, then fight. Damage between players is halved. Nobody dies: the first to drop to 1 health loses. Stepping out of the ring forfeits. Your wins are saved.

## For developers
- Code: `scripts/Net.cs` (connection, roster, poses, enemy snapshots, kills), `scripts/NetParty.cs` (loot rules, parties, party chest, duels, router port), `scripts/RemoteHero.cs`, `scripts/PlayerNet.cs`, `scripts/UiNet.cs`.
- Test: `powershell -File tools/run.ps1 mp` opens two game windows (host and joiner) that play through every feature and write `bot_out/mp_host.txt` and `bot_out/mp_join.txt`.
- Overworld enemies are matched by build order (`World.NetEnemies`); the world build is seeded, so every machine builds the same list (`[WORLD] ... fingerprint` in the log must match).
