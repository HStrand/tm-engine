using TmEngine.Domain.Cards;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Pure static validation of moves against game state.
/// Returns null on success, or an error message on failure.
/// </summary>
public static class MoveValidator
{
    public static string? Validate(GameState state, Move move)
    {
        // Universal checks
        if (state.IsGameOver)
            return "Game is over.";

        // If there's a pending action, only sub-move resolutions are allowed
        if (state.PendingAction != null)
            return ValidatePendingActionMove(state, move);

        // Check it's the correct player's turn
        if (move.PlayerId != state.ActivePlayer.PlayerId)
            return $"It is not player {move.PlayerId}'s turn.";

        return move switch
        {
            PassMove m => ValidatePass(state, m),
            ConvertHeatMove m => ValidateConvertHeat(state, m),
            ConvertPlantsMove m => ValidateConvertPlants(state, m),
            UseStandardProjectMove m => ValidateStandardProject(state, m),
            ClaimMilestoneMove m => ValidateClaimMilestone(state, m),
            FundAwardMove m => ValidateFundAward(state, m),
            PlayCardMove m => ValidatePlayCard(state, m),
            UseCardActionMove m => ValidateUseCardAction(state, m),
            BuyCardsMove m => ValidateBuyCards(state, m),
            DraftCardMove m => ValidateDraftCard(state, m),
            SetupMove m => ValidateSetup(state, m),
            // Sub-move types when no PendingAction (invalid)
            PlaceTileMove or ChooseTargetPlayerMove or SelectCardMove
                or ChooseOptionMove or DiscardCardsMove =>
                "No pending action to resolve.",
            _ => $"Unknown move type: {move.GetType().Name}",
        };
    }

    // ── Action Phase ───────────────────────────────────────────

    private static string? ValidatePass(GameState state, PassMove move)
    {
        if (state.Phase != GamePhase.Action && state.Phase != GamePhase.FinalGreeneryConversion)
            return "Can only pass during the action phase or final greenery conversion.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.Passed)
            return "Player has already passed.";

        return null;
    }

    private static string? ValidateConvertHeat(GameState state, ConvertHeatMove move)
    {
        if (state.Phase != GamePhase.Action)
            return "Can only convert heat during the action phase.";

        var map = MapDefinitions.GetMap(state.Map);
        if (state.Temperature >= map.MaxTemperature)
            return "Temperature is already at maximum.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.Resources.Heat < Constants.HeatPerTemperature)
            return $"Need {Constants.HeatPerTemperature} heat, have {player.Resources.Heat}.";

        if (player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        return null;
    }

    private static string? ValidateConvertPlants(GameState state, ConvertPlantsMove move)
    {
        if (state.Phase != GamePhase.Action && state.Phase != GamePhase.FinalGreeneryConversion)
            return "Can only convert plants during the action phase or final greenery conversion.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.Resources.Plants < Constants.PlantsPerGreenery)
            return $"Need {Constants.PlantsPerGreenery} plants, have {player.Resources.Plants}.";

        if (state.Phase == GamePhase.Action && player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        // Validate placement location using BoardLogic
        var validPlacements = BoardLogic.GetValidGreeneryPlacements(state, move.PlayerId);
        if (!validPlacements.Contains(move.Location))
            return "Invalid greenery placement location.";

        return null;
    }

    private static string? ValidateStandardProject(GameState state, UseStandardProjectMove move)
    {
        if (state.Phase != GamePhase.Action)
            return "Can only use standard projects during the action phase.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        return move.Project switch
        {
            StandardProject.SellPatents => ValidateSellPatents(state, move, player),
            StandardProject.PowerPlant => ValidateResourceCost(player, Constants.PowerPlantCost, "Power Plant"),
            StandardProject.Asteroid => ValidateAsteroidProject(state, player),
            StandardProject.Aquifer => ValidateAquiferProject(state, move, player),
            StandardProject.Greenery => ValidateGreeneryProject(state, move, player),
            StandardProject.City => ValidateCityProject(state, move, player),
            _ => $"Unknown standard project: {move.Project}",
        };
    }

    private static string? ValidateSellPatents(GameState state, UseStandardProjectMove move, PlayerState player)
    {
        if (move.CardsToDiscard.IsDefaultOrEmpty)
            return "Must discard at least one card.";

        foreach (var cardId in move.CardsToDiscard)
        {
            if (!player.Hand.Contains(cardId))
                return $"Card {cardId} is not in player's hand.";
        }

        return null;
    }

    private static string? ValidateResourceCost(PlayerState player, int cost, string projectName)
    {
        if (player.Resources.MegaCredits < cost)
            return $"Need {cost} MC for {projectName}, have {player.Resources.MegaCredits}.";

        return null;
    }

    private static string? ValidateAsteroidProject(GameState state, PlayerState player)
    {
        if (player.Resources.MegaCredits < Constants.AsteroidCost)
            return $"Need {Constants.AsteroidCost} MC for Asteroid, have {player.Resources.MegaCredits}.";

        // Can still play even if temperature is maxed (just no TR gain)
        return null;
    }

    private static string? ValidateAquiferProject(GameState state, UseStandardProjectMove move, PlayerState player)
    {
        if (player.Resources.MegaCredits < Constants.AquiferCost)
            return $"Need {Constants.AquiferCost} MC for Aquifer, have {player.Resources.MegaCredits}.";

        var map = MapDefinitions.GetMap(state.Map);
        if (state.OceansPlaced >= map.MaxOceans)
            return "All oceans have been placed.";

        if (move.Location == null)
            return "Must specify location for ocean tile.";

        var validPlacements = BoardLogic.GetValidOceanPlacements(state);
        return validPlacements.Contains(move.Location.Value) ? null : "Invalid ocean placement location.";
    }

    private static string? ValidateGreeneryProject(GameState state, UseStandardProjectMove move, PlayerState player)
    {
        if (player.Resources.MegaCredits < Constants.GreeneryCost)
            return $"Need {Constants.GreeneryCost} MC for Greenery, have {player.Resources.MegaCredits}.";

        if (move.Location == null)
            return "Must specify location for greenery tile.";

        var validPlacements = BoardLogic.GetValidGreeneryPlacements(state, move.PlayerId);
        return validPlacements.Contains(move.Location.Value) ? null : "Invalid greenery placement location.";
    }

    private static string? ValidateCityProject(GameState state, UseStandardProjectMove move, PlayerState player)
    {
        if (player.Resources.MegaCredits < Constants.CityCost)
            return $"Need {Constants.CityCost} MC for City, have {player.Resources.MegaCredits}.";

        if (move.Location == null)
            return "Must specify location for city tile.";

        var validPlacements = BoardLogic.GetValidCityPlacements(state);
        return validPlacements.Contains(move.Location.Value) ? null : "Invalid city placement location.";
    }

    private static string? ValidateClaimMilestone(GameState state, ClaimMilestoneMove move)
    {
        if (state.Phase != GamePhase.Action)
            return "Can only claim milestones during the action phase.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        if (player.Resources.MegaCredits < Constants.MilestoneCost)
            return $"Need {Constants.MilestoneCost} MC to claim a milestone.";

        if (state.ClaimedMilestones.Count >= Constants.MaxClaimedMilestones)
            return "Maximum milestones already claimed.";

        if (state.ClaimedMilestones.Any(m => m.MilestoneName == move.MilestoneName))
            return $"Milestone '{move.MilestoneName}' is already claimed.";

        var map = MapDefinitions.GetMap(state.Map);
        if (!map.MilestoneNames.Contains(move.MilestoneName))
            return $"Milestone '{move.MilestoneName}' does not exist on this map.";

        // Milestone-specific requirements will be checked by MilestoneLogic (Phase 6)
        return null;
    }

    private static string? ValidateFundAward(GameState state, FundAwardMove move)
    {
        if (state.Phase != GamePhase.Action)
            return "Can only fund awards during the action phase.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        if (state.FundedAwards.Count >= Constants.MaxFundedAwards)
            return "Maximum awards already funded.";

        if (state.FundedAwards.Any(a => a.AwardName == move.AwardName))
            return $"Award '{move.AwardName}' is already funded.";

        var map = MapDefinitions.GetMap(state.Map);
        if (!map.AwardNames.Contains(move.AwardName))
            return $"Award '{move.AwardName}' does not exist on this map.";

        var cost = state.FundedAwards.Count switch
        {
            0 => Constants.AwardFundCost1,
            1 => Constants.AwardFundCost2,
            2 => Constants.AwardFundCost3,
            _ => int.MaxValue,
        };

        if (player.Resources.MegaCredits < cost)
            return $"Need {cost} MC to fund an award, have {player.Resources.MegaCredits}.";

        return null;
    }

    private static string? ValidatePlayCard(GameState state, PlayCardMove move)
    {
        if (state.Phase != GamePhase.Action)
            return "Can only play cards during the action phase.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        if (!player.Hand.Contains(move.CardId))
            return $"Card {move.CardId} is not in player's hand.";

        // Card lookup — if card isn't registered yet, accept structurally valid moves
        if (!CardRegistry.TryGet(move.CardId, out var entry))
            return null;

        var card = entry.Definition;

        // Check requirements (with player's requirement modifier)
        var reqError = RequirementChecker.CanPlayCard(state, move.PlayerId, card);
        if (reqError != null)
            return reqError;

        // Validate payment: steel only for Building tags, titanium only for Space tags
        var payment = move.Payment;
        if (payment.Steel > 0 && !card.Tags.Contains(Tag.Building))
            return "Steel can only be used to pay for cards with a Building tag.";
        if (payment.Titanium > 0 && !card.Tags.Contains(Tag.Space))
            return "Titanium can only be used to pay for cards with a Space tag.";
        if (payment.Heat > 0 && !RequirementChecker.CanUseHeatAsPayment(player))
            return "Cannot use heat to pay for cards (requires Helion corporation).";

        // Check player has the resources they're trying to spend
        if (payment.MegaCredits > player.Resources.MegaCredits)
            return $"Not enough MC: have {player.Resources.MegaCredits}, trying to spend {payment.MegaCredits}.";
        if (payment.Steel > player.Resources.Steel)
            return $"Not enough steel: have {player.Resources.Steel}, trying to spend {payment.Steel}.";
        if (payment.Titanium > player.Resources.Titanium)
            return $"Not enough titanium: have {player.Resources.Titanium}, trying to spend {payment.Titanium}.";
        if (payment.Heat > player.Resources.Heat)
            return $"Not enough heat: have {player.Resources.Heat}, trying to spend {payment.Heat}.";

        // Check total payment covers effective cost (after discounts)
        var discount = RequirementChecker.GetCardDiscount(player, card.Tags);
        var effectiveCost = Math.Max(0, card.Cost - discount);
        var steelValue = RequirementChecker.GetSteelValue(player);
        var titaniumValue = RequirementChecker.GetTitaniumValue(player);
        var totalValue = payment.TotalValue(steelValue, titaniumValue);

        if (totalValue < effectiveCost)
            return $"Payment ({totalValue} MC value) does not cover card cost ({effectiveCost} MC).";

        return null;
    }

    private static string? ValidateUseCardAction(GameState state, UseCardActionMove move)
    {
        if (state.Phase != GamePhase.Action)
            return "Can only use card actions during the action phase.";

        var player = state.GetPlayer(move.PlayerId);
        if (player.ActionsThisTurn >= 2)
            return "Already took 2 actions this turn.";

        if (!player.PlayedCards.Contains(move.CardId) && player.CorporationId != move.CardId)
            return $"Card {move.CardId} is not in play.";

        if (player.UsedCardActions.Contains(move.CardId))
            return $"Card {move.CardId} action already used this generation.";

        return null;
    }

    // ── Research Phase ─────────────────────────────────────────

    private static string? ValidateBuyCards(GameState state, BuyCardsMove move)
    {
        if (state.Phase != GamePhase.Research)
            return "Can only buy cards during the research phase.";

        var player = state.GetPlayer(move.PlayerId);
        var cost = move.CardIds.Length * Constants.CardBuyCost;
        if (player.Resources.MegaCredits < cost)
            return $"Need {cost} MC to buy {move.CardIds.Length} cards, have {player.Resources.MegaCredits}.";

        return null;
    }

    private static string? ValidateDraftCard(GameState state, DraftCardMove move)
    {
        if (state.Phase != GamePhase.Research)
            return "Can only draft during the research phase.";

        if (!state.DraftVariant)
            return "Draft variant is not enabled.";

        if (state.Draft == null)
            return "No active draft.";

        return null;
    }

    // ── Setup Phase ────────────────────────────────────────────

    private static string? ValidateSetup(GameState state, SetupMove move)
    {
        if (state.Phase != GamePhase.Setup)
            return "Can only submit setup during the setup phase.";

        if (state.PreludeExpansion && move.PreludeIds.Length != Constants.PreludesKept)
            return $"Must select exactly {Constants.PreludesKept} preludes.";

        if (!state.PreludeExpansion && !move.PreludeIds.IsDefaultOrEmpty && move.PreludeIds.Length > 0)
            return "Prelude expansion is not enabled.";

        // Card cost validation will be done during application (after corporation starting MC is known)
        return null;
    }

    // ── Sub-Move Resolution ────────────────────────────────────

    private static string? ValidatePendingActionMove(GameState state, Move move)
    {
        if (move.PlayerId != state.ActivePlayer.PlayerId)
            return $"It is not player {move.PlayerId}'s turn.";

        return (state.PendingAction, move) switch
        {
            (PlaceTilePending pending, PlaceTileMove placeTile) =>
                pending.ValidLocations.Contains(placeTile.Location)
                    ? null
                    : "Invalid tile placement location.",

            (RemoveResourcePending pending, ChooseTargetPlayerMove choose) =>
                pending.ValidTargetPlayerIds.Contains(choose.TargetPlayerId)
                    ? null
                    : "Invalid target player.",

            (AddCardResourcePending pending, SelectCardMove select) =>
                pending.ValidCardIds.Contains(select.CardId)
                    ? null
                    : "Invalid card selection.",

            (ChooseOptionPending pending, ChooseOptionMove choose) =>
                choose.OptionIndex >= 0 && choose.OptionIndex < pending.Options.Length
                    ? null
                    : "Invalid option index.",

            (ReduceProductionPending pending, ChooseTargetPlayerMove choose) =>
                pending.ValidTargetPlayerIds.Contains(choose.TargetPlayerId)
                    ? null
                    : "Invalid target player.",

            (DiscardCardsPending pending, DiscardCardsMove discard) =>
                discard.CardIds.Length == pending.Count
                    ? null
                    : $"Must discard exactly {pending.Count} cards.",

            _ => $"Expected resolution for {state.PendingAction!.GetType().Name}, got {move.GetType().Name}.",
        };
    }

}
