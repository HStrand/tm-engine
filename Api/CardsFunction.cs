using System;
using System.Collections.Generic;
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
using TmEngine.Domain.Models;

namespace tm_engine.Api;

/// <summary>
/// Card metadata for a specific card, suitable for API responses.
/// </summary>
public sealed record CardInfo(
    string Id,
    string Name,
    CardType Type,
    int Cost,
    ImmutableArray<Tag> Tags,
    ImmutableArray<CardRequirement> Requirements,
    string Description);

public class CardsFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly ILogger<CardsFunction> _logger;

    public CardsFunction(IGameStore store, JsonSerializerSettings jsonSettings, ILogger<CardsFunction> logger)
    {
        _store = store;
        _jsonSettings = jsonSettings;
        _logger = logger;
    }

    /// <summary>
    /// Returns card metadata. If <c>gameId</c> is provided as a query parameter, only cards
    /// referenced by that game are returned; otherwise the full card catalog is returned.
    /// </summary>
    [FunctionName("GetCards")]
    public async Task<IActionResult> GetCards(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "cards")] HttpRequest req)
    {
        var sw = Stopwatch.StartNew();

        var gameId = req.Query.TryGetValue("gameId", out var values) ? values.ToString() : null;

        IEnumerable<string> cardIds;
        if (!string.IsNullOrWhiteSpace(gameId))
        {
            GameState state;
            try
            {
                state = await _store.LoadStateAsync(gameId);
            }
            catch (InvalidOperationException)
            {
                return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{gameId}' not found."));
            }

            cardIds = CollectAllCardIds(state);
        }
        else
        {
            cardIds = CardRegistry.All.Keys;
        }

        var cards = cardIds
            .Where(cardId => CardRegistry.TryGet(cardId, out _))
            .Select(cardId => ToCardInfo(CardRegistry.GetDefinition(cardId)))
            .ToImmutableDictionary(c => c.Id);

        _logger.LogInformation(
            "GetCards completed in {ElapsedMs}ms (gameId={GameId}, count={Count})",
            sw.ElapsedMilliseconds, gameId ?? "<all>", cards.Count);
        return JsonResult(HttpStatusCode.OK, cards);
    }

    private static CardInfo ToCardInfo(CardDefinition def) => new(
        def.Id,
        def.Name,
        def.Type,
        def.Cost,
        def.Tags,
        def.Requirements,
        def.Description);

    private static ImmutableHashSet<string> CollectAllCardIds(GameState state)
    {
        var ids = ImmutableHashSet.CreateBuilder<string>();

        foreach (var player in state.Players)
        {
            ids.UnionWith(player.Hand);
            ids.UnionWith(player.PlayedCards);
            ids.UnionWith(player.PlayedEvents);
            ids.UnionWith(player.CardResources.Keys);
            if (!string.IsNullOrEmpty(player.CorporationId))
                ids.Add(player.CorporationId);
        }

        if (state.Setup != null)
        {
            foreach (var list in state.Setup.DealtCorporations) ids.UnionWith(list);
            foreach (var list in state.Setup.DealtPreludes) ids.UnionWith(list);
            foreach (var list in state.Setup.DealtCards) ids.UnionWith(list);
        }

        if (state.Prelude != null)
            foreach (var list in state.Prelude.RemainingPreludes) ids.UnionWith(list);

        if (state.Research != null)
            foreach (var list in state.Research.AvailableCards) ids.UnionWith(list);

        if (state.Draft != null)
        {
            foreach (var list in state.Draft.DraftHands) ids.UnionWith(list);
            foreach (var list in state.Draft.DraftedCards) ids.UnionWith(list);
        }

        // Include draw pile and discard pile for full game card reference
        ids.UnionWith(state.DrawPile);
        ids.UnionWith(state.DiscardPile);
        ids.UnionWith(state.PreludeDeck);

        return ids.ToImmutable();
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
