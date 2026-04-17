---
name: tm-memory
description: Protocol for reading and updating MEMORY.md — the agent's tactical notes file for Terraforming Mars. Load at game start. Read before playing. Update only on user instruction or when you observe a factual, non-obvious tactical detail worth recording.
---

# Memory protocol

`MEMORY.md` lives at the **repo root**: `../../../../MEMORY.md` relative to
this skill.

## Read at game start

Before starting a game, read the entire file once. Treat its entries as
observations from prior sessions — useful context, but not prescriptive
strategy.

## When to write

Write **only** in these cases:

1. **User instructs** you to — e.g. "remember that", "add to memory",
   "note for next time".
2. You observe a **factual, non-obvious, tactical** detail — something
   that will save a future session time and that isn't strategy. Examples:
   - Engine quirks: "Convert Heat at capped temp consumes heat but gives
     no TR (legal stall action, not a bug)."
   - Map facts: "Hellas hex (6,5) gives +6 MC placement bonus."
   - Deal patterns observed across games: "Hellas CE consistently deals
     CORP04 as one of the two corp options."
   - API behaviors: "`/api/cards?gameId=...` returns only cards present
     in the game's deck, not all cards."

## When NOT to write

Do **not** write:

- **Strategic claims** — those belong in user-authored skills
  (`tm-corporations`, `tm-economy`, etc.). "Mining Guild is strong on
  Hellas" is strategy, not memory.
- **Per-game narration** or scores — those are session-scoped, not
  cross-session.
- **Speculation** or **inferences** you're not sure about. If in doubt,
  don't write.

## Format

One line per entry. Append to the appropriate section (add sections as
needed). Terse, factual, verifiable.

```markdown
## Engine quirks
- 2026-04-18 Convert Heat at capped temp consumes 8 heat, 0 TR — stall action
- ...

## Map facts
- 2026-04-18 Hellas (6,5) placement bonus: +6 MC
- ...
```

Date each entry. Delete/fix entries that turn out to be wrong.

## Never

- Don't invent entries to seem productive.
- Don't reword user-authored strategy into "memory".
- Don't duplicate what's in `knowledge/rules/*.md` or placement-bonus
  maps — reference them instead.
