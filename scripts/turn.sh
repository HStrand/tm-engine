#!/usr/bin/env bash
# Harness driver: plays bot (player 1) moves until it's player 0's (Claude's) turn
# or the game ends, then prints state + legal moves. Usage: turn.sh <gameId>
set -e
GID="$1"
if [ -z "$GID" ]; then
  echo "usage: turn.sh <gameId>" >&2
  exit 2
fi

API="http://localhost:7102/api"
CLI_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

moves_json() {
  # $1 = playerId
  curl -s "$API/games/$GID/legal-moves?playerId=$1"
}

bot_move_count=0
while true; do
  # Check player 0 first — if P0 can act, stop and show state.
  p0=$(moves_json 0)
  if echo "$p0" | grep -q '"gameOver":true'; then
    # Game over — let the show command print final scores.
    break
  fi
  p0_waiting=$(echo "$p0" | grep -oE '"waitingForOtherPlayer":(true|false)' | head -1 | cut -d: -f2)

  p1=$(moves_json 1)
  p1_waiting=$(echo "$p1" | grep -oE '"waitingForOtherPlayer":(true|false)' | head -1 | cut -d: -f2)

  if [ "$p0_waiting" = "false" ]; then
    # Claude can act — stop and show state.
    break
  fi

  if [ "$p1_waiting" = "false" ]; then
    # Bot's turn — play one bot move.
    dotnet run --project "$CLI_ROOT/TmEngine.Cli" --no-build -c Release -- harness-bot "$GID" 1
    bot_move_count=$((bot_move_count + 1))
    continue
  fi

  # Neither can act — game is stuck or advancing silently. Give it a tick.
  sleep 0.2
  # Safety: avoid infinite loop
  bot_move_count=$((bot_move_count + 1))
  if [ "$bot_move_count" -gt 200 ]; then
    echo "STUCK — no progress after 200 iterations" >&2
    exit 1
  fi
done

# Show Claude's view of the board + legal moves.
dotnet run --project "$CLI_ROOT/TmEngine.Cli" --no-build -c Release -- harness-show "$GID"
