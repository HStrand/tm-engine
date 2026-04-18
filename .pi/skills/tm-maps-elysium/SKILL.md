---
name: tm-maps-elysium
description: Elysium-map-specific strategy for Terraforming Mars — map layout, placement patterns, map-specific milestones (Generalist, Specialist, Ecologist, Tycoon, Legend). Load when a game is on the Elysium map.
---

# Elysium

## Required reading
Before evaluating any tile placement on Elysium, **read `knowledge/maps/elysium-placement-bonuses.md`**. It is the authoritative hex-by-hex bonus table. Do not guess bonuses from memory.

## When to consult
- Any action that places a tile (city, greenery, ocean, special tile).
- Evaluating cards that target volcanic hexes (Lava Flows, Lava Tube Settlement).
- Choosing **greenery placements near your own cities** — each greenery adjacent to a city you own is 1 VP at game end.

For live score projections, do **not** compute by hand. Call `GET /games/{id}/status` — the engine returns a per-player breakdown (TR, card VP, milestones, awards, greeneries, cities, total).

## Core map facts
- **12 ocean-reserved hexes**, concentrated in rows 1–3 (9 of 12 are in the north).
- **4 volcanic hexes** (Hecates Tholus, Elysium Mons, Olympus Mons, Arsia Mons). Lava Flows and Lava Tube Settlement may only target these four. **Noctis City may be placed anywhere** on Elysium.
- **`(8,3)` Olympus Mons gives 3 cards** — the single richest bonus hex on any in-scope map. Lava Flows here is a standout 3-card swing.
- Ocean-adjacency placement pays **2 MC per adjacent ocean**.

## Strategic priorities
- **Claim Olympus Mons `(8,3)` early** whenever possible — prelude tiles or early-generation tile effects should target it. Very high priority.
- **`(6,5)` triple-plant** is the second-best placement target; an excellent early-city hex. If Olympus Mons is already taken, this is typically the top alternative.
- **North-heavy opening**: the north is ocean-heavy and plant-rich — early tiles up north collect ocean-adjacency MC and plants that fuel greenery conversions.

## Map-specific scoring
- Milestones: Generalist, Specialist, Ecologist, Tycoon, Legend.
- Awards: Celebrity, Industrialist, Desert Settler, Estate Dealer, Benefactor.
- Cross-reference `../tm-milestones-awards/SKILL.md` for criteria.
