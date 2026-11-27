# Twin Realms Online (MMO server)

One shared world on a server that is always on. Players create an account with email and password, pick one of three heroes stored **on the server**, and play together with everyone online. Friend hosting (Multiplayer > Host / Join) still works as before.

## How it works
- **The server** is the game itself running without a window: `godot --headless --path . -- --server`. It builds the same seeded world, runs the overworld enemies, loot, parties and duels exactly like a friend host does, but has no hero of its own.
- **Connection**: WebSocket (`wss://`). Railway, like most hosts, only forwards HTTP/WebSocket traffic, not the UDP that friend hosting (ENet) uses. Railway adds the encryption (TLS) for `wss://`.
- **Login**: Godot's authentication step. Until the server accepts the login, the player gets no game messages and cannot send any.
- **Accounts**: `TR_DATA/accounts/<id>.json` holds the email, a random salt and a PBKDF2-SHA256 hash (120,000 rounds, `data/online.json` "hashIterations"). Passwords are never stored or logged; logs show `te***@example.com`.
- **Saves**: `TR_DATA/saves/<id>.json`, the same three-slot format as the local save file. The player's game uploads it on every save: every 20 s in the world (`saveEverySec`), on level changes, and when quitting. The previous copy is kept as `.bak`.
- **Protection**: a wrong password 5 times in a row locks the account for 60 s. One login per account (logging in elsewhere closes the older session). At most 60 login messages per connection. Saves over 1 MB or with the wrong shape are refused.
- **Server address**: fixed in the game, `wss://twin-realms-production.up.railway.app` (`OnlineCfg.Server` in `scripts/Accounts.cs`). Players cannot change it.
- **Settings**: `data/online.json` (port, max players, save interval, password length, lockout).

## Installer and launcher
- **Installer** (`installer/`, `TwinRealmsSetup.exe`, about 0.5 MB, .NET Framework 4.8 which Windows 10/11 include): installs for the current user into `%LOCALAPPDATA%\Programs\Twin Realms` (no admin prompt; the folder must stay writable so the game can update itself). It reads `data/release.json` from GitHub, downloads that zip, unpacks it, adds Start menu / desktop shortcuts and an Apps & features entry (uninstall: `TwinRealmsSetup.exe /uninstall`, which asks before deleting offline saves), then starts the game. Running it again repairs the install with the newest version. Link for players: `https://github.com/elomilo-web/twin-realms/releases/latest/download/TwinRealmsSetup.exe`. It is not code-signed, so Windows SmartScreen shows "More info > Run anyway".
- **Launcher** (`scripts/Updater.cs`): every start of the game opens it first (a 1280x760 window). It checks the server for the current version and updates the game with progress bars, shows the patch notes (from `tools/release_notes.md`, sent by the server), and has the online login with **Remember me** and **Log in automatically**. **PLAY ONLINE** (after logging in) goes straight to the online hero select; **PLAY OFFLINE** opens the usual title screen (offline heroes, Multiplayer).
- **Saved login**: the server hands out a login token (only its hash is stored in the account file, valid 30 days, at most 5 per account). With Remember me the launcher keeps it in `user://launcher.dat`, encrypted with a key tied to this computer; the password is never stored. Log out in the launcher also tells the server to forget the token.

## Updates on launch
Before the title screen the game asks the server which build players must run (`scripts/Updater.cs`). The server answers from `data/release.json`.
- The exported game has a different build: it downloads that release zip from GitHub (progress bar), closes, copies the new files over its folder and starts again. Saves are in %APPDATA% and are not touched.
- Same build: **Play Online** unlocks.
- Server unreachable, download failed, or the game cannot be updated (for example running from source on another network version): **Play Online** stays locked; offline play and friend hosting still work. The title screen says why.
- Releasing: `tools/run.ps1 publish` exports the game with a build id (`data/build.json`, not committed), uploads `TwinRealms-<version>-Windows-<build>.zip` as the GitHub Release, then writes, commits and pushes `data/release.json`. That push redeploys the server, and from then on every player's game updates itself on launch. Until the redeploy finishes (5-10 minutes) the server still names the previous build, whose zip is gone, so players see "download failed" and can try again shortly.
- `-- --no-update` skips the check (Play Online stays locked).

## Play
Title screen > **Play Online** > email and password > **Create account** the first time, **Log in** afterwards. Pick or create a hero, then **Play**. **Log out** (title) or **Quit to title** (pause menu) returns to the heroes saved on this computer.

## Deploy on Railway (from GitHub)
The repository already contains `Dockerfile`, `.dockerignore` and `railway.json`.
1. On railway.com: **New Project > Deploy from GitHub repo** > pick `twin-realms` (branch `main`, the only branch). Railway sees `railway.json` and builds the `Dockerfile`. The first build takes about 5-10 minutes (it downloads Godot and imports the assets).
2. Open the service > **Variables**: add `PORT` = `8080`.
3. Right-click the service (or the canvas) > **Attach Volume** > mount path **`/data`**. Without a volume every redeploy deletes all accounts and saves.
4. Service > **Settings > Networking > Generate Domain**, port **8080**. You get an address like `twin-realms-production.up.railway.app`.
5. **Deployments > View logs** should show:
   `[SERVER] Twin Realms <version> listening on port 8080 (WebSocket), up to 64 players, data in /data`
6. The domain must be exactly `twin-realms-production.up.railway.app` (the game always connects there). Then run `tools/run.ps1 publish` so players get a build that matches the server.

Every push to `main` redeploys the server (players are disconnected for a minute; their heroes are saved up to the last autosave). Keep **1 replica**: the world and the account files live in one process and one volume.

Cost: Railway bills for memory and CPU while the server runs; the Hobby plan includes $5 of usage a month. Check Metrics after a few days.

## Test
`tools/online_test.sh` starts a server on port 8089 with a temporary data folder and runs two test clients at the same time. Each registers, checks that short passwords, duplicate emails, wrong passwords and repeated wrong passwords are refused, creates a hero, enters the world, checks that it sees the other player, saves, logs out, logs in again and checks the hero and its progress came back from the server. Results: `bot_out/online_<pid>.txt`, `bot_out/online_server_log.txt`.

## Admins, commands and block rules (F-42 to F-44)
- **Command bar**: `/` or `~` in game (`scripts/CommandBar.cs`, registry `scripts/Commands.cs`, commands `scripts/AdminCommands.cs`). `/help` lists what you may use.
- **Admin access**: typing the admin login line into the bar once. The game holds only its PBKDF2-SHA256 hash (`scripts/Admin.cs`; a server can replace it with the environment variable `TR_ADMIN_HASH`). Online the server checks the line, sets `Admin` on the account file and checks that flag for every command (`scripts/NetAdmin.cs`). Wrong attempts: 5 per account (20 per address) in 10 minutes lock out for 10 minutes, doubling each time; every attempt goes to `TR_DATA/admin_log.txt` (never the typed text). `/deadmin <player>` removes it.
- **Bans and mutes** are stored on the account file (`BanUntil`, `BanReason`, `MuteUntil`); a banned account's login is refused with the reason. There is no chat yet, so a mute only shows in `/whois`.
- **No mining online**: the server refuses every removal of a world block by a non-admin (`BlockAudit.Judge`); players place blocks outside town zones and may take out blocks they placed themselves. Monsters drop building blocks instead (`data/blockdrops.json`, `scripts/BlockDrops.cs`).
- **Block log**: every accepted change is written to `TR_DATA/blocklog/blocks.log` (rotated at 1 MB, 5 files kept; `scripts/BlockAudit.cs`). `/blocklog` (aimed block, `r <radius>`, `<player>`) reads it; `/rollback <player> [time]` undoes a player's changes.
- **Limits**: the server has no block world of its own and forgets placed blocks when it restarts (as before), so after a restart the log can still be read but there is nothing left to roll back. It cannot tell air from a natural block, so a modified game could overwrite a natural block with a placed one. Behind Railway's proxy every player may share one address, so the per-address limit acts as a limit for everyone.
## Known limits (next steps)
- **The client is trusted with its own hero.** The game uploads the save and the server checks only its size and shape, so a modified game could give itself gold or items. Making the server authoritative over levels, items and gold is a large change.
- **No email verification or password reset.** Both need an email-sending service (for example Resend or SendGrid) and its API key.
- **R shifts the realm for everyone on the server**, as in friend hosting. With many strangers online this should probably become per-player or a world event.
- **The party chest is shared by every player on the server.**
- Dungeons and the arena stay private (or run by a party member), as in friend hosting.
- Tested with 2 players at once on one machine; 64 is the configured limit, not a measured one.
