using System.Collections.Immutable;
using System.Linq;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

/// <summary>
/// Filters a GameState to hide private information based on the requesting player.
/// </summary>
public static class GameStateView
{
    /// <summary>
    /// Returns a copy of the game state with private information hidden.
    /// If playerId is null (spectator), all hands are hidden.
    /// </summary>
    public static GameState FilterForPlayer(GameState state, int? playerId)
    {
        // Always hide draw pile, discard pile, and prelude deck
        state = state with
        {
            DrawPile = [],
            DiscardPile = [],
            PreludeDeck = [],
        };

        // Hide other players' hands
        var filteredPlayers = state.Players.Select((p, index) =>
            p.PlayerId == playerId
                ? p
                : p with { Hand = [] }
        ).ToImmutableList();
        state = state with { Players = filteredPlayers };

        // Filter setup state: only show this player's dealt cards
        if (state.Setup != null)
        {
            var playerIndex = playerId.HasValue
                ? state.Players.FindIndex(p => p.PlayerId == playerId.Value)
                : -1;

            state = state with
            {
                Setup = state.Setup with
                {
                    DealtCorporations = FilterPerPlayerList(state.Setup.DealtCorporations, playerIndex),
                    DealtPreludes = FilterPerPlayerList(state.Setup.DealtPreludes, playerIndex),
                    DealtCards = FilterPerPlayerList(state.Setup.DealtCards, playerIndex),
                }
            };
        }

        // Filter research state: only show this player's available cards
        if (state.Research != null)
        {
            var playerIndex = playerId.HasValue
                ? state.Players.FindIndex(p => p.PlayerId == playerId.Value)
                : -1;

            state = state with
            {
                Research = state.Research with
                {
                    AvailableCards = FilterPerPlayerList(state.Research.AvailableCards, playerIndex),
                }
            };
        }

        // Filter draft state: only show this player's draft hand
        if (state.Draft != null)
        {
            var playerIndex = playerId.HasValue
                ? state.Players.FindIndex(p => p.PlayerId == playerId.Value)
                : -1;

            state = state with
            {
                Draft = state.Draft with
                {
                    DraftHands = FilterPerPlayerList(state.Draft.DraftHands, playerIndex),
                }
            };
        }

        return state;
    }

    /// <summary>
    /// Replaces all entries except the given player index with empty lists.
    /// </summary>
    private static ImmutableList<ImmutableList<string>> FilterPerPlayerList(
        ImmutableList<ImmutableList<string>> lists, int visibleIndex)
    {
        return lists.Select((list, i) =>
            i == visibleIndex ? list : ImmutableList<string>.Empty
        ).ToImmutableList();
    }
}
