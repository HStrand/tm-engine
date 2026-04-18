---
name: tm-strategy
description: General strategy for Terraforming Mars. Terraformer vs. engine archetypes, game phase definitions (early/mid/late), production valuations, mid-game synergies, VP timing, payment optimization. Load at game start.
---

# Strategy

Format: 2-player, base game + Prelude, Corporate Era, Draft variant.

## Archetype axis: terraformer vs. engine

The most important strategic question in any game is: **am I the
terraformer or the engine player?**

**Terraformer** — early plant and heat production, leads on TR and
milestones. Wants to end the game in gen 9, gen 10 at most. Plays lean
(fewer cards), focuses on efficient terraforming through standard
projects and resource conversion. Every gen the game extends favors the
opponent.

**Engine player** — builds card draw, discounts, and synergies (Jovian
multipliers, animal VP, bio packages). Catches up and surpasses the
terraformer through superior scoring in the final gens. May still
terraform, but should **hold back on the lagging parameter** (the global
most likely to be last to max) to buy time for their engine to overtake
the terraformer's score.

In practice, it's a continuum — most setups land somewhere in between.
But recognizing which side you're on is critical:
- If you're ahead on TR + milestones/awards, **rush**. Push globals,
  play few cards, close the game before the opponent's engine outscales
  you.
- If you're behind on TR but have strong card synergies building, **slow
  the game**. Avoid pushing the lagging parameter. Invest in VP cards
  and let your engine compound.

Re-evaluate each gen — a lead can flip if the opponent lands a key
synergy piece or claims a milestone you were contesting.

## Game phases

These phase definitions are referenced by other skills (`tm-drafting`,
`tm-milestones-awards`, `tm-endgame`, etc.). Load this skill at the
start of every game alongside `tm-protocol`.

### Early game (gens 1–3)

Priority: **economic development**. Build the production engine that will
fund the rest of the game. Preludes and early card plays should maximize
production gains. Direct TR pushes are fine when efficient (cheap events,
ocean placements with bonuses), but don't sacrifice production investment
to do it.

### Mid game (gens 4–7)

Priority: **milestones, synergies, and positioning**. Production cards
must pass a payoff test (see below). Claim milestones as soon as they're
available — they won't get cheaper.

This phase is dominated by:
- **Milestone jostling** — in 2-player, each milestone is a 10 VP
  swing (5 VP gained + 5 VP denied to opponent). Prioritize reaching
  thresholds.
- **Bonus bumps** — the bonus temperature step at 8% O₂ and the bonus
  ocean at 0°C are effectively free TR. Time your plays to be the one
  who triggers them.
- **Developing synergies** — the strongest mid-game synergies are:
  - *Global discounts*: Earth Catapult, Anti-Gravity Technology,
    Research Outpost. These pay for themselves quickly when you're
    playing multiple cards per gen.
  - *Continuous card draw*: AI Central, Restricted Area, Mars
    University. Card flow keeps your options open and finds you
    efficient plays.
  - *Bio package*: Decomposers + Ecological Zone + Viral Enhancers.
    These multiply value from Plant/Microbe/Animal tags across many
    cards.
- **Rushing / terraforming** — if you're ahead on TR and have milestones
  locked, it's often correct to play lean (few cards) and focus on
  efficient terraforming through standard projects and heat/plant
  conversion. Don't over-invest in engine when you can close the game
  out.

### Late game (gens 8+)

Priority: **direct VP and TR**. Production is mostly dead — spend MC on
standard projects (Asteroid, Greenery), convert plants/heat, and play
VP-bearing cards. Every MC spent on production this late is MC not spent
on points.

## Production valuations (gen 1)

How much you should be willing to pay (card cost + 3 MC buy cost, total)
for +1 of each production type at the start of the game. These values
remain roughly accurate through gen 3, then decay as fewer generations
remain to collect income.

| Production      | Gen 1 value (MC) | Notes |
|-----------------|-------------------|-------|
| Titanium        | ~10               | High because Ti pays for Space cards at 3 MC each; compounds with Space-heavy strategies |
| Plants          | ~10               | Each 8 plants = greenery = +1 O₂ + 1 TR + 1 VP tile. Strongest late-game payoff |
| Steel           | ~8                | Pays for Building cards at 2 MC each; strong with IC or Building-heavy corps |
| MC              | ~6.5              | The baseline. Pure income, no conversion needed |
| Energy          | ~7                | Converts to heat each gen; also required by some powerful cards |
| Heat            | ~5                | 8 heat = +1 temp/TR, but temp maxes before other globals. Lowest ceiling |

For reference, **1 TR ≈ 8 MC** in value (income over remaining gens +
1 VP at game end). This is the benchmark for evaluating direct
terraforming actions against production investments.

## Production payoff test (mid game)

Before playing a production card in gen 4+, ask:

> **"Will this card pay for itself at least 2 full generations before the
> game ends?"**

A typical game lasts **10–11 generations** (range 9–12). To estimate
payoff:

1. Calculate total cost: card cost (after discounts) + 3 MC buy cost.
2. Calculate income per gen from the production gained.
3. Divide cost by income = **payoff generation count**.
4. Add current gen + payoff gens. If that's within 2 of expected game
   end, **don't play it**.

Even breaking even before game end is often not good enough, because:
- **Opportunity cost**: the MC spent on the card could have gone toward
  direct TR (Asteroid = 14 MC for +1 TR, Greenery = 23 MC for +1 TR +
  1 VP).
- **MC value decay**: money is worth the most at the start of the game
  and loses value as the game progresses, because early MC can be
  invested in production that compounds, while late MC can only buy
  fixed-rate TR/VP.

### Example

Sponsors costs 6 MC (card) + 3 MC (buy) = 9 MC total, gives +2 MC
production. Payoff = 9 / 2 = 4.5 gens. In gen 3, pays off by gen 7–8
with several gens to spare — **good play**. In gen 7, pays off by gen
11–12, right at game end — **bad play** unless heavily discounted.

## VP valuation by phase

| Phase      | Value of 1 VP (in MC) |
|------------|------------------------|
| Early game | ~1–2 MC                |
| Late game  | ~5–6+ MC               |

Early game: VP cards are low priority. A card that gives 2 VP for 10 MC
is poor when that 10 MC could buy +1.5 MC production (worth ~15 MC over
the game). Late game: VP cards are premium. A card giving 2 VP for 8 MC
is excellent when Greenery costs 23 MC for 1 TR + 1 VP.

## Payment optimization

Always minimize MC spent by using alternative resources first:

- **Steel** (2 MC each): use for Building-tagged cards before spending MC.
- **Titanium** (3 MC each): use for Space-tagged cards before spending MC.
- **Heat** (Helion only): can substitute for MC at 1:1.

Steel/Ti in hand is only worth their conversion rate **if you have cards
to spend them on**. A stockpile of 15 steel with no Building cards in
hand or coming in the draft is dead weight — sell patents or use Space
Elevator if available. Don't hoard resources without an outlet.
