---
name: tm-maps-hellas
description: Hellas-map-specific strategy for Terraforming Mars — south pole ocean ring, placement patterns, map-specific milestones (Diversifier, Tactician, Polar Explorer, Energizer, Rim Settler). Load when a game is on the Hellas map.
---

# Hellas

## Required reading
Before evaluating any tile placement on Hellas, **read `knowledge/maps/hellas-placement-bonuses.md`**. It is the authoritative hex-by-hex bonus table. Do not guess bonuses from memory.

## When to consult
- Any action that places a tile (city, greenery, ocean, special tile).
- Any decision involving the South Pole or polar placements.
- Choosing **greenery placements near your own cities** — each greenery adjacent to a city you own is 1 VP at game end.

For live score projections, do **not** compute by hand. Call `GET /games/{id}/status` — the engine returns a per-player breakdown (TR, card VP, milestones, awards, greeneries, cities, total).

## Core map facts
- **12 ocean-reserved hexes.** The Hellas crater in the east clusters 7 ocean hexes — oceans there enable **4 MC and 6 MC ocean-adjacency rebates**.
- **No volcanic restriction.** Lava Flows, Lava Tube Settlement, and Noctis City can go anywhere on Hellas.
- **`(5,9)` South Pole** places an **ocean** (unique placement bonus) for **6 MC rebate** potential via its surroundings. This is strong value but not always first priority — depends on strategy.
- **`(7,5)` triple-heat ocean** is the single best ocean-reserved hex on any in-scope map for heat-income pickup.
- Ocean-adjacency placement pays **2 MC per adjacent ocean**.

## Strategic priorities
- **North-vs-south tension**: early tiles should commit to either (a) **Polar Explorer** — 3 tiles on the bottom two rows — or (b) plant/ocean rebates in the north. Don't straddle.
- **Best city spots**: `(5,2)` and `(7,2)` in the north — plant-rich with ocean adjacencies. Strong choice with ground-game corps like Ecoline.
- **Alternative early tile**: `(5,4)` for double-steel — good for Mining Guild, Space Elevator, or Electro Catapult fuel.
- **Heat band** (`(5,8)`, `(6,8)`, `(4,9)`, `(6,9)`, plus the triple-heat ocean `(7,5)`) is unique to Hellas and can be clutch in endgame heat-to-temperature pushes.
- **Ocean placement on the Hellas crater (east)** maximises ocean-adjacency rebates — prioritise these hexes when placing the 9 oceans.

## Map-specific scoring
- Milestones: Diversifier, Tactician, Polar Explorer, Energizer, Rim Settler.
- Awards: Cultivator, Magnate, Space Baron, Excentric, Contractor.
- Cross-reference `../tm-milestones-awards/SKILL.md` for criteria.
