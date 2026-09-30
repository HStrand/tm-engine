using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>
/// The facts about a log that hold for every position in it: map, card pool, seat order.
/// Creating one is the eligibility check.
/// </summary>
internal sealed class BgaImportContext
{
    public required BgaGameLog Log { get; init; }
    public required MapName Map { get; init; }

    /// <summary>Project card ids in this game's deck.</summary>
    public required ImmutableHashSet<string> ProjectDeck { get; init; }

    /// <summary>BGA player ids in turn order. The index is the engine player id.</summary>
    public required ImmutableList<string> Seats { get; init; }

    private BgaSnapshot?[] _snapshots = [];

    public int SeatOf(string bgaPlayerId) => Seats.IndexOf(bgaPlayerId);

    /// <summary>The most recent snapshot at or before a move. Not every move carries one.</summary>
    public BgaSnapshot? SnapshotAt(int moveIndex) => _snapshots[moveIndex];

    /// <summary>Matches a BGA player id or a player name (names in log text can be misspelled) to a player id.</summary>
    public string? ResolvePlayer(string? idOrName) => ResolvePlayer(Log, idOrName);

    public static (BgaImportContext? Context, List<ImportIssue> Issues) Create(BgaGameLog log)
    {
        var issues = new List<ImportIssue>();

        var map = MapResolver.Resolve(log);
        if (map.Map is null)
            issues.Add(ImportIssue.Blocking("UNSUPPORTED_MAP", map.Problem!));

        if (log.ColoniesOn == true)
            issues.Add(ImportIssue.Blocking("COLONIES", "The Colonies expansion is not supported by the engine."));

        if (log.PreludeOn is null || log.CorporateEraOn is null || log.ColoniesOn is null)
            issues.Add(ImportIssue.Blocking("OPTIONS_UNKNOWN", "The log does not record which expansions were used. Re-scrape the game."));

        // Seat order, draft direction and turn tracking have only been verified against two-player logs.
        if (log.Players.Count != 2)
            issues.Add(ImportIssue.Blocking("PLAYER_COUNT", $"Only two-player games can be played on for now; this one has {log.Players.Count} players."));

        foreach (var player in log.Players.Values)
        {
            var corporation = player.Corporation is null ? null : CardNameMap.Resolve(player.Corporation);
            if (corporation is null || CardRegistry.GetDefinition(corporation).Type != CardType.Corporation)
                issues.Add(ImportIssue.Blocking("UNKNOWN_CORPORATION", $"{player.PlayerName}'s corporation '{player.Corporation}' is not in the engine."));
        }

        var unknownCards = BgaLogCards.NamesIn(log).Distinct().Where(n => !CardNameMap.TryResolve(n, out _)).Order().ToList();
        if (unknownCards.Count > 0)
            issues.Add(ImportIssue.Blocking("UNKNOWN_CARD", $"Cards not in the engine: {string.Join(", ", unknownCards)}."));

        if (!log.Moves.Any(m => m.GameState?.DrawPile is not null))
            issues.Add(ImportIssue.Blocking("OLD_LOG_FORMAT", "The log was parsed by an old scraper version and lacks hands and deck counts. Re-scrape the game."));

        if (issues.Any(i => i.Severity == IssueSeverity.Blocking))
            return (null, issues);

        var expansions = DeckBuilder.GetEnabledExpansions(new GameSetupOptions(
            PlayerCount: log.Players.Count,
            Map: map.Map!.Value,
            CorporateEra: log.CorporateEraOn == true,
            DraftVariant: log.DraftOn == true,
            PreludeExpansion: log.PreludeOn == true));

        var context = new BgaImportContext
        {
            Log = log,
            Map = map.Map.Value,
            ProjectDeck = [.. CardRegistry.GetProjectCardIds(expansions)],
            Seats = SeatOrder(log, issues),
        };

        context._snapshots = new BgaSnapshot?[log.Moves.Count];
        BgaSnapshot? latest = null;
        for (int i = 0; i < log.Moves.Count; i++)
        {
            latest = log.Moves[i].GameState ?? latest;
            context._snapshots[i] = latest;
        }

        return (context, issues);
    }

    /// <summary>
    /// Turn order, read off the starting player of each generation: the first player marker moves one seat
    /// per generation, so the first n generations spell out the order.
    /// </summary>
    private static ImmutableList<string> SeatOrder(BgaGameLog log, List<ImportIssue> issues)
    {
        var order = new List<string>();
        int? generation = null;
        foreach (var snapshot in log.Moves.Select(m => m.GameState))
        {
            if (snapshot?.Generation is null || snapshot.Generation == generation)
                continue;
            generation = snapshot.Generation;

            var starter = ResolvePlayer(log, snapshot.StartingPlayer);
            if (starter is not null && !order.Contains(starter))
                order.Add(starter);
        }

        var unplaced = log.Players.Keys.Where(id => !order.Contains(id)).ToList();
        if (unplaced.Count > 1)
        {
            issues.Add(ImportIssue.Warning("SEAT_ORDER_GUESSED",
                "The game was too short to show the full turn order; the order of the remaining players is a guess."));
        }
        order.AddRange(unplaced);

        return [.. order];
    }

    private static string? ResolvePlayer(BgaGameLog log, string? idOrName)
    {
        if (string.IsNullOrWhiteSpace(idOrName))
            return null;
        if (log.Players.ContainsKey(idOrName))
            return idOrName;

        var closest = log.Players.Values
            .Select(p => (p.PlayerId, Distance: EditDistance(p.PlayerName, idOrName)))
            .MinBy(p => p.Distance);
        return closest.Distance <= 2 ? closest.PlayerId : null;
    }

    private static int EditDistance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(substitution, Math.Min(previous[j], current[j - 1]) + 1);
            }
            previous = current;
        }
        return previous[b.Length];
    }
}
