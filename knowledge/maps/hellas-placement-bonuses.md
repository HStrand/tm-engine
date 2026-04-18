# Hellas placement bonuses

Source of truth: `TmEngine.Domain/Models/MapDefinitions.cs` (`BuildHellas`). Regenerate if that file changes.

Coordinates are `(col, row)` using odd-row-offset-right. Bonuses trigger for the player placing a tile. Hellas has no volcanic-restricted hexes — Lava Flows, Lava Tube Settlement and Noctis City may be placed anywhere. Placing a tile next to oceans yields 2 MC per ocean adjacency.

## Named (reserved) hexes
- `(5,9)` South Pole — placing here gives an **ocean** placement bonus (unique).

## Ocean-reserved hexes (12)
`(3,1)`, `(3,2)`, `(2,3)`, `(2,4)`, `(2,7)`, `(7,4)`, `(8,4)`, `(6,5)`, `(7,5)`, `(8,5)`, `(7,6)`, `(8,6)`.

## Bonus table

`type=O` = ocean-reserved, `N` = named, `L` = land.

| Hex   | Type | Bonus                   |
|-------|------|-------------------------|
| (3,1) | O    | plant, plant            |
| (4,1) | L    | plant, plant            |
| (5,1) | L    | plant, plant            |
| (6,1) | L    | plant, steel            |
| (7,1) | L    | plant                   |
| (3,2) | O    | plant, plant            |
| (4,2) | L    | plant, plant            |
| (5,2) | L    | plant                   |
| (6,2) | L    | plant, steel            |
| (7,2) | L    | plant                   |
| (8,2) | L    | plant                   |
| (2,3) | O    | plant                   |
| (3,3) | L    | plant                   |
| (4,3) | L    | steel                   |
| (5,3) | L    | steel                   |
| (6,3) | L    |                         |
| (7,3) | L    | plant, plant            |
| (8,3) | L    | card, plant             |
| (2,4) | O    | plant                   |
| (3,4) | L    | plant                   |
| (4,4) | L    | steel                   |
| (5,4) | L    | steel, steel            |
| (6,4) | L    | steel                   |
| (7,4) | O    | plant                   |
| (8,4) | O    | plant                   |
| (9,4) | L    | plant                   |
| (1,5) | L    | card                    |
| (2,5) | L    |                         |
| (3,5) | L    |                         |
| (4,5) | L    | steel, steel            |
| (5,5) | L    |                         |
| (6,5) | O    | card                    |
| (7,5) | O    | heat, heat, heat        |
| (8,5) | O    |                         |
| (9,5) | L    | plant                   |
| (2,6) | L    | titanium                |
| (3,6) | L    |                         |
| (4,6) | L    | steel                   |
| (5,6) | L    |                         |
| (6,6) | L    |                         |
| (7,6) | O    |                         |
| (8,6) | O    | steel                   |
| (9,6) | L    |                         |
| (2,7) | O    | titanium, titanium      |
| (3,7) | L    |                         |
| (4,7) | L    |                         |
| (5,7) | L    | card                    |
| (6,7) | L    |                         |
| (7,7) | L    |                         |
| (8,7) | L    | titanium                |
| (3,8) | L    | steel                   |
| (4,8) | L    | card                    |
| (5,8) | L    | heat, heat              |
| (6,8) | L    | heat, heat              |
| (7,8) | L    | titanium                |
| (8,8) | L    | titanium                |
| (3,9) | L    |                         |
| (4,9) | L    | heat, heat              |
| (5,9) | N    | ocean (South Pole)      |
| (6,9) | L    | heat, heat              |
| (7,9) | L    |                         |

## Strategic notes
- **South Pole `(5,9)` places an ocean for 6 MC** — This is good value, but not necessarily what you want to go for immediately, depending on your strategy.
- Early tile placements should consider whether you can realistically go for Polar Explorer (3 tiles on bottow two rows) in the south or if you want to prioritize plant and ocean rebates in the north.
- Hex (5,2) and (7,2) in the north are surrounded by plants and potential ocean adjacencies. These are generally the best city spots, though in the early game it's always a question of whether you want to go for Polar Explorer in the south or put tiles here. If you have a strong ground game, let's say with Ecoline, it can often be beneficial to start in the north.
- Hex (5,4) can be a good alternative early tile placement if you need steel as Mining Guild or to fuel Space Elevator or Electro Catapult.
- The **heat band** along the southern rows (`(5,8)`, `(6,8)`, `(4,9)`, `(6,9)`, and especially the triple-heat ocean `(7,5)`) is unique to Hellas — this can be clutch in the endgame.
- If you're terraforming and putting down oceans, it can often be beneficial to place oceans in the Hellas crater in the east, where the 7 clustered ocean tiles can give you lots of 4 MC and even 6 MC ocean rebates.
