# Network
Purpose: Peer-to-peer multiplayer (host/join, parties, loot rules, duels), the dedicated online server (MMO mode, accounts, cloud saves) and the launcher/auto-updater.
## Files
- `scripts/Net.cs` (441 lines): Net autoload node, ENet host/join, roster, hero sync, RPC hub. Key: `Host()` L86, `Join()` L101, `Shutdown()` L112, `R_Hello()` L173, `R_Welcome()` L188, `SendRoster()` L200, `SyncHeroes()` L222
- `scripts/NetOnline.cs` (264 lines): WebSocket online server + client auth handshake. Key: `Serve()` L48, `ServerAuth()` L61, `AuthDone()` L117, `R_SaveUp()` L152, `Connect()` L163, `ClientAuth()` L186, `EnterOnline()` L214, `UploadSave()` L222, `AskVersion()` L246
- `scripts/NetParty.cs` (385 lines): shared loot, party invites, loot modes, duels, party chest. Key: `DropLoot()` L32, `Claim()` L78, `Invite()` L115, `LeaveParty()` L141, `SetLootMode()` L152
- `scripts/NetFx.cs` (92 lines): ambush spawns, shared fx/damage numbers. Key: `AmbushSpawn()` L14, `AskEnemyList()` L33, `Fx()` L54, `Number()` L78
- `scripts/NetAggro.cs` (50 lines): taunts, scar grab, enchant, revive over the wire. Key: `SendTaunt()` L9, `SendEnchant()` L36, `ReviveRemote()` L41
- `scripts/PlayerNet.cs` (35 lines): hero's pose packet and duel save. Key: `NetPacket()` L14, `DuelSave()` L29
- `scripts/RemoteHero.cs` (159 lines): friend's avatar (HeroRig). Key: `Refresh()` L54, `Push()` L82, `_Process()` L88, `AnimateAvatar()` L139
- `scripts/Accounts.cs` (201 lines): server-side accounts, token login, cloud saves, OnlineCfg. Key: `OnlineCfg.Load()` L20, `Register()` L82, `Login()` L98, `IssueToken()` L136, `LoginToken()` L152, `LoadSave()` L177
- `scripts/NetBreak.cs` (A-37): breakable props online: R_BreakAsk / R_BreakGrant / R_BreakSet / R_BreakSync; server table in Breakables.Auth (BreakServer), TR_DATA/breakables.json. Protocol 17.
- `scripts/UiNet.cs` (205 lines): multiplayer windows. Key: `OpenMultiplayer()` L23, `LobbyPage()` L69, `Ask()` L136, `OpenPlayerMenu()` L147, `PartyChestNode` L186
- `scripts/UiOnline.cs` (46 lines): Play Online login screen. Key: `ShowOnline()` L10, `OnOnlineChanged()` L38
- `scripts/Updater.cs` (475 lines): launch check, zip download/unpack/restart, launcher UI + account panel. Key: `Updater.Wanted()` L27, `Load()` L76, `ShowLauncher()` L104, `OnServerVersion()` L206, `OnZipDone()` L254, `Install()` L308, `BuildAccount()` L335
## Scenes / nodes
Built in code. Autoload-style `Net` child of Main (/root/Main/Net); `RemoteHero` nodes per peer; `PartyChestNode`; Loot nodes tracked in `LootNodes`.
## Data
data/online.json (server URL), data/build.json (local build id/notes), data/release.json (published version); server dir TR_DATA or user://server (accounts/*.json, saves/*.json); user://launcher.dat (remembered token); user://update/ (zip); party chest file (`Net.ChestPath`).
## Links
[[core]] [[player]] [[loot]] [[enemies]] [[dungeons]] [[ui]] [[devtools]] [[progression]]
## Scores
- visuals n/a: mostly logic; lobby/launcher UI is plain panels
- feel 6/10: host-authoritative with pose packets; no interpolation/prediction audit, latency unknown
- code 6/10: Net is one partial class split across 4 files with dense one-liners; solid test bots (MpBot, OnlineBot)
## Ideas
- Add pose interpolation/extrapolation buffer in `RemoteHero.Push()`/`_Process()` to hide jitter.
- Split `Net.cs` roster/party state into a PartyState class (`Net.cs` PartyInfo/`SendRoster()`).
- Rate-limit and validate RPC args server-side in `NetOnline.ServerAuth()` and `Net.R_Hello()`.
- Add password hashing with salt/KDF check in `Accounts.Login()` (currently SHA256 key); lockout on repeated failures.
- Show ping and connection quality in `UiNet.LobbyPage()`.
- Delta-compress `PlayerNet.NetPacket()` floats to cut bandwidth.
- Resume/reconnect after drop in `Net.OnServerGone()`.
- Verify zip checksum in `Updater.OnZipDone()` before `Install()`.
