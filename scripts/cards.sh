#!/usr/bin/env bash
# Card lookup. Usage:
#   cards.sh <gameId> <cardId> [<cardId> ...]   — specific cards
#   cards.sh <gameId> --hand                    — player 0's hand
#   cards.sh <gameId> --all                     — all cards in the game
set -e
API="http://localhost:7102/api"
GID="$1"; shift
if [ -z "$GID" ]; then echo "usage: cards.sh <gameId> <cardId|--hand|--all> [...]" >&2; exit 2; fi

python -c "
import json, urllib.request, sys

api = '$API'
gid = '$GID'
mode = sys.argv[1] if len(sys.argv) > 1 else ''
card_ids = sys.argv[1:] if mode not in ('--all', '--hand') else []

# Fetch all card metadata
with urllib.request.urlopen(f'{api}/cards?gameId={gid}') as r:
    all_cards = json.loads(r.read())

def show(cid):
    c = all_cards.get(cid)
    if c:
        tags = ', '.join(str(t) for t in c.get('tags', []))
        reqs = c.get('requirements', [])
        req_str = ', '.join(str(r) for r in reqs) if reqs else '-'
        print(f\"{cid}  {c['name']}  [{c['type']}]  cost={c['cost']}  tags=[{tags}]  req=[{req_str}]  {c.get('description','')}\")
    else:
        print(f'{cid}  (not found)')

if mode == '--all':
    for cid in sorted(all_cards.keys()):
        show(cid)
elif mode == '--hand':
    with urllib.request.urlopen(f'{api}/games/{gid}?playerId=0') as r:
        state = json.loads(r.read())
    hand = state.get('state', {}).get('players', [{}])[0].get('hand', [])
    if not hand:
        print('(empty hand)')
    else:
        for cid in hand:
            show(cid)
else:
    for cid in sys.argv[1:]:
        show(cid)
" "$@"
