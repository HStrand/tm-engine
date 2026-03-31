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
        GameState state, int playerId, Effect effect)
    {
        return effect switch
        {
            // Resource & production
            ChangeProductionEffect e => (ApplyChangeProduction(state, playerId, e), null),
            ChangeResourceEffect e => (ApplyChangeResource(state, playerId, e), null),
            RemoveResourceEffect e => ApplyRemoveResource(state, playerId, e),
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
            PlayCardFromHandEffect e => (state, new PlayCardFromHandPending(
                e.IgnoreGlobalRequirements ? "Play a card from hand (ignoring global requirements):" : $"Play a card from hand ({e.CostDiscount} MC discount):",
                e.IgnoreGlobalRequirements, e.CostDiscount)),
            GrantFreeAwardEffect => (state.UpdatePlayer(playerId, p => p with { HasFreeAwardFunding = true }), null),

            // Choices
            ChooseEffect e => ApplyChoose(state, e),

            // Compound
            CompoundEffect e => ApplyCompound(state, playerId, e),

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
    /// Execute a list of effects sequentially. Stops and returns a PendingAction
    /// if any effect requires player input.
    /// </summary>
    public static (GameState State, PendingAction? Pending) ExecuteAll(
        GameState state, int playerId, ImmutableArray<Effect> effects)
    {
        foreach (var effect in effects)
        {
            var (newState, pending) = Execute(state, playerId, effect);
            state = newState;
            if (pending != null)
                return (state, pending);
        }
        return (state, null);
    }

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
        return ExecuteAll(state, playerId, entry.OnPlayEffects);
    }

    // ── TR ──────────────────────────────────────────────────────

    private static GameState ApplyChangeTR(GameState state, int playerId, ChangeTREffect e) =>
        state.UpdatePlayer(playerId, p => p with
        {
            TerraformRating = p.TerraformRating + e.Amount,
            IncreasedTRThisGeneration = e.Amount > 0 ? true : p.IncreasedTRThisGeneration,
        });

    // ── Choices ─────────────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyChoose(GameState state, ChooseEffect e)
    {
        var options = e.Options.Select(o => o.Description).ToImmutableArray();
        return (state, new ChooseOptionPending("Choose one:", options));
    }

    // ── Compound ────────────────────────────────────────────────

    private static (GameState, PendingAction?) ApplyCompound(
        GameState state, int playerId, CompoundEffect e)
    {
        return ExecuteAll(state, playerId, e.Effects);
    }
}
