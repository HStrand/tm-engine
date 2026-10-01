using System.Text.RegularExpressions;

namespace TmEngine.BgaImport;

internal enum TurnEventKind
{
    PlayCard,
    UseCardAction,
    StandardProject,
    ClaimMilestone,
    FundAward,
    ConvertHeat,
    ConvertPlants,
    SellPatents,

    /// <summary>A corporation's first action, which generation 1 opens with for its player.</summary>
    FirstAction,

    Pass,

    /// <summary>The player ended their turn after one action.</summary>
    EndTurn,
}

/// <summary>The start of one of a player's actions, or the end of their turn, as found in the log.</summary>
/// <param name="Subject">The card, project, milestone or award the action names; null for the rest.</param>
internal sealed record TurnEvent(int MoveIndex, string PlayerId, TurnEventKind Kind, string? Subject = null)
{
    public bool IsAction => Kind is not (TurnEventKind.Pass or TurnEventKind.EndTurn);

    /// <summary>
    /// Whether the event is the first thing its move logs, apart from paying for it. When it is not, the move
    /// began as the continuation of an earlier action and the position before it is not a settled one.
    /// </summary>
    public bool OpensMove { get; init; }
}

/// <summary>
/// Reads the turn structure of the action phase out of a log. The log has no record of whose turn it is or how
/// many actions they have taken; it has to be reconstructed from the lines that begin each action. The scraper's
/// own move types are too coarse for this (a temperature raise from any source is filed as a heat conversion),
/// so the log text is matched instead.
/// </summary>
internal static partial class TurnTracker
{
    [GeneratedRegex(@"^(?<player>.+) plays card (?<subject>.+)$")]
    private static partial Regex PlaysCard();

    [GeneratedRegex(@"^(?<player>.+) activates (?<subject>.+)$")]
    private static partial Regex Activates();

    [GeneratedRegex(@"^(?<player>.+) plays standard project (?<subject>.+)$")]
    private static partial Regex StandardProject();

    [GeneratedRegex(@"^(?<player>.+) claims milestone (?<subject>.+)$")]
    private static partial Regex ClaimsMilestone();

    [GeneratedRegex(@"^(?<player>.+) funds (?<subject>.+) award$")]
    private static partial Regex FundsAward();

    // Plants and heat lost to another player's card carry a "(... effect of X)" suffix, so they do not match.
    [GeneratedRegex(@"^(?<player>.+) pays 8 Heat$")]
    private static partial Regex PaysHeat();

    // Ecoline converts seven plants.
    [GeneratedRegex(@"^(?<player>.+) pays [78] Plant$")]
    private static partial Regex PaysPlants();

    [GeneratedRegex(@"^(?<player>.+) discards \d+ card/s$")]
    private static partial Regex SellsPatents();

    [GeneratedRegex(@"^(?<player>.+) passes$")]
    private static partial Regex Passes();

    [GeneratedRegex(@"^(?<player>.+) skips second action$")]
    private static partial Regex SkipsSecondAction();

    /// <summary>What a player spends on the action that the same move goes on to name.</summary>
    [GeneratedRegex(@" pays \d+ ")]
    private static partial Regex Payment();

    private static readonly (Func<Regex> Pattern, TurnEventKind Kind)[] Patterns =
    [
        (PlaysCard, TurnEventKind.PlayCard),
        (Activates, TurnEventKind.UseCardAction),
        (StandardProject, TurnEventKind.StandardProject),
        (ClaimsMilestone, TurnEventKind.ClaimMilestone),
        (FundsAward, TurnEventKind.FundAward),
        (PaysHeat, TurnEventKind.ConvertHeat),
        (PaysPlants, TurnEventKind.ConvertPlants),
        (SellsPatents, TurnEventKind.SellPatents),
        (Passes, TurnEventKind.Pass),
        (SkipsSecondAction, TurnEventKind.EndTurn),
    ];

    /// <summary>The turn events in the moves from <paramref name="first"/> to <paramref name="last"/>, in order.</summary>
    public static List<TurnEvent> Events(BgaImportContext context, int first, int last)
    {
        var events = LoggedEvents(context, first, last);

        // Corporation first actions have no line of their own in the log; the opening tracker finds them.
        foreach (var firstAction in context.Opening?.FirstActions ?? [])
        {
            if (firstAction.MoveIndex < first || firstAction.MoveIndex > last)
                continue;

            // A prelude played as a first action (Valley Trust) is logged as a card played.
            var logged = events.FindIndex(e => e.MoveIndex == firstAction.MoveIndex && e.PlayerId == firstAction.PlayerId && e.Kind == TurnEventKind.PlayCard);
            if (logged >= 0)
            {
                events[logged] = events[logged] with { Kind = TurnEventKind.FirstAction };
                continue;
            }

            var next = events.FindIndex(e => e.MoveIndex >= firstAction.MoveIndex);
            events.Insert(next < 0 ? events.Count : next, firstAction);
        }

        return events;
    }

    /// <summary>The turn events the log itself spells out. Leaves out corporation first actions; see <see cref="Events"/>.</summary>
    public static List<TurnEvent> LoggedEvents(BgaImportContext context, int first, int last)
    {
        var events = new List<TurnEvent>();
        for (int i = first; i <= last; i++)
        {
            var move = context.Log.Moves[i];
            if (move.ActionType == "cancelled")
                continue;

            var lines = BgaMoveText.Lines(move).ToList();

            // Heat paid towards a card (Helion) sits in the same move as the card play; that is not a conversion.
            var paysForSomething = lines.Any(l => PlaysCard().IsMatch(l) || Activates().IsMatch(l) || StandardProject().IsMatch(l));

            // Lines that belong to an action already under way (its effects, or the tail of a sale) mean the move
            // does not open with whatever event follows them.
            var midAction = false;
            var eventsBefore = events.Count;

            foreach (var line in lines)
            {
                var matched = false;
                foreach (var (pattern, kind) in Patterns)
                {
                    var match = pattern().Match(line);
                    if (!match.Success || (kind == TurnEventKind.ConvertHeat && paysForSomething))
                        continue;

                    var player = context.ResolvePlayer(match.Groups["player"].Value) ?? move.PlayerId;
                    var subject = match.Groups["subject"] is { Success: true } s ? s.Value : null;

                    // Newer logs announce a sale as a standard project and then list the discards; that is one action.
                    var alreadyAnnounced = kind == TurnEventKind.SellPatents
                        && events.LastOrDefault() is { Kind: TurnEventKind.SellPatents, Subject: not null } announcement
                        && announcement.PlayerId == player;
                    if (alreadyAnnounced)
                        break;

                    events.Add(new TurnEvent(i, player, IsSellPatents(subject) ? TurnEventKind.SellPatents : kind, subject)
                    {
                        OpensMove = !midAction && events.Count == eventsBefore,
                    });
                    matched = true;
                    break;
                }

                if (!matched && !Payment().IsMatch(line))
                    midAction = true;
            }

            // Some scraper versions drop the "activates X" line when the action only draws cards. The move is
            // still filed as an activation, so the action is counted even though its card is unknown.
            if (move.ActionType == "activate_card" && events.Count == eventsBefore)
                events.Add(new TurnEvent(i, move.PlayerId, TurnEventKind.UseCardAction) { OpensMove = true });
        }
        return events;
    }

    private static bool IsSellPatents(string? standardProject) =>
        string.Equals(standardProject, "Sell patents", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// How many actions a player has already taken in their current turn, given the events so far this generation.
    /// A turn is up to two actions; once everyone else has passed a player takes turn after turn, so their run of
    /// actions is counted in pairs.
    /// </summary>
    public static int ActionsThisTurn(IReadOnlyList<TurnEvent> eventsSoFar, string playerId)
    {
        var run = 0;
        for (int i = eventsSoFar.Count - 1; i >= 0; i--)
        {
            var e = eventsSoFar[i];
            if (e.PlayerId != playerId)
            {
                // Another player's pass does not interrupt the turn: it is logged when their turn comes round
                // and they have already passed, or in the same move that ends ours.
                if (e.Kind == TurnEventKind.Pass)
                    continue;
                break;
            }
            if (!e.IsAction)
                break;
            run++;
        }
        return run % 2;
    }
}
