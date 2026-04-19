#!/usr/bin/env bash
# Show AI's (Player 1) view: state + hand + legal moves.
# Unlike turn.sh this does NOT auto-advance any bot moves — both players
# are real (human P0, AI P1). Just shows what P1 can see and do.
# Usage: turn-ai.sh <gameId>
set -e
GID="$1"
if [ -z "$GID" ]; then
  echo "usage: turn-ai.sh <gameId>" >&2
  exit 2
fi
CLI_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
dotnet run --project "$CLI_ROOT/TmEngine.Cli" --no-build -c Release -- harness-show-ai "$GID"
