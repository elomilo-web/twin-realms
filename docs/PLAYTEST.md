# Play testing with a friend

## Make the game
1. `powershell -File tools/run.ps1 export`
2. This builds `build/TwinRealms.exe` (plus `TwinRealms.pck` and a `data_TwinRealms_windows_x86_64` folder) and zips all of it as `TwinRealms-playtest.zip` (about 120 MB).
3. Send the zip (Google Drive, Discord, a USB stick). Your friend unzips it **anywhere** and runs `TwinRealms.exe`. Nothing else needs installing: .NET is inside the build.
   - Keep the three items together. The .exe alone will not start.
   - Windows SmartScreen may say "Windows protected your PC" because the game is not signed: click **More info > Run anyway**.

First time only: the export templates must be installed (`%APPDATA%\Godot\export_templates\4.7.2.stable.mono`). They are installed on this computer already.

## Play together
1. The host picks a hero on the title screen, then **Multiplayer > Host a game**. Windows asks to allow Twin Realms through the firewall: allow it (both Private and Public if you are unsure).
2. The friend picks their hero, **Multiplayer**, types the host's address, **Join**.
3. Addresses: same Wi-Fi uses the home address the host's menu shows (192.168.x.x). Over the internet see `docs/MULTIPLAYER.md` (UPnP, port forwarding UDP 24565, or Tailscale / Radmin VPN, which is the easiest).

## Saves
- Three heroes share one save file: `%APPDATA%\Godot\app_userdata\TwinRealms\save.json` (a `.bak` of the previous save sits next to it).
- Older single-hero saves load into slot 1 automatically.
- Return to title (Esc > Quit to title) saves and goes back to the character select screen.

## What to try
- Levels 1 to 5 come quickly; later levels take much longer (curve in `data/progression.json`).
- Each level: 3 attribute points (Esc > Character), 1 skill point (L), 2 skill tree points (K).
- Dungeons: monsters are your level + 2 (+1 per floor deeper), wear a gear tier and roll upgrades (Frenzied, Venomous, Searing, Stalwart...). Blue glow = one upgrade, gold glow = two or more. They give more experience and better loot.
