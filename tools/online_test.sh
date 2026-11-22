#!/bin/bash
# Online server self-test: starts a local headless server, runs the online test client against it, stops the server.
# Results: bot_out/online.txt (client) and bot_out/online_server_log.txt. Exit code 0 = all checks passed.
# Set GODOT to your Godot .NET executable if `godot` is not on your PATH.
set -e
cd "$(dirname "$0")/.."
GODOT="${GODOT:-godot}"
PORT="${TEST_PORT:-8089}"
dotnet build
[ -d .godot/imported ] || "$GODOT" --headless --path . --import
mkdir -p bot_out
DATA="$(mktemp -d)"
TR_DATA="$DATA" PORT="$PORT" "$GODOT" --headless --path . -- --server > bot_out/online_server_log.txt 2>&1 &
SERVER=$!
trap 'kill $SERVER 2>/dev/null || true; rm -rf "$DATA"' EXIT
for i in $(seq 1 120); do grep -q "listening on port" bot_out/online_server_log.txt && break; sleep 1; done
grep -q "listening on port" bot_out/online_server_log.txt || { echo "Server did not start:"; tail -30 bot_out/online_server_log.txt; exit 1; }
set +e
# two clients at once: each runs every check and must also see the other player in the shared world
timeout 400 "$GODOT" --headless --path . -- --online-test --expect=2 --url="ws://127.0.0.1:$PORT" > bot_out/online_client2_log.txt 2>&1 &
SECOND=$!
timeout 400 "$GODOT" --headless --path . -- --online-test --expect=2 --url="ws://127.0.0.1:$PORT" 2>&1 | tee bot_out/online_client_log.txt
CODE=${PIPESTATUS[0]}
wait $SECOND; CODE2=$?
echo "--- second client:"; grep ONLINEBOT bot_out/online_client2_log.txt
[ "$CODE2" = 0 ] || CODE=1
set -e
echo "--- saved on the server:"; ls "$DATA/accounts" "$DATA/saves" 2>/dev/null
if grep -E "SCRIPT ERROR|Unhandled exception|FATAL" bot_out/online_client_log.txt bot_out/online_client2_log.txt bot_out/online_server_log.txt; then echo "ENGINE ERRORS FOUND"; CODE=1; fi
[ "$CODE" = 0 ] && echo ONLINE_OK || echo "ONLINE_FAIL ($CODE)"
exit $CODE
