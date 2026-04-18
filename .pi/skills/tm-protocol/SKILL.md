---
name: tm-protocol
description: How to play a Terraforming Mars game via the tm-engine HTTP API. Covers creating games, the turn loop, move JSON shapes, resolving pending sub-actions (PlaceTile / ChooseOption / etc.), card lookup, and which strategy skills to load. Load this first when asked to play TM.
---

# tm-protocol

Mechanical "how to play a TM game through the engine" reference. **Strategy
lives in other skills** — load them as documented at the bottom of this file.

Repo paths in this doc are relative to **repo root** `C:\Code\tm-engine\`.
Scripts are at repo root `scripts/` (`C:\Code\tm-engine\scripts\`).

## 0. Pre-flight

Check the Functions host is running:

```bash
curl -s -o /dev/null -w "%{http_code}" http://localhost:7102/api/games
```

- Expect `404` (route exists, GET not allowed — host is up).
- If connection refused: tell the user to start the host. They know the
  command; don't spawn it yourself.

## 1. Create a game

`POST /api/games` with JSON. **Use the exact camelCase keys below** — the
API rejects unknown keys (400), but missing bools silently default to
`false`, which is the trap we've hit before.

```bash
curl -s -X POST http://localhost:7102/api/games \
  -H "Content-Type: application/json" \
  -d '{
        "playerCount": 2,
        "map": "Hellas",
        "corporateEra": true,
        "draftVariant": true,
        "preludeExpansion": true,
        "seed": 42
      }'
# → {"gameId": "<hex>"}
```

Valid `map` values: `"Tharsis"`, `"Hellas"`, `"Elysium"`.

Save the returned `gameId` somewhere stable (e.g. `/tmp/current_game.txt`).

## 2. The turn loop

```bash
bash scripts/turn.sh <gameId>
```

`turn.sh` advances bot (player 1) moves until either:
- Player 0 can act (prints the current state + `HAND (id → name)` + raw
  legal-moves JSON), **or**
- The game ends (prints final scores + `GAME_OVER`).

If you read `GAME_OVER`, stop and report final scores.

## 3. Submit a move

```bash
bash scripts/submit.sh <gameId> '<move-json>'
```

Prints a compact summary (`OK Gen … / TR … / resources …`) or
`ERROR: <message>`. If the move triggered a sub-action (place a tile, pick
an option, target a player), the summary ends with a `PENDING …` line —
resolve it with another `submit.sh` call using the matching sub-move
type (see §4).

For the full raw response JSON, pass `--raw` as the 3rd arg:

```bash
bash scripts/submit.sh <gameId> '<move-json>' --raw
```

## 4. Move JSON reference

Every move includes `"type"` (case-insensitive) and `"playerId"` (always
`0` for you). Extra fields per type below.

### Phase moves

| Type | Extra fields | Notes |
|------|--------------|-------|
| `Setup` | `corporationId`, `preludeIds`: [...], `cardIdsToBuy`: [...] | With Prelude expansion, pick exactly 2 preludes. With no Prelude, use `[]`. |
| `DraftCard` | `cardId` | Only legal during Draft phase. |
| `BuyCards` | `cardIds`: [...] | Accepts `[]` for zero. |
| `Pass` | — | Ends your participation for the generation. |
| `EndTurn` | — | Skip remaining actions of current turn, generation continues. |
| `PlayPrelude` | `preludeId` | Prelude phase only. |
| `PerformFirstAction` | — | For corps with a mandatory first action (gen 1). |

### Action-phase moves

| Type | Extra fields | Notes |
|------|--------------|-------|
| `PlayCard` | `cardId`, `payment`: `{megaCredits, steel, titanium, heat, cardResources?}` | Use steel for Building, titanium for Space. |
| `UseCardAction` | `cardId`, optional `payment` | Once-per-gen card actions. |
| `SellPatents` | `cardIds`: [...] | 1 MC per card sold. |
| `PowerPlant` | — | 11 MC → +1 E prod. |
| `Asteroid` | — | 14 MC → +1 temp, +1 TR. |
| `Aquifer` | `location`: `{col, row}` | 18 MC → +1 ocean (must be in ocean-valid hex). |
| `Greenery` | `location`: `{col, row}` | 23 MC → greenery tile + raise O₂. |
| `City` | `location`: `{col, row}` | 25 MC. |
| `ClaimMilestone` | `milestoneName` | 8 MC, max 3 per game. |
| `FundAward` | `awardName` | 8 / 14 / 20 MC (1st / 2nd / 3rd). Max 3 per game. |
| `ConvertPlants` | `location`: `{col, row}` | 8 plants → greenery (Ecoline: 7). |
| `ConvertHeat` | — | 8 heat → +1 temp (+1 TR if temp not yet maxed; legal stall if maxed). |

### Sub-move resolution (only valid when `pendingAction` is set)

| Pending type | Move to submit | Extra fields |
|--------------|----------------|--------------|
| `PlaceTile` | `PlaceTile` | `location`: `{col, row}` |
| `ChooseOption` | `ChooseOption` | `optionIndex` (0-based) |
| `ChooseTargetPlayer` / `RemoveResource` | `ChooseTargetPlayer` | `targetPlayerId` |
| `SelectCard` / `CopyProduction` | `SelectCard` | `cardId` |
| `DiscardCards` / `MarsUniversity` | `DiscardCards` | `cardIds`: [...] (may be empty to skip if optional) |
| `ChooseEffectOrder` | `ChooseEffectOrder` | `effectIndex` (or `-1` to auto-resolve remaining) |

Always resolve the pending action before submitting an unrelated move —
`submit.sh`'s `PENDING …` line tells you which one is open.

### Payment examples

```json
// Pay 16 MC cash
{"megaCredits":16}

// Pay 14 MC + 1 steel for a Building card (steel = 2 MC each normally,
// +1 MC more per steel if you've played Advanced Alloys)
{"megaCredits":14,"steel":1}

// Pay 6 MC + 3 titanium for a Space card (Ti = 3 MC each normally,
// +1 MC more per Ti with Advanced Alloys)
{"megaCredits":6,"titanium":3}

// Helion-only: pay with heat
{"megaCredits":0,"heat":11}
```

## 5. Card lookup

```bash
bash scripts/cards.sh <gameId> <cardId> [<cardId> …]   # specific cards
bash scripts/cards.sh <gameId> --hand                  # all cards in your hand
bash scripts/cards.sh <gameId> --all                   # every card in the game
```

Prints `id  name  [type]  cost=N  tags=[...]  req=...  VP=...` + description.

## 6. Reference documents

When a decision depends on map layout, rules, or corner cases, read the
relevant user-authored file. Paths from the skill dir, or absolute:

- **Rules**: `../../../knowledge/rules/{base-game,corporate-era,prelude,hellas-elysium}.md`
- **Placement bonuses**: `../../../knowledge/maps/{tharsis,hellas,elysium}-placement-bonuses.md`

These may be empty/sparse — that's fine, play with the info available.

## 7. Which strategy skills to load, when

Load **at game start** (before the first move):

- `tm-memory` — read `MEMORY.md` for observations from prior sessions.
- `tm-strategy` — core strategy framework (archetypes, phases,
  production valuations, synergies). Consult throughout the game.
- `tm-corporations` — to pick a corp in Setup.
- `tm-maps-<map>` — the one matching the current game (`hellas` /
  `tharsis` / `elysium`).
- `tm-drafting` — only if `draftVariant: true`.
- `tm-starting-hand` — for corp/prelude/card keep decisions in Setup.

Load **mid-game as needed**:


- `tm-milestones-awards` — when claimable milestones or fundable awards
  become relevant.
- `tm-endgame` — when 2 of 3 globals are maxed.

Use `read` to load the relevant `SKILL.md` files; they're progressive
disclosure per pi's skills system.

## 8. Game-ending checklist

Game ends when all three globals max:

- Temperature: **+8 °C**
- Oxygen: **14 %**
- Oceans: **9 / 9**

Then a final-greenery phase runs (you can Convert Plants for free
greeneries if you have ≥ threshold). Submit `Pass` to exit that phase.

After game over:

1. Report final scores via `/api/games/<gameId>/status` (has full VP
   breakdown: TR / milestones / awards / greeneries / cities / cards).
2. Ask the user if there are observations worth appending to `MEMORY.md`.
   Only write per the `tm-memory` skill's rules.
