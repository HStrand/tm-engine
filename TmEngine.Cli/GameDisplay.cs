namespace TmEngine.Cli;

public class GameDisplay
{
    private int _lastLogCount;

    public void ShowGameState(GameStateDto state, Dictionary<string, string> cardNames, int humanPlayerId)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"=== Generation {state.Generation} | {state.Phase} | " +
            $"Player {state.ActivePlayerIndex}'s turn ===");
        Console.ResetColor();

        // Global parameters
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine($"O2: {state.Oxygen}%  Temp: {state.Temperature}°C  Oceans: {state.OceansPlaced}/9");
        Console.ResetColor();
        Console.WriteLine();

        // Players
        for (int i = 0; i < state.Players.Count; i++)
        {
            var p = state.Players[i];
            var corpName = cardNames.GetValueOrDefault(p.CorporationId, p.CorporationId);
            var label = p.PlayerId == humanPlayerId ? "" : " [BOT]";

            Console.ForegroundColor = p.PlayerId == humanPlayerId ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"Player {p.PlayerId} ({corpName}) TR:{p.TerraformRating}{label}");
            Console.ResetColor();

            Console.WriteLine($"  MC: {p.Resources.MegaCredits}/{Prod(p.Production.MegaCredits)}  " +
                $"Steel: {p.Resources.Steel}/{Prod(p.Production.Steel)}  " +
                $"Ti: {p.Resources.Titanium}/{Prod(p.Production.Titanium)}  " +
                $"Plants: {p.Resources.Plants}/{Prod(p.Production.Plants)}  " +
                $"Energy: {p.Resources.Energy}/{Prod(p.Production.Energy)}  " +
                $"Heat: {p.Resources.Heat}/{Prod(p.Production.Heat)}");

            if (p.PlayerId == humanPlayerId && p.Hand.Count > 0)
            {
                var names = p.Hand.Select(id => cardNames.GetValueOrDefault(id, id));
                Console.WriteLine($"  Hand ({p.Hand.Count}): {string.Join(", ", names)}");
            }
            else
            {
                Console.WriteLine($"  Hand: {p.Hand.Count} cards");
            }

            if (p.PlayedCards.Count > 0 || p.PlayedEvents.Count > 0)
            {
                var all = p.PlayedCards.Concat(p.PlayedEvents)
                    .Select(id => cardNames.GetValueOrDefault(id, id));
                Console.WriteLine($"  Played ({p.PlayedCards.Count + p.PlayedEvents.Count}): {string.Join(", ", all)}");
            }

            if (p.CardResources.Count > 0)
            {
                var res = p.CardResources.Select(kv =>
                    $"{cardNames.GetValueOrDefault(kv.Key, kv.Key)}: {kv.Value}");
                Console.WriteLine($"  Card resources: {string.Join(", ", res)}");
            }

            Console.WriteLine();
        }

        // Board
        if (state.PlacedTiles.Count > 0 || state.OffMapTiles.Count > 0)
        {
            Console.ForegroundColor = ConsoleColor.DarkYellow;
            Console.Write("Board: ");
            Console.ResetColor();
            var tiles = state.PlacedTiles.Values
                .Select(t => $"{t.Location} {t.Type}" + (t.OwnerId.HasValue ? $" [P{t.OwnerId}]" : ""));
            var offMap = state.OffMapTiles
                .Select(t => $"{t.Name} {t.TileType}" + (t.OwnerId.HasValue ? $" [P{t.OwnerId}]" : ""));
            Console.WriteLine(string.Join("  ", tiles.Concat(offMap)));
        }

        // Milestones & Awards
        if (state.ClaimedMilestones.Count > 0 || state.FundedAwards.Count > 0)
        {
            var ms = state.ClaimedMilestones.Select(m => $"{m.MilestoneName} (P{m.PlayerId})");
            var aw = state.FundedAwards.Select(a => $"{a.AwardName} (P{a.PlayerId})");
            Console.WriteLine($"Milestones: {(ms.Any() ? string.Join(", ", ms) : "none")} | " +
                $"Awards: {(aw.Any() ? string.Join(", ", aw) : "none")}");
        }
    }

    public void ShowNewLogEntries(List<string> log)
    {
        if (log.Count <= _lastLogCount) return;

        Console.ForegroundColor = ConsoleColor.DarkGray;
        for (int i = _lastLogCount; i < log.Count; i++)
            Console.WriteLine($"  {log[i]}");
        Console.ResetColor();
        _lastLogCount = log.Count;
    }

    public void ShowFinalScores(GameStateDto state, Dictionary<string, string> cardNames)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine("========== GAME OVER ==========");
        Console.ResetColor();
        Console.WriteLine();

        foreach (var p in state.Players.OrderByDescending(p => p.TerraformRating))
        {
            var corpName = cardNames.GetValueOrDefault(p.CorporationId, p.CorporationId);
            Console.WriteLine($"Player {p.PlayerId} ({corpName}): TR {p.TerraformRating}");
        }

        Console.WriteLine();
        Console.WriteLine("(Full scoring with milestones/awards/greeneries/cities/cards");
        Console.WriteLine(" is calculated server-side at game end)");
    }

    private static string Prod(int value) => value >= 0 ? $"+{value}" : $"{value}";
}
