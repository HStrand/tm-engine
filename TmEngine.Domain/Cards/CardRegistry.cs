using System.Collections.Immutable;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Cards;

/// <summary>
/// Full card entry including definition, play effects, ongoing effects, and actions.
/// </summary>
public sealed record CardEntry
{
    public required CardDefinition Definition { get; init; }

    /// <summary>Effects applied when the card is played (immediate effects).</summary>
    public ImmutableArray<Effect> OnPlayEffects { get; init; } = [];

    /// <summary>Ongoing/triggered effects (for blue cards and corporations).</summary>
    public ImmutableArray<Effect> OngoingEffects { get; init; } = [];

    /// <summary>Card action (for blue cards — usable once per generation).</summary>
    public CardAction? Action { get; init; }
}

/// <summary>
/// Central registry of all card definitions and their effects.
/// Maps card ID to CardEntry. Cards are registered by expansion.
/// </summary>
public static class CardRegistry
{
    private static readonly Lazy<ImmutableDictionary<string, CardEntry>> _cards =
        new(BuildRegistry);

    public static ImmutableDictionary<string, CardEntry> All => _cards.Value;

    public static CardEntry Get(string cardId) =>
        All.TryGetValue(cardId, out var entry)
            ? entry
            : throw new ArgumentException($"Unknown card ID: {cardId}");

    public static bool TryGet(string cardId, out CardEntry entry) =>
        All.TryGetValue(cardId, out entry!);

    public static CardDefinition GetDefinition(string cardId) => Get(cardId).Definition;

    public static ImmutableArray<Tag> GetTags(string cardId) =>
        TryGet(cardId, out var entry) ? entry.Definition.Tags : [];

    /// <summary>
    /// Get all card IDs for a given expansion set.
    /// </summary>
    public static ImmutableArray<string> GetCardIdsByExpansion(Expansion expansion) =>
        [.. All.Values.Where(e => e.Definition.Expansion == expansion).Select(e => e.Definition.Id)];

    /// <summary>
    /// Get all project card IDs (not corporations or preludes) for the given expansions.
    /// </summary>
    public static ImmutableArray<string> GetProjectCardIds(ImmutableHashSet<Expansion> expansions) =>
        [.. All.Values
            .Where(e => expansions.Contains(e.Definition.Expansion))
            .Where(e => e.Definition.Type is CardType.Automated or CardType.Active or CardType.Event)
            .Select(e => e.Definition.Id)];

    /// <summary>
    /// Get all corporation card IDs for the given expansions.
    /// </summary>
    public static ImmutableArray<string> GetCorporationIds(ImmutableHashSet<Expansion> expansions) =>
        [.. All.Values
            .Where(e => expansions.Contains(e.Definition.Expansion))
            .Where(e => e.Definition.Type == CardType.Corporation)
            .Select(e => e.Definition.Id)];

    /// <summary>
    /// Get all prelude card IDs.
    /// </summary>
    public static ImmutableArray<string> GetPreludeIds() =>
        [.. All.Values
            .Where(e => e.Definition.Type == CardType.Prelude)
            .Select(e => e.Definition.Id)];

    private static ImmutableDictionary<string, CardEntry> BuildRegistry()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, CardEntry>();

        // Cards will be registered in Phase 7 batches
        // RegisterBaseCards(builder);
        // RegisterCorporateEraCards(builder);
        // RegisterCorporations(builder);
        // RegisterPreludes(builder);

        return builder.ToImmutable();
    }
}
