using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

/// <summary>
/// Builds a card ID → name lookup for all card IDs present in a response.
/// </summary>
public static class CardNameResolver
{
    public static ImmutableDictionary<string, string> FromGameState(GameState state)
    {
        var ids = new HashSet<string>();

        // Players' cards
        foreach (var player in state.Players)
        {
            ids.UnionWith(player.Hand);
            ids.UnionWith(player.PlayedCards);
            ids.UnionWith(player.PlayedEvents);
            ids.UnionWith(player.CardResources.Keys);
            if (!string.IsNullOrEmpty(player.CorporationId))
                ids.Add(player.CorporationId);
        }

        // Setup state
        if (state.Setup != null)
        {
            foreach (var list in state.Setup.DealtCorporations)
                ids.UnionWith(list);
            foreach (var list in state.Setup.DealtPreludes)
                ids.UnionWith(list);
            foreach (var list in state.Setup.DealtCards)
                ids.UnionWith(list);
        }

        // Prelude state
        if (state.Prelude != null)
        {
            foreach (var list in state.Prelude.RemainingPreludes)
                ids.UnionWith(list);
        }

        // Research state
        if (state.Research != null)
        {
            foreach (var list in state.Research.AvailableCards)
                ids.UnionWith(list);
        }

        // Draft state
        if (state.Draft != null)
        {
            foreach (var list in state.Draft.DraftHands)
                ids.UnionWith(list);
            foreach (var list in state.Draft.DraftedCards)
                ids.UnionWith(list);
        }

        // Milestones and awards reference names, not card IDs — skip

        return Resolve(ids);
    }

    public static ImmutableDictionary<string, string> FromAvailableMoves(AvailableMoves moves)
    {
        var ids = new HashSet<string>();

        if (moves.Setup != null)
        {
            ids.UnionWith(moves.Setup.AvailableCorporations);
            ids.UnionWith(moves.Setup.AvailablePreludes);
            ids.UnionWith(moves.Setup.AvailableCards);
        }

        if (moves.Prelude != null)
            ids.UnionWith(moves.Prelude.RemainingPreludes);

        if (moves.Draft != null)
            ids.UnionWith(moves.Draft.DraftHand);

        if (moves.BuyCards != null)
            ids.UnionWith(moves.BuyCards.AvailableCards);

        if (moves.Actions != null)
        {
            foreach (var card in moves.Actions.PlayableCards)
                ids.Add(card.CardId);
            foreach (var action in moves.Actions.UsableCardActions)
                ids.Add(action.CardId);
        }

        if (moves.PendingAction != null)
        {
            switch (moves.PendingAction)
            {
                case AddCardResourcePending p:
                    ids.UnionWith(p.ValidCardIds);
                    break;
                case ChooseCardToPlayPending p:
                    ids.UnionWith(p.CardIds);
                    break;
                case BuyCardsPending p:
                    ids.UnionWith(p.AvailableCardIds);
                    break;
            }
        }

        return Resolve(ids);
    }

    private static ImmutableDictionary<string, string> Resolve(HashSet<string> ids)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, string>();

        foreach (var id in ids)
        {
            if (CardRegistry.TryGet(id, out var entry))
                builder[id] = entry.Definition.Name;
        }

        return builder.ToImmutable();
    }
}
