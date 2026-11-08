#!/bin/bash
# Builds, then runs the look-and-feel observer in a real window.
# Output: observe_out/*.png (scenes, menus, animation strips) and observe_out/metrics.json.
set -e
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
dotnet build
"$GODOT" --path . -- --observe 2>&1 | tee observe_log.txt || true
test -f observe_out/metrics.json && echo "OBSERVE_OK" || { echo "OBSERVE_FAIL: no metrics written, see observe_log.txt"; exit 1; }
