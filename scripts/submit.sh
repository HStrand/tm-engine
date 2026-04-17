#!/usr/bin/env bash
# Submit a move for player 0. Usage: submit.sh <gameId> '<move-json>'
set -e
GID="$1"
JSON="$2"
if [ -z "$GID" ] || [ -z "$JSON" ]; then
  echo "usage: submit.sh <gameId> '<move-json>'" >&2
  exit 2
fi
curl -s -X POST "http://localhost:7102/api/games/$GID/moves?playerId=0" \
  -H "Content-Type: application/json" \
  -d "$JSON" | head -c 4000
echo
