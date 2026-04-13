using System.Collections.Immutable;
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
    public static GameState FireTrigger(
        GameState state, int triggeringPlayerId, TriggerCondition condition,
        string? triggeringCardId = null)
    {
        // Check all players for WhenAnyoneEffect (these are always immediate — no ordering needed)
        foreach (var player in state.Players)
        {
            state = FireAnyoneTriggersForPlayer(state, player, condition, triggeringCardId);
        }

        // Collect all WhenYouEffect triggers for the triggering player
        var triggeringPlayer = state.GetPlayer(triggeringPlayerId);
        var triggers = CollectWhenYouTriggers(triggeringPlayer, condition, triggeringCardId);

        // Execute collected triggers with ordering support
        state = ExecuteWhenYouTriggers(state, triggeringPlayerId, triggers);

        return state;
    }

    /// <summary>
    /// Resume execution of queued triggers after a pending action resolves.
    /// </summary>
    public static GameState ResumeTriggerQueue(GameState state, int playerId)
    {
        var queue = state.TriggerQueue!.Value;
        state = state with { TriggerQueue = null };

        if (queue.Length == 0)
            return state;

        // Execute remaining triggers (same ordering logic)
        state = ExecuteWhenYouTriggers(state, playerId, queue);
        return state;
    }

    private static GameState FireAnyoneTriggersForPlayer(
        GameState state, PlayerState player, TriggerCondition condition, string? triggeringCardId)
    {
        void CheckCard(string cardId)
        {
            if (!CardRegistry.TryGet(cardId, out var entry)) return;
            foreach (var effect in entry.OngoingEffects)
            {
                if (effect is WhenAnyoneEffect anyone && anyone.Trigger == condition)
                {
                    var (newState, _) = EffectExecutor.Execute(state, player.PlayerId, anyone.Effect,
                        triggeringCardId: triggeringCardId);
                    state = newState;
                }
            }
        }

        if (!string.IsNullOrEmpty(player.CorporationId))
            CheckCard(player.CorporationId);

        foreach (var cardId in player.PlayedCards)
            CheckCard(cardId);

        return state;
    }

    /// <summary>
    /// Collect all matching WhenYouEffect triggers as queue entries for the given player.
    /// </summary>
    private static ImmutableArray<TriggerQueueEntry> CollectWhenYouTriggers(
        PlayerState player, TriggerCondition condition, string? triggeringCardId)
    {
        var result = ImmutableArray.CreateBuilder<TriggerQueueEntry>();

        void CheckCard(string cardId)
        {
            if (!CardRegistry.TryGet(cardId, out var entry)) return;
            foreach (var effect in entry.OngoingEffects)
            {
                if (effect is WhenYouEffect whenYou && whenYou.Trigger == condition)
                    result.Add(new TriggerQueueEntry(cardId, condition, triggeringCardId));
            }
        }

        if (!string.IsNullOrEmpty(player.CorporationId))
            CheckCard(player.CorporationId);

        foreach (var cardId in player.PlayedCards)
            CheckCard(cardId);

        return result.ToImmutable();
    }

    /// <summary>
    /// Execute collected WhenYou triggers, handling ordering when multiple interactive triggers exist.
    /// </summary>
    private static GameState ExecuteWhenYouTriggers(
        GameState state, int playerId, ImmutableArray<TriggerQueueEntry> triggers)
    {
        // Partition into immediate (non-interactive) and interactive triggers
        var immediate = ImmutableArray.CreateBuilder<TriggerQueueEntry>();
        var interactive = ImmutableArray.CreateBuilder<TriggerQueueEntry>();

        foreach (var trigger in triggers)
        {
            var innerEffect = GetInnerEffect(trigger);
            if (innerEffect != null && IsInteractiveTrigger(innerEffect))
                interactive.Add(trigger);
            else
                immediate.Add(trigger);
        }

        // Execute all immediate triggers
        foreach (var trigger in immediate.ToImmutable())
        {
            var innerEffect = GetInnerEffect(trigger);
            if (innerEffect == null) continue;
            var (newState, _) = EffectExecutor.Execute(state, playerId, innerEffect,
                triggeringCardId: trigger.TriggeringCardId);
            state = newState;
        }

        // Handle interactive triggers
        if (interactive.Count == 0)
            return state;

        if (interactive.Count == 1)
        {
            var trigger = interactive[0];
            var innerEffect = GetInnerEffect(trigger)!;
            var (newState, pending) = EffectExecutor.Execute(state, playerId, innerEffect,
                triggeringCardId: trigger.TriggeringCardId);
            state = newState;
            if (pending != null && state.PendingAction == null)
                state = state with { PendingAction = pending };
            return state;
        }

        // Multiple interactive triggers — let the player choose the order
        var descriptions = interactive.ToImmutable().Select(t => DescribeTriggerEntry(t)).ToImmutableArray();
        state = state with { PendingAction = new ChooseTriggerOrderPending(interactive.ToImmutable(), descriptions) };
        return state;
    }

    private static Effect? GetInnerEffect(TriggerQueueEntry entry)
    {
        if (entry.Trigger == null)
            return null;
        if (!CardRegistry.TryGet(entry.OwnerCardId, out var cardEntry))
            return null;
        foreach (var effect in cardEntry.OngoingEffects)
        {
            if (effect is WhenYouEffect whenYou && whenYou.Trigger == entry.Trigger)
                return whenYou.Effect;
        }
        return null;
    }

    /// <summary>
    /// Returns true if a triggered inner effect is likely to produce a pending action
    /// requiring player interaction.
    /// </summary>
    private static bool IsInteractiveTrigger(Effect innerEffect) => innerEffect is
        OlympusConferenceEffect or
        MarsUniversityEffect or
        ViralEnhancersEffect or
        ChooseEffect or
        AddCardResourceEffect { TargetCardId: null } or
        DiscardCardsEffect;

    /// <summary>
    /// Fire all tag-based triggers for a card (both WhenAnyone and WhenYou), including compound triggers.
    /// Collects all WhenYou triggers across all tags first, then executes them as a batch
    /// so duplicate tags (e.g., Research with 2 Science tags) correctly fire triggers multiple times.
    /// </summary>
    public static GameState FireCardTagTriggers(
        GameState state, int playerId, string cardId, CardDefinition card)
    {
        var conditions = new List<TriggerCondition>();
        foreach (var tag in card.Tags)
        {
            var condition = EffectToCondition(tag);
            if (condition != null)
                conditions.Add(condition.Value);
        }

        // Compound tag triggers
        if (card.Tags.Contains(Tag.Space) && card.Tags.Contains(Tag.Event))
            conditions.Add(TriggerCondition.PlaySpaceEventTag);

        // Fire all WhenAnyone triggers (these are always immediate)
        foreach (var condition in conditions)
        {
            foreach (var player in state.Players)
                state = FireAnyoneTriggersForPlayer(state, player, condition, cardId);
        }

        // Collect all WhenYou triggers across all tags, then batch-execute
        var triggeringPlayer = state.GetPlayer(playerId);
        var allWhenYou = ImmutableArray.CreateBuilder<TriggerQueueEntry>();
        foreach (var condition in conditions)
        {
            var triggers = CollectWhenYouTriggers(triggeringPlayer, condition, cardId);
            allWhenYou.AddRange(triggers);
        }

        if (allWhenYou.Count > 0)
            state = ExecuteWhenYouTriggers(state, playerId, allWhenYou.ToImmutable());

        return state;
    }

    public static string DescribeTriggerEntry(TriggerQueueEntry entry)
    {
        if (!CardRegistry.TryGet(entry.OwnerCardId, out var cardEntry))
            return entry.OwnerCardId;
        return cardEntry.Definition.Name;
    }

    /// <summary>
    /// Collect interactive WhenYou trigger entries for a card's tags (without executing them).
    /// Used to detect whether ordering is needed between on-play effects and triggers.
    /// </summary>
    public static ImmutableArray<TriggerQueueEntry> CollectInteractiveWhenYouTriggers(
        GameState state, PlayerState player, CardDefinition card, string cardId)
    {
        var result = ImmutableArray.CreateBuilder<TriggerQueueEntry>();

        foreach (var tag in card.Tags)
        {
            var condition = EffectToCondition(tag);
            if (condition == null) continue;

            var triggers = CollectWhenYouTriggers(player, condition.Value, cardId);
            foreach (var trigger in triggers)
            {
                var inner = GetInnerEffect(trigger);
                if (inner != null && IsInteractiveTrigger(inner))
                    result.Add(trigger);
            }
        }

        return result.ToImmutable();
    }

    /// <summary>
    /// Fire only the non-interactive WhenYou triggers for a card's tags.
    /// Called before presenting an ordering choice so immediate effects are resolved.
    /// </summary>
    public static GameState FireNonInteractiveWhenYouTriggers(
        GameState state, int playerId, PlayerState player, CardDefinition card, string cardId)
    {
        // WhenAnyone triggers always fire immediately
        foreach (var p in state.Players)
        {
            state = FireAnyoneTriggersForPlayer(state, p,
                card.Tags.Select(t => EffectToCondition(t)).Where(c => c != null).Select(c => c!.Value),
                cardId);
        }

        // Fire non-interactive WhenYou triggers
        foreach (var tag in card.Tags)
        {
            var condition = EffectToCondition(tag);
            if (condition == null) continue;

            var triggers = CollectWhenYouTriggers(player, condition.Value, cardId);
            foreach (var trigger in triggers)
            {
                var inner = GetInnerEffect(trigger);
                if (inner != null && !IsInteractiveTrigger(inner))
                {
                    var (newState, _) = EffectExecutor.Execute(state, playerId, inner,
                        triggeringCardId: trigger.TriggeringCardId);
                    state = newState;
                }
            }
        }

        return state;
    }

    private static GameState FireAnyoneTriggersForPlayer(
        GameState state, PlayerState player,
        IEnumerable<TriggerCondition> conditions, string? triggeringCardId)
    {
        foreach (var condition in conditions)
            state = FireAnyoneTriggersForPlayer(state, player, condition, triggeringCardId);
        return state;
    }

    private static TriggerCondition? EffectToCondition(Tag tag) => tag switch
    {
        Tag.Building => TriggerCondition.PlayBuildingTag,
        Tag.Space => TriggerCondition.PlaySpaceTag,
        Tag.Science => TriggerCondition.PlayScienceTag,
        Tag.Power => TriggerCondition.PlayPowerTag,
        Tag.Jovian => TriggerCondition.PlayJovianTag,
        Tag.Earth => TriggerCondition.PlayEarthTag,
        Tag.Plant => TriggerCondition.PlayPlantTag,
        Tag.Microbe => TriggerCondition.PlayMicrobeTag,
        Tag.Animal => TriggerCondition.PlayAnimalTag,
        Tag.City => TriggerCondition.PlayCityTag,
        Tag.Event => TriggerCondition.PlayEventTag,
        _ => null,
    };
}
