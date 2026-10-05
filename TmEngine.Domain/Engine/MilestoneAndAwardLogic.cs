using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Checks milestone eligibility and scores awards for every map.
/// </summary>
public static class MilestoneAndAwardLogic
{
    // ═══════════════════════════════════════════════════════════
    //  MILESTONE ELIGIBILITY
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Check if a player meets the threshold for the named milestone.
    /// Returns null if eligible, or an error message.
    /// </summary>
    public static string? CheckMilestoneEligibility(GameState state, int playerId, string milestoneName)
    {
        var player = state.GetPlayer(playerId);
        var score = GetMilestoneScore(state, player, milestoneName);
        var threshold = GetMilestoneThreshold(milestoneName);

        if (score < threshold)
            return $"Need {threshold} for {milestoneName}, have {score}.";

        return null;
    }

    /// <summary>
    /// Get a player's current score for a milestone metric.
    /// </summary>
    public static int GetMilestoneScore(GameState state, PlayerState player, string milestoneName)
    {
        return milestoneName switch
        {
            // Tharsis
            "Terraformer" => player.TerraformRating,
            "Mayor" => CountOwnedTilesOfType(state, player.PlayerId, TileType.City)
                      + CountOwnedTilesOfType(state, player.PlayerId, TileType.Capital),
            "Gardener" => CountOwnedTilesOfType(state, player.PlayerId, TileType.Greenery),
            "Builder" => CountPlayerTagsWithWild(player, Tag.Building),
            "Planner" => player.Hand.Count,

            // Hellas
            "Diversifier" => CountDistinctTagTypes(player),
            "Tactician" => CountCardsWithRequirements(player),
            "Polar Explorer" => CountOwnedTilesInBottomRows(state, player.PlayerId),
            "Energizer" => player.Production.Energy,
            "Rim Settler" => CountPlayerTagsWithWild(player, Tag.Jovian),

            // Elysium
            "Generalist" => CountProductionTypesAtLeast1(player),
            "Specialist" => GetHighestSingleProduction(player),
            "Ecologist" => CountBioTags(player),
            "Tycoon" => player.PlayedCards.Count, // blue + green cards (not events)
            "Legend" => player.PlayedEvents.Count,

            // Vastitas Borealis
            "Agronomist" => CountPlayerTagsWithWild(player, Tag.Plant),
            "Engineer" => player.Production.Energy + player.Production.Heat,
            "Spacefarer" => CountPlayerTagsWithWild(player, Tag.Space),
            "Geologist" => CountOwnedTilesOnOrNextToVolcanoes(state, player.PlayerId),
            "Farmer" => CountAnimalAndMicrobeResources(player),

            _ => 0,
        };
    }

    /// <summary>
    /// Get the threshold needed to claim a milestone.
    /// </summary>
    public static int GetMilestoneThreshold(string milestoneName)
    {
        return milestoneName switch
        {
            "Terraformer" => 35,
            "Mayor" => 3,
            "Gardener" => 3,
            "Builder" => 8,
            "Planner" => 16,
            "Diversifier" => 8,
            "Tactician" => 5,
            "Polar Explorer" => 3,
            "Energizer" => 6,
            "Rim Settler" => 3,
            "Generalist" => 6,
            "Specialist" => 10,
            "Ecologist" => 4,
            "Tycoon" => 15,
            "Legend" => 5,
            "Agronomist" => 4,
            "Engineer" => 10,
            "Spacefarer" => 4,
            "Geologist" => 3,
            "Farmer" => 5,
            _ => int.MaxValue,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  AWARD SCORING
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// Get a player's score for a funded award metric.
    /// </summary>
    public static int GetAwardScore(GameState state, PlayerState player, string awardName)
    {
        return awardName switch
        {
            // Tharsis
            "Landlord" => CountOwnedTiles(state, player.PlayerId),
            "Banker" => player.Production.MegaCredits,
            "Scientist" => CountPlayerTags(player, Tag.Science),
            "Thermalist" => player.Resources.Heat,
            "Miner" => player.Resources.Steel + player.Resources.Titanium,

            // Hellas
            "Cultivator" => CountOwnedTilesOfType(state, player.PlayerId, TileType.Greenery),
            "Magnate" => CountAutomatedCards(player),
            "Space Baron" => CountPlayerTags(player, Tag.Space), // event tags don't count
            "Excentric" => CountTotalCardResources(player),
            "Contractor" => CountPlayerTags(player, Tag.Building), // event tags don't count

            // Elysium
            "Celebrity" => CountExpensiveCards(player, 20),
            "Industrialist" => player.Resources.Steel + player.Resources.Energy,
            "Desert Settler" => CountOwnedTilesInBottomRows(state, player.PlayerId),
            "Estate Dealer" => CountTilesAdjacentToOcean(state, player.PlayerId),
            "Benefactor" => player.TerraformRating,

            // Vastitas Borealis
            "Traveller" => CountPlayerTags(player, Tag.Jovian) + CountPlayerTags(player, Tag.Earth),
            "Landscaper" => LargestGroupOfOwnedTiles(state, player.PlayerId),
            "Highlander" => CountOwnedTiles(state, player.PlayerId, onBoardOnly: true)
                            - CountTilesAdjacentToOcean(state, player.PlayerId),
            "Promoter" => player.PlayedEvents.Count,
            "Blacksmith" => player.Production.Steel + player.Production.Titanium,

            _ => 0,
        };
    }

    // ═══════════════════════════════════════════════════════════
    //  SCORING HELPERS
    // ═══════════════════════════════════════════════════════════

    private static int CountPlayerTags(PlayerState player, Tag tag) =>
        player.CountTag(tag, CardRegistry.GetTags);

    /// <summary>
    /// Tags for a milestone: a wild tag counts as whichever tag is being counted. Awards do not get this;
    /// they use <see cref="CountPlayerTags"/>.
    /// </summary>
    private static int CountPlayerTagsWithWild(PlayerState player, Tag tag) =>
        CountPlayerTags(player, tag) + CountPlayerTags(player, Tag.Wild);

    private static int CountOwnedTiles(GameState state, int playerId, bool onBoardOnly = false) =>
        state.PlacedTiles.Values.Count(t => t.OwnerId == playerId)
        + (onBoardOnly ? 0 : state.OffMapTiles.Count(t => t.OwnerId == playerId));

    /// <summary>Geologist: tiles on a volcanic area or next to one.</summary>
    private static int CountOwnedTilesOnOrNextToVolcanoes(GameState state, int playerId)
    {
        var volcanic = new HashSet<HexCoord>();
        foreach (var volcano in MapDefinitions.GetMap(state.Map).VolcanicAreas)
        {
            volcanic.Add(volcano);
            foreach (var adjacent in volcano.GetAdjacentCoords())
                volcanic.Add(adjacent);
        }

        return state.PlacedTiles.Values.Count(t => t.OwnerId == playerId && volcanic.Contains(t.Location));
    }

    /// <summary>Farmer: animals and microbes on the player's cards.</summary>
    private static int CountAnimalAndMicrobeResources(PlayerState player) =>
        player.CardResources
            .Where(kv => EffectExecutor.CardHasResourceType(kv.Key, CardResourceType.Animal)
                         || EffectExecutor.CardHasResourceType(kv.Key, CardResourceType.Microbe))
            .Sum(kv => kv.Value);

    /// <summary>Landscaper: the largest group of the player's tiles that are connected to each other.</summary>
    private static int LargestGroupOfOwnedTiles(GameState state, int playerId)
    {
        var owned = state.PlacedTiles.Values.Where(t => t.OwnerId == playerId).Select(t => t.Location).ToHashSet();
        var seen = new HashSet<HexCoord>();
        var largest = 0;

        foreach (var start in owned)
        {
            if (!seen.Add(start))
                continue;

            var size = 0;
            var open = new Stack<HexCoord>([start]);
            while (open.Count > 0)
            {
                var hex = open.Pop();
                size++;
                foreach (var adjacent in hex.GetAdjacentCoords())
                {
                    if (owned.Contains(adjacent) && seen.Add(adjacent))
                        open.Push(adjacent);
                }
            }

            largest = Math.Max(largest, size);
        }

        return largest;
    }

    private static int CountOwnedTilesOfType(GameState state, int playerId, TileType type) =>
        state.PlacedTiles.Values.Count(t => t.OwnerId == playerId && t.Type == type)
        + state.OffMapTiles.Count(t => t.OwnerId == playerId && t.Type == type);

    private static int CountOwnedTilesInBottomRows(GameState state, int playerId)
    {
        // Bottom two rows (rows 8 and 9) for all maps
        return state.PlacedTiles.Values.Count(t =>
            t.OwnerId == playerId && (t.Location.Row == 8 || t.Location.Row == 9));
    }

    private static int CountDistinctTagTypes(PlayerState player)
    {
        var tagTypes = new HashSet<Tag>();
        int wildCount = 0;

        foreach (var cardId in player.PlayedCards)
        {
            var tags = CardRegistry.GetTags(cardId);
            foreach (var tag in tags)
            {
                if (tag == Tag.Wild)
                    wildCount++;
                else if (tag != Tag.Event) // Event tags don't count
                    tagTypes.Add(tag);
            }
        }

        // Corporation tags
        if (!string.IsNullOrEmpty(player.CorporationId))
        {
            foreach (var tag in CardRegistry.GetTags(player.CorporationId))
            {
                if (tag == Tag.Wild) wildCount++;
                else if (tag != Tag.Event) tagTypes.Add(tag);
            }
        }

        // Wild tags can stand in for missing tag types (up to the total possible types)
        return Math.Min(tagTypes.Count + wildCount, 10); // 10 non-event, non-wild tag types
    }

    private static int CountCardsWithRequirements(PlayerState player)
    {
        int count = 0;
        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var entry) && entry.Definition.HasRequirements)
                count++;
        }
        return count;
    }

    private static int CountProductionTypesAtLeast1(PlayerState player)
    {
        int count = 0;
        if (player.Production.MegaCredits >= 1) count++;
        if (player.Production.Steel >= 1) count++;
        if (player.Production.Titanium >= 1) count++;
        if (player.Production.Plants >= 1) count++;
        if (player.Production.Energy >= 1) count++;
        if (player.Production.Heat >= 1) count++;
        return count;
    }

    private static int GetHighestSingleProduction(PlayerState player)
    {
        return Math.Max(player.Production.MegaCredits,
            Math.Max(player.Production.Steel,
            Math.Max(player.Production.Titanium,
            Math.Max(player.Production.Plants,
            Math.Max(player.Production.Energy,
                     player.Production.Heat)))));
    }

    private static int CountBioTags(PlayerState player)
    {
        int count = 0;
        count += CountPlayerTags(player, Tag.Plant);
        count += CountPlayerTags(player, Tag.Microbe);
        count += CountPlayerTags(player, Tag.Animal);
        count += CountPlayerTags(player, Tag.Wild); // Wild tags count as bio tags
        return count;
    }

    private static int CountAutomatedCards(PlayerState player)
    {
        int count = 0;
        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var entry) && entry.Definition.Type == CardType.Automated)
                count++;
        }
        return count;
    }

    private static int CountTotalCardResources(PlayerState player) =>
        player.CardResources.Values.Sum();

    private static int CountExpensiveCards(PlayerState player, int minCost)
    {
        int count = 0;
        foreach (var cardId in player.PlayedCards)
        {
            if (CardRegistry.TryGet(cardId, out var entry) && entry.Definition.Cost >= minCost)
                count++;
        }
        return count;
    }

    private static int CountTilesAdjacentToOcean(GameState state, int playerId)
    {
        int count = 0;
        foreach (var (coord, tile) in state.PlacedTiles)
        {
            if (tile.OwnerId != playerId) continue;
            if (BoardLogic.CountAdjacentOceans(state, coord) > 0)
                count++;
        }
        return count;
    }
}
