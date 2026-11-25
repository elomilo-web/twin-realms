# Dev loop capture: runs the game with --devcapture (scripts/dev/DevCapture.cs) and adds engine errors to the report.
#   powershell -File tools/capture.ps1                 -> every viewpoint in .devloop/viewpoints.json
#   powershell -File tools/capture.ps1 hud,village     -> only these viewpoints
# Output: .devloop/screenshots/current/*.png + report.json (previous run in screenshots/previous/). Exit 1 on failure.
param([string]$only = "")
$ErrorActionPreference = "Continue"
Set-Location (Join-Path $PSScriptRoot "..")
# .devloop/godot_path.txt is per machine (not committed); fallback: $env:GODOT, then the default in tools/run.ps1
$godot = if (Test-Path .devloop/godot_path.txt) { (Get-Content .devloop/godot_path.txt -Raw).Trim() } elseif ($env:GODOT) { $env:GODOT } else { Join-Path $env:USERPROFILE "Downloads\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe" }
if (-not (Test-Path $godot)) { "CAPTURE_FAIL: Godot not found at $godot (edit .devloop/godot_path.txt)"; exit 1 }
$gargs = @("--path", ".", "--", "--devcapture"); if ($only) { $gargs += "--devcapture-only=$only" }
$log = ".devloop/capture_log.txt"
$p = Start-Process -FilePath $godot -ArgumentList $gargs -RedirectStandardOutput $log -RedirectStandardError ".devloop/capture_err.txt" -PassThru -WindowStyle Normal
if (-not $p.WaitForExit(600000)) { $p.Kill(); "CAPTURE_FAIL: timed out after 10 min"; exit 1 }
$rep = ".devloop/screenshots/current/report.json"
if (-not (Test-Path $rep)) { "CAPTURE_FAIL: no report (crashed?), see $log"; Get-Content $log, .devloop/capture_err.txt -ErrorAction SilentlyContinue | Select-String "ERROR|Unhandled|Exception" | Select-Object -First 10; exit 1 }
# the long-known missing outfit normal maps are not counted (those files never existed)
# (stderr is a separate file, so a cut at "[DEVCAP] done" cannot work; the .NET teardown on quit logs a harmless "Handle is not initialized")
$all = @(Get-Content $log, .devloop/capture_err.txt -ErrorAction SilentlyContinue)
$errs = @($all | Select-String "SCRIPT ERROR|ERROR:|Unhandled exception|\[DEVCAP\].*failed|tour threw" | Where-Object { $_ -notmatch "assets/models/outfit/T_|Handle is not initialized" } | ForEach-Object { $_.Line.Trim() } | Select-Object -Unique -First 40)
$j = Get-Content $rep -Raw | ConvertFrom-Json; $j.errors = $errs
$j | ConvertTo-Json -Depth 5 | Out-File -Encoding utf8 $rep
$n = (Get-ChildItem .devloop/screenshots/current -Filter *.png).Count
foreach ($v in $j.views) { if ($v.error) { "  $($v.name): ERROR $($v.error)" } else { "  $($v.name): $($v.fps_avg) fps (min $($v.fps_min))" } }
"CAPTURE: $n pictures, avg $($j.fps_avg_all) fps, $($errs.Count) engine errors"
if ($errs.Count -gt 0) { $errs | Select-Object -First 10 }
if ($n -eq 0) { exit 1 }
exit 0
