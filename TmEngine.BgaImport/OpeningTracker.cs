using System.Collections.Immutable;
using System.Text.RegularExpressions;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>
/// How generation 1 reaches its action phase in a log.
/// </summary>
/// <param name="LastMove">The last move of the opening: the last prelude played, or the last opening hand kept when there are no preludes.</param>
/// <param name="FirstActions">The corporation first actions taken in the action phase that follows.</param>
internal sealed record Opening(int LastMove, ImmutableList<TurnEvent> FirstActions);

/// <summary>
/// Generation 1 starts with the players choosing their opening hands and playing their preludes, and its action
/// phase with the first actions some corporations must take. The log marks none of this: preludes are logged
/// like any other card played, and a first action only by what it does.
/// </summary>
internal static partial class OpeningTracker
{
    [GeneratedRegex(@"^(.+?) places ")]
    private static partial Regex PlacesLine();

    /// <summary>
    /// Null when the opening is not in a shape this recognises, in which case generation 1 can only be picked up
    /// from the deal.
    /// </summary>
    public static Opening? Find(BgaImportContext context)
    {
        var log = context.Log;
        var moves = log.Moves;

        var lastOfGeneration = Enumerable.Range(0, moves.Count)
            .TakeWhile(i => context.SnapshotAt(i)?.Generation is null or 1)
            .DefaultIfEmpty(-1)
            .Last();
        var logged = TurnTracker.LoggedEvents(context, 0, lastOfGeneration);

        // The opening hands are kept in "draft" moves, before anything is played.
        var firstEvent = logged.FirstOrDefault()?.MoveIndex ?? lastOfGeneration + 1;
        var setupEnd = Enumerable.Range(0, firstEvent).LastOrDefault(i => moves[i].ActionType == "draft", -1);
        if (setupEnd < 0)
            return null;

        // Then every player plays the preludes they kept, before anyone takes an action.
        var preludes = log.PreludeOn == true ? Constants.PreludesKept * log.Players.Count : 0;
        var lastMove = setupEnd;
        foreach (var e in logged.Take(preludes))
        {
            if (e.Kind != TurnEventKind.PlayCard || !IsPrelude(e.Subject))
                return null;
            lastMove = e.MoveIndex;
        }
        if (logged.Count < preludes)
            return null;

        var actions = logged.Skip(preludes).ToList();
        var firstActions = context.Seats
            .Select(bgaId => FirstAction(context, bgaId, actions, lastMove, lastOfGeneration))
            .OfType<TurnEvent>()
            .OrderBy(e => e.MoveIndex)
            .ToImmutableList();

        return new Opening(lastMove, firstActions);
    }

    /// <summary>
    /// The cards a player kept from their starting hand (corporation, preludes and project cards in one list),
    /// or null when the log does not say.
    /// </summary>
    public static HashSet<string?>? Keeps(BgaImportContext context, string bgaId) =>
        context.Log.Moves
            .TakeWhile(m => m.CardPlayed is null)
            .Where(m => m.ActionType != "cancelled")
            .Select(m => m.CardsKept?.GetValueOrDefault(bgaId))
            .LastOrDefault(cards => cards is { Count: > 0 })
            ?.Select(CardNameMap.Resolve).ToHashSet();

    /// <summary>
    /// A corporation's first action is the first thing its player does in the action phase, so it is the first
    /// move after the opening that does what the first action does, provided the player has done nothing else yet.
    /// </summary>
    private static TurnEvent? FirstAction(BgaImportContext context, string bgaId, List<TurnEvent> actions, int openingEnd, int lastOfGeneration)
    {
        var corporation = CardNameMap.Resolve(context.Log.Players[bgaId].Corporation!)!;
        if (CardRegistry.Get(corporation).FirstActionEffects is not [var effect, ..])
            return null;

        var moves = context.Log.Moves;
        var firstLogged = actions.FirstOrDefault(e => e.PlayerId == bgaId);
        var before = firstLogged?.MoveIndex ?? lastOfGeneration + 1;
        var candidates = Enumerable.Range(openingEnd + 1, Math.Max(0, before - openingEnd - 1))
            .Where(i => moves[i].ActionType != "cancelled");

        int? index = effect switch
        {
            // Inventrix draws three cards.
            DrawCardsEffect draw => candidates
                .Cast<int?>()
                .FirstOrDefault(i => moves[i!.Value].CardsKept?.GetValueOrDefault(bgaId)?.Count == draw.Count),

            // Tharsis Republic places a city.
            PlaceTileEffect { TileType: TileType.City } => candidates
                .Cast<int?>()
                .FirstOrDefault(i => moves[i!.Value].TilePlaced == "City" && Placer(context, moves[i.Value]) == bgaId),

            // Valley Trust plays a prelude, which the log does record as a card played.
            DrawAndPlayOneEffect { CardTypeToFind: CardType.Prelude } when firstLogged is { Kind: TurnEventKind.PlayCard } && IsPrelude(firstLogged.Subject) =>
                firstLogged.MoveIndex,

            _ => null,
        };

        return index is { } moveIndex
            ? new TurnEvent(moveIndex, bgaId, TurnEventKind.FirstAction) { OpensMove = true }
            : null;
    }

    private static string Placer(BgaImportContext context, BgaMove move) =>
        BgaMoveText.Lines(move)
            .Select(line => PlacesLine().Match(line))
            .Where(m => m.Success)
            .Select(m => context.ResolvePlayer(m.Groups[1].Value))
            .FirstOrDefault() ?? move.PlayerId;

    private static bool IsPrelude(string? cardName) =>
        cardName is not null
        && CardNameMap.TryResolve(cardName, out var id)
        && CardRegistry.GetDefinition(id).Type == CardType.Prelude;
}
