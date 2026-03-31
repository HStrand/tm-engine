using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using tm_engine.Storage;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

public class LegalMovesFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;

    public LegalMovesFunction(IGameStore store, JsonSerializerSettings jsonSettings)
    {
        _store = store;
        _jsonSettings = jsonSettings;
    }

    [FunctionName("GetLegalMoves")]
    public async Task<IActionResult> GetLegalMoves(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "games/{id}/legal-moves")] HttpRequest req,
        string id)
    {
        var playerIdStr = req.Query["playerId"];
        if (string.IsNullOrEmpty(playerIdStr))
            return JsonResult(HttpStatusCode.BadRequest,
                new ErrorResponse("Query parameter 'playerId' is required."));

        var playerId = int.Parse(playerIdStr);

        GameState state;
        try
        {
            state = await _store.LoadStateAsync(id);
        }
        catch (InvalidOperationException)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' not found."));
        }

        var legalMoves = LegalMoveGenerator.GetLegalMoves(state, playerId);
        var cardNames = CardNameResolver.FromAvailableMoves(legalMoves);
        return JsonResult(HttpStatusCode.OK, new LegalMovesResponse(legalMoves, cardNames));
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
