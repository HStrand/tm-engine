# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Terraforming Mars game engine — a serverless C#/.NET 8.0 Azure Functions project implementing the board game rules. Supports the base game, Prelude expansion, and Hellas & Elysium maps. Corporate Era and Draft variant are supported; Solo, Venus, Colonies, Turmoil, Prelude 2, and promo cards are explicitly out of scope (but may be added later — keep extensibility in mind).

## Build & Run Commands

```bash
# Build
dotnet build

# Run locally (Azure Functions host on port 7102)
func start --port 7102

# Run tests (no test project yet — create with dotnet new xunit or similar)
dotnet test
```

## Architecture

**Stateless command-based engine.** The core design principle:

1. **Pure domain library** — Game logic lives in pure C# with no external dependencies (no Azure, no HTTP, no I/O). This is the engine core.
2. **Canonical game state** — A JSON-serializable state object represents the full game state at any point.
3. **Idempotent state transitions** — Moves are commands applied to game state, producing new game state. Moves must be explicit, auditable, replayable, and deterministic.
4. **Sub-move effects** — Some moves trigger further required actions from the same player before turn handover. These are still modeled as idempotent state transitions.
5. **No undo support.**

**Infrastructure layer:**
- Azure Functions HTTP triggers expose the API to clients
- Game states stored as JSON in Azure Storage
- Game metadata stored in Azure SQL
- Eventual GUI will be adapted from the replay viewer at `C:\Code\BgaTmScraperRegistry\web\src\components\replay`

## Resource Files

- `project resources/rules/` — PDF rulebooks (base game, Prelude, Hellas & Elysium)
- `project resources/project requirements/project requirements.txt` — Full project specification
- `project resources/card metadata/TM card list.html` — Raw card reference data (HTML)
- `project resources/card metadata/cards.json` — Structured card reference data (JSON)
- `project resources/images/` — Card images, award/milestone icons, tile assets, tag icons

## Key Design Constraints

- Domain logic must have zero coupling to infrastructure (Azure, HTTP, storage)
- Game state format is project-specific — not tied to Board Game Arena's replay format
- All state transitions must be deterministic for replayability
- Design for future expansion support without breaking changes
