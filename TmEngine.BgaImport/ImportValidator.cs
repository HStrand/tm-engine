using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>
/// Cross-checks an imported state against the log snapshot it was built from. The card zones and board are
/// accumulated from moves while the snapshot's counters come straight from BGA, so where the two disagree the
/// accumulation has gone wrong (an unlogged takeback, a draw the scraper missed) and the position is rejected.
/// </summary>
internal static class ImportValidator
{
    public static List<ImportIssue> Validate(BgaImportContext context, GameState state, BgaSnapshot snapshot)
    {
        var issues = new List<ImportIssue>();

        CheckCardZones(context, state, issues);

        var oceans = state.PlacedTiles.Values.Count(t => t.Type == TileType.Ocean);
        if (oceans != state.OceansPlaced)
            issues.Add(ImportIssue.Blocking("OCEAN_COUNT", $"{state.OceansPlaced} oceans have been placed but {oceans} ocean tiles were found in the log."));

        CheckTerraformRatingIsRecorded(context, state, issues);

        var scores = ScoresOrNull(state, issues);
        foreach (var player in state.Players)
        {
            var bgaId = context.Seats[player.PlayerId];
            var name = context.Log.Players[bgaId].PlayerName;
            var trackers = snapshot.PlayerTrackers?.GetValueOrDefault(bgaId) ?? [];

            CheckTableau(player, name, trackers, issues);

            if (scores?.FirstOrDefault(s => s.PlayerId == player.PlayerId) is { } score
                && snapshot.PlayerVp?.GetValueOrDefault(bgaId)?.TotalDetails is { } expected)
            {
                // Milestone points are not compared: BGA's scoring table can trail the claim by a move, and
                // the claims themselves are imported directly.
                CheckScore(name, "greeneries", expected.Greeneries, score.GreeneryPoints, issues);
                CheckScore(name, "cities", expected.Cities, score.CityPoints, issues);
                CheckCardScores(state, player, name, snapshot.PlayerVp[bgaId].Details?.Cards ?? [], issues);
            }
        }

        try
        {
            if (LegalMoveGenerator.GetLegalMoves(state, state.ActivePlayerId).Actions is null)
                issues.Add(ImportIssue.Blocking("ENGINE_REJECTS_STATE", "The engine offers the active player no actions in this position."));
        }
        catch (Exception e)
        {
            issues.Add(ImportIssue.Blocking("ENGINE_REJECTS_STATE", $"The engine cannot generate moves for this position: {e.Message}"));
        }

        return issues;
    }

    /// <summary>
    /// The real game's next action must be one the engine allows here. It checks what the counters cannot: that
    /// the right player is to move, with the right number of actions left, holding a hand that can do it.
    /// </summary>
    public static List<ImportIssue> CheckNextMoveIsLegal(BgaImportContext context, GameState state, ForkPoint fork)
    {
        var issues = new List<ImportIssue>();

        var lastOfGeneration = Enumerable.Range(fork.MoveIndex, context.Log.Moves.Count - fork.MoveIndex)
            .TakeWhile(i => context.SnapshotAt(i)?.Generation == fork.Generation)
            .Last();
        var next = TurnTracker.Events(context, fork.MoveIndex + 1, lastOfGeneration).FirstOrDefault();
        if (next is null)
            return issues;

        var seat = context.SeatOf(next.PlayerId);
        ActionPhaseOptions? actions;
        try
        {
            actions = LegalMoveGenerator.GetLegalMoves(state, seat).Actions;
        }
        catch (Exception)
        {
            // Already reported by Validate.
            return issues;
        }

        var player = state.GetPlayer(seat);
        var subject = next.Subject is null ? null : CardNameMap.Resolve(next.Subject);
        var legal = actions is not null && next.Kind switch
        {
            TurnEventKind.PlayCard => actions.PlayableCards.Any(c => c.CardId == subject),
            TurnEventKind.UseCardAction => actions.UsableCardActions.Any(a => next.Subject is null || a.CardId == subject),
            TurnEventKind.StandardProject => Enum.TryParse<StandardProject>(next.Subject!.Replace(" ", ""), ignoreCase: true, out var project)
                && actions.StandardProjects.Any(p => p.Project == project && p.Available),
            TurnEventKind.ClaimMilestone => MapResolver.ResolveMilestoneOrAward(state.Map, next.Subject!) is { } milestone
                && actions.ClaimableMilestones.Any(m => m.Name == milestone),
            TurnEventKind.FundAward => MapResolver.ResolveMilestoneOrAward(state.Map, next.Subject!) is { } award
                && actions.FundableAwards.Any(a => a.Name == award),
            TurnEventKind.ConvertHeat => actions.CanConvertHeat,
            TurnEventKind.ConvertPlants => actions.CanConvertPlants,
            TurnEventKind.SellPatents => player.Hand.Count > 0,
            TurnEventKind.Pass => actions.CanPass,
            TurnEventKind.EndTurn => actions.CanEndTurn,
            _ => true,
        };

        if (!legal)
        {
            var what = next.Kind switch
            {
                TurnEventKind.PlayCard => $"played {next.Subject}",
                TurnEventKind.UseCardAction => $"used the action of {next.Subject}",
                TurnEventKind.StandardProject => $"used the standard project {next.Subject}",
                TurnEventKind.ClaimMilestone => $"claimed the milestone {next.Subject}",
                TurnEventKind.FundAward => $"funded the award {next.Subject}",
                TurnEventKind.ConvertHeat => "converted heat",
                TurnEventKind.ConvertPlants => "converted plants",
                TurnEventKind.SellPatents => "sold patents",
                TurnEventKind.Pass => "passed",
                _ => "ended their turn",
            };
            issues.Add(ImportIssue.Warning("NEXT_MOVE_NOT_LEGAL",
                $"In the real game {context.Log.Players[next.PlayerId].PlayerName} {what} next, which the engine does not allow in this position."));
        }

        return issues;
    }

    /// <summary>Every project card in the game's deck must be in exactly one place.</summary>
    private static void CheckCardZones(BgaImportContext context, GameState state, List<ImportIssue> issues)
    {
        var projectCards = state.Players
            .SelectMany(p => p.Hand.Concat(p.PlayedCards).Concat(p.PlayedEvents))
            .Concat(state.DrawPile)
            .Concat(state.DiscardPile)
            .Where(context.ProjectDeck.Contains)
            .ToList();

        var duplicates = projectCards.GroupBy(c => c).Where(g => g.Count() > 1).Select(g => CardRegistry.GetDefinition(g.Key).Name).ToList();
        if (duplicates.Count > 0)
            issues.Add(ImportIssue.Blocking("CARD_ZONES", $"Cards found in more than one place: {string.Join(", ", duplicates)}."));

        var missing = context.ProjectDeck.Count - projectCards.Distinct().Count();
        if (missing != 0)
            issues.Add(ImportIssue.Blocking("CARD_ZONES", $"{missing} card(s) from the deck are not in any hand, tableau or pile."));
    }

    private static void CheckTableau(PlayerState player, string name, Dictionary<string, int> trackers, List<ImportIssue> issues)
    {
        foreach (var tag in BgaTrackers.CountedTags)
        {
            var actual = player.CountTag(tag, CardRegistry.GetTags);
            if (BgaTrackers.Read(trackers, BgaTrackers.TagCount(tag)) is { } expected && expected != actual)
            {
                issues.Add(ImportIssue.Blocking("TAG_COUNT",
                    $"{name} should have {expected} {tag} tag(s) but the cards found in their tableau have {actual}."));
            }
        }

        if (BgaTrackers.Read(trackers, BgaTrackers.PlayedEvents) is { } events && events != player.PlayedEvents.Count)
        {
            issues.Add(ImportIssue.Blocking("EVENT_COUNT",
                $"{name} should have played {events} event(s) but {player.PlayedEvents.Count} were found in the log."));
        }

        if (BgaTrackers.Read(trackers, BgaTrackers.HandCount) is { } handCount && handCount != player.Hand.Count)
        {
            issues.Add(ImportIssue.Blocking("HAND_COUNT",
                $"{name} should hold {handCount} card(s) but {player.Hand.Count} were found in the log."));
        }
    }

    /// <summary>
    /// Terraform rating is read from BGA's live scoring table. On tables played without live scoring the table
    /// stays at its initial 20 for the whole game, which would silently import everyone at TR 20.
    /// </summary>
    private static void CheckTerraformRatingIsRecorded(BgaImportContext context, GameState state, List<ImportIssue> issues)
    {
        if (state.Players.Any(p => p.TerraformRating != Constants.StartingTR))
            return;

        var moves = context.Log.Moves;
        var earlierGenerations = Enumerable.Range(0, moves.Count)
            .TakeWhile(i => context.SnapshotAt(i)?.Generation is not { } generation || generation < state.Generation)
            .Select(i => moves[i]);
        if (earlierGenerations.Any(m => m.ActionType != "cancelled" && BgaMoveText.TerraformGains(m).Any()))
        {
            issues.Add(ImportIssue.Blocking("NO_LIVE_SCORING",
                "This table was played without live scoring, so the log does not record terraform ratings."));
        }
    }

    /// <summary>
    /// Card points are compared card by card so that a difference names the card responsible. Cards scored
    /// from the resources on them are left out: BGA's scoring table and its resource counters update on
    /// different moves, so the two disagree mid-turn without anything being wrong.
    /// </summary>
    private static void CheckCardScores(
        GameState state, PlayerState player, string name, Dictionary<string, BgaCardVp> expected, List<ImportIssue> issues)
    {
        var expectedById = expected.ToDictionary(c => CardNameMap.Resolve(c.Key)!, c => c.Value.Vp);
        foreach (var card in player.PlayedCards.Concat(player.PlayedEvents).Union(expectedById.Keys))
        {
            if (CardRegistry.GetDefinition(card).VictoryPoints is PerResourceVictoryPoints or HasResourceVictoryPoints)
                continue;

            var onBga = expectedById.GetValueOrDefault(card);
            var inEngine = player.PlayedCards.Contains(card) || player.PlayedEvents.Contains(card)
                ? Scoring.CalculateCardPoints(state, player, card)
                : 0;
            if (onBga != inEngine)
            {
                issues.Add(ImportIssue.Warning("SCORE_MISMATCH",
                    $"{name}'s {CardRegistry.GetDefinition(card).Name} is worth {onBga} on BGA but {inEngine} in the engine."));
            }
        }
    }

    private static void CheckScore(string name, string component, int? expected, int actual, List<ImportIssue> issues)
    {
        if (expected is { } value && value != actual)
        {
            issues.Add(ImportIssue.Warning("SCORE_MISMATCH",
                $"{name}'s points for {component} are {value} on BGA but {actual} in the engine."));
        }
    }

    private static IReadOnlyList<PlayerScore>? ScoresOrNull(GameState state, List<ImportIssue> issues)
    {
        try
        {
            return Scoring.CalculateFinalScores(state);
        }
        catch (Exception e)
        {
            issues.Add(ImportIssue.Blocking("ENGINE_REJECTS_STATE", $"The engine cannot score this position: {e.Message}"));
            return null;
        }
    }
}
