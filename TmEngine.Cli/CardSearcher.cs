using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.Cli;

/// <summary>
/// Searches for a random seed where a specific card appears in player 0's initial hand.
/// Replicates the exact RNG call order from GameEngine.Setup to ensure consistency.
/// </summary>
public static class CardSearcher
{
    /// <summary>
    /// Resolves a user-entered card name to a card ID. Supports partial, case-insensitive matching.
    /// Returns null if the user cancels.
    /// </summary>
    public static string? ResolveCardName(string input)
    {
        var matches = CardRegistry.All
            .Where(kvp => kvp.Value.Definition.Name.Contains(input, StringComparison.OrdinalIgnoreCase))
            .OrderBy(kvp => kvp.Value.Definition.Name)
            .ToList();

        if (matches.Count == 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"No cards found matching \"{input}\".");
            Console.ResetColor();
            return null;
        }

        if (matches.Count == 1)
        {
            var card = matches[0].Value.Definition;
            Console.WriteLine($"  Found: {card.Name} ({card.Type})");
            return matches[0].Key;
        }

        // Multiple matches — let user pick
        Console.WriteLine($"  Multiple matches for \"{input}\":");
        for (int i = 0; i < matches.Count; i++)
        {
            var card = matches[i].Value.Definition;
            Console.WriteLine($"  {i + 1}. {card.Name} ({card.Type})");
        }

        Console.Write("  Pick a number (or 0 to cancel): ");
        var choice = Console.ReadLine()?.Trim();
        if (int.TryParse(choice, out int idx) && idx >= 1 && idx <= matches.Count)
            return matches[idx - 1].Key;

        return null;
    }

    /// <summary>
    /// Searches random seeds until the given card ID appears in player 0's dealt hand.
    /// Must replicate the exact RNG call order from GameEngine.Setup (lines 99-120).
    /// Returns the found seed.
    /// </summary>
    public static int SearchForCard(string cardId, bool corporateEra, bool preludeExpansion)
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, corporateEra, true, preludeExpansion);
        var expansions = DeckBuilder.GetEnabledExpansions(options);
        var allCorps = DeckBuilder.GetCorporationIds(expansions);
        var allPreludes = preludeExpansion ? DeckBuilder.GetPreludeIds() : ImmutableArray<string>.Empty;

        const int maxAttempts = 1_000_000;
        var seedRng = new Random();

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            int candidateSeed = seedRng.Next();

            if (IsCardDealtToPlayer0(candidateSeed, expansions, allCorps, allPreludes, preludeExpansion, cardId))
            {
                Console.WriteLine();
                return candidateSeed;
            }

            if (attempt % 10_000 == 0)
                Console.Write($"\r  Searched {attempt:N0} seeds...");
        }

        throw new InvalidOperationException($"Card not found after {maxAttempts:N0} seeds. This should not happen.");
    }

    private static bool IsCardDealtToPlayer0(
        int seed,
        ImmutableHashSet<Expansion> expansions,
        ImmutableArray<string> allCorps,
        ImmutableArray<string> allPreludes,
        bool preludeExpansion,
        string targetCardId)
    {
        // Must match GameEngine.Setup RNG call order exactly:
        // 1. BuildProjectDeck (shuffles project cards)
        // 2. Shuffle corporations
        // 3. Shuffle preludes
        var rng = new Random(seed);
        var projectDeck = DeckBuilder.BuildProjectDeck(expansions, rng);
        var shuffledCorps = DeckBuilder.Shuffle(allCorps, rng);
        var shuffledPreludes = preludeExpansion
            ? DeckBuilder.Shuffle(allPreludes, rng)
            : ImmutableList<string>.Empty;

        // Player 0 is dealt first from each deck
        var player0Corps = shuffledCorps.Take(Constants.CorporationsDealt);
        var player0Preludes = shuffledPreludes.Take(Constants.PreludesDealt);
        var player0Projects = projectDeck.Take(Constants.InitialCardsDealt);

        return player0Corps.Contains(targetCardId)
            || player0Preludes.Contains(targetCardId)
            || player0Projects.Contains(targetCardId);
    }
}
