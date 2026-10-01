using System.Text.RegularExpressions;

namespace TmEngine.BgaImport;

/// <summary>
/// Finds the moves in a log after which the position can be rebuilt as an engine state.
/// Only positions where nothing is half-resolved qualify, because the log does not record pending choices:
/// the start of the game, the start of a generation's action phase, and every point in it where a player is
/// about to begin an action.
/// </summary>
internal static partial class ForkPointFinder
{
    /// <summary>Log lines that occur in the research phase: drafting, buying, buying nothing, and taking either back.</summary>
    [GeneratedRegex(@" drafts? | keeps | skips rest of actions$| takes back ")]
    private static partial Regex ResearchLine();

    /// <summary>
    /// Whether a move belongs to the research phase that opens a generation. The scraper files some research
    /// moves under the catch-all "other" type, so those are recognised by their text.
    /// </summary>
    private static bool IsResearchMove(BgaMove move) => move.ActionType switch
    {
        "draft" or "game_state_change" or "cancelled" or "takeback" => true,
        "other" => BgaMoveText.Lines(move).ToList() is { Count: > 0 } lines && lines.All(line => ResearchLine().IsMatch(line)),
        _ => false,
    };

    /// <summary>
    /// Candidate fork points. A candidate is a position that looks settled; whether it actually imports is
    /// decided by building the state and validating it.
    /// </summary>
    public static List<ForkPoint> Find(BgaImportContext context)
    {
        var moves = context.Log.Moves;
        var forks = new List<ForkPoint>();

        if (GameStart(context) is { } gameStart)
            forks.Add(gameStart);

        foreach (var (generation, first, last) in Generations(context))
        {
            // Generation 1 opens with setup, preludes and corporation first actions. The log does not say how far
            // through those the players are, so the generation can only be picked up from the deal.
            if (generation < 2
                || ResearchEnd(context, first) is not { } researchEnd
                || context.ResolvePlayer(context.SnapshotAt(researchEnd)?.StartingPlayer) is not { } firstPlayer)
            {
                continue;
            }

            forks.Add(new ForkPoint(researchEnd, moves[researchEnd].MoveNumber, ForkKind.GenerationStart, generation, firstPlayer));
            forks.AddRange(ActionStarts(context, generation, researchEnd, last));
        }

        return forks;
    }

    /// <summary>
    /// The position after the log's first move, which announces the game: everyone has been dealt their
    /// corporations, preludes and project cards (the scraper records them per player) and has chosen nothing.
    /// </summary>
    private static ForkPoint? GameStart(BgaImportContext context)
    {
        var moves = context.Log.Moves;
        if (moves.Count < 2
            || moves[0].CardsKept is { Count: > 0 }
            || context.Log.Players.Values.Any(p => p.StartingHand is null))
        {
            return null;
        }

        var firstPlayer = context.ResolvePlayer(context.SnapshotAt(0)?.StartingPlayer) ?? context.Seats[0];
        return new ForkPoint(0, moves[0].MoveNumber, ForkKind.GameStart, Generation: 1, firstPlayer);
    }

    private static IEnumerable<(int Generation, int First, int Last)> Generations(BgaImportContext context)
    {
        var count = context.Log.Moves.Count;
        int? generation = null;
        var first = 0;
        for (int i = 0; i < count; i++)
        {
            if (context.SnapshotAt(i)?.Generation is not { } current || current == generation)
                continue;

            if (generation is { } previous)
                yield return (previous, first, i - 1);
            generation = current;
            first = i;
        }

        if (generation is { } final)
            yield return (final, first, count - 1);
    }

    /// <summary>
    /// The last move of the research phase that begins at <paramref name="generationStart"/>, or null when the
    /// generation does not open with one (the game ended, or the log is in a shape this does not recognise).
    /// </summary>
    private static int? ResearchEnd(BgaImportContext context, int generationStart)
    {
        var moves = context.Log.Moves;

        int? end = null;
        for (int i = generationStart; i < moves.Count && IsResearchMove(moves[i]); i++)
        {
            // A trailing game_state_change belongs to the first action, so the phase does not end on one.
            if (moves[i].ActionType != "game_state_change")
                end = i;
        }

        // The game must go on: a position after the final move is not worth forking.
        return end is { } last && last + 1 < moves.Count ? last : null;
    }

    /// <summary>
    /// The positions inside a generation's action phase where a player is about to begin an action: the move
    /// before each move that starts one.
    /// </summary>
    private static IEnumerable<ForkPoint> ActionStarts(BgaImportContext context, int generation, int researchEnd, int last)
    {
        var moves = context.Log.Moves;
        var events = TurnTracker.Events(context, researchEnd + 1, last);
        var undone = TakebackWindows(context, events, researchEnd, last);

        // In the final generation the game moves on to production and final greenery placement once everyone has
        // passed, and the last pass is not logged. Positions after the first pass cannot be told apart from that.
        var isFinalGeneration = generation == context.SnapshotAt(moves.Count - 1)?.Generation;
        var cutoff = isFinalGeneration
            ? events.FirstOrDefault(e => e.Kind == TurnEventKind.Pass)?.MoveIndex ?? last
            : last;

        foreach (var starts in events.GroupBy(e => e.MoveIndex))
        {
            var index = starts.Key - 1;
            if (index <= researchEnd || index >= cutoff || !starts.First().OpensMove
                || undone.Any(w => index >= w.From && index < w.To))
            {
                continue;
            }

            yield return new ForkPoint(index, moves[index].MoveNumber, ForkKind.ActionStart, generation, starts.First().PlayerId);
        }
    }

    /// <summary>
    /// The stretches of moves that a takeback undid: from the start of that player's turn up to the takeback.
    /// The log keeps the undone moves, so positions inside such a stretch never really stood.
    /// </summary>
    private static List<(int From, int To)> TakebackWindows(BgaImportContext context, List<TurnEvent> events, int researchEnd, int last)
    {
        var moves = context.Log.Moves;
        var windows = new List<(int, int)>();
        for (int i = researchEnd + 1; i <= last; i++)
        {
            if (!BgaFold.IsTakeback(moves[i]))
                continue;

            var player = moves[i].PlayerId;
            var previousTurn = events.LastOrDefault(e => e.MoveIndex < i && e.PlayerId != player && e.IsAction)?.MoveIndex ?? researchEnd;
            var turnStart = events.FirstOrDefault(e => e.MoveIndex > previousTurn && e.MoveIndex < i && e.PlayerId == player)?.MoveIndex ?? i;
            windows.Add((turnStart, i));
        }
        return windows;
    }
}
