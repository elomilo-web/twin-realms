# PowerShell version of tools/bot.sh and tools/observe.sh, for Windows machines without bash.
#   powershell -File tools/run.ps1 bot       -> builds, runs the self-test bot, prints FAIL lines and the total
#   powershell -File tools/run.ps1 bot scaling,dungeons -> only the bot steps whose names contain these words
#   powershell -File tools/run.ps1 showcase  -> builds, takes ~10 showcase pictures of the game into screenshots/showcase/
#   powershell -File tools/run.ps1 quit      -> builds, plays a hero, quits to the title and plays again (QUIT_OK / QUIT_FAIL)
#   powershell -File tools/run.ps1 observe  -> builds, runs the observer, writes observe_out/
#   powershell -File tools/run.ps1 release   -> exports the Windows game and zips it into beta/ (the zip is not committed)
#   powershell -File tools/run.ps1 upload    -> uploads the zip already in beta/ (no rebuild)
#   powershell -File tools/run.ps1 publish   -> release, then uploads the zip as a GitHub Release v<version> (notes: tools/release_notes.md)
# Set $env:GODOT to your Godot .NET console executable if it is not at the default path below.
param([string]$mode = "bot")
$ErrorActionPreference = "Continue"
Set-Location (Join-Path $PSScriptRoot "..")
$godot = $env:GODOT
if (-not $godot) { $godot = Join-Path $env:USERPROFILE "Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }
$b = dotnet build 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { $b -split "`n" | Select-String "error" | Select-Object -First 30; "BUILD_FAIL"; exit 1 }
if ($mode -eq "bot") {
  if (Test-Path bot_out) { Remove-Item -Recurse -Force bot_out }
  New-Item -ItemType Directory -Force bot_out | Out-Null; New-Item -ItemType File -Force bot_out/.gdignore | Out-Null   # keeps Godot from importing test pictures
  # optional: tools/run.ps1 bot scaling,dungeons  -> only the steps whose names contain those words (fast checks while working on one thing)
  $only = @(); if ($args.Count -gt 0) { $only += "--only=" + ($args -join ",") }
  & $godot --path . -- --bot @only 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 bot_log.txt
  if (Test-Path bot_out/report.txt) { Get-Content bot_out/report.txt | Select-String "FAIL|passed" } else { "NO REPORT: the game crashed before the bot finished. See bot_log.txt"; exit 1 }
  $err = Get-Content bot_log.txt | Select-String "ERROR|SCRIPT ERROR|Unhandled|leaked"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} elseif ($mode -eq "fit" -or $mode -eq "fit-tune") {
  # gear fit bot (scripts/FitBot.cs): every weapon, shield and armor piece on mannequins -> bot_out/fit/ (report.txt, pictures, sheet_*.png)
  # fit-tune also writes bot_out/fit/fit_suggest.json. Extra args go to the game, e.g. tools/run.ps1 fit --only=head
  $extra = @(); if ($mode -eq "fit-tune") { $extra += "--tune" }; $extra += $args
  & $godot --path . -- --fit @extra 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 fit_log.txt
  if (Test-Path bot_out/fit/report.txt) { Get-Content bot_out/fit/report.txt | Select-String "^(FAIL|WARN|===)|^weapon |^shield:|^head " } else { "NO REPORT: the fit bot crashed. See fit_log.txt"; exit 1 }
  $err = Get-Content fit_log.txt | Select-String "ERROR|SCRIPT ERROR|Unhandled|Exception"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} elseif ($mode -eq "shots") {
  # screenshot tour (scripts/Shots.cs): hero day/night/dungeon, village, kit weapons, every skill -> screenshots/
  if (Test-Path screenshots) { Get-ChildItem screenshots -Filter *.png | Remove-Item }
  & $godot --path . -- --shots 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 shots_log.txt
  $n = (Get-ChildItem screenshots -Filter *.png -ErrorAction SilentlyContinue).Count
  "SHOTS: $n screenshots in screenshots/ (log: shots_log.txt)"
  $err = Get-Content shots_log.txt | Select-String "ERROR|SCRIPT ERROR|Unhandled|Exception|\[SPELLFX\]|\[KIT\]|\[WEAPONS\]|\[GENSHIN\]"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 30 }
} elseif ($mode -eq "showcase") {
  # showcase bot (scripts/Showcase.cs): ~10 good-looking pictures of the game on top graphics -> screenshots/showcase/
  if (Test-Path screenshots/showcase) { Get-ChildItem screenshots/showcase -Filter *.png | Remove-Item }
  & $godot --path . -- --showcase 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 showcase_log.txt
  $n = (Get-ChildItem screenshots/showcase -Filter *.png -ErrorAction SilentlyContinue).Count
  "SHOWCASE: $n pictures in screenshots/showcase/ (log: showcase_log.txt)"
  $err = Get-Content showcase_log.txt | Select-String "ERROR|SCRIPT ERROR|Unhandled|Exception|missing place"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} elseif ($mode -eq "quit") {
  # quit-to-title check (Main.cs --quit-test): starts a hero, quits to the title, then plays the hero again
  & $godot --path . -- --quit-test 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 quit_log.txt
  $l = Get-Content quit_log.txt; $l | Select-String "\[QT\]"
  if (($l | Select-String "title up=True") -and ($l | Select-String "replay started=True player=True")) { "QUIT_OK" } else { "QUIT_FAIL (see quit_log.txt)" }
  $err = $l | Select-String "ERROR|Unhandled|Exception"; if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} elseif ($mode -eq "mp") {
  # two game windows on this computer: one hosts, one joins 127.0.0.1 and plays through the multiplayer checks
  New-Item -ItemType Directory -Force bot_out | Out-Null
  Remove-Item bot_out/mp_*.txt -ErrorAction SilentlyContinue
  $h = Start-Process -FilePath $godot -ArgumentList "--path", ".", "--", "--mp-host" -RedirectStandardOutput mp_host_log.txt -RedirectStandardError mp_host_err.txt -PassThru -WindowStyle Normal
  # the host needs ~25 s to build the world: start the joiner 30 s later; the joiner itself waits another 30 s before it joins (MpBot)
  Start-Sleep -Seconds 30
  & $godot --path . -- --mp-join 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 mp_join_log.txt
  if (-not $h.WaitForExit(420000)) { $h.Kill(); "HOST TIMED OUT" }
  foreach ($s in "host", "join") {
    if (Test-Path "bot_out/mp_$s.txt") { "--- $s"; Get-Content "bot_out/mp_$s.txt" | Select-String "FAIL|passed" } else { "NO REPORT from $s (see mp_${s}_log.txt)" }
  }
  $err = Get-Content mp_join_log.txt, mp_host_log.txt, mp_host_err.txt -ErrorAction SilentlyContinue | Select-String "ERROR|Unhandled|Exception"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} elseif ($mode -eq "export") {
  # builds the Windows game into build/ and zips it as TwinRealms-playtest.zip (send the zip to a friend; they unzip and run TwinRealms.exe)
  if (Test-Path build) { Remove-Item -Recurse -Force build }
  New-Item -ItemType Directory -Force build | Out-Null
  & $godot --headless --path . --export-release "Windows Desktop" build/TwinRealms.exe 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 export_log.txt
  if (-not (Test-Path build/TwinRealms.exe)) { "EXPORT_FAIL: see export_log.txt (are the export templates installed? Godot > Editor > Manage Export Templates)"; exit 1 }
  if (Test-Path TwinRealms-playtest.zip) { Remove-Item TwinRealms-playtest.zip }
  Compress-Archive -Path build/* -DestinationPath TwinRealms-playtest.zip
  "EXPORT_OK  build/TwinRealms.exe  ->  TwinRealms-playtest.zip ($([math]::Round((Get-Item TwinRealms-playtest.zip).Length / 1MB)) MB)"
} elseif ($mode -eq "release" -or $mode -eq "publish" -or $mode -eq "upload") {
  # builds the game and puts the download for players in beta/ (one zip, replaced each release; GitHub allows files up to 100 MB)
  $ver = ((Select-String project.godot -Pattern 'config/version="([^"]+)"').Matches[0].Groups[1].Value) -replace '[^0-9A-Za-z.]+', '-'
  $zip = "beta/TwinRealms-$ver-Windows.zip"
  if ($mode -eq "upload") { if (-not (Test-Path $zip)) { "UPLOAD_FAIL: $zip missing, run publish"; exit 1 } } else {
  if (Test-Path build) { Remove-Item -Recurse -Force build }
  New-Item -ItemType Directory -Force build, beta | Out-Null
  & $godot --headless --path . --export-release "Windows Desktop" build/TwinRealms.exe 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 export_log.txt
  if (-not (Test-Path build/TwinRealms.exe)) { "EXPORT_FAIL: see export_log.txt"; exit 1 }
  $stage = "build_stage/TwinRealms"; if (Test-Path build_stage) { Remove-Item -Recurse -Force build_stage }
  New-Item -ItemType Directory -Force $stage | Out-Null; Copy-Item -Recurse build/* $stage
  Copy-Item beta/HOW_TO_PLAY.txt $stage -ErrorAction SilentlyContinue
  Get-ChildItem beta -Filter *.zip | Remove-Item
  $zip = "beta/TwinRealms-$ver-Windows.zip"
  Compress-Archive -Path build_stage/TwinRealms -DestinationPath $zip -CompressionLevel Optimal
  Remove-Item -Recurse -Force build_stage
  $mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
  "RELEASE_OK  $zip ($mb MB)"
  }
  if ($mode -eq "publish" -or $mode -eq "upload") {
    # upload the zip as a GitHub Release (2 GB per file, so the 100 MB git limit does not apply). Uses the GitHub login git already has
    # (Windows Credential Manager); the release is tagged v<version> on the current branch and marked as a pre-release.
    # Release notes: tools/release_notes.md if it exists. Extra arg: the branch/commit to tag (default: the current branch).
    $git = (Get-Command git -ErrorAction SilentlyContinue).Source
    if (-not $git) { $git = Get-ChildItem "$env:LOCALAPPDATA\GitHubDesktop\app-*\resources\app\git\cmd\git.exe" | Select-Object -Last 1 -ExpandProperty FullName }
    $remote = & $git remote get-url origin; $repo = ($remote -replace '^https://github.com/', '' -replace '\.git$', '')
    # (fed from a file: a PowerShell pipe adds a byte-order mark and git then rejects the request)
    $q = Join-Path $env:TEMP "tr_credq.txt"; [IO.File]::WriteAllText($q, "protocol=https`nhost=github.com`n`n")
    $cred = cmd /c "`"$git`" credential fill < `"$q`"" 2>$null; Remove-Item $q -ErrorAction SilentlyContinue
    $token = ($cred | Where-Object { $_ -like "password=*" }) -replace '^password=', ''
    if (-not $token) { "PUBLISH_FAIL: no saved GitHub login. Push once from a normal terminal to sign in."; exit 1 }
    $h = @{ Authorization = "Bearer $token"; Accept = "application/vnd.github+json"; "User-Agent" = "twin-realms-release" }
    $branch = if ($args.Count -gt 0) { $args[0] } else { (& $git branch --show-current) }
    $tag = "v" + $ver
    $notes = if (Test-Path tools/release_notes.md) { [IO.File]::ReadAllText((Resolve-Path tools/release_notes.md)) } else { "Twin Realms $ver for Windows. Download the zip below, extract it and run TwinRealms.exe." }
    $old = $null; try { $old = Invoke-RestMethod -Headers $h "https://api.github.com/repos/$repo/releases/tags/$tag" } catch {}
    if ($old) { "Replacing the existing release $tag"; Invoke-RestMethod -Method Delete -Headers $h "https://api.github.com/repos/$repo/releases/$($old.id)" | Out-Null; try { Invoke-RestMethod -Method Delete -Headers $h "https://api.github.com/repos/$repo/git/refs/tags/$tag" | Out-Null } catch {} }
    $body = @{ tag_name = $tag; target_commitish = $branch; name = "Twin Realms $($ver -replace '-', ' ')"; body = $notes; prerelease = $true } | ConvertTo-Json
    try {
    $rel = Invoke-RestMethod -Method Post -Headers $h -ContentType "application/json; charset=utf-8" -Body ([Text.Encoding]::UTF8.GetBytes($body)) "https://api.github.com/repos/$repo/releases"
    $name = Split-Path $zip -Leaf
    $up = Invoke-RestMethod -Method Post -Headers $h -ContentType "application/zip" -InFile $zip -TimeoutSec 3600 "https://uploads.github.com/repos/$repo/releases/$($rel.id)/assets?name=$name"
    "PUBLISH_OK  $($rel.html_url)"
    "DOWNLOAD    $($up.browser_download_url)"
    } catch { "PUBLISH_FAIL: $($_.Exception.Message) $($_.ErrorDetails.Message)"; exit 1 }
  }
} else {
  & $godot --path . -- --observe 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 observe_log.txt
  if (Test-Path observe_out/metrics.json) { "OBSERVE_OK" } else { "OBSERVE_FAIL: no metrics written, see observe_log.txt"; exit 1 }
}
