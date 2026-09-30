using System.Collections.Immutable;
using System.Linq;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

/// <summary>
/// Derives each player's score breakdown, tile counts and tag counts from a game state.
/// </summary>
public static class PlayerStatusBuilder
{
    public static ImmutableList<PlayerStatusDto> Build(GameState state)
    {
        var scores = Scoring.CalculateFinalScores(state);
        var players = ImmutableList.CreateBuilder<PlayerStatusDto>();

        foreach (var player in state.Players)
        {
            var score = scores.First(s => s.PlayerId == player.PlayerId);

            var corpName = "";
            if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corpEntry))
                corpName = corpEntry.Definition.Name;

            players.Add(new PlayerStatusDto(
                PlayerId: player.PlayerId,
                Corporation: corpName,
                Points: new PlayerPointsDto(
                    Total: score.TotalScore,
                    TR: score.TerraformRating,
                    VP: score.CardPoints,
                    Milestones: score.MilestonePoints,
                    Awards: score.AwardPoints,
                    Greeneries: score.GreeneryPoints,
                    Cities: score.CityPoints),
                Tiles: GetPlayerTiles(state, player.PlayerId),
                Resources: player.Resources,
                Production: player.Production,
                Tags: GetPlayerTags(player),
                SpendableMegaCredits: Wallet.Spendable(player)));
        }

        return players.ToImmutable();
    }

    private static PlayerTilesDto GetPlayerTiles(GameState state, int playerId)
    {
        int greeneries = 0, cities = 0, special = 0;

        foreach (var tile in state.PlacedTiles.Values)
        {
            if (tile.OwnerId != playerId) continue;

            switch (tile.Type)
            {
                case TileType.Greenery:
                    greeneries++;
                    break;
                case TileType.City:
                    cities++;
                    break;
                case TileType.Capital:
                    cities++;
                    break;
                default:
                    if (tile.Type != TileType.Ocean)
                        special++;
                    break;
            }
        }

        // Count off-map cities
        foreach (var tile in state.OffMapTiles)
        {
            if (tile.OwnerId != playerId) continue;
            if (tile.Type == TileType.City)
                cities++;
        }

        return new PlayerTilesDto(greeneries, cities, special);
    }

    private static PlayerTagsDto GetPlayerTags(PlayerState player)
    {
        int CountTag(Tag tag) => player.CountTag(tag, CardRegistry.GetTags);

        // Event tags: count from played events (they don't count via CountTag which excludes events)
        int eventCount = 0;
        foreach (var cardId in player.PlayedEvents)
        {
            var tags = CardRegistry.GetTags(cardId);
            eventCount += tags.Count(t => t == Tag.Event);
        }

        return new PlayerTagsDto(
            Building: CountTag(Tag.Building),
            Space: CountTag(Tag.Space),
            Power: CountTag(Tag.Power),
            Science: CountTag(Tag.Science),
            Jovian: CountTag(Tag.Jovian),
            Earth: CountTag(Tag.Earth),
            Plant: CountTag(Tag.Plant),
            Microbe: CountTag(Tag.Microbe),
            Animal: CountTag(Tag.Animal),
            City: CountTag(Tag.City),
            Event: eventCount,
            Wild: CountTag(Tag.Wild));
    }
}
