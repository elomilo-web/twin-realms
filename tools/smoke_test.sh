#!/bin/bash
# Builds and runs the project headless for a few seconds. Fails on any build error or script exception.
# Claude Code: adapt this once the main scene exists (set GODOT to your Godot .NET binary).
set -e
GODOT="${GODOT:-godot}"
dotnet build
timeout 20 "$GODOT" --headless --path . --quit-after 300 2>&1 | tee /tmp/smoke.log
! grep -E "ERROR|SCRIPT ERROR|Unhandled exception" /tmp/smoke.log
echo SMOKE_OK
