# Tharsis placement bonuses

Source of truth: `TmEngine.Domain/Models/MapDefinitions.cs` (`BuildTharsis`). Regenerate if that file changes.

Coordinates are `(col, row)` using odd-row-offset-right. Bonuses are gained by the player who places a tile on that hex. Ocean hexes are ocean-reserved (only oceans may be placed there) — the bonus still triggers for the placing player. Named hexes are reserved for the specified card/effect. Placing a tile next to oceans yields 2 MC per ocean adjacency.

## Named (reserved) hexes
- `(4,2)` Tharsis Tholus — volcanic
- `(2,3)` Ascraeus Mons — volcanic
- `(2,4)` Pavonis Mons — volcanic
- `(1,5)` Arsia Mons — volcanic
- `(3,5)` Noctis City (reserved for City – Noctis)

Lava Flows may only target the four volcanic hexes above.

## Ocean-reserved hexes (12)
`(4,1)`, `(6,1)`, `(7,1)`, `(8,2)`, `(9,4)`, `(4,5)`, `(5,5)`, `(6,5)`, `(7,6)`, `(8,6)`, `(9,6)`, `(7,9)`.

## Bonus table

Empty `Bonus` means the hex has no placement bonus. `type=O` = ocean-reserved, `N` = named, `L` = land.

| Hex   | Type | Bonus                 |
|-------|------|-----------------------|
| (3,1) | L    | steel, steel          |
| (4,1) | O    | steel, steel          |
| (5,1) | L    |                       |
| (6,1) | O    | card                  |
| (7,1) | O    |                       |
| (3,2) | L    |                       |
| (4,2) | N    | steel                 |
| (5,2) | L    |                       |
| (6,2) | L    |                       |
| (7,2) | L    |                       |
| (8,2) | O    | card, card            |
| (2,3) | N    | card                  |
| (3,3) | L    |                       |
| (4,3) | L    |                       |
| (5,3) | L    |                       |
| (6,3) | L    |                       |
| (7,3) | L    |                       |
| (8,3) | L    | steel                 |
| (2,4) | N    | plant, titanium       |
| (3,4) | L    | plant                 |
| (4,4) | L    | plant                 |
| (5,4) | L    | plant                 |
| (6,4) | L    | plant, plant          |
| (7,4) | L    | plant                 |
| (8,4) | L    | plant                 |
| (9,4) | O    | plant, plant          |
| (1,5) | N    | plant, plant          |
| (2,5) | L    | plant, plant          |
| (3,5) | N    | plant, plant          |
| (4,5) | O    | plant, plant          |
| (5,5) | O    | plant, plant          |
| (6,5) | O    | plant, plant          |
| (7,5) | L    | plant, plant          |
| (8,5) | L    | plant, plant          |
| (9,5) | L    | plant, plant          |
| (2,6) | L    | plant                 |
| (3,6) | L    | plant, plant          |
| (4,6) | L    | plant                 |
| (5,6) | L    | plant                 |
| (6,6) | L    | plant                 |
| (7,6) | O    | plant                 |
| (8,6) | O    | plant                 |
| (9,6) | O    | plant                 |
| (2,7) | L    |                       |
| (3,7) | L    |                       |
| (4,7) | L    |                       |
| (5,7) | L    |                       |
| (6,7) | L    |                       |
| (7,7) | L    | plant                 |
| (8,7) | L    |                       |
| (3,8) | L    | steel, steel          |
| (4,8) | L    |                       |
| (5,8) | L    | card                  |
| (6,8) | L    | card                  |
| (7,8) | L    |                       |
| (8,8) | L    | titanium              |
| (3,9) | L    | steel                 |
| (4,9) | L    | steel, steel          |
| (5,9) | L    |                       |
| (6,9) | L    |                       |
| (7,9) | O    | titanium, titanium    |

## Strategic notes
- The central Valles Marineris band (row 5) and the broad row-4/6 plant belt give rich ocean (MC) and plant rebates. This is perfect for so-called "ocean-walking", a core strategy on the Tharsis map, where the player converts greeneries next to the oceans to get MC and plants back.
- Hex (8,4) is generally the best city spot on the map because of the strong adjacencies. Hex (2,5) is a good altnerative with solid plant adjacencies and Hex (5,7) is also strong because of the adjacencies to ocean rebates and cards.
- Converting around the Noctis city tile (3,5) should be done with caution if the card Noctis City is not accounted for, since that city can be placed without restrictions and leech points of any greeneries surrounding it.
