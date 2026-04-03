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

public sealed record DraftOptions(ImmutableList<string> DraftHand);

public sealed record BuyCardsOptions(
    ImmutableList<string> AvailableCards,
    int CostPerCard);

public sealed record PlayableCard(
    string CardId,
    int EffectiveCost,
    bool CanUseSteel,
    bool CanUseTitanium,
    bool CanUseHeat);

public sealed record StandardProjectOption(
    StandardProject Project,
    bool Available,
    int Cost,
    ImmutableArray<HexCoord>? ValidLocations = null);

public sealed record ClaimableMilestone(string Name);

public sealed record FundableAward(string Name, int Cost);

public sealed record UsableCardAction(string CardId);

public sealed record ActionPhaseOptions
{
    public bool CanPass { get; init; }
    public bool CanEndTurn { get; init; }
    public bool CanConvertHeat { get; init; }
    public bool CanConvertPlants { get; init; }
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
        if (state.IsGameOver)
            return new AvailableMoves { GameOver = true };

        // Pending action — only the active player can resolve it
        if (state.PendingAction != null)
        {
            if (state.ActivePlayer.PlayerId != playerId)
                return new AvailableMoves { WaitingForOtherPlayer = true };

            return new AvailableMoves { PendingAction = state.PendingAction };
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
        if (state.Prelude == null || state.ActivePlayer.PlayerId != playerId)
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
            var draftHand = state.Draft.DraftHands[playerIndex];
            if (draftHand.Count > 0)
                return new AvailableMoves { Draft = new DraftOptions(draftHand) };
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
        if (state.ActivePlayer.PlayerId != playerId)
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
        return new AvailableMoves
        {
            Actions = new ActionPhaseOptions
            {
                CanPass = player.ActionsThisTurn == 0,
                CanEndTurn = player.ActionsThisTurn >= 1,
                CanConvertHeat = player.Resources.Heat >= Constants.HeatPerTemperature
                                 && state.Temperature < map.MaxTemperature,
                CanConvertPlants = player.Resources.Plants >= Constants.PlantsPerGreenery
                                   && BoardLogic.GetValidGreeneryPlacements(state, playerId).Length > 0,
                ValidGreeneryLocations = player.Resources.Plants >= Constants.PlantsPerGreenery
                    ? BoardLogic.GetValidGreeneryPlacements(state, playerId) : [],
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
        if (state.ActivePlayer.PlayerId != playerId)
            return new AvailableMoves { WaitingForOtherPlayer = true };

        var player = state.GetPlayer(playerId);
        var greeneryLocations = player.Resources.Plants >= Constants.PlantsPerGreenery
            ? BoardLogic.GetValidGreeneryPlacements(state, playerId) : [];
        bool canConvert = greeneryLocations.Length > 0;

        return new AvailableMoves
        {
            FinalGreenery = new FinalGreeneryOptions(canConvert, CanPass: true, greeneryLocations)
        };
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static ImmutableList<PlayableCard> GetPlayableCards(GameState state, PlayerState player)
    {
        var result = ImmutableList.CreateBuilder<PlayableCard>();

        foreach (var cardId in player.Hand)
        {
            if (!CardRegistry.TryGet(cardId, out var entry))
                continue;

            var card = entry.Definition;

            // Check requirements
            if (RequirementChecker.CanPlayCard(state, player.PlayerId, card) != null)
                continue;

            // Check mandatory effects affordability
            if (RequirementChecker.CanAffordEffects(state, player.PlayerId, entry) != null)
                continue;

            var discount = RequirementChecker.GetCardDiscount(player, card.Tags);
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

            result.Add(new PlayableCard(cardId, effectiveCost, canUseSteel, canUseTitanium, canUseHeat));
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

        // Greenery
        var greeneryLocations = BoardLogic.GetValidGreeneryPlacements(state, player.PlayerId);
        result.Add(new StandardProjectOption(StandardProject.Greenery,
            mc >= Constants.GreeneryCost && greeneryLocations.Length > 0, Constants.GreeneryCost, greeneryLocations));

        // City
        var cityLocations = BoardLogic.GetValidCityPlacements(state);
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

            result.Add(new UsableCardAction(cardId));
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
            if (preconditionMet)
                result.Add(new UsableCardAction(player.CorporationId));
        }

        return result.ToImmutable();
    }
}
