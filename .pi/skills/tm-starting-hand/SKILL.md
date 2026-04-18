---
name: tm-starting-hand
description: Starting-hand evaluation for Terraforming Mars — uses baseline stats and corporation-combo synergy data from tfmstats API to decide which corporations, preludes, and project cards to keep. Load during Setup phase.
---

# Card Evaluation

## Starting-hand keep decisions — consult baseline stats first

Before deciding which corporations, preludes, and project cards to keep from the starting hand, fetch baseline statistics from:

```
GET https://api.tfmstats.com/api/combinations/baselines
```

This endpoint returns baseline stats for all cards available in the starting hand (corporations, preludes, project cards). The field to use is **`avgEloChange`** — the average Elo change players gained when keeping that card from their starting hand.

### Interpreting `avgEloChange`

| Value         | Meaning                                               |
|---------------|-------------------------------------------------------|
| ≈ +1.00 or higher | Premium card — almost always a must-keep          |
| ≈ +0.50       | Good card — usually a keep                            |
| ≈ 0.00        | Conditional keep                                      |
| ≈ -0.50       | Slightly weak — keep only if you have specific reasons |
| ≈ -1.00 or lower  | Very weak card                                     |

### Corporation pairing — synergy lookup

For starting-hand selection, also consult the combo endpoints to evaluate synergy with the chosen (or candidate) corporation:

```
GET https://api.tfmstats.com/api/combinations/combos/corp-prelude
GET https://api.tfmstats.com/api/combinations/combos/corp-card
```

These return `avgEloChange` for Corporation + Prelude and Corporation + Project Card pairings respectively. **Compare each combo's `avgEloChange` to the card's standalone baseline from `/baselines`** — a combo value noticeably higher than the card's baseline indicates positive synergy with that corporation; noticeably lower indicates a poor fit.

Use this to pick preludes/cards that pair well with the corporation you're keeping, not just cards that are strong in isolation.

### When this data is relevant
- **Primary use:** starting-hand selection (corporation, prelude, and project-card keeps).
- **Secondary use:** card drafts in the first couple of generations.
- Later-generation drafts and mid-game card buys are less well served by these baselines — fall back on situational evaluation.

<!-- Additional card-evaluation framework (cost/VP ratios, tag heuristics, active vs automated vs event cards) to be authored by the user. -->
