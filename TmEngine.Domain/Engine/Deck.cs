using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Drawing from the project deck. When the draw pile runs out, the discard pile is shuffled into a new one.
/// The shuffle is seeded from the game state so that the same state and move always give the same result.
/// </summary>
public static class Deck
{
    /// <summary>
    /// Take up to <paramref name="count"/> cards off the top of the deck. Returns fewer only when the draw and
    /// discard piles are both exhausted.
    /// </summary>
    public static (GameState State, ImmutableList<string> Cards) Draw(GameState state, int count)
    {
        var cards = ImmutableList.CreateBuilder<string>();
        for (int i = 0; i < count; i++)
        {
            if (state.DrawPile.IsEmpty)
                state = Reshuffle(state);
            if (state.DrawPile.IsEmpty)
                break;

            cards.Add(state.DrawPile[0]);
            state = state with { DrawPile = state.DrawPile.RemoveAt(0) };
        }
        return (state, cards.ToImmutable());
    }

    /// <summary>Draw cards straight into a player's hand.</summary>
    public static GameState DrawToHand(GameState state, int playerId, int count)
    {
        var (drawn, cards) = Draw(state, count);
        return drawn.UpdatePlayer(playerId, p => p with { Hand = p.Hand.AddRange(cards) });
    }

    private static GameState Reshuffle(GameState state)
    {
        if (state.DiscardPile.IsEmpty)
            return state;

        var rng = new Random(unchecked(state.ShuffleSeed * 31 + state.ReshuffleCount + 1));
        return state with
        {
            DrawPile = DeckBuilder.Shuffle([.. state.DiscardPile], rng),
            DiscardPile = [],
            ReshuffleCount = state.ReshuffleCount + 1,
        };
    }
}
