# PowerShell version of tools/bot.sh and tools/observe.sh, for Windows machines without bash.
#   powershell -File tools/run.ps1 bot       -> builds, runs the self-test bot, prints FAIL lines and the total
#   powershell -File tools/run.ps1 observe   -> builds, runs the observer, writes observe_out/
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
  & $godot --path . -- --bot 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 bot_log.txt
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
} elseif ($mode -eq "mp") {
  # two game windows on this computer: one hosts, one joins 127.0.0.1 and plays through the multiplayer checks
  New-Item -ItemType Directory -Force bot_out | Out-Null
  Remove-Item bot_out/mp_*.txt -ErrorAction SilentlyContinue
  $h = Start-Process -FilePath $godot -ArgumentList "--path", ".", "--", "--mp-host" -RedirectStandardOutput mp_host_log.txt -RedirectStandardError mp_host_err.txt -PassThru -WindowStyle Normal
  Start-Sleep -Seconds 6
  & $godot --path . -- --mp-join 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 mp_join_log.txt
  if (-not $h.WaitForExit(240000)) { $h.Kill(); "HOST TIMED OUT" }
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
} elseif ($mode -eq "release") {
  # builds the game and puts the download for players in beta/ (one zip, replaced each release; GitHub allows files up to 100 MB)
  $ver = ((Select-String project.godot -Pattern 'config/version="([^"]+)"').Matches[0].Groups[1].Value) -replace '[^0-9A-Za-z.]+', '-'
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
  if ($mb -ge 100) { "NOTE: $zip is $mb MB. A git push refuses files of 100 MB or more; upload it as a GitHub Release asset instead (2 GB limit)." }
  "RELEASE_OK  $zip ($mb MB)"
} else {
  & $godot --path . -- --observe 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 observe_log.txt
  if (Test-Path observe_out/metrics.json) { "OBSERVE_OK" } else { "OBSERVE_FAIL: no metrics written, see observe_log.txt"; exit 1 }
}
