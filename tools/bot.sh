#!/bin/bash
# Builds, then lets the self-test bot play the game in a real window (needed for screenshots).
# Results: bot_out/report.txt and bot_out/*.png.  Exit code 0 = all checks passed.
# Set GODOT to your Godot .NET executable if `godot` is not on your PATH.
set -e
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
dotnet build
rm -rf bot_out
"$GODOT" --path . -- --bot 2>&1 | tee bot_log.txt || true
echo; cat bot_out/report.txt 2>/dev/null || echo "No report written: the game crashed before the bot finished. See bot_log.txt"
grep -E "ERROR|SCRIPT ERROR|Unhandled" bot_log.txt && echo "ENGINE ERRORS FOUND" || true
