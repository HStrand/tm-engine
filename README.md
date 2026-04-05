# Terraforming Mars Engine

A stateless, serverless game engine for Terraforming Mars, built with C#/.NET 8.0 and Azure Functions.

Supports the base game, Prelude expansion, and Hellas & Elysium maps. Corporate Era and Draft variants are supported.

## Running Locally

```bash
dotnet build
func start --port 7102
```

## Architecture

### How Moves Are Processed

The engine is a pure, deterministic state machine. Game state is an immutable record — every move produces a new state object.

```
Client                    API Layer                  Domain Engine
  │                          │                            │
  │  POST /games/{id}/moves  │                            │
  │ ─────────────────────────>  deserialize JSON           │
  │                          │  (MoveJsonConverter)        │
  │                          │                            │
  │                          │  load state from storage    │
  │                          │                            │
  │                          │      GameEngine.Apply() ──>│
  │                          │                            │
  │                          │          1. MoveValidator.Validate()
  │                          │             └─ reject if illegal
  │                          │
  │                          │          2. Apply state transition
  │                          │             └─ new immutable GameState
  │                          │
  │                          │          3. Execute card effects
  │                          │             └─ EffectExecutor
  │                          │             └─ TriggerSystem (ongoing effects)
  │                          │
  │                          │          4. Advance phase/turn
  │                          │             └─ PhaseManager
  │                          │
  │                          │  <── (newState, result) ───│
  │                          │                            │
  │                          │  save new state to storage  │
  │  <── response ───────────│                            │
```

Key properties:
- **Deterministic**: Same state + same move = same result. Games are fully replayable.
- **Immutable**: State is never mutated. Each move produces a new `GameState` record.
- **Stateless API**: The server holds no in-memory state. State is loaded from storage, transformed, and saved back.

### Pending Actions (Sub-Moves)

Some moves trigger effects that need further player input before the turn can continue. These are modeled as **pending actions** on the game state.

```
1. Player plays card "Comet" (010)
       │
       v
2. Engine executes effects:
   - Raise temperature 1 step       ✓ immediate
   - Place an ocean tile            ✗ needs player to choose location
       │
       v
3. GameState.PendingAction = PlaceTilePending
   (remaining effects queued in EffectQueue)
       │
       v
4. Client calls GET /legal-moves
   → response contains PendingAction with valid locations
       │
       v
5. Player submits PlaceTile sub-move
       │
       v
6. Engine resolves pending action, resumes queued effects:
   - Remove up to 3 plants from any player  ✗ needs target
       │
       v
7. GameState.PendingAction = RemoveResourcePending
   ... cycle repeats until all effects resolved
       │
       v
8. Turn advances normally
```

The 12 pending action types cover tile placement, target selection, card selection, option choices, discarding, and effect ordering.

## API

### Create Game

```
POST /games
```

**Request body:**

```json
{
  "playerCount": 2,
  "map": "Tharsis",
  "corporateEra": true,
  "draftVariant": false,
  "preludeExpansion": false,
  "seed": 12345
}
```

All fields except `seed` are required. `map` accepts `"Tharsis"`, `"Hellas"`, or `"Elysium"`.

**Response** `201 Created`:

```json
{
  "gameId": "abc-123"
}
```

### Get Game State

```
GET /games/{id}?playerId={playerId}
```

The optional `playerId` query parameter filters the response to that player's perspective.

**Response** `200 OK`:

```json
{
  "state": { ... },
  "cardNames": { "001": "Card Name", ... }
}
```

### Get Legal Moves

```
GET /games/{id}/legal-moves?playerId={playerId}
```

Returns all moves currently available to the specified player.

**Response** `200 OK`:

```json
{
  "moves": { ... },
  "cardNames": { "001": "Card Name", ... }
}
```

### Submit Move

```
POST /games/{id}/moves?playerId={playerId}
```

Submits a move for the specified player. The request body contains the move object.

**Response** `200 OK`:

```json
{
  "success": true,
  "state": { ... },
  "cardNames": { "001": "Card Name", ... }
}
```

### Get Game History

```
GET /games/{id}/history
```

Returns the action log for a game.

**Response** `200 OK`:

```json
{
  "log": ["Player 1 played card X", ...]
}
```

### Get Game Cards

```
GET /games/{id}/cards
```

Returns metadata for all cards referenced in a game.

## Move Commands

Moves are submitted as JSON to `POST /games/{id}/moves?playerId={playerId}`. Every move has a `type` discriminator and `playerId`. Property names are camelCase.

### Setup Phase

**Setup** — Choose corporation, preludes, and starting cards:

```json
{
  "type": "Setup",
  "playerId": 0,
  "corporationId": "CORP02",
  "preludeIds": ["P01", "P07"],
  "cardIdsToBuy": ["047", "048"]
}
```

### Research Phase

**DraftCard** — Draft one card (Draft variant only):

```json
{ "type": "DraftCard", "playerId": 0, "cardId": "047" }
```

**BuyCards** — Buy cards from dealt/drafted selection (3 MC each):

```json
{ "type": "BuyCards", "playerId": 0, "cardIds": ["047", "048"] }
```

### Action Phase

**PlayCard** — Play a project card from hand:

```json
{
  "type": "PlayCard",
  "playerId": 0,
  "cardId": "047",
  "payment": { "megaCredits": 10, "steel": 0, "titanium": 0, "heat": 0 }
}
```

**UseStandardProject** — Use a standard project (`SellPatents`, `PowerPlant`, `Asteroid`, `Aquifer`, `Greenery`, `City`):

```json
{ "type": "UseStandardProject", "playerId": 0, "project": "Aquifer", "location": { "col": 4, "row": 3 } }
```

`location` is required for Aquifer, Greenery, and City. `cardsToDiscard` is required for SellPatents:

```json
{ "type": "UseStandardProject", "playerId": 0, "project": "SellPatents", "cardsToDiscard": ["047", "048"] }
```

**UseCardAction** — Use a blue card's action:

```json
{ "type": "UseCardAction", "playerId": 0, "cardId": "033" }
```

**ClaimMilestone**:

```json
{ "type": "ClaimMilestone", "playerId": 0, "milestoneName": "Terraformer" }
```

**FundAward**:

```json
{ "type": "FundAward", "playerId": 0, "awardName": "Landlord" }
```

**ConvertPlants** — Convert 8 plants to a greenery tile:

```json
{ "type": "ConvertPlants", "playerId": 0, "location": { "col": 4, "row": 3 } }
```

**ConvertHeat** — Convert 8 heat to raise temperature:

```json
{ "type": "ConvertHeat", "playerId": 0 }
```

**Pass** — Pass for the rest of the generation:

```json
{ "type": "Pass", "playerId": 0 }
```

**EndTurn** — Skip remaining action(s) this turn without passing for the generation:

```json
{ "type": "EndTurn", "playerId": 0 }
```

**PerformFirstAction** — Perform corporation's mandatory first action (generation 1 only):

```json
{ "type": "PerformFirstAction", "playerId": 0 }
```

### Prelude Phase

**PlayPrelude** — Play a prelude card:

```json
{ "type": "PlayPrelude", "playerId": 0, "preludeId": "P01" }
```

### Sub-Moves

Some moves trigger pending actions that require follow-up sub-moves before play continues. The game state's pending action indicates which sub-move is needed.

**PlaceTile** — Place a tile at a board location:

```json
{ "type": "PlaceTile", "playerId": 0, "location": { "col": 4, "row": 3 } }
```

**ChooseTargetPlayer** — Select a target player (e.g. for resource removal):

```json
{ "type": "ChooseTargetPlayer", "playerId": 0, "targetPlayerId": 1 }
```

**SelectCard** — Select a card (e.g. for adding resources):

```json
{ "type": "SelectCard", "playerId": 0, "cardId": "052" }
```

**ChooseOption** — Choose from a list of options by index:

```json
{ "type": "ChooseOption", "playerId": 0, "optionIndex": 0 }
```

**DiscardCards** — Discard specific cards:

```json
{ "type": "DiscardCards", "playerId": 0, "cardIds": ["047"] }
```

**ChooseEffectOrder** — Choose which triggered effect to resolve next (use -1 to auto-execute all remaining):

```json
{ "type": "ChooseEffectOrder", "playerId": 0, "effectIndex": 0 }
```

### Error Responses

All endpoints may return `400 Bad Request`, `404 Not Found`, or `409 Conflict` with:

```json
{
  "error": "Description of what went wrong"
}
```
