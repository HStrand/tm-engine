using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>
/// Turns a position in a BGA game log into an engine <see cref="GameState"/> that can be played on from.
/// The start of the game is the engine's own setup with the log's deal in place of a random one. For later
/// positions, counters (resources, production, terraform rating, global parameters) are read from the log's snapshot at
/// the fork; card zones and the board are accumulated from the moves; the deck is rebuilt by
/// <see cref="DeckReconstructor"/>. The result is checked against the snapshot by <see cref="ImportValidator"/>.
/// </summary>
public static class BgaImporter
{
    /// <summary>
    /// Whether the log can be forked, and the moves it can be forked after. Every fork point returned has been
    /// built and validated, so importing it will succeed.
    /// </summary>
    public static ForkPointsResult FindForkPoints(BgaGameLog log)
    {
        var (context, issues) = BgaImportContext.Create(log);
        if (context is null)
            return new ForkPointsResult([], [.. issues]);

        var candidates = ForkPointFinder.Find(context);
        var forks = candidates
            .Where(fork => Build(context, candidates, fork, new BgaImportOptions(fork.MoveIndex), []).Success)
            .ToImmutableList();
        if (forks.Count == 0)
            issues.Add(ImportIssue.Blocking("NO_FORK_POINTS", "No position in this game can be played on from."));

        return new ForkPointsResult(forks, [.. issues]);
    }

    /// <summary>The fork point at or nearest before a move, or null when there is none that early.</summary>
    public static ForkPoint? SnapToForkPoint(ForkPointsResult forkPoints, int moveIndex) =>
        forkPoints.ForkPoints.LastOrDefault(f => f.MoveIndex <= moveIndex);

    public static BgaImportResult Import(BgaGameLog log, BgaImportOptions options)
    {
        var (context, issues) = BgaImportContext.Create(log);
        if (context is null)
            return Failed(issues);

        var candidates = ForkPointFinder.Find(context);
        var fork = candidates.FirstOrDefault(f => f.MoveIndex == options.MoveIndex);
        if (fork is null)
        {
            issues.Add(ImportIssue.Blocking("NOT_A_FORK_POINT", $"The game cannot be played on from move index {options.MoveIndex}."));
            return Failed(issues);
        }

        return Build(context, candidates, fork, options, issues);
    }

    /// <summary>
    /// Import the position at <see cref="BgaImportOptions.MoveIndex"/>, or failing that the nearest earlier one
    /// that can be imported. Cheaper than <see cref="FindForkPoints"/> followed by <see cref="Import"/>, since it
    /// stops at the first position that validates.
    /// </summary>
    public static BgaImportResult ImportAtOrBefore(BgaGameLog log, BgaImportOptions options)
    {
        var (context, issues) = BgaImportContext.Create(log);
        if (context is null)
            return Failed(issues);

        var candidates = ForkPointFinder.Find(context);
        foreach (var fork in candidates.Where(f => f.MoveIndex <= options.MoveIndex).Reverse())
        {
            var result = Build(context, candidates, fork, options with { MoveIndex = fork.MoveIndex }, [.. issues]);
            if (result.Success)
                return result;
        }

        issues.Add(ImportIssue.Blocking("NOT_A_FORK_POINT",
            $"The game cannot be played on from move index {options.MoveIndex} or any position before it."));
        return Failed(issues);
    }

    private static BgaImportResult Build(
        BgaImportContext context, List<ForkPoint> candidates, ForkPoint fork, BgaImportOptions options, List<ImportIssue> issues)
    {
        if (fork.Kind == ForkKind.GameStart)
            return BuildGameStart(context, fork, options, issues);

        var log = context.Log;

        var snapshot = context.SnapshotAt(fork.MoveIndex);
        if (snapshot is not { Temperature: not null, Oxygen: not null, Oceans: not null, PlayerTrackers: not null, PlayerVp: not null })
        {
            issues.Add(ImportIssue.Blocking("SNAPSHOT_INCOMPLETE", "The log has no full game state at this move."));
            return Failed(issues, fork);
        }

        var fold = BgaFold.Run(context, fork.MoveIndex);
        issues.AddRange(fold.Issues);

        // Everything that happened in this generation's action phase before the fork.
        var actionPhaseStart = candidates.Last(f => f.Kind == ForkKind.GenerationStart && f.MoveIndex <= fork.MoveIndex).MoveIndex;
        var turn = new TurnSoFar(
            TurnTracker.Events(context, actionPhaseStart + 1, fork.MoveIndex),
            // In generation 1 the preludes come before the action phase and can raise the rating too.
            PlayersWhoGainedTerraformRating(context, fork.Generation == 1 ? 0 : actionPhaseStart + 1, fork.MoveIndex),
            fork.ActiveBgaPlayerId,
            fork.Generation);

        var players = context.Seats
            .Select((bgaId, seat) => BuildPlayer(context, fold, snapshot, turn, bgaId, seat, issues))
            .ToImmutableList();
        var piles = DeckReconstructor.Reconstruct(context, fold, fork.MoveIndex, options.ShuffleSeed, issues);
        var firstPlayer = context.SeatOf(context.ResolvePlayer(snapshot.StartingPlayer) ?? fork.ActiveBgaPlayerId);

        var state = new GameState
        {
            GameId = options.GameId ?? Guid.NewGuid().ToString(),
            Map = context.Map,
            CorporateEra = log.CorporateEraOn == true,
            DraftVariant = log.DraftOn == true,
            PreludeExpansion = log.PreludeOn == true,
            MoveNumber = 0,
            Phase = GamePhase.Action,
            Generation = fork.Generation,
            ActivePlayerId = context.SeatOf(fork.ActiveBgaPlayerId),
            FirstPlayerId = firstPlayer,
            Oxygen = snapshot.Oxygen.Value,
            Temperature = snapshot.Temperature.Value,
            OceansPlaced = snapshot.Oceans.Value,
            Players = players,
            PlacedTiles = BuildTiles(context, fold, issues),
            OffMapTiles = BuildOffMapTiles(players),
            ClaimedHexes = fold.ClaimedHexes.ToImmutableDictionary(c => c.Key, c => context.SeatOf(c.Value)),
            ClaimedMilestones = Claims(context, fork.MoveIndex, s => s.Milestones, issues)
                .Select(c => new MilestoneClaim(c.Name, c.Seat)).ToImmutableList(),
            FundedAwards = Claims(context, fork.MoveIndex, s => s.Awards, issues)
                .Select(c => new AwardFunding(c.Name, c.Seat)).ToImmutableList(),
            DrawPile = piles.Draw,
            DiscardPile = piles.Discard,
            PreludeDeck = fork.Generation == 1 ? UndealtPreludes(context, options.ShuffleSeed) : [],
            ShuffleSeed = options.ShuffleSeed,
        };

        issues.AddRange(ImportValidator.Validate(context, state, snapshot, fork.MoveIndex));
        issues.AddRange(ImportValidator.CheckNextMoveIsLegal(context, state, fork));
        if (issues.Any(i => i.Severity == IssueSeverity.Blocking))
            return Failed(issues, fork);

        return new BgaImportResult(state, Meta(context, fork, options), fork, [.. issues]);
    }

    private static GameMeta Meta(BgaImportContext context, ForkPoint fork, BgaImportOptions options)
    {
        var log = context.Log;
        var seats = context.Seats
            .Select((bgaId, seat) => new SeatMeta(seat, bgaId, log.Players[bgaId].PlayerName, log.Players[bgaId].Color))
            .ToImmutableList();
        var source = new SourceRef(log.TableId ?? log.ReplayId, log.PlayerPerspective, fork.MoveIndex, fork.MoveNumber);
        return new GameMeta(seats, source, options.ShuffleSeed);
    }

    // ── Game start ─────────────────────────────────────────────

    /// <summary>
    /// The engine's setup phase with the real game's deal: each player chooses again from the corporations,
    /// preludes and project cards they were dealt, and the deck holds the cards the real game went on to draw.
    /// </summary>
    private static BgaImportResult BuildGameStart(BgaImportContext context, ForkPoint fork, BgaImportOptions options, List<ImportIssue> issues)
    {
        var log = context.Log;
        var preludesDealt = log.PreludeOn == true ? Constants.PreludesDealt : 0;

        var corporations = Dealt(context, "corporations", h => h.Corporations, Constants.CorporationsDealt, CardType.Corporation, issues);
        var preludes = Dealt(context, "preludes", h => h.Preludes, preludesDealt, CardType.Prelude, issues);
        var cards = Dealt(context, "project cards", h => h.ProjectCards, Constants.InitialCardsDealt, null, issues);

        foreach (var (bgaId, seat) in context.Seats.Select((id, seat) => (id, seat)))
        {
            var player = log.Players[bgaId];
            if (!corporations[seat].Contains(CardNameMap.Resolve(player.Corporation!)!))
            {
                issues.Add(ImportIssue.Blocking("STARTING_HAND",
                    $"{player.PlayerName} played {player.Corporation}, which is not among the corporations the log says they were dealt."));
            }
        }

        var dealtCards = cards.SelectMany(c => c).ToList();
        if (dealtCards.Distinct().Count() != dealtCards.Count || dealtCards.Any(c => !context.ProjectDeck.Contains(c)))
            issues.Add(ImportIssue.Blocking("STARTING_HAND", "The opening hands in the log are not distinct cards from this game's deck."));

        if (issues.Any(i => i.Severity == IssueSeverity.Blocking))
            return Failed(issues, fork);


        var setup = new GameSetupOptions(
            PlayerCount: log.Players.Count,
            Map: context.Map,
            CorporateEra: log.CorporateEraOn == true,
            DraftVariant: log.DraftOn == true,
            PreludeExpansion: log.PreludeOn == true);
        var deal = new SetupDeal(
            corporations, preludes, cards,
            DeckReconstructor.ReconstructAtGameStart(context, [.. dealtCards], options.ShuffleSeed),
            UndealtPreludes(context, options.ShuffleSeed));

        // Seat 0 is whoever started generation 1, which is where the engine's setup puts the first player.
        var state = GameEngine.Setup(setup, options.ShuffleSeed, deal) with { GameId = options.GameId ?? Guid.NewGuid().ToString() };

        issues.AddRange(ImportValidator.CheckOpeningIsLegal(context, state));
        if (issues.Any(i => i.Severity == IssueSeverity.Blocking))
            return Failed(issues, fork);

        return new BgaImportResult(state, Meta(context, fork, options), fork, [.. issues]);
    }

    /// <summary>
    /// The preludes nobody was dealt, shuffled by seed. Valley Trust's first action draws from them; the log
    /// does not say in which order they lay.
    /// </summary>
    private static ImmutableList<string> UndealtPreludes(BgaImportContext context, int shuffleSeed)
    {
        if (context.Log.PreludeOn != true)
            return [];

        var dealt = context.Log.Players.Values
            .SelectMany(p => p.StartingHand?.Preludes ?? [])
            .Select(CardNameMap.Resolve)
            .ToHashSet();
        return DeckBuilder.Shuffle([.. CardRegistry.GetPreludeIds().Where(id => !dealt.Contains(id))], new Random(shuffleSeed));
    }

    /// <summary>One kind of card from each player's starting hand, by seat, as engine card ids.</summary>
    private static ImmutableList<ImmutableList<string>> Dealt(
        BgaImportContext context, string what, Func<BgaStartingHand, List<string>?> select, int expected, CardType? type, List<ImportIssue> issues)
    {
        var dealt = ImmutableList.CreateBuilder<ImmutableList<string>>();
        foreach (var bgaId in context.Seats)
        {
            var player = context.Log.Players[bgaId];
            var ids = expected == 0
                ? []
                : (select(player.StartingHand!) ?? []).Select(CardNameMap.Resolve).OfType<string>().ToImmutableList();

            if (ids.Count != expected || (type is { } required && ids.Any(id => CardRegistry.GetDefinition(id).Type != required)))
            {
                issues.Add(ImportIssue.Blocking("STARTING_HAND",
                    $"The log should list the {expected} {what} {player.PlayerName} was dealt, but lists {ids.Count}."));
            }
            dealt.Add(ids);
        }
        return dealt.ToImmutable();
    }

    private static BgaImportResult Failed(List<ImportIssue> issues, ForkPoint? fork = null) =>
        new(null, null, fork, [.. issues]);

    // ── Players ────────────────────────────────────────────────

    /// <summary>What the action phase's moves up to the fork say about whose turn it is and what has been used up.</summary>
    private sealed record TurnSoFar(List<TurnEvent> Events, HashSet<string> GainedTerraformRating, string ActivePlayerId, int Generation);

    private static HashSet<string> PlayersWhoGainedTerraformRating(BgaImportContext context, int first, int last) =>
        Enumerable.Range(first, Math.Max(0, last - first + 1))
            .Select(i => context.Log.Moves[i])
            .Where(m => m.ActionType != "cancelled")
            .SelectMany(m => BgaMoveText.TerraformGains(m).Select(gain => context.ResolvePlayer(gain.PlayerName) ?? m.PlayerId))
            .ToHashSet();

    private static PlayerState BuildPlayer(
        BgaImportContext context, BgaFold fold, BgaSnapshot snapshot, TurnSoFar turn, string bgaId, int seat, List<ImportIssue> issues)
    {
        var name = context.Log.Players[bgaId].PlayerName;
        var trackers = snapshot.PlayerTrackers!.GetValueOrDefault(bgaId);
        var terraformRating = snapshot.PlayerVp!.GetValueOrDefault(bgaId)?.TotalDetails?.Tr;
        if (trackers is null || terraformRating is null)
        {
            issues.Add(ImportIssue.Blocking("SNAPSHOT_INCOMPLETE", $"The log has no counters for {name} at this move."));
            trackers ??= [];
        }

        int Counter(string key)
        {
            if (trackers.TryGetValue(key, out var value))
                return value;
            issues.Add(ImportIssue.Blocking("SNAPSHOT_INCOMPLETE", $"The log has no '{key}' counter for {name} at this move."));
            return 0;
        }

        var played = fold.Played[bgaId];
        var cardResources = snapshot.PlayerVp.GetValueOrDefault(bgaId)?.Details?.CardResources ?? [];

        var ownEvents = turn.Events.Where(e => e.PlayerId == bgaId).ToList();
        var usedActions = UsedCardActions(context, ownEvents, played, name, issues);

        // Indentured Workers and Special Design change the next card played; the effect is still waiting if
        // the card that set it up is the last one this player played.
        var lastCardPlayed = ownEvents.LastOrDefault(e => e.Kind == TurnEventKind.PlayCard)?.Subject is { } lastCard
            && CardNameMap.TryResolve(lastCard, out var lastCardId)
            ? CardRegistry.Get(lastCardId).OnPlayEffects
            : [];

        var corporation = CardRegistry.Get(CardNameMap.Resolve(context.Log.Players[bgaId].Corporation!)!);

        return PlayerState.CreateInitial(seat, terraformRating ?? 0) with
        {
            // A first action is the first thing its player does in generation 1, so it is still owed until
            // they have done something. Vitor's free award is funded the same way.
            PerformedFirstAction = turn.Generation > 1 || corporation.FirstActionEffects.IsEmpty || ownEvents.Count > 0,
            HasFreeAwardFunding = turn.Generation == 1 && corporation.OnPlayEffects.OfType<GrantFreeAwardEffect>().Any() && ownEvents.Count == 0,
            Passed = ownEvents.Any(e => e.Kind == TurnEventKind.Pass),
            ActionsThisTurn = bgaId == turn.ActivePlayerId ? TurnTracker.ActionsThisTurn(turn.Events, bgaId) : 0,
            UsedCardActions = usedActions,
            IncreasedTRThisGeneration = turn.GainedTerraformRating.Contains(bgaId),
            NextCardDiscount = lastCardPlayed.OfType<NextCardDiscountEffect>().Sum(e => e.Discount),
            NextCardRequirementModifier = lastCardPlayed.OfType<NextCardRequirementModifierEffect>().Sum(e => e.Amount),
            CorporationId = CardNameMap.Resolve(context.Log.Players[bgaId].Corporation!)!,
            Resources = new ResourceSet(
                Counter(BgaTrackers.MegaCredits), Counter("Steel"), Counter("Titanium"),
                Counter("Plant"), Counter("Energy"), Counter("Heat")),
            Production = new ProductionSet(
                Counter(BgaTrackers.MegaCredits + " Production"), Counter("Steel Production"), Counter("Titanium Production"),
                Counter("Plant Production"), Counter("Energy Production"), Counter("Heat Production")),
            Hand = [.. fold.Hands[bgaId]],
            PlayedCards = [.. played],
            PlayedEvents = [.. fold.Events[bgaId]],
            CardResources = cardResources
                .Select(r => (Card: CardNameMap.Resolve(r.Key)!, r.Value.Count))
                .Where(r => r.Count > 0 && played.Contains(r.Card))
                .ToImmutableDictionary(r => r.Card, r => r.Count),
        };
    }

    /// <summary>
    /// The cards (or corporation) whose action a player has used this generation. Some scraper versions log a
    /// drawing action without naming its card; the card is then worked out from how many cards were drawn,
    /// when only one of the player's unused actions fits.
    /// </summary>
    private static ImmutableHashSet<string> UsedCardActions(
        BgaImportContext context, List<TurnEvent> ownEvents, List<string> played, string playerName, List<ImportIssue> issues)
    {
        var used = new HashSet<string>();
        foreach (var e in ownEvents.Where(e => e.Kind == TurnEventKind.UseCardAction))
        {
            if (e.Subject is not null)
            {
                if (CardNameMap.TryResolve(e.Subject, out var id))
                    used.Add(id);
                continue;
            }

            var drawn = context.Log.Moves[e.MoveIndex].CardsKept?.GetValueOrDefault(e.PlayerId)?.Count ?? 0;
            var fitting = played
                .Where(id => !used.Contains(id) && CardRegistry.Get(id).Action is { } action && action.Effects.Any(effect => effect switch
                {
                    DrawCardsEffect draw => draw.Count == drawn,
                    DrawKeepEffect draw => drawn <= draw.Keep,
                    _ => false,
                }))
                .ToList();

            if (fitting.Count == 1)
            {
                used.Add(fitting[0]);
            }
            else
            {
                issues.Add(ImportIssue.Warning("USED_ACTION_UNKNOWN",
                    $"{playerName} used a card action earlier this generation that the log does not name, so that card's action is available again here."));
            }
        }
        return [.. used];
    }

    // ── Board ──────────────────────────────────────────────────

    private static ImmutableDictionary<HexCoord, PlacedTile> BuildTiles(BgaImportContext context, BgaFold fold, List<ImportIssue> issues)
    {
        var hexes = MapDefinitions.GetMap(context.Map).Hexes;
        var tiles = ImmutableDictionary.CreateBuilder<HexCoord, PlacedTile>();

        foreach (var (coord, tile) in fold.Tiles)
        {
            if (!hexes.ContainsKey(coord))
            {
                issues.Add(ImportIssue.Blocking("TILE_OFF_BOARD", $"A {tile.Kind} tile is at {coord}, which is not a hex on {context.Map}."));
                continue;
            }

            if (TileTypeOf(tile.Kind) is not { } type)
            {
                issues.Add(ImportIssue.Blocking("UNKNOWN_TILE", $"The tile '{tile.Kind}' at {coord} is not in the engine."));
                continue;
            }

            tiles[coord] = new PlacedTile(type, tile.OwnerBgaId is null ? null : context.SeatOf(tile.OwnerBgaId), coord);
        }

        return tiles.ToImmutable();
    }

    /// <summary>BGA names tiles after their card ("Nuclear Zone"); the engine's tile types are the same names without spaces.</summary>
    private static TileType? TileTypeOf(string kind) => kind switch
    {
        "Forest" => TileType.Greenery,
        "tile" or "Mining" => null,
        _ => Enum.TryParse<TileType>(kind.Replace(" ", ""), out var type) ? type : null,
    };

    /// <summary>Cities that are not on the board. BGA logs no placement for them, so they follow from the card being in play.</summary>
    private static readonly string[] OffMapCities = ["Phobos Space Haven", "Ganymede Colony"];

    private static ImmutableList<OffMapTile> BuildOffMapTiles(ImmutableList<PlayerState> players) =>
        (from player in players
         from card in player.PlayedCards
         let name = CardRegistry.GetDefinition(card).Name
         where OffMapCities.Contains(name)
         select new OffMapTile(name, TileType.City, player.PlayerId)).ToImmutableList();

    // ── Milestones and awards ──────────────────────────────────

    /// <summary>
    /// Milestones claimed or awards funded up to the fork, in claim order. Collected across snapshots because
    /// some scraper versions leave the lists empty on individual moves.
    /// </summary>
    private static List<(string Name, int Seat)> Claims(
        BgaImportContext context, int forkIndex, Func<BgaSnapshot, Dictionary<string, BgaClaim>?> select, List<ImportIssue> issues)
    {
        var moves = context.Log.Moves;
        var cancelled = moves.Where(m => m.ActionType == "cancelled").Select(m => m.MoveNumber).ToHashSet();

        var claims = new Dictionary<string, BgaClaim>();
        for (int i = 0; i <= forkIndex; i++)
        {
            foreach (var (name, claim) in (moves[i].GameState is { } s ? select(s) : null) ?? [])
            {
                if (!cancelled.Contains(claim.MoveNumber))
                    claims[name] = claim;
            }
        }

        var result = new List<(string, int)>();
        foreach (var (bgaName, claim) in claims.OrderBy(c => c.Value.MoveNumber))
        {
            var name = MapResolver.ResolveMilestoneOrAward(context.Map, bgaName);
            var seat = claim.PlayerId is null ? -1 : context.SeatOf(claim.PlayerId);
            if (name is null || seat < 0)
                issues.Add(ImportIssue.Blocking("UNKNOWN_CLAIM", $"Cannot place milestone/award '{bgaName}' on {context.Map}."));
            else
                result.Add((name, seat));
        }
        return result;
    }
}
