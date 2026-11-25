# Dev loop verify: dotnet build (errors and warnings in our own scripts only), then capture. Exit 1 on failure.
#   powershell -File tools/verify.ps1 [viewpoint,viewpoint]
param([string]$only = "")
Set-Location (Join-Path $PSScriptRoot "..")
$b = dotnet build -nologo -v q 2>&1 | Out-String
$code = $LASTEXITCODE
$lines = $b -split "`r?`n" | Where-Object { $_ -match ": (error|warning) " } | Select-Object -Unique
$errors = @($lines | Where-Object { $_ -match ": error " })
$warns = @($lines | Where-Object { $_ -match ": warning " })
if ($code -ne 0 -or $errors.Count -gt 0) { $errors | Select-Object -First 30; "BUILD_FAIL"; exit 1 }
"BUILD_OK ($($warns.Count) warnings)"; $warns | Select-Object -First 10
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "capture.ps1") $only
if ($LASTEXITCODE -ne 0) { "VERIFY_FAIL"; exit 1 }
"VERIFY_OK"
