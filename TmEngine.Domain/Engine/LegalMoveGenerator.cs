using System;
using System.Collections.Immutable;
using System.Linq;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Describes the legal moves available to a player in the current game state.
/// Exactly one of the phase-specific properties will be non-null.
/// </summary>
public sealed record AvailableMoves
{
    /// <summary>True if the game is over — no moves available.</summary>
    public bool GameOver { get; init; }

    /// <summary>True if it is not this player's turn and no simultaneous action is expected.</summary>
    public bool WaitingForOtherPlayer { get; init; }

    /// <summary>Available during Setup phase.</summary>
    public SetupOptions? Setup { get; init; }

    /// <summary>Available during PreludePlacement phase.</summary>
    public PreludeOptions? Prelude { get; init; }

    /// <summary>Available during Research phase (draft sub-phase).</summary>
    public DraftOptions? Draft { get; init; }

    /// <summary>Available during Research phase (buy sub-phase).</summary>
    public BuyCardsOptions? BuyCards { get; init; }

    /// <summary>Available during Action phase when a PendingAction must be resolved.</summary>
    public PendingAction? PendingAction { get; init; }

    /// <summary>
    /// Cards from the player's hand that are playable in response to a
    /// <see cref="PlayCardFromHandPending"/>. Effective costs reflect the pending
    /// action's discount and any "ignore global requirements" waiver. Empty for
    /// other pending action types.
    /// </summary>
    public ImmutableList<PlayableCard> PendingPlayableCards { get; init; } = [];

    /// <summary>Available during Action phase for normal actions.</summary>
    public ActionPhaseOptions? Actions { get; init; }

    /// <summary>Available during FinalGreeneryConversion phase.</summary>
    public FinalGreeneryOptions? FinalGreenery { get; init; }
}

public sealed record PreludeOptions(ImmutableList<string> RemainingPreludes);

public sealed record SetupOptions(
    ImmutableList<string> AvailableCorporations,
    ImmutableList<string> AvailablePreludes,
    ImmutableList<string> AvailableCards);

public sealed record DraftOptions(ImmutableList<string> DraftHand, int PassingToPlayerId);

public sealed record BuyCardsOptions(
    ImmutableList<string> AvailableCards,
    int CostPerCard);

public sealed record PlayableCard(
    string CardId,
    int EffectiveCost,
    bool CanUseSteel,
    bool CanUseTitanium,
    bool CanUseHeat,
    int SteelValue,
    int TitaniumValue);

public sealed record StandardProjectOption(
    StandardProject Project,
    bool Available,
    int Cost,
    ImmutableArray<HexCoord>? ValidLocations = null);

public sealed record ClaimableMilestone(string Name);

public sealed record FundableAward(string Name, int Cost);

public sealed record UsableCardAction(string CardId, bool AllowSteel = false, bool CanUseHeat = false, int MCCost = 0, int SteelValue = 0);

public sealed record ActionPhaseOptions
{
    public bool CanPass { get; init; }
    public bool CanEndTurn { get; init; }
    public bool CanConvertHeat { get; init; }
    public bool CanConvertPlants { get; init; }
    public int PlantConversionCost { get; init; } = Constants.PlantsPerGreenery;
    public ImmutableArray<HexCoord> ValidGreeneryLocations { get; init; } = [];
    public bool CanPerformFirstAction { get; init; }
    public ImmutableList<PlayableCard> PlayableCards { get; init; } = [];
    public ImmutableList<StandardProjectOption> StandardProjects { get; init; } = [];
    public ImmutableList<ClaimableMilestone> ClaimableMilestones { get; init; } = [];
    public ImmutableList<FundableAward> FundableAwards { get; init; } = [];
    public ImmutableList<UsableCardAction> UsableCardActions { get; init; } = [];
}

public sealed record FinalGreeneryOptions(
    bool CanConvert,
    bool CanPass,
    ImmutableArray<HexCoord> ValidGreeneryLocations = default);

/// <summary>
/// Generates the set of legal moves available to a player in the current game state.
/// Pure function: state → available moves.
/// </summary>
public static class LegalMoveGenerator
{
    public static AvailableMoves GetLegalMoves(GameState state, int playerId)
    {
        if (state.IsGameOver())
            return new AvailableMoves { GameOver = true };

        // Pending action — only the active player can resolve it
        if (state.PendingAction != null)
        {
            if (state.GetActivePlayer().PlayerId != playerId)
                return new AvailableMoves { WaitingForOtherPlayer = true };

            var pendingPlayable = state.PendingAction is PlayCardFromHandPending pcfh
                ? GetPlayableCards(state, state.GetPlayer(playerId),
                    costDiscount: pcfh.CostDiscount,
                    ignoreGlobalRequirements: pcfh.IgnoreGlobalRequirements)
                : [];

            return new AvailableMoves
            {
                PendingAction = state.PendingAction,
                PendingPlayableCards = pendingPlayable,
            };
        }

        return state.Phase switch
        {
            GamePhase.Setup => GetSetupMoves(state, playerId),
            GamePhase.PreludePlacement => GetPreludeMoves(state, playerId),
            GamePhase.Research => GetResearchMoves(state, playerId),
            GamePhase.Action => GetActionMoves(state, playerId),
            GamePhase.FinalGreeneryConversion => GetFinalGreeneryMoves(state, playerId),
            _ => new AvailableMoves { GameOver = true },
        };
    }

    private static AvailableMoves GetSetupMoves(GameState state, int playerId)
    {
        if (state.Setup == null)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        var playerIndex = state.GetPlayerIndex(playerId);
        if (state.Setup.SubmittedMoves[playerIndex] != null)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        return new AvailableMoves
        {
            Setup = new SetupOptions(
                state.Setup.DealtCorporations[playerIndex],
                state.Setup.DealtPreludes[playerIndex],
                state.Setup.DealtCards[playerIndex])
        };
    }

    private static AvailableMoves GetPreludeMoves(GameState state, int playerId)
    {
        if (state.Prelude == null || state.GetActivePlayer().PlayerId != playerId)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        var playerIndex = state.GetPlayerIndex(playerId);
        var remaining = state.Prelude.RemainingPreludes[playerIndex];

        if (remaining.Count == 0)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        return new AvailableMoves
        {
            Prelude = new PreludeOptions(remaining)
        };
    }

    private static AvailableMoves GetResearchMoves(GameState state, int playerId)
    {
        var playerIndex = state.GetPlayerIndex(playerId);

        // Draft sub-phase
        if (state.DraftVariant && state.Draft != null)
        {
            // Check if this player has already picked this round
            var alreadyPicked = state.Draft.DraftedCards[playerIndex].Count > state.Draft.DraftRound;
            if (alreadyPicked)
                return new AvailableMoves { WaitingForOtherPlayer = true };

            var draftHand = state.Draft.DraftHands[playerIndex];
            if (draftHand.Count > 0)
            {
                var passingToId = state.Draft.PassingTo[playerId];
                return new AvailableMoves { Draft = new DraftOptions(draftHand, passingToId) };
            }
        }

        // Buy sub-phase
        if (state.Research != null)
        {
            if (state.Research.Submitted[playerIndex])
                return new AvailableMoves { WaitingForOtherPlayer = true };

            return new AvailableMoves
            {
                BuyCards = new BuyCardsOptions(
                    state.Research.AvailableCards[playerIndex],
                    Constants.CardBuyCost)
            };
        }

        return new AvailableMoves { WaitingForOtherPlayer = true };
    }

    private static AvailableMoves GetActionMoves(GameState state, int playerId)
    {
        if (state.GetActivePlayer().PlayerId != playerId)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        var player = state.GetPlayer(playerId);
        if (player.Passed)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        if (player.ActionsThisTurn >= 2)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        var map = MapDefinitions.GetMap(state.Map);

        // Check first action requirements
        bool mustPerformFirstAction = !player.PerformedFirstAction;
        bool mustFundFreeAward = player.HasFreeAwardFunding;

        // If first action is required, only that + pass/end turn are available
        if (mustPerformFirstAction)
        {
            return new AvailableMoves
            {
                Actions = new ActionPhaseOptions
                {
                    CanPass = player.ActionsThisTurn == 0,
                    CanEndTurn = player.ActionsThisTurn >= 1,
                    CanPerformFirstAction = true,
                }
            };
        }

        // If Vitor must fund free award, only awards + pass/end turn
        if (mustFundFreeAward)
        {
            var freeAwards = GetFundableAwards(state, player, map, isFree: true);
            return new AvailableMoves
            {
                Actions = new ActionPhaseOptions
                {
                    CanPass = player.ActionsThisTurn == 0,
                    CanEndTurn = player.ActionsThisTurn >= 1,
                    FundableAwards = freeAwards,
                }
            };
        }

        // Normal action phase — enumerate all available actions
        var plantGreeneryLocations = player.Resources.Plants >= RequirementChecker.GetPlantConversionCost(player)
            ? BoardLogic.FilterAffordablePlacements(state, playerId,
                BoardLogic.GetValidGreeneryPlacements(state, playerId))
            : [];

        return new AvailableMoves
        {
            Actions = new ActionPhaseOptions
            {
                CanPass = player.ActionsThisTurn == 0,
                CanEndTurn = player.ActionsThisTurn >= 1,
                CanConvertHeat = player.Resources.Heat >= Constants.HeatPerTemperature,
                CanConvertPlants = plantGreeneryLocations.Length > 0,
                PlantConversionCost = RequirementChecker.GetPlantConversionCost(player),
                ValidGreeneryLocations = plantGreeneryLocations,
                CanPerformFirstAction = false,
                PlayableCards = GetPlayableCards(state, player),
                StandardProjects = GetStandardProjects(state, player, map),
                ClaimableMilestones = GetClaimableMilestones(state, player, map),
                FundableAwards = GetFundableAwards(state, player, map, isFree: false),
                UsableCardActions = GetUsableCardActions(state, player),
            }
        };
    }

    private static AvailableMoves GetFinalGreeneryMoves(GameState state, int playerId)
    {
        if (state.GetActivePlayer().PlayerId != playerId)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        var player = state.GetPlayer(playerId);
        var greeneryLocations = player.Resources.Plants >= RequirementChecker.GetPlantConversionCost(player)
            ? BoardLogic.FilterAffordablePlacements(state, playerId,
                BoardLogic.GetValidGreeneryPlacements(state, playerId))
            : [];
        bool canConvert = greeneryLocations.Length > 0;

        return new AvailableMoves
        {
            FinalGreenery = new FinalGreeneryOptions(canConvert, CanPass: true, greeneryLocations)
        };
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static ImmutableList<PlayableCard> GetPlayableCards(
        GameState state, PlayerState player,
        int costDiscount = 0, bool ignoreGlobalRequirements = false)
    {
        var result = ImmutableList.CreateBuilder<PlayableCard>();

        foreach (var cardId in player.Hand)
        {
            if (!CardRegistry.TryGet(cardId, out var entry))
                continue;

            var card = entry.Definition;

            // Check requirements (optionally waiving global parameters)
            if (RequirementChecker.CanPlayCard(state, player.PlayerId, card,
                    ignoreGlobalRequirements: ignoreGlobalRequirements) != null)
                continue;

            // Check mandatory effects affordability
            if (RequirementChecker.CanAffordEffects(state, player.PlayerId, entry) != null)
                continue;

            // Check tile placement effects have valid locations
            if (!HasValidPlacements(state, player.PlayerId, entry))
                continue;

            var discount = RequirementChecker.GetCardDiscount(player, card.Tags) + costDiscount + player.NextCardDiscount;
            var effectiveCost = Math.Max(0, card.Cost - discount);

            // Check if the player can afford it with any combination of resources
            var steelValue = RequirementChecker.GetSteelValue(player);
            var titaniumValue = RequirementChecker.GetTitaniumValue(player);
            bool canUseSteel = card.Tags.Contains(Tag.Building);
            bool canUseTitanium = card.Tags.Contains(Tag.Space);
            bool canUseHeat = RequirementChecker.CanUseHeatAsPayment(player);

            int maxPayable = player.Resources.MegaCredits
                + (canUseSteel ? player.Resources.Steel * steelValue : 0)
                + (canUseTitanium ? player.Resources.Titanium * titaniumValue : 0)
                + (canUseHeat ? player.Resources.Heat : 0);

            if (maxPayable < effectiveCost)
                continue;

            result.Add(new PlayableCard(cardId, effectiveCost, canUseSteel, canUseTitanium, canUseHeat, steelValue, titaniumValue));
        }

        return result.ToImmutable();
    }

    private static ImmutableList<StandardProjectOption> GetStandardProjects(
        GameState state, PlayerState player, MapDefinition map)
    {
        var result = ImmutableList.CreateBuilder<StandardProjectOption>();
        int mc = player.Resources.MegaCredits;

        // Sell Patents — available if player has cards in hand
        result.Add(new StandardProjectOption(StandardProject.SellPatents, player.Hand.Count > 0, 0));

        // Power Plant
        var ppCost = RequirementChecker.GetPowerPlantCost(player);
        result.Add(new StandardProjectOption(StandardProject.PowerPlant, mc >= ppCost, ppCost));

        // Asteroid
        result.Add(new StandardProjectOption(StandardProject.Asteroid, mc >= Constants.AsteroidCost, Constants.AsteroidCost));

        // Aquifer
        bool oceansAvailable = state.OceansPlaced < map.MaxOceans;
        var oceanLocations = oceansAvailable ? BoardLogic.GetValidOceanPlacements(state) : [];
        result.Add(new StandardProjectOption(StandardProject.Aquifer,
            mc >= Constants.AquiferCost && oceanLocations.Length > 0, Constants.AquiferCost, oceanLocations));

        // Greenery: filter out hexes where the extra placement cost can't be
        // paid from MC left after the project cost (Hellas South Pole: +6 MC).
        var greeneryLocations = BoardLogic.GetValidGreeneryPlacements(state, player.PlayerId);
        greeneryLocations = FilterAffordableAfterSpending(state, greeneryLocations, mc - Constants.GreeneryCost);
        result.Add(new StandardProjectOption(StandardProject.Greenery,
            mc >= Constants.GreeneryCost && greeneryLocations.Length > 0, Constants.GreeneryCost, greeneryLocations));

        // City
        var cityLocations = BoardLogic.GetValidCityPlacements(state);
        cityLocations = FilterAffordableAfterSpending(state, cityLocations, mc - Constants.CityCost);
        result.Add(new StandardProjectOption(StandardProject.City,
            mc >= Constants.CityCost && cityLocations.Length > 0, Constants.CityCost, cityLocations));

        return result.ToImmutable();
    }

    private static ImmutableList<ClaimableMilestone> GetClaimableMilestones(
        GameState state, PlayerState player, MapDefinition map)
    {
        if (state.ClaimedMilestones.Count >= Constants.MaxClaimedMilestones)
            return [];
        if (player.Resources.MegaCredits < Constants.MilestoneCost)
            return [];

        var result = ImmutableList.CreateBuilder<ClaimableMilestone>();

        foreach (var name in map.MilestoneNames)
        {
            // Skip already claimed
            if (state.ClaimedMilestones.Any(m => m.MilestoneName == name))
                continue;

            // Check eligibility
            if (MilestoneAndAwardLogic.CheckMilestoneEligibility(state, player.PlayerId, name) == null)
                result.Add(new ClaimableMilestone(name));
        }

        return result.ToImmutable();
    }

    private static ImmutableList<FundableAward> GetFundableAwards(
        GameState state, PlayerState player, MapDefinition map, bool isFree)
    {
        if (state.FundedAwards.Count >= Constants.MaxFundedAwards)
            return [];

        int cost = isFree ? 0 : state.FundedAwards.Count switch
        {
            0 => Constants.AwardFundCost1,
            1 => Constants.AwardFundCost2,
            2 => Constants.AwardFundCost3,
            _ => int.MaxValue,
        };

        if (!isFree && player.Resources.MegaCredits < cost)
            return [];

        var result = ImmutableList.CreateBuilder<FundableAward>();

        foreach (var name in map.AwardNames)
        {
            if (state.FundedAwards.Any(a => a.AwardName == name))
                continue;

            result.Add(new FundableAward(name, cost));
        }

        return result.ToImmutable();
    }

    private static ImmutableList<UsableCardAction> GetUsableCardActions(GameState state, PlayerState player)
    {
        var result = ImmutableList.CreateBuilder<UsableCardAction>();

        // Blue cards in tableau
        foreach (var cardId in player.PlayedCards)
        {
            if (player.UsedCardActions.Contains(cardId))
                continue;

            if (!CardRegistry.TryGet(cardId, out var entry) || entry.Action == null)
                continue;

            // Check precondition
            if (entry.Action.Precondition != null)
            {
                bool preconditionMet = entry.Action.Precondition.Value switch
                {
                    ActionPrecondition.IncreasedTRThisGeneration => player.IncreasedTRThisGeneration,
                    _ => true,
                };
                if (!preconditionMet) continue;
            }

            // Check affordability of the explicit cost
            if (!CanAffordActionCost(player, entry.Action))
                continue;

            // Check affordability of the effects (production floors etc.)
            if (RequirementChecker.CanAffordActionEffects(state, player.PlayerId, entry.Action) != null)
                continue;

            var isSteelCost = entry.Action.Cost is SpendMCOrSteelCost;
            var mcCost = entry.Action.Cost is SpendMCOrSteelCost sc ? sc.Amount
                       : entry.Action.Cost is SpendMCCost mc ? mc.Amount : 0;
            result.Add(new UsableCardAction(cardId, AllowSteel: isSteelCost,
                CanUseHeat: isSteelCost && RequirementChecker.CanUseHeatAsPayment(player),
                MCCost: mcCost,
                SteelValue: isSteelCost ? RequirementChecker.GetSteelValue(player) : 0));
        }

        // Corporation action
        if (!string.IsNullOrEmpty(player.CorporationId)
            && !player.UsedCardActions.Contains(player.CorporationId)
            && CardRegistry.TryGet(player.CorporationId, out var corpEntry)
            && corpEntry.Action != null)
        {
            bool preconditionMet = true;
            if (corpEntry.Action.Precondition != null)
            {
                preconditionMet = corpEntry.Action.Precondition.Value switch
                {
                    ActionPrecondition.IncreasedTRThisGeneration => player.IncreasedTRThisGeneration,
                    _ => true,
                };
            }
            if (preconditionMet
                && CanAffordActionCost(player, corpEntry.Action)
                && RequirementChecker.CanAffordActionEffects(state, player.PlayerId, corpEntry.Action) == null)
            {
                var isSteelCost2 = corpEntry.Action.Cost is SpendMCOrSteelCost;
                var mcCost2 = corpEntry.Action.Cost is SpendMCOrSteelCost sc2 ? sc2.Amount
                            : corpEntry.Action.Cost is SpendMCCost mc2 ? mc2.Amount : 0;
                result.Add(new UsableCardAction(player.CorporationId, AllowSteel: isSteelCost2,
                    CanUseHeat: isSteelCost2 && RequirementChecker.CanUseHeatAsPayment(player),
                    MCCost: mcCost2,
                    SteelValue: isSteelCost2 ? RequirementChecker.GetSteelValue(player) : 0));
            }
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Filter placements by whether an extra placement cost (Hellas South Pole)
    /// can be paid from the MC balance the player will have after already
    /// spending `mcAfterCost` on the action itself.
    /// </summary>
    private static ImmutableArray<HexCoord> FilterAffordableAfterSpending(
        GameState state, ImmutableArray<HexCoord> locations, int mcAfterCost)
    {
        var builder = ImmutableArray.CreateBuilder<HexCoord>();
        foreach (var loc in locations)
        {
            var extra = BoardLogic.GetExtraPlacementCost(state, loc);
            if (extra == 0 || mcAfterCost >= extra)
                builder.Add(loc);
        }
        return builder.ToImmutable();
    }

    private static bool HasValidPlacements(GameState state, int playerId, CardEntry entry)
    {
        foreach (var effect in entry.OnPlayEffects)
        {
            if (effect is PlaceTileEffect tileEffect)
            {
                // For AdjacentTo2Cities, start from open land (not the default city
                // placement which excludes hexes adjacent to any city).
                var locations = tileEffect.Constraint == PlacementConstraint.AdjacentTo2Cities
                    ? BoardLogic.GetValidLandPlacements(state, playerId)
                    : BoardLogic.GetValidTilePlacements(state, tileEffect.TileType, playerId);
                // Apply constraint filter if present
                if (tileEffect.Constraint != null)
                {
                    locations = tileEffect.Constraint.Value switch
                    {
                        PlacementConstraint.Isolated => locations
                            .Where(c => !c.GetAdjacentCoords().Any(a => state.PlacedTiles.ContainsKey(a)))
                            .ToImmutableArray(),
                        PlacementConstraint.AdjacentTo2Cities => locations
                            .Where(c => c.GetAdjacentCoords().Count(a =>
                                state.PlacedTiles.TryGetValue(a, out var t) && (t.Type == TileType.City || t.Type == TileType.Capital)) >= 2)
                            .ToImmutableArray(),
                        _ => locations,
                    };
                }
                // Filter out hexes whose extra placement cost (Hellas South Pole)
                // the player can't afford.
                if (tileEffect.TileType != TileType.Ocean)
                    locations = BoardLogic.FilterAffordablePlacements(state, playerId, locations);
                if (locations.Length == 0) return false;
            }
            else if (effect is PlaceOceanEffect)
            {
                var map = MapDefinitions.GetMap(state.Map);
                if (state.OceansPlaced >= map.MaxOceans) continue;
                var locations = BoardLogic.GetValidOceanPlacements(state);
                if (locations.Length == 0) return false;
            }
        }
        return true;
    }

    private static bool CanAffordActionCost(PlayerState player, CardAction action)
    {
        if (action.Cost == null) return true;

        return action.Cost switch
        {
            SpendMCOrSteelCost c =>
                player.Resources.MegaCredits
                + (player.Resources.Steel * RequirementChecker.GetSteelValue(player))
                + (RequirementChecker.CanUseHeatAsPayment(player) ? player.Resources.Heat : 0)
                >= c.Amount,
            SpendMCCost c => player.Resources.MegaCredits >= c.Amount,
            SpendEnergyCost c => player.Resources.Energy >= c.Amount,
            SpendSteelCost c => player.Resources.Steel >= c.Amount,
            SpendTitaniumCost c => player.Resources.Titanium >= c.Amount,
            SpendHeatCost c => player.Resources.Heat >= c.Amount,
            SpendCardResourceCost c => player.CardResources.Values.Any(v => v >= c.Amount),
            _ => true,
        };
    }
}
