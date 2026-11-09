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
  & $godot --path . -- --bot 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 bot_log.txt
  if (Test-Path bot_out/report.txt) { Get-Content bot_out/report.txt | Select-String "FAIL|passed" } else { "NO REPORT: the game crashed before the bot finished. See bot_log.txt"; exit 1 }
  $err = Get-Content bot_log.txt | Select-String "ERROR|SCRIPT ERROR|Unhandled|leaked"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} elseif ($mode -eq "mp") {
  # two game windows on this computer: one hosts, one joins 127.0.0.1 and plays through the multiplayer checks
  New-Item -ItemType Directory -Force bot_out | Out-Null
  Remove-Item bot_out/mp_*.txt -ErrorAction SilentlyContinue
  $h = Start-Process -FilePath $godot -ArgumentList "--path", ".", "--", "--mp-host" -RedirectStandardOutput mp_host_log.txt -RedirectStandardError mp_host_err.txt -PassThru -WindowStyle Normal
  Start-Sleep -Seconds 6
  & $godot --path . -- --mp-join 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 mp_join_log.txt
  if (-not $h.WaitForExit(90000)) { $h.Kill(); "HOST TIMED OUT" }
  foreach ($s in "host", "join") {
    if (Test-Path "bot_out/mp_$s.txt") { "--- $s"; Get-Content "bot_out/mp_$s.txt" | Select-String "FAIL|passed" } else { "NO REPORT from $s (see mp_${s}_log.txt)" }
  }
  $err = Get-Content mp_join_log.txt, mp_host_log.txt, mp_host_err.txt -ErrorAction SilentlyContinue | Select-String "ERROR|Unhandled|Exception"
  if ($err) { "ENGINE ERRORS FOUND"; $err | Select-Object -First 20 }
} else {
  & $godot --path . -- --observe 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 observe_log.txt
  if (Test-Path observe_out/metrics.json) { "OBSERVE_OK" } else { "OBSERVE_FAIL: no metrics written, see observe_log.txt"; exit 1 }
}
