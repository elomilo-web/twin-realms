#!/bin/bash
# Builds the C# project, then runs the game headless for ~5 seconds. Fails on build errors or script exceptions.
# Set GODOT to your Godot .NET executable if `godot` is not on your PATH, e.g.
#   Mac:     GODOT="/Applications/Godot_mono.app/Contents/MacOS/Godot" tools/smoke_test.sh
#   Windows: GODOT="C:/path/to/Godot_v4.x_mono_win64.exe" bash tools/smoke_test.sh
set -e
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
dotnet build
LOG="$(mktemp)"
timeout 60 "$GODOT" --headless --path . --quit-after 300 2>&1 | tee "$LOG"
if grep -E "ERROR|SCRIPT ERROR|Unhandled exception|Failed to load" "$LOG"; then echo "SMOKE_FAIL"; exit 1; fi
echo SMOKE_OK
