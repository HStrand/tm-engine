---
name: tm-maps-tharsis
description: Tharsis-map-specific strategy for Terraforming Mars — reserved ocean zones, Noctis City, map-wide tile-placement considerations. Load when a game is on the Tharsis map.
---

# Tharsis

## Required reading
Before evaluating any tile placement on Tharsis, **read `knowledge/maps/tharsis-placement-bonuses.md`**. It is the authoritative hex-by-hex bonus table (plants, steel, titanium, cards, ocean-reserved hexes, named volcanic hexes). Do not guess bonuses from memory.

## When to consult
- Any action that places a tile (city, greenery, ocean, special tile).
- Evaluating cards that reference specific map features (Noctis City, volcanic hexes, Mining Area/Rights).
- Choosing **greenery placements near your own cities** — each greenery adjacent to a city you own is 1 VP at game end.

For live score projections, do **not** compute by hand. Call `GET /games/{id}/status` — the engine returns a per-player breakdown (TR, card VP, milestones, awards, greeneries, cities, total).

## Core map facts
- **12 ocean-reserved hexes.** Placing the 9th ocean here still triggers the hex bonus for the placer.
- **4 volcanic hexes** (Tharsis Tholus, Ascraeus Mons, Pavonis Mons, Arsia Mons) restrict Lava Flows and Lava Tube Settlement placement.
- **`(3,5)` Noctis City** is reserved for the *Noctis City* card. If that card has been drawn or played, track accordingly — otherwise the hex is dead space, but surrounding greeneries can be leeched by whoever eventually plays it.
- Ocean-adjacency placement pays **2 MC per adjacent ocean**.

## Strategic priorities
- **Ocean-walking** is the dominant Tharsis strategy: the Valles Marineris band (row 5) and the row 4/6 plant belts let greeneries chain ocean adjacencies for plant + MC rebates.
- **Best city spots**: `(8,4)` is the strongest — rich adjacencies. `(2,5)` is a solid plant-adjacency alternative. `(5,7)` is strong for ocean-rebate and card hexes.
- **Around Noctis `(3,5)`**: be cautious about greenery conversions adjacent to it unless you've accounted for the Noctis City card — the owner can leech your adjacency points later.

## Map-specific scoring
- Milestones: Terraformer, Mayor, Gardener, Builder, Planner.
- Awards: Landlord, Banker, Scientist, Thermalist, Miner.
- Cross-reference `../tm-milestones-awards/SKILL.md` for criteria.
