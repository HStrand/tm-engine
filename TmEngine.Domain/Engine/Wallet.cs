using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Costs stated in MC that come without a payment breakdown: standard projects, milestones, awards, buying
/// cards, card actions that cost MC, and the Hellas South Pole ocean. Helion may pay any of them with heat.
/// A move can say how much heat to use (<see cref="Move.Heat"/>); when it does not, MC is spent first and heat
/// covers what MC cannot.
/// </summary>
public static class Wallet
{
    /// <summary>How much the player can put towards such a cost.</summary>
    public static int Spendable(PlayerState player) =>
        player.Resources.MegaCredits + (RequirementChecker.CanUseHeatAsPayment(player) ? player.Resources.Heat : 0);

    /// <summary>The player's resources after paying. The cost must already have been checked against <see cref="Spendable"/>.</summary>
    public static ResourceSet Pay(PlayerState player, int cost)
    {
        var fromHeat = RequirementChecker.CanUseHeatAsPayment(player)
            ? Math.Clamp(cost - player.Resources.MegaCredits, 0, player.Resources.Heat)
            : 0;

        return player.Resources
            .Add(ResourceType.MegaCredits, -(cost - fromHeat))
            .Add(ResourceType.Heat, -fromHeat);
    }

    /// <summary>
    /// The MC a move costs where the move itself carries no payment breakdown. Zero for moves that cost nothing
    /// or that say how they are paid (playing a card, actions payable with steel or titanium).
    /// </summary>
    public static int FixedCost(GameState state, Move move)
    {
        var player = state.GetPlayer(move.PlayerId);
        return move switch
        {
            PowerPlantMove => RequirementChecker.GetPowerPlantCost(player),
            AsteroidMove => Constants.AsteroidCost,
            AquiferMove => Constants.AquiferCost,
            GreeneryMove m => Constants.GreeneryCost + BoardLogic.GetExtraPlacementCost(state, m.Location),
            CityMove m => Constants.CityCost + BoardLogic.GetExtraPlacementCost(state, m.Location),
            ConvertPlantsMove m => BoardLogic.GetExtraPlacementCost(state, m.Location),
            PlaceTileMove m when state.PendingAction is PlaceTilePending { TileType: not TileType.Ocean } =>
                BoardLogic.GetExtraPlacementCost(state, m.Location),
            ClaimMilestoneMove => Constants.MilestoneCost,
            FundAwardMove when !player.HasFreeAwardFunding => state.FundedAwards.Count switch
            {
                0 => Constants.AwardFundCost1,
                1 => Constants.AwardFundCost2,
                _ => Constants.AwardFundCost3,
            },
            BuyCardsMove m => m.CardIds.Length
                * (state.PendingAction is DrawKeepPending drawKeep ? drawKeep.CostPerCard : Constants.CardBuyCost),
            UseCardActionMove { Payment: null } m
                when CardRegistry.TryGet(m.CardId, out var card) && card.Action?.Cost is SpendMCCost cost => cost.Amount,
            _ => 0,
        };
    }

    /// <summary>
    /// Check a move's explicit heat payment: only Helion may make one, it cannot exceed the cost or the heat
    /// held, and MC must cover the rest. Returns null when there is none or it is fine.
    /// </summary>
    public static string? ValidateHeat(GameState state, Move move)
    {
        if (move.Heat is not { } heat)
            return null;

        var player = state.GetPlayer(move.PlayerId);
        var cost = FixedCost(state, move);

        if (heat < 0)
            return "Heat payment cannot be negative.";
        if (heat > 0 && !RequirementChecker.CanUseHeatAsPayment(player))
            return "Cannot use heat to pay (requires Helion corporation).";
        if (heat > cost)
            return $"Cannot pay {heat} heat towards a cost of {cost} MC.";
        if (heat > player.Resources.Heat)
            return $"Not enough heat: have {player.Resources.Heat}, trying to spend {heat}.";
        if (cost - heat > player.Resources.MegaCredits)
            return $"Not enough MC: {cost - heat} MC is left to pay after {heat} heat, have {player.Resources.MegaCredits}.";

        return null;
    }

    /// <summary>
    /// Honour a move's explicit heat payment by exchanging that much heat for MC up front. The move's cost is
    /// then paid from MC alone, which <see cref="ValidateHeat"/> has made sure is enough.
    /// </summary>
    public static GameState ExchangeChosenHeat(GameState state, Move move)
    {
        if (move.Heat is not { } heat || heat == 0)
            return state;

        return state.UpdatePlayer(move.PlayerId, p => p with
        {
            Resources = p.Resources.Add(ResourceType.Heat, -heat).Add(ResourceType.MegaCredits, heat),
        });
    }
}
