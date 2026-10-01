using System.Collections.Immutable;
using TmEngine.Domain.Engine;

namespace TmEngine.BgaImport;

/// <summary>
/// Rebuilds the draw and discard piles at a fork. The log has no deck order, but it names every card the real
/// game went on to draw, so those go on top in the order they came off; the cards nobody ever saw are shuffled
/// underneath with a seed. Replaying the real game's moves from the fork therefore draws the real cards.
/// </summary>
internal static class DeckReconstructor
{
    public sealed record Piles(ImmutableList<string> Draw, ImmutableList<string> Discard);

    public static Piles Reconstruct(BgaImportContext context, BgaFold fold, int forkIndex, int shuffleSeed, List<ImportIssue> issues)
    {
        var placed = fold.Hands.Values.Concat(fold.Played.Values).Concat(fold.Events.Values)
            .SelectMany(cards => cards)
            .Concat(fold.Discard)
            .ToHashSet();

        var upcoming = UpcomingDraws(context, forkIndex, placed);

        var unseen = context.ProjectDeck.Except(placed).Except(upcoming).Order().ToImmutableArray();
        var shuffled = DeckBuilder.Shuffle(unseen, new Random(shuffleSeed));

        var discard = fold.Discard.ToList();
        var snapshot = context.SnapshotAt(forkIndex);

        // Cards the real game discarded without the log naming them can only be among the unseen ones.
        if (snapshot?.DiscardPile is { } discardCount && discardCount != discard.Count)
        {
            var missing = discardCount - discard.Count;
            if (missing > 0 && missing <= shuffled.Count)
            {
                discard.AddRange(shuffled.GetRange(shuffled.Count - missing, missing));
                shuffled = shuffled.RemoveRange(shuffled.Count - missing, missing);
                issues.Add(ImportIssue.Warning("DISCARD_UNNAMED",
                    $"The log does not name {missing} of the discarded cards; random unseen cards stand in for them."));
            }
            else
            {
                issues.Add(ImportIssue.Blocking("DISCARD_COUNT",
                    $"The discard pile should hold {discardCount} cards but {discard.Count} discards were found in the log."));
            }
        }

        var draw = upcoming.Concat(shuffled).ToImmutableList();
        if (snapshot?.DrawPile is { } drawCount && drawCount != draw.Count)
        {
            issues.Add(ImportIssue.Blocking("DRAW_PILE_COUNT",
                $"The draw pile should hold {drawCount} cards but the cards left unaccounted for number {draw.Count}."));
        }

        return new Piles(draw, [.. discard]);
    }

    /// <summary>
    /// The draw pile at the start of the game, once every player has been dealt their opening cards.
    /// </summary>
    public static ImmutableList<string> ReconstructAtGameStart(BgaImportContext context, HashSet<string> dealt, int shuffleSeed)
    {
        var upcoming = UpcomingDraws(context, forkIndex: -1, dealt);
        var unseen = context.ProjectDeck.Except(dealt).Except(upcoming).Order().ToImmutableArray();
        return [.. upcoming, .. DeckBuilder.Shuffle(unseen, new Random(shuffleSeed))];
    }

    /// <summary>
    /// Cards the real game drew after the fork, in the order the log first names them. Stops at a reshuffle:
    /// cards drawn after one were in the discard pile at the fork.
    /// </summary>
    private static List<string> UpcomingDraws(BgaImportContext context, int forkIndex, HashSet<string> placed)
    {
        var moves = context.Log.Moves;
        var upcoming = new List<string>();
        var seen = new HashSet<string>();

        var drawPile = forkIndex < 0 ? null : context.SnapshotAt(forkIndex)?.DrawPile;
        for (int i = forkIndex + 1; i < moves.Count; i++)
        {
            var move = moves[i];
            if (move.GameState?.DrawPile is { } count)
            {
                if (count > drawPile)
                    break;
                drawPile = count;
            }

            if (move.ActionType == "cancelled")
                continue;

            foreach (var card in NamesInDealOrder(context, move).Select(CardNameMap.Resolve))
            {
                if (context.ProjectDeck.Contains(card!) && !placed.Contains(card!) && seen.Add(card!))
                    upcoming.Add(card!);
            }
        }

        return upcoming;
    }

    /// <summary>
    /// Card names in a move with per-player lists taken in seat order, which is the order the engine deals
    /// research cards in.
    /// </summary>
    private static IEnumerable<string> NamesInDealOrder(BgaImportContext context, BgaMove move)
    {
        foreach (var reveal in BgaMoveText.Reveals(move))
            yield return reveal.CardName;

        foreach (var cards in new[] { move.CardOptions, move.CardsKept })
        {
            foreach (var seat in context.Seats)
            {
                if (cards is not null && cards.TryGetValue(seat, out var names))
                {
                    foreach (var name in names)
                        yield return name;
                }
            }
        }
    }
}
