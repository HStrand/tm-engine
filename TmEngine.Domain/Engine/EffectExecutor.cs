using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Executes card effects against game state. Returns updated state and optionally
/// a PendingAction when the effect requires a player decision.
/// </summary>
public static class EffectExecutor
{
    /// <summary>
    /// Execute an effect for the given player. Returns the new state and any pending action.
    /// If a pending action is returned, the remaining effects must be queued for later.
    /// </summary>
    public static (GameState State, PendingAction? Pending) Execute(
        GameState state, int playerId, Effect effect, string? sourceCardId = null)
    {
        return effect switch
        {
            // Resource & production
            ChangeProductionEffect e => (ApplyChangeProduction(state, playerId, e), null),
            ChangeResourceEffect e => (ApplyChangeResource(state, playerId, e), null),
            RemoveResourceEffect e => ApplyRemoveResource(state, playerId, e),
            StealResourceEffect e => ApplyStealResource(state, playerId, e),
            ReduceAnyProductionEffect e => ApplyReduceAnyProduction(state, playerId, e),

            // Global parameters
            RaiseOxygenEffect e => (ApplyRaiseOxygen(state, playerId, e), null),
            RaiseTemperatureEffect e => (ApplyRaiseTemperature(state, playerId, e), null),
            PlaceOceanEffect e => ApplyPlaceOcean(state, playerId, e),

            // Tiles
            PlaceTileEffect e => ApplyPlaceTile(state, playerId, e),

            // Cards
            DrawCardsEffect e => (ApplyDrawCards(state, playerId, e), null),
            DiscardCardsEffect e => (state, new DiscardCardsPending(e.Count)),

            // Card resources
            AddCardResourceEffect e => ApplyAddCardResource(state, playerId, e),
            RemoveCardResourceEffect e => ApplyRemoveCardResource(state, playerId, e),

            // TR
            ChangeTREffect e => (ApplyChangeTR(state, playerId, e), null),

            // Special
            IncreaseLowestProductionEffect => ApplyIncreaseLowestProduction(state, playerId),
            DrawAndPlayOneEffect e => ApplyDrawAndPlayOne(state, playerId, e),
            RevealUntilTagEffect e => (ApplyRevealUntilTag(state, playerId, e), null),
            PlaceOffMapCityEffect e => ApplyPlaceOffMapCity(state, playerId, e),
            ChangeProductionPerTagEffect e => (ApplyChangeProductionPerTag(state, playerId, e), null),
            ChangeTRPerTagEffect e => (ApplyChangeTRPerTag(state, playerId, e), null),
            ClaimLandEffect => ApplyClaimLand(state, playerId),
            PlayCardFromHandEffect e => (state, new PlayCardFromHandPending(
                e.IgnoreGlobalRequirements ? "Play a card from hand (ignoring global requirements):" : $"Play a card from hand ({e.CostDiscount} MC discount):",
                e.IgnoreGlobalRequirements, e.CostDiscount)),
            GrantFreeAwardEffect => (state.UpdatePlayer(playerId, p => p with { HasFreeAwardFunding = true }), null),

            // Choices
            ChooseEffect e => ApplyChoose(state, e, sourceCardId),

            // Compound
            CompoundEffect e => ApplyCompound(state, playerId, e, sourceCardId),

            // Passive modifiers (these are stored on the card entry, not executed at play time)
            // They take effect by being present in OngoingEffects and queried by RequirementChecker.
            RequirementModifierEffect or SteelValueModifierEffect or TitaniumValueModifierEffect
                or TagDiscountEffect or GlobalDiscountEffect or PlantConversionModifierEffect
                or HeatAsPaymentEffect or PowerPlantDiscountEffect
                or HighCostRebateEffect or VPCardRebateEffect => (state, null),

            // Triggered effects are registered, not executed immediately
            WhenYouEffect or WhenAnyoneEffect => (state, null),

            _ => throw new InvalidOperationException($"Unhandled effect type: {effect.GetType().Name}"),
        };
    }

    /// <summary>
    /// Execute a list of effects sequentially by index. Stops and returns a PendingAction
    /// if any effect requires player input. Stores remaining indices in EffectQueue.
    /// </summary>
    public static (GameState State, PendingAction? Pending) ExecuteSequential(
        GameState state, int playerId, ImmutableArray<Effect> effects,
        ImmutableArray<int> indices, string? sourceCardId = null, string effectSource = "onPlay")
    {
        for (int pos = 0; pos < indices.Length; pos++)
        {
            int i = indices[pos];
            var (newState, pending) = Execute(state, playerId, effects[i], sourceCardId);
            state = newState;
            if (pending != null)
            {
                // Store remaining indices (after this one) in the queue
                if (sourceCardId != null && pos + 1 < indices.Length)
                {
                    var remaining = indices.RemoveRange(0, pos + 1);
                    state = state with
                    {
                        EffectQueue = new PendingEffectQueue(sourceCardId, remaining, effectSource),
                    };
                }
                return (state, pending);
            }
        }
        return (state, null);
    }

    /// <summary>
    /// Execute effects with player-ordered execution. Auto-executes immediate effects,
    /// then presents orderable effects for the player to choose the order.
    /// </summary>
    public static (GameState State, PendingAction? Pending) ExecuteWithOrdering(
        GameState state, int playerId, ImmutableArray<Effect> effects,
        string? sourceCardId = null, string effectSource = "onPlay")
    {
        // Phase 1: Auto-execute all non-orderable effects
        var orderableIndices = ImmutableArray.CreateBuilder<int>();
        for (int i = 0; i < effects.Length; i++)
        {
            if (IsOrderable(effects[i]))
            {
                orderableIndices.Add(i);
            }
            else
            {
                var (newState, pending) = Execute(state, playerId, effects[i], sourceCardId);
                state = newState;
                // Auto-execute effects shouldn't create pending actions, but handle it just in case
                if (pending != null)
                {
                    // Store remaining effects (orderable + remaining auto) in queue
                    var remaining = orderableIndices.ToImmutable();
                    for (int j = i + 1; j < effects.Length; j++)
                        remaining = remaining.Add(j);
                    if (sourceCardId != null && remaining.Length > 0)
                        state = state with { EffectQueue = new PendingEffectQueue(sourceCardId, remaining, effectSource) };
                    return (state, pending);
                }
            }
        }

        var orderable = orderableIndices.ToImmutable();

        // Phase 2: Handle orderable effects
        if (orderable.Length == 0)
            return (state, null);

        if (orderable.Length == 1)
        {
            // Only one orderable effect — execute directly
            return ExecuteSequential(state, playerId, effects, orderable, sourceCardId, effectSource);
        }

        // 2+ orderable effects — let the player choose the order
        var descriptions = orderable.Select(i => DescribeEffect(effects[i])).ToImmutableArray();
        return (state, new ChooseEffectOrderPending(
            sourceCardId ?? "", effectSource, orderable, descriptions));
    }

    /// <summary>
    /// Returns true if this effect type should be player-ordered (strategically meaningful).
    /// </summary>
    public static bool IsOrderable(Effect effect) => effect switch
    {
        PlaceOceanEffect => true,
        PlaceTileEffect => true,
        ClaimLandEffect => true,
        RemoveResourceEffect => true,
        StealResourceEffect => true,
        DrawCardsEffect => true,
        DrawAndPlayOneEffect => true,
        RevealUntilTagEffect => true,
        ChooseEffect => true,
        AddCardResourceEffect e => e.TargetCardId == null, // only if target not specified
        PlayCardFromHandEffect => true,
        DiscardCardsEffect => true,
        CompoundEffect => true,
        _ => false,
    };

    /// <summary>
    /// Returns a human-readable description of an effect for the ordering UI.
    /// </summary>
    public static string DescribeEffect(Effect effect) => effect switch
    {
        RaiseTemperatureEffect e => e.Steps == 1 ? "Raise temperature 1 step" : $"Raise temperature {e.Steps} steps",
        RaiseOxygenEffect e => e.Steps == 1 ? "Raise oxygen 1 step" : $"Raise oxygen {e.Steps} steps",
        PlaceOceanEffect e => e.Count == 1 ? "Place an ocean tile" : $"Place {e.Count} ocean tiles",
        PlaceTileEffect e => $"Place a {e.TileType} tile",
        ClaimLandEffect => "Claim a land hex",
        RemoveResourceEffect e => $"Remove up to {e.Amount} {e.Resource} from any player",
        StealResourceEffect e => $"Steal up to {e.Amount} {e.Resource} from any player",
        ReduceAnyProductionEffect e => $"Reduce any player's {e.Resource} production by {e.Amount}",
        ChangeProductionEffect e => e.Amount >= 0 ? $"+{e.Amount} {e.Resource} production" : $"{e.Amount} {e.Resource} production",
        ChangeResourceEffect e => e.Amount >= 0 ? $"Gain {e.Amount} {e.Resource}" : $"Lose {-e.Amount} {e.Resource}",
        DrawCardsEffect e => e.Count == 1 ? "Draw 1 card" : $"Draw {e.Count} cards",
        DrawAndPlayOneEffect e => $"Draw {e.DrawCount} cards and play 1",
        RevealUntilTagEffect e => $"Reveal cards until {e.Count} {e.Tag} tag(s) found",
        ChooseEffect => "Choose one option",
        AddCardResourceEffect e => $"Add {e.Amount} {e.ResourceType} to a card",
        PlayCardFromHandEffect => "Play a card from hand",
        DiscardCardsEffect e => $"Discard {e.Count} card(s)",
        ChangeTREffect e => $"{(e.Amount >= 0 ? "+" : "")}{e.Amount} TR",
        CompoundEffect e => string.Join("; ", e.Effects.Select(DescribeEffect)),
        _ => effect.GetType().Name,
    };

    // ── Resource & Production ──────────────────────────────────

    private static GameState ApplyChangeProduction(GameState state, int playerId, ChangeProductionEffect e) =>
        state.UpdatePlayer(playerId, p => p with
        {
            Production = p.Production.Add(e.Resource, e.Amount),
        });

    private static GameState ApplyChangeResource(GameState state, int playerId, ChangeResourceEffect e) =>
        state.UpdatePlayer(playerId, p => p with
        {
            Resources = p.Resources.Add(e.Resource, e.Amount),
        });

    private static (GameState, PendingAction?) ApplyRemoveResource(
        GameState state, int playerId, RemoveResourceEffect e)
    {
        // Find players who have the resource (excluding active player for "any player" targeting)
        var validTargets = state.Players
            .Where(p => p.PlayerId != playerId && p.Resources.Get(e.Resource) > 0)
            .Select(p => p.PlayerId)
            .ToImmutableArray();

        if (validTargets.Length == 0)
            return (state, null); // No valid targets, effect is optional

        if (validTargets.Length == 1)
        {
            // Only one valid target, apply directly
            return (state.UpdatePlayer(validTargets[0], p => p with
            {
                Resources = p.Resources.Add(e.Resource, -Math.Min(e.Amount, p.Resources.Get(e.Resource))),
            }), null);
        }

        // Multiple valid targets — player must choose
        return (state, new RemoveResourcePending(e.Resource, e.Amount, validTargets));
    }

    private static (GameState, PendingAction?) ApplyStealResource(
        GameState state, int playerId, StealResourceEffect e)
    {
        var validTargets = state.Players
            .Where(p => p.PlayerId != playerId && p.Resources.Get(e.Resource) > 0)
            .Select(p => p.PlayerId)
            .ToImmutableArray();

        if (validTargets.Length == 0)
            return (state, null);

        if (validTargets.Length == 1)
        {
            var targetId = validTargets[0];
            var target = state.GetPlayer(targetId);
            var stolen = Math.Min(e.Amount, target.Resources.Get(e.Resource));

            state = state.UpdatePlayer(targetId, p => p with
            {
                Resources = p.Resources.Add(e.Resource, -stolen),
            });
            state = state.UpdatePlayer(playerId, p => p with
            {
                Resources = p.Resources.Add(e.Resource, stolen),
            });
            return (state, null);
        }

        // Multiple valid targets — player must choose
        // TODO: StealResourcePending for proper steal with gain — for now reuse RemoveResource
        // and the gain is handled after target selection
        return (state, new RemoveResourcePending(e.Resource, e.Amount, validTargets));
    }

    private static (GameState, PendingAction?) ApplyReduceAnyProduction(
        GameState state, int playerId, ReduceAnyProductionEffect e)
    {
        // Must reduce someone's production. Check opponents first, then self.
        var validTargets = state.Players
            .Where(p => p.Production.Get(e.Resource) >= e.Amount)
            .Select(p => p.PlayerId)
            .ToImmutableArray();

        if (validTargets.Length == 1)
        {
            return (state.UpdatePlayer(validTargets[0], p => p with
            {
                Production = p.Production.Add(e.Resource, -e.Amount),
            }), null);
        }

        if (validTargets.Length > 1)
        {
            return (state, new ReduceProductionPending(e.Resource, e.Amount, validTargets));
        }

        // No one has enough production — this shouldn't happen if validation was correct
        return (state, null);
    }

    // ── Global Parameters ──────────────────────────────────────

    private static GameState ApplyRaiseOxygen(GameState state, int playerId, RaiseOxygenEffect e)
    {
        for (int i = 0; i < e.Steps; i++)
            state = GlobalParameters.RaiseOxygen(state, playerId);
        return state;
    }

    private static GameState ApplyRaiseTemperature(GameState state, int playerId, RaiseTemperatureEffect e)
    {
        for (int i = 0; i < e.Steps; i++)
            state = GlobalParameters.RaiseTemperature(state, playerId);
        return state;
    }

    private static (GameState, PendingAction?) ApplyPlaceOcean(
        GameState state, int playerId, PlaceOceanEffect e)
    {
        var map = MapDefinitions.GetMap(state.Map);
        if (state.OceansPlaced >= map.MaxOceans)
            return (state, null); // All oceans placed, skip

        var validLocations = BoardLogic.GetValidOceanPlacements(state);
        if (validLocations.Length == 0)
            return (state, null);

        // If placing multiple oceans, place first one and queue the rest
        // For now, trigger a pending action for the player to choose location
        return (state, new PlaceTilePending(TileType.Ocean, validLocations));
    }

    // ── Tiles ──────────────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyPlaceTile(
        GameState state, int playerId, PlaceTileEffect e)
    {
        var validLocations = GetConstrainedPlacements(state, e.TileType, playerId, e.Constraint);

        if (validLocations.Length == 0)
            return (state, null); // Can't place tile, but card can still be played

        return (state, new PlaceTilePending(e.TileType, validLocations));
    }

    private static ImmutableArray<HexCoord> GetConstrainedPlacements(
        GameState state, TileType tileType, int playerId, PlacementConstraint? constraint)
    {
        // Get base valid placements for the tile type
        var basePlacements = BoardLogic.GetValidTilePlacements(state, tileType, playerId);

        if (constraint == null)
            return basePlacements;

        // Apply additional constraint filter
        return constraint.Value switch
        {
            PlacementConstraint.Isolated => basePlacements
                .Where(c => !c.GetAdjacentCoords().Any(a => state.PlacedTiles.ContainsKey(a)))
                .ToImmutableArray(),

            PlacementConstraint.AdjacentTo2Cities => basePlacements
                .Where(c =>
                {
                    int cityCount = 0;
                    foreach (var adj in c.GetAdjacentCoords())
                    {
                        if (state.PlacedTiles.TryGetValue(adj, out var t) &&
                            (t.Type == TileType.City || t.Type == TileType.Capital))
                            cityCount++;
                    }
                    return cityCount >= 2;
                })
                .ToImmutableArray(),

            PlacementConstraint.NoctisCity =>
                MapDefinitions.GetMap(state.Map).Hexes.Values
                    .Where(h => h.ReservedFor == "Noctis City" && !state.PlacedTiles.ContainsKey(h.Coord))
                    .Select(h => h.Coord)
                    .ToImmutableArray(),

            PlacementConstraint.OceanReserved => BoardLogic.GetValidOceanPlacements(state),

            PlacementConstraint.OnOceanArea => BoardLogic.GetValidOceanPlacements(state),

            PlacementConstraint.OceanOnLand => BoardLogic.GetValidLandPlacements(state, playerId),

            PlacementConstraint.Volcanic =>
                MapDefinitions.GetMap(state.Map).VolcanicAreas
                    .Where(c => !state.PlacedTiles.ContainsKey(c))
                    .ToImmutableArray(),

            _ => basePlacements,
        };
    }

    // ── Cards ──────────────────────────────────────────────────

    private static GameState ApplyDrawCards(GameState state, int playerId, DrawCardsEffect e)
    {
        for (int i = 0; i < e.Count; i++)
        {
            if (state.DrawPile.IsEmpty)
                break; // TODO: reshuffle discard pile

            var cardId = state.DrawPile[0];
            state = state with { DrawPile = state.DrawPile.RemoveAt(0) };
            state = state.UpdatePlayer(playerId, p => p with { Hand = p.Hand.Add(cardId) });
        }
        return state;
    }

    // ── Card Resources ─────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyAddCardResource(
        GameState state, int playerId, AddCardResourceEffect e)
    {
        if (e.TargetCardId != null)
        {
            // Add to specific card
            return (AddResourcesToCard(state, playerId, e.TargetCardId, e.Amount), null);
        }

        // Player must choose which card to add resources to
        var player = state.GetPlayer(playerId);
        var validCards = player.PlayedCards
            .Where(cardId => CardHasResourceType(cardId, e.ResourceType))
            .ToImmutableArray();

        // Also check corporation
        if (!string.IsNullOrEmpty(player.CorporationId) && CardHasResourceType(player.CorporationId, e.ResourceType))
            validCards = validCards.Add(player.CorporationId);

        if (validCards.Length == 0)
            return (state, null); // No valid cards, skip

        if (validCards.Length == 1)
            return (AddResourcesToCard(state, playerId, validCards[0], e.Amount), null);

        return (state, new AddCardResourcePending(e.ResourceType, e.Amount, validCards));
    }

    private static (GameState, PendingAction?) ApplyRemoveCardResource(
        GameState state, int playerId, RemoveCardResourceEffect e)
    {
        // TODO: Find cards with the resource type and let player choose
        // For now, return no pending action
        return (state, null);
    }

    private static GameState AddResourcesToCard(GameState state, int playerId, string cardId, int amount)
    {
        return state.UpdatePlayer(playerId, p =>
        {
            var current = p.CardResources.GetValueOrDefault(cardId, 0);
            return p with { CardResources = p.CardResources.SetItem(cardId, current + amount) };
        });
    }

    private static bool CardHasResourceType(string cardId, CardResourceType resourceType)
    {
        // TODO: Check card definition for resource type compatibility
        // For now, return false — will be implemented when cards are registered
        return false;
    }

    // ── Special ─────────────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyIncreaseLowestProduction(GameState state, int playerId)
    {
        var player = state.GetPlayer(playerId);
        var prod = player.Production;

        var min = Math.Min(prod.MegaCredits,
            Math.Min(prod.Steel,
            Math.Min(prod.Titanium,
            Math.Min(prod.Plants,
            Math.Min(prod.Energy, prod.Heat)))));

        // Find which resource types are at the minimum
        var lowestTypes = new List<ResourceType>();
        if (prod.MegaCredits == min) lowestTypes.Add(ResourceType.MegaCredits);
        if (prod.Steel == min) lowestTypes.Add(ResourceType.Steel);
        if (prod.Titanium == min) lowestTypes.Add(ResourceType.Titanium);
        if (prod.Plants == min) lowestTypes.Add(ResourceType.Plants);
        if (prod.Energy == min) lowestTypes.Add(ResourceType.Energy);
        if (prod.Heat == min) lowestTypes.Add(ResourceType.Heat);

        if (lowestTypes.Count == 1)
        {
            // Only one lowest — increase it directly
            return (state.UpdatePlayer(playerId, p => p with
            {
                Production = p.Production.Add(lowestTypes[0], 1),
            }), null);
        }

        // Multiple tied — player must choose
        var options = lowestTypes
            .Select(r => $"{r} production")
            .ToImmutableArray();

        return (state, new ChooseOptionPending(
            $"Choose which production to increase (all at {min}):",
            options));
    }

    private static (GameState, PendingAction?) ApplyPlaceOffMapCity(GameState state, int playerId, PlaceOffMapCityEffect e)
    {
        state = state with
        {
            OffMapTiles = state.OffMapTiles.Add(new OffMapTile(e.CityName, TileType.City, playerId)),
        };

        // Fire city triggers — off-map cities fire PlaceAnyCityTile but NOT PlaceCityTileOnMars
        state = TriggerSystem.FireTrigger(state, playerId, TriggerCondition.PlaceAnyCityTile);

        return (state, null);
    }

    private static GameState ApplyChangeProductionPerTag(GameState state, int playerId, ChangeProductionPerTagEffect e)
    {
        var player = state.GetPlayer(playerId);
        var tagCount = player.CountTag(e.Tag, CardRegistry.GetTags);
        var totalAmount = tagCount * e.AmountPerTag;

        if (totalAmount == 0)
            return state;

        return state.UpdatePlayer(playerId, p => p with
        {
            Production = p.Production.Add(e.Resource, totalAmount),
        });
    }

    private static GameState ApplyChangeTRPerTag(GameState state, int playerId, ChangeTRPerTagEffect e)
    {
        var player = state.GetPlayer(playerId);
        var tagCount = player.CountTag(e.Tag, CardRegistry.GetTags);
        var totalTR = tagCount * e.AmountPerTag;

        if (totalTR == 0)
            return state;

        return state.UpdatePlayer(playerId, p => p with
        {
            TerraformRating = p.TerraformRating + totalTR,
            IncreasedTRThisGeneration = totalTR > 0 ? true : p.IncreasedTRThisGeneration,
        });
    }

    private static (GameState, PendingAction?) ApplyClaimLand(GameState state, int playerId)
    {
        // Valid hexes: non-ocean, non-reserved, unoccupied, not already claimed
        var map = MapDefinitions.GetMap(state.Map);
        var valid = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved) continue;
            if (state.PlacedTiles.ContainsKey(coord)) continue;
            if (state.ClaimedHexes.ContainsKey(coord)) continue;
            // Reserved named hexes CAN be claimed (that's the point of Land Claim)

            valid.Add(coord);
        }

        if (valid.Count == 0)
            return (state, null);

        return (state, new ClaimLandPending(valid.ToImmutable()));
    }

    private static GameState ApplyRevealUntilTag(GameState state, int playerId, RevealUntilTagEffect e)
    {
        var kept = new List<string>();
        var discarded = new List<string>();
        var allRevealed = new List<string>();
        var deck = state.DrawPile;

        int found = 0;
        while (found < e.Count && !deck.IsEmpty)
        {
            var cardId = deck[0];
            deck = deck.RemoveAt(0);
            allRevealed.Add(cardId);

            var tags = CardRegistry.GetTags(cardId);
            if (tags.Contains(e.Tag))
            {
                kept.Add(cardId);
                found++;
            }
            else
            {
                discarded.Add(cardId);
            }
        }

        state = state with
        {
            DrawPile = deck,
            DiscardPile = state.DiscardPile.AddRange(discarded),
        };

        // Add matching cards to player's hand
        state = state.UpdatePlayer(playerId, p => p with
        {
            Hand = p.Hand.AddRange(kept),
        });

        // Log all revealed cards (visible to all players)
        var revealedNames = string.Join(", ", allRevealed);
        var keptNames = string.Join(", ", kept);
        state = state.AppendLog(
            $"Player {playerId} reveals: [{revealedNames}]. Keeps: [{keptNames}].");

        return state;
    }

    private static (GameState, PendingAction?) ApplyDrawAndPlayOne(
        GameState state, int playerId, DrawAndPlayOneEffect e)
    {
        // Draw from the appropriate deck
        var deck = e.CardTypeToFind == CardType.Prelude ? state.PreludeDeck : state.DrawPile;

        var (dealt, remaining) = DeckBuilder.Deal(deck, e.DrawCount);

        // Update the deck
        state = e.CardTypeToFind == CardType.Prelude
            ? state with { PreludeDeck = remaining }
            : state with { DrawPile = remaining };

        if (dealt.Count == 0)
            return (state, null);

        if (dealt.Count == 1)
        {
            // Only one option — play it directly
            return PlayCardImmediately(state, playerId, dealt[0]);
        }

        // Multiple options — player must choose
        return (state, new ChooseCardToPlayPending(
            $"Choose a {e.CardTypeToFind} to play:",
            [.. dealt]));
    }

    internal static (GameState, PendingAction?) PlayCardImmediately(
        GameState state, int playerId, string cardId)
    {
        if (!CardRegistry.TryGet(cardId, out var entry))
            return (state, null);

        // Add to played cards
        state = state.UpdatePlayer(playerId, p => p with
        {
            PlayedCards = p.PlayedCards.Add(cardId),
        });

        // Execute the card's on-play effects
        return ExecuteWithOrdering(state, playerId, entry.OnPlayEffects, cardId);
    }

    // ── TR ──────────────────────────────────────────────────────

    private static GameState ApplyChangeTR(GameState state, int playerId, ChangeTREffect e) =>
        state.UpdatePlayer(playerId, p => p with
        {
            TerraformRating = p.TerraformRating + e.Amount,
            IncreasedTRThisGeneration = e.Amount > 0 ? true : p.IncreasedTRThisGeneration,
        });

    // ── Choices ─────────────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyChoose(GameState state, ChooseEffect e, string? sourceCardId)
    {
        var options = e.Options.Select(o => o.Description).ToImmutableArray();
        return (state, new ChooseOptionPending("Choose one:", options, sourceCardId));
    }

    // ── Compound ────────────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyCompound(
        GameState state, int playerId, CompoundEffect e, string? sourceCardId)
    {
        // CompoundEffect is treated as a single unit — sub-effects execute sequentially
        var indices = Enumerable.Range(0, e.Effects.Length).ToImmutableArray();
        return ExecuteSequential(state, playerId, e.Effects, indices, sourceCardId);
    }
}
