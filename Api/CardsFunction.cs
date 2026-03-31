using System;
using System.Collections.Immutable;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
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

    public CardsFunction(IGameStore store, JsonSerializerSettings jsonSettings)
    {
        _store = store;
        _jsonSettings = jsonSettings;
    }

    /// <summary>
    /// Returns card metadata for all cards referenced in a game.
    /// </summary>
    [FunctionName("GetGameCards")]
    public async Task<IActionResult> GetGameCards(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "games/{id}/cards")] HttpRequest req,
        string id)
    {
        GameState state;
        try
        {
            state = await _store.LoadStateAsync(id);
        }
        catch (InvalidOperationException)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' not found."));
        }

        var cards = CollectAllCardIds(state)
            .Where(cardId => CardRegistry.TryGet(cardId, out _))
            .Select(cardId =>
            {
                var def = CardRegistry.GetDefinition(cardId);
                return new CardInfo(
                    def.Id,
                    def.Name,
                    def.Type,
                    def.Cost,
                    def.Tags,
                    def.Requirements,
                    def.Description);
            })
            .ToImmutableDictionary(c => c.Id);

        return JsonResult(HttpStatusCode.OK, cards);
    }

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
