# Twin Realms Online (MMO server)

One shared world on a server that is always on. Players create an account with email and password, pick one of three heroes stored **on the server**, and play together with everyone online. Friend hosting (Multiplayer > Host / Join) still works as before.

## How it works
- **The server** is the game itself running without a window: `godot --headless --path . -- --server`. It builds the same seeded world, runs the overworld enemies, loot, parties and duels exactly like a friend host does, but has no hero of its own.
- **Connection**: WebSocket (`wss://`). Railway, like most hosts, only forwards HTTP/WebSocket traffic, not the UDP that friend hosting (ENet) uses. Railway adds the encryption (TLS) for `wss://`.
- **Login**: Godot's authentication step. Until the server accepts the login, the player gets no game messages and cannot send any.
- **Accounts**: `TR_DATA/accounts/<id>.json` holds the email, a random salt and a PBKDF2-SHA256 hash (120,000 rounds, `data/online.json` "hashIterations"). Passwords are never stored or logged; logs show `te***@example.com`.
- **Saves**: `TR_DATA/saves/<id>.json`, the same three-slot format as the local save file. The player's game uploads it on every save: every 20 s in the world (`saveEverySec`), on level changes, and when quitting. The previous copy is kept as `.bak`.
- **Protection**: a wrong password 5 times in a row locks the account for 60 s. One login per account (logging in elsewhere closes the older session). At most 60 login messages per connection. Saves over 1 MB or with the wrong shape are refused.
- **Settings**: `data/online.json` (server address shown on the login screen, port, max players, save interval, password length, lockout).

## Play
Title screen > **Play Online** > type the server address (filled in from `data/online.json`), email and password > **Create account** the first time, **Log in** afterwards. Pick or create a hero, then **Play**. **Log out** (title) or **Quit to title** (pause menu) returns to the heroes saved on this computer.

## Deploy on Railway (from GitHub)
The repository already contains `Dockerfile`, `.dockerignore` and `railway.json`.
1. On railway.com: **New Project > Deploy from GitHub repo** > pick `twin-realms`, then under the service's **Settings > Source** pick the branch to run (`gothic-realism` has the newest game plus the server; `main` has the server on the older art). Railway sees `railway.json` and builds the `Dockerfile`. The first build takes about 5-10 minutes (it downloads Godot and imports the assets).
2. Open the service > **Variables**: add `PORT` = `8080`.
3. Right-click the service (or the canvas) > **Attach Volume** > mount path **`/data`**. Without a volume every redeploy deletes all accounts and saves.
4. Service > **Settings > Networking > Generate Domain**, port **8080**. You get an address like `twin-realms-production.up.railway.app`.
5. **Deployments > View logs** should show:
   `[SERVER] Twin Realms <version> listening on port 8080 (WebSocket), up to 64 players, data in /data`
6. Put the address in `data/online.json` as `"server": "wss://twin-realms-production.up.railway.app"`, push, and export a new Windows build (`tools/run.ps1 release`) so the login screen fills it in. Players can also type it.

Every push to the chosen branch redeploys the server (players are disconnected for a minute; their heroes are saved up to the last autosave). Keep **1 replica**: the world and the account files live in one process and one volume.

Cost: Railway bills for memory and CPU while the server runs; the Hobby plan includes $5 of usage a month. Check Metrics after a few days.

## Test
`tools/online_test.sh` starts a server on port 8089 with a temporary data folder and runs two test clients at the same time. Each registers, checks that short passwords, duplicate emails, wrong passwords and repeated wrong passwords are refused, creates a hero, enters the world, checks that it sees the other player, saves, logs out, logs in again and checks the hero and its progress came back from the server. Results: `bot_out/online_<pid>.txt`, `bot_out/online_server_log.txt`.

## Known limits (next steps)
- **The client is trusted with its own hero.** The game uploads the save and the server checks only its size and shape, so a modified game could give itself gold or items. Making the server authoritative over levels, items and gold is a large change.
- **No email verification or password reset.** Both need an email-sending service (for example Resend or SendGrid) and its API key.
- **R shifts the realm for everyone on the server**, as in friend hosting. With many strangers online this should probably become per-player or a world event.
- **The party chest is shared by every player on the server.**
- Dungeons and the arena stay private (or run by a party member), as in friend hosting.
- Tested with 2 players at once on one machine; 64 is the configured limit, not a measured one.
