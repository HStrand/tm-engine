using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using tm_engine.Storage;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

public class PlayerStatusFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly ILogger<PlayerStatusFunction> _logger;

    public PlayerStatusFunction(IGameStore store, JsonSerializerSettings jsonSettings, ILogger<PlayerStatusFunction> logger)
    {
        _store = store;
        _jsonSettings = jsonSettings;
        _logger = logger;
    }

    [FunctionName("GetPlayerStatus")]
    public async Task<IActionResult> GetPlayerStatus(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "games/{id}/status")] HttpRequest req,
        string id)
    {
        var sw = Stopwatch.StartNew();

        GameState state;
        try
        {
            state = await _store.LoadStateAsync(id);
        }
        catch (InvalidOperationException)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' not found."));
        }

        var scores = Scoring.CalculateFinalScores(state);
        var players = ImmutableList.CreateBuilder<PlayerStatusDto>();

        foreach (var player in state.Players)
        {
            var score = scores.First(s => s.PlayerId == player.PlayerId);

            var corpName = "";
            if (!string.IsNullOrEmpty(player.CorporationId) && CardRegistry.TryGet(player.CorporationId, out var corpEntry))
                corpName = corpEntry.Definition.Name;

            var tiles = GetPlayerTiles(state, player.PlayerId);
            var tags = GetPlayerTags(player);

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
                Tiles: tiles,
                Resources: player.Resources,
                Production: player.Production,
                Tags: tags));
        }

        _logger.LogInformation("GetPlayerStatus completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return JsonResult(HttpStatusCode.OK, new PlayerStatusResponse(players.ToImmutable()));
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

    private ContentResult JsonResult(HttpStatusCode status, object body)
    {
        return new ContentResult
        {
            StatusCode = (int)status,
            Content = JsonConvert.SerializeObject(body, _jsonSettings),
            ContentType = "application/json",
        };
    }
}
