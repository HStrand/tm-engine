using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Fires triggered effects (WhenYouEffect, WhenAnyoneEffect) on played cards and corporations.
/// </summary>
public static class TriggerSystem
{
    /// <summary>
    /// Fire a trigger for the given player. Checks all players' cards for WhenAnyoneEffect,
    /// and the triggering player's cards for WhenYouEffect.
    /// </summary>
    public static GameState FireTrigger(GameState state, int triggeringPlayerId, TriggerCondition condition)
    {
        // Check all players for WhenAnyoneEffect
        foreach (var player in state.Players)
        {
            state = FireTriggersForPlayer(state, player, condition, isAnyoneTrigger: true);
        }

        // Check triggering player for WhenYouEffect
        var triggeringPlayer = state.GetPlayer(triggeringPlayerId);
        state = FireTriggersForPlayer(state, triggeringPlayer, condition, isAnyoneTrigger: false);

        return state;
    }

    private static GameState FireTriggersForPlayer(
        GameState state, PlayerState player, TriggerCondition condition, bool isAnyoneTrigger)
    {
        // Check corporation
        if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corp))
        {
            state = CheckEffects(state, player.PlayerId, corp.OngoingEffects, condition, isAnyoneTrigger);
        }

        // Check played blue cards
        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var card))
            {
                state = CheckEffects(state, player.PlayerId, card.OngoingEffects, condition, isAnyoneTrigger);
            }
        }

        return state;
    }

    private static GameState CheckEffects(
        GameState state, int playerId,
        System.Collections.Immutable.ImmutableArray<Effect> effects,
        TriggerCondition condition, bool isAnyoneTrigger)
    {
        foreach (var effect in effects)
        {
            if (isAnyoneTrigger && effect is WhenAnyoneEffect anyone && anyone.Trigger == condition)
            {
                var (newState, _) = EffectExecutor.Execute(state, playerId, anyone.Effect);
                state = newState;
            }
            else if (!isAnyoneTrigger && effect is WhenYouEffect whenYou && whenYou.Trigger == condition)
            {
                var (newState, _) = EffectExecutor.Execute(state, playerId, whenYou.Effect);
                state = newState;
            }
        }

        return state;
    }
}
