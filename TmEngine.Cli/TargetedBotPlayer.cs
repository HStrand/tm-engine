using Newtonsoft.Json.Linq;

namespace TmEngine.Cli;

/// <summary>
/// A bot player that ensures a specific target card is bought and played.
/// Always selects/buys the target card when available, makes random moves otherwise,
/// and prioritizes playing the target card when it's in hand and playable.
/// </summary>
public class TargetedBotPlayer : BotPlayer
{
    private readonly string _targetCardId;
    private bool _targetCardPlayed;

    public TargetedBotPlayer(int playerId, string targetCardId) : base(playerId)
    {
        _targetCardId = targetCardId;
    }

    public bool TargetCardPlayed => _targetCardPlayed;

    public new (JObject Move, string Description) PickMove(
        AvailableMovesDto moves, PlayerStateDto botState)
    {
        if (moves.Setup != null)
            return PickSetupWithTarget(moves.Setup, botState);
        if (moves.Draft != null)
            return PickDraftWithTarget(moves.Draft);
        if (moves.BuyCards != null)
            return PickBuyCardsWithTarget(moves.BuyCards, botState);
        if (moves.Actions != null)
            return PickActionWithTarget(moves.Actions, botState);

        if (moves.PendingAction != null)
            return PickPendingActionWithTarget(moves.PendingAction, botState);

        // For everything else (preludes, final greenery), use base behavior
        return base.PickMove(moves, botState);
    }

    private (JObject, string) PickSetupWithTarget(SetupOptionsDto setup, PlayerStateDto botState)
    {
        // Always buy the target card if it's in the dealt cards
        var basePick = base.PickMove(new AvailableMovesDto { Setup = setup }, botState);
        var move = basePick.Move;

        // Ensure target card is in the buy list if available
        var availableCards = setup.AvailableCards;
        if (availableCards.Contains(_targetCardId))
        {
            var cardIds = move["cardIdsToBuy"]?.ToObject<List<string>>() ?? new();
            if (!cardIds.Contains(_targetCardId))
                cardIds.Add(_targetCardId);
            move["cardIdsToBuy"] = new JArray(cardIds.ToArray());
        }

        // If target is a corporation, select it
        if (setup.AvailableCorporations.Contains(_targetCardId))
            move["corporationId"] = _targetCardId;

        // If target is a prelude, select it
        if (setup.AvailablePreludes.Contains(_targetCardId))
        {
            var preludeIds = move["preludeIds"]?.ToObject<List<string>>() ?? new();
            if (!preludeIds.Contains(_targetCardId))
            {
                if (preludeIds.Count >= 2)
                    preludeIds[0] = _targetCardId;
                else
                    preludeIds.Add(_targetCardId);
            }
            move["preludeIds"] = new JArray(preludeIds.ToArray());
        }

        return (move, basePick.Description);
    }

    private (JObject, string) PickDraftWithTarget(DraftOptionsDto draft)
    {
        // Always draft the target card if it's in the draft hand
        if (draft.DraftHand.Contains(_targetCardId))
        {
            var move = MakeMove("DraftCard");
            move["cardId"] = _targetCardId;
            return (move, $"drafts target card {CardName(_targetCardId)}");
        }

        return base.PickMove(new AvailableMovesDto { Draft = draft },
            new PlayerStateDto()); // botState not used for draft
    }

    private (JObject, string) PickBuyCardsWithTarget(BuyCardsOptionsDto buyCards, PlayerStateDto botState)
    {
        // Always buy the target card if available
        if (buyCards.AvailableCards.Contains(_targetCardId))
        {
            var cardIds = new List<string> { _targetCardId };
            var move = MakeMove("BuyCards");
            move["cardIds"] = new JArray(cardIds.ToArray());
            return (move, $"buys target card {CardName(_targetCardId)}");
        }

        return base.PickMove(new AvailableMovesDto { BuyCards = buyCards }, botState);
    }

    private (JObject, string) PickActionWithTarget(ActionPhaseOptionsDto actions, PlayerStateDto botState)
    {
        // Prioritize playing the target card if it's playable
        var targetPlayable = actions.PlayableCards.FirstOrDefault(c => c.CardId == _targetCardId);
        if (targetPlayable != null)
        {
            var payment = PaymentCalculator.Calculate(
                targetPlayable.EffectiveCost, targetPlayable.CanUseSteel, targetPlayable.CanUseTitanium,
                targetPlayable.CanUseHeat,
                botState.Resources.MegaCredits, botState.Resources.Steel,
                botState.Resources.Titanium, botState.Resources.Heat,
                targetPlayable.SteelValue, targetPlayable.TitaniumValue);

            var move = MakeMove("PlayCard");
            move["cardId"] = _targetCardId;
            move["payment"] = new JObject
            {
                ["megaCredits"] = payment.MegaCredits,
                ["steel"] = payment.Steel,
                ["titanium"] = payment.Titanium,
                ["heat"] = payment.Heat,
            };
            _targetCardPlayed = true;
            return (move, $"*** PLAYS TARGET CARD: {CardName(_targetCardId)} (cost {targetPlayable.EffectiveCost}) ***");
        }

        // Otherwise, use base random behavior but with SellPatents filtered
        // Remove target card from the bot's hand view so base won't sell it
        var safeState = ExcludeTargetFromHand(botState);
        return base.PickMove(new AvailableMovesDto { Actions = actions }, safeState);
    }

    private (JObject, string) PickPendingActionWithTarget(JObject pending, PlayerStateDto botState)
    {
        var type = pending["type"]?.Value<string>() ?? "";

        // For discard, never discard the target card
        if (type == "DiscardCards")
        {
            var safeState = ExcludeTargetFromHand(botState);
            return base.PickMove(new AvailableMovesDto { PendingAction = pending }, safeState);
        }

        return base.PickMove(new AvailableMovesDto { PendingAction = pending }, botState);
    }

    private PlayerStateDto ExcludeTargetFromHand(PlayerStateDto botState)
    {
        return new PlayerStateDto
        {
            PlayerId = botState.PlayerId,
            CorporationId = botState.CorporationId,
            TerraformRating = botState.TerraformRating,
            Resources = botState.Resources,
            Production = botState.Production,
            Hand = botState.Hand.Where(id => id != _targetCardId).ToList(),
            PlayedCards = botState.PlayedCards,
            PlayedEvents = botState.PlayedEvents,
            CardResources = botState.CardResources,
            Passed = botState.Passed,
            ActionsThisTurn = botState.ActionsThisTurn,
        };
    }

    private JObject MakeMove(string type) =>
        new() { ["type"] = type, ["playerId"] = PlayerId };
}
