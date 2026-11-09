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
} else {
  & $godot --path . -- --observe 2>&1 | ForEach-Object { "$_" } | Out-File -Encoding utf8 observe_log.txt
  if (Test-Path observe_out/metrics.json) { "OBSERVE_OK" } else { "OBSERVE_FAIL: no metrics written, see observe_log.txt"; exit 1 }
}
