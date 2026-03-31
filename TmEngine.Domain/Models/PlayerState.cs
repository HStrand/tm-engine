using System.Collections.Immutable;

namespace TmEngine.Domain.Models;

/// <summary>
/// Complete state of a single player at a point in the game.
/// </summary>
public sealed record PlayerState
{
    public required int PlayerId { get; init; }
    public required string CorporationId { get; init; }
    public required int TerraformRating { get; init; }
    public required ResourceSet Resources { get; init; }
    public required ProductionSet Production { get; init; }

    /// <summary>Card IDs in the player's hand.</summary>
    public required ImmutableList<string> Hand { get; init; }

    /// <summary>Card IDs the player has played (their tableau of green + blue cards).</summary>
    public required ImmutableList<string> PlayedCards { get; init; }

    /// <summary>Card IDs of played events (face-down pile, tags don't count after play).</summary>
    public required ImmutableList<string> PlayedEvents { get; init; }

    /// <summary>Resources stored on specific cards (e.g., animals, microbes). CardId -> count.</summary>
    public required ImmutableDictionary<string, int> CardResources { get; init; }

    /// <summary>Which card actions have been used this generation. CardId -> used.</summary>
    public required ImmutableHashSet<string> UsedCardActions { get; init; }

    /// <summary>Whether this player has passed for the current action phase.</summary>
    public required bool Passed { get; init; }

    /// <summary>Number of actions taken in the current turn (0, 1, or 2).</summary>
    public required int ActionsThisTurn { get; init; }

    /// <summary>Whether the player has increased their TR this generation (for UNMI action).</summary>
    public required bool IncreasedTRThisGeneration { get; init; }

    /// <summary>
    /// Creates a fresh player state for game start.
    /// </summary>
    public static PlayerState CreateInitial(int playerId, int startingTR) => new()
    {
        PlayerId = playerId,
        CorporationId = "",
        TerraformRating = startingTR,
        Resources = ResourceSet.Zero,
        Production = ProductionSet.Zero,
        Hand = [],
        PlayedCards = [],
        PlayedEvents = [],
        CardResources = ImmutableDictionary<string, int>.Empty,
        UsedCardActions = [],
        Passed = false,
        ActionsThisTurn = 0,
        IncreasedTRThisGeneration = false,
    };

    /// <summary>
    /// Count of tags currently in play (non-event played cards + corporation).
    /// Event tags don't count after being played.
    /// </summary>
    public int CountTag(Tag tag, Func<string, ImmutableArray<Tag>> getCardTags)
    {
        int count = 0;
        foreach (var cardId in PlayedCards)
        {
            var tags = getCardTags(cardId);
            count += tags.Count(t => t == tag);
        }
        // Corporation tags
        if (!string.IsNullOrEmpty(CorporationId))
        {
            var corpTags = getCardTags(CorporationId);
            count += corpTags.Count(t => t == tag);
        }
        return count;
    }
}
