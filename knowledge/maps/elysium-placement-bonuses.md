# Elysium placement bonuses

Source of truth: `TmEngine.Domain/Models/MapDefinitions.cs` (`BuildElysium`). Regenerate if that file changes.

Coordinates are `(col, row)` using odd-row-offset-right. Bonuses trigger for the player placing the tile. Placing a tile next to oceans yields 2 MC per ocean adjacency.

## Named (reserved) hexes
- `(3,2)` Hecates Tholus — volcanic
- `(2,3)` Elysium Mons — volcanic
- `(8,3)` Olympus Mons — volcanic
- `(9,5)` Arsia Mons — volcanic

Lava Flows and Lava Tube Settlement may only target the four volcanic hexes above. Noctis City may be placed anywhere.

## Ocean-reserved hexes (12)
`(3,1)`, `(4,1)`, `(5,1)`, `(6,1)`, `(6,2)`, `(7,2)`, `(6,3)`, `(7,3)`, `(5,4)`, `(7,4)`, `(8,4)`, `(4,5)`.

## Bonus table

`type=O` = ocean-reserved, `N` = named, `L` = land.

| Hex   | Type | Bonus                        |
|-------|------|------------------------------|
| (3,1) | O    |                              |
| (4,1) | O    | titanium                     |
| (5,1) | O    | card                         |
| (6,1) | O    | steel                        |
| (7,1) | L    | card                         |
| (3,2) | N    | titanium                     |
| (4,2) | L    |                              |
| (5,2) | L    |                              |
| (6,2) | O    |                              |
| (7,2) | O    |                              |
| (8,2) | L    | steel, steel                 |
| (2,3) | N    | titanium, titanium           |
| (3,3) | L    |                              |
| (4,3) | L    | card                         |
| (5,3) | L    |                              |
| (6,3) | O    | plant                        |
| (7,3) | O    |                              |
| (8,3) | N    | card, card, card             |
| (2,4) | L    | plant                        |
| (3,4) | L    | plant                        |
| (4,4) | L    | plant, plant                 |
| (5,4) | O    | plant                        |
| (6,4) | L    | plant                        |
| (7,4) | O    | plant                        |
| (8,4) | O    | plant                        |
| (9,4) | L    | steel, plant                 |
| (1,5) | L    | plant, plant                 |
| (2,5) | L    | plant, plant                 |
| (3,5) | L    | plant, plant                 |
| (4,5) | O    | plant, plant                 |
| (5,5) | L    | plant, plant                 |
| (6,5) | L    | plant, plant, plant          |
| (7,5) | L    | plant, plant                 |
| (8,5) | L    | plant, plant                 |
| (9,5) | N    | plant, titanium              |
| (2,6) | L    | steel                        |
| (3,6) | L    | plant                        |
| (4,6) | L    | plant                        |
| (5,6) | L    | plant                        |
| (6,6) | L    | plant                        |
| (7,6) | L    | plant                        |
| (8,6) | L    | plant                        |
| (9,6) | L    |                              |
| (2,7) | L    | titanium                     |
| (3,7) | L    | steel                        |
| (4,7) | L    |                              |
| (5,7) | L    |                              |
| (6,7) | L    | steel                        |
| (7,7) | L    |                              |
| (8,7) | L    |                              |
| (3,8) | L    | steel, steel                 |
| (4,8) | L    |                              |
| (5,8) | L    |                              |
| (6,8) | L    |                              |
| (7,8) | L    | steel, steel                 |
| (8,8) | L    |                              |
| (3,9) | L    | steel                        |
| (4,9) | L    |                              |
| (5,9) | L    | card                         |
| (6,9) | L    | card                         |
| (7,9) | L    | steel, steel                 |

## Strategic notes
- **Olympus Mons `(8,3)` gives 3 cards** — the single richest bonus hex on any map. Ensuring this spot as early as possible with prelude tiles or early tiles is usually a high priority.
- Hex (6,5) with 3 plants is the second best spot on the map. This is great tile for an early city. Though if Olympus Mons is vacant, that is usually a slightly stronger placement, depending on the strategy.
- The **north of the map is ocean-heavy and rich in plants** You generally want to place your tiles up north in the early game to take advantage of the juicy bonuses from ocean adjacencies and get plants for greenery conversions.