namespace TmEngine.BgaImport;

public static class BgaLogCards
{
    /// <summary>Every card name a log mentions, with repeats, wherever the scraper records one.</summary>
    public static IEnumerable<string> NamesIn(BgaGameLog log)
    {
        foreach (var player in log.Players.Values)
        {
            if (player.Corporation is not null)
                yield return player.Corporation;
            foreach (var name in player.StartingHand?.Corporations ?? [])
                yield return name;
            foreach (var name in player.StartingHand?.Preludes ?? [])
                yield return name;
            foreach (var name in player.StartingHand?.ProjectCards ?? [])
                yield return name;
        }

        foreach (var move in log.Moves)
        {
            foreach (var name in NamesIn(move))
                yield return name;
            foreach (var name in ByPlayer(move.GameState?.PlayerHands))
                yield return name;
            foreach (var name in move.GameState?.PlayerVp?.Values.SelectMany(v => v.Details?.CardResources?.Keys ?? Enumerable.Empty<string>()) ?? [])
                yield return name;
        }
    }

    /// <summary>Card names in the move itself (not its snapshot), in the order the cards came off the deck.</summary>
    public static IEnumerable<string> NamesIn(BgaMove move)
    {
        foreach (var reveal in BgaMoveText.Reveals(move))
            yield return reveal.CardName;
        foreach (var name in ByPlayer(move.CardOptions))
            yield return name;
        foreach (var name in ByPlayer(move.CardsKept))
            yield return name;
        foreach (var name in move.CardsDiscarded ?? [])
            yield return name;
        if (move.CardPlayed is not null)
            yield return move.CardPlayed;
        if (move.CardDrafted is not null)
            yield return move.CardDrafted;
    }

    private static IEnumerable<string> ByPlayer(Dictionary<string, List<string>>? cards) =>
        cards?.Values.SelectMany(c => c) ?? [];
}
