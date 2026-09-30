using System.Text.RegularExpressions;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport;

/// <summary>A tile on the board as the log describes it. Kind is "City", "Forest", "Ocean" or a special tile's name.</summary>
internal sealed record FoldTile(string Kind, string? OwnerBgaId, int MoveIndex);

/// <summary>
/// Where every card and tile is after a given move, accumulated from the moves up to it.
/// The log's per-move snapshots carry counters (resources, production, tags) but not these zones:
/// their hand lists lag behind plays in some scraper versions, and they have no tableau or board.
/// </summary>
internal sealed partial class BgaFold
{
    private readonly BgaImportContext _context;

    /// <summary>BGA player id -> project card ids in hand.</summary>
    public Dictionary<string, List<string>> Hands { get; } = [];

    /// <summary>BGA player id -> played preludes and non-event project cards, in play order.</summary>
    public Dictionary<string, List<string>> Played { get; } = [];

    /// <summary>BGA player id -> played event cards, in play order.</summary>
    public Dictionary<string, List<string>> Events { get; } = [];

    /// <summary>Project card ids in the discard pile, oldest first.</summary>
    public List<string> Discard { get; } = [];

    public Dictionary<HexCoord, FoldTile> Tiles { get; } = [];

    /// <summary>Hexes reserved by Land Claim that have no tile yet.</summary>
    public Dictionary<HexCoord, string> ClaimedHexes { get; } = [];

    public List<ImportIssue> Issues { get; } = [];

    /// <summary>Cards that have left every hand for good (played or discarded).</summary>
    private readonly HashSet<string> _gone = [];

    private BgaFold(BgaImportContext context)
    {
        _context = context;
        foreach (var id in context.Seats)
        {
            Hands[id] = [];
            Played[id] = [];
            Events[id] = [];
        }
    }

    public static BgaFold Run(BgaImportContext context, int lastMoveIndex)
    {
        var fold = new BgaFold(context);
        for (int i = 0; i <= lastMoveIndex; i++)
        {
            var move = context.Log.Moves[i];
            if (move.ActionType == "cancelled")
                continue;

            fold.ApplyCards(move);
            fold.ApplyTile(move, i);
            if (IsTakeback(move))
                fold.ResyncHandAfterTakeback(move);
        }

        fold.ApplySpecialTiles(context.SnapshotAt(lastMoveIndex));
        return fold;
    }

    public static bool IsTakeback(BgaMove move) =>
        move.ActionType == "takeback" || move.Description.Contains("takes back");

    // ── Cards ──────────────────────────────────────────────────

    private void ApplyCards(BgaMove move)
    {
        foreach (var (playerId, names) in move.CardsKept ?? [])
        {
            if (!Hands.TryGetValue(playerId, out var hand))
                continue;
            foreach (var card in names.Select(CardNameMap.Resolve))
                AddToHand(hand, card!);
        }

        foreach (var reveal in BgaMoveText.Reveals(move).Where(r => !r.Kept))
            MoveToDiscard(CardNameMap.Resolve(reveal.CardName)!);

        if (move.CardPlayed is not null)
            Play(move, CardNameMap.Resolve(move.CardPlayed)!);

        foreach (var name in move.CardsDiscarded ?? [])
            MoveToDiscard(CardNameMap.Resolve(name)!);
    }

    /// <summary>
    /// Keeps are repeated across adjacent moves, so adding is idempotent. Preludes and corporations never
    /// enter the hand: by the time a game can be forked they are all in play.
    /// </summary>
    private void AddToHand(List<string> hand, string card)
    {
        if (!_context.ProjectDeck.Contains(card) || _gone.Contains(card) || Hands.Values.Any(h => h.Contains(card)))
            return;
        hand.Add(card);
    }

    private void Play(BgaMove move, string card)
    {
        var type = CardRegistry.GetDefinition(card).Type;
        if (type == CardType.Corporation)
            return;

        var playerId = PlayerWhoPlayed(move);
        if (!Played.ContainsKey(playerId) || Played.Values.Concat(Events.Values).Any(cards => cards.Contains(card)))
            return;

        if (type != CardType.Prelude && !Hands[playerId].Contains(card))
        {
            Issues.Add(ImportIssue.Blocking("HAND_TRACKING",
                $"Move {move.MoveNumber}: {move.CardPlayed} was played but the log never showed it entering that player's hand."));
        }

        RemoveFromHands(card);
        _gone.Add(card);
        (type == CardType.Event ? Events : Played)[playerId].Add(card);
    }

    private void MoveToDiscard(string card)
    {
        if (!_context.ProjectDeck.Contains(card))
            return;

        RemoveFromHands(card);
        _gone.Add(card);
        if (!Discard.Contains(card))
            Discard.Add(card);
    }

    private void RemoveFromHands(string card)
    {
        foreach (var hand in Hands.Values)
            hand.Remove(card);
    }

    [GeneratedRegex(@"^(.+) plays card ")]
    private static partial Regex PlaysCardLine();

    /// <summary>A move can log another player's play, so the name in the text wins over the move's own player.</summary>
    private string PlayerWhoPlayed(BgaMove move)
    {
        var name = BgaMoveText.Lines(move)
            .Select(line => PlaysCardLine().Match(line))
            .Where(m => m.Success)
            .Select(m => m.Groups[1].Value)
            .FirstOrDefault();
        return _context.ResolvePlayer(name) ?? move.PlayerId;
    }

    /// <summary>
    /// A takeback can undo a purchase or a sale without the redo being logged. BGA resends the player's hand
    /// when it happens, so the snapshot on the takeback move is the authority for cards the fold has lost track of.
    /// </summary>
    private void ResyncHandAfterTakeback(BgaMove move)
    {
        if (move.GameState?.PlayerHands is not { } hands
            || !hands.TryGetValue(move.PlayerId, out var names)
            || !Hands.TryGetValue(move.PlayerId, out var hand))
        {
            return;
        }

        foreach (var card in names.Select(CardNameMap.Resolve))
        {
            if (Discard.Remove(card!))
                _gone.Remove(card!);
            AddToHand(hand, card!);
        }
    }

    // ── Tiles ──────────────────────────────────────────────────

    [GeneratedRegex(@"^(.+?) places (?:tile )?(.+?) (?:on|into) ")]
    private static partial Regex PlacesLine();

    private void ApplyTile(BgaMove move, int moveIndex)
    {
        if (move.TilePlaced is null || HexLocationParser.Parse(move.TileLocation) is not { } coord || HexLocationParser.IsOffMap(coord))
            return;

        var kind = move.TilePlaced;
        // The move's own player is often not the one placing (the log files a placement under whoever's
        // turn it was logged in), so the name in "X places Y on Z" is the owner. Special tiles are logged
        // as the generic "tile"; their name is in the same text.
        var named = BgaMoveText.Lines(move).Select(line => PlacesLine().Match(line)).FirstOrDefault(m => m.Success);
        var owner = _context.ResolvePlayer(named?.Groups[1].Value) ?? move.PlayerId;
        if (kind is not ("City" or "Forest" or "Ocean"))
            kind = named?.Groups[2].Value ?? kind;

        if (kind == "Mining")
            kind = MiningCardBehind(owner);

        Tiles[coord] = new FoldTile(kind, kind == "Ocean" ? null : owner, moveIndex);
        ClaimedHexes.Remove(coord);
    }

    /// <summary>Mining Area and Mining Rights both place a tile BGA calls "Mining"; the card played last tells which.</summary>
    private string MiningCardBehind(string playerId) =>
        Played[playerId].Select(id => CardRegistry.GetDefinition(id).Name)
            .LastOrDefault(name => name is "Mining Area" or "Mining Rights") ?? "Mining";

    /// <summary>
    /// The snapshot lists each player's special tiles by name. It fills in placements whose move text did not
    /// name the tile, and is the only record of Land Claim.
    /// </summary>
    private void ApplySpecialTiles(BgaSnapshot? snapshot)
    {
        foreach (var (playerId, tiles) in snapshot?.SpecialTiles ?? [])
        {
            foreach (var (name, location) in tiles)
            {
                if (HexLocationParser.Parse(location) is not { } coord || HexLocationParser.IsOffMap(coord))
                    continue;

                if (name == "Land Claim")
                {
                    if (!Tiles.ContainsKey(coord))
                        ClaimedHexes[coord] = playerId;
                }
                else if (!Tiles.TryGetValue(coord, out var tile) || tile.Kind == "tile")
                {
                    Tiles[coord] = new FoldTile(name == "Mining" ? MiningCardBehind(playerId) : name, playerId, tile?.MoveIndex ?? 0);
                }
            }
        }
    }
}
