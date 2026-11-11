#!/bin/bash
# Screenshot tour (scripts/Shots.cs): the hero by day, by night and in a dungeon, the village, the kit weapons and every skill.
# Writes PNGs to screenshots/ and quits. Needs a window (not --headless). On Windows without bash: powershell -File tools/run.ps1 shots
# Set GODOT to your Godot .NET executable if `godot` is not on your PATH.
set -e
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
dotnet build
mkdir -p screenshots && rm -f screenshots/*.png
"$GODOT" --path . -- --shots 2>&1 | tee shots_log.txt
echo "SHOTS: $(ls screenshots/*.png 2>/dev/null | wc -l) screenshots in screenshots/"
if grep -E "ERROR|SCRIPT ERROR|Unhandled|\[SPELLFX\]|\[KIT\]|\[WEAPONS\]" shots_log.txt; then echo "ENGINE ERRORS FOUND"; exit 1; fi
