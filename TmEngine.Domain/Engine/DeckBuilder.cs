using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Builds and shuffles card decks based on enabled expansions.
/// Uses a seeded Random for deterministic, replayable shuffling.
/// </summary>
public static class DeckBuilder
{
    /// <summary>
    /// Build the project card deck (automated + active + event cards) for the given expansions.
    /// Returns a shuffled deck using the provided random source.
    /// </summary>
    public static ImmutableList<string> BuildProjectDeck(ImmutableHashSet<Expansion> expansions, Random rng)
    {
        var cardIds = CardRegistry.GetProjectCardIds(expansions);
        return Shuffle(cardIds, rng);
    }

    /// <summary>
    /// Get corporation card IDs for the given expansions (not shuffled — dealt from a shuffled selection).
    /// </summary>
    public static ImmutableArray<string> GetCorporationIds(ImmutableHashSet<Expansion> expansions)
    {
        return CardRegistry.GetCorporationIds(expansions);
    }

    /// <summary>
    /// Get prelude card IDs (not shuffled — dealt from a shuffled selection).
    /// </summary>
    public static ImmutableArray<string> GetPreludeIds()
    {
        return CardRegistry.GetPreludeIds();
    }

    /// <summary>
    /// Get the set of enabled expansions based on game options.
    /// </summary>
    public static ImmutableHashSet<Expansion> GetEnabledExpansions(GameSetupOptions options)
    {
        var builder = ImmutableHashSet.CreateBuilder<Expansion>();
        builder.Add(Expansion.Base);

        if (options.CorporateEra)
            builder.Add(Expansion.CorporateEra);
        if (options.PreludeExpansion)
            builder.Add(Expansion.Prelude);

        // HellasElysium cards are always included if any map is used
        // (the expansion adds corporations/preludes, not map-specific project cards)
        builder.Add(Expansion.HellasElysium);

        return builder.ToImmutable();
    }

    /// <summary>
    /// Deal cards from the top of a deck. Returns the dealt cards and remaining deck.
    /// </summary>
    public static (ImmutableList<string> Dealt, ImmutableList<string> Remaining) Deal(
        ImmutableList<string> deck, int count)
    {
        var actualCount = Math.Min(count, deck.Count);
        var dealt = deck.GetRange(0, actualCount).ToImmutableList();
        var remaining = deck.RemoveRange(0, actualCount);
        return (dealt, remaining);
    }

    /// <summary>
    /// Pick N random items from a list using the provided random source.
    /// Returns the selected items and the remaining items.
    /// </summary>
    public static (ImmutableArray<string> Selected, ImmutableArray<string> Remaining) PickRandom(
        ImmutableArray<string> items, int count, Random rng)
    {
        var shuffled = Shuffle(items, rng);
        var actualCount = Math.Min(count, shuffled.Count);
        var selected = shuffled.GetRange(0, actualCount).ToImmutableArray();
        var remaining = shuffled.RemoveRange(0, actualCount).ToImmutableArray();
        return (selected, remaining);
    }

    /// <summary>
    /// Fisher-Yates shuffle using the provided random source for determinism.
    /// </summary>
    public static ImmutableList<string> Shuffle(ImmutableArray<string> items, Random rng)
    {
        var array = items.ToArray();
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        return [.. array];
    }

    private static ImmutableList<string> Shuffle(ImmutableList<string> items, Random rng)
    {
        var array = items.ToArray();
        for (int i = array.Length - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
        return [.. array];
    }
}
