using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using tm_engine.Storage;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace tm_engine.Api;

public class MovesFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;

    public MovesFunction(IGameStore store, JsonSerializerSettings jsonSettings)
    {
        _store = store;
        _jsonSettings = jsonSettings;
    }

    [FunctionName("SubmitMove")]
    public async Task<IActionResult> SubmitMove(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "games/{id}/moves")] HttpRequest req,
        string id)
    {
        var playerIdStr = req.Query["playerId"];
        if (string.IsNullOrEmpty(playerIdStr))
            return JsonResult(HttpStatusCode.BadRequest,
                new ErrorResponse("Query parameter 'playerId' is required."));

        var playerId = int.Parse(playerIdStr);

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        if (string.IsNullOrEmpty(body))
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Request body is required."));

        var move = JsonConvert.DeserializeObject<Move>(body, _jsonSettings);
        if (move == null)
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Invalid move."));

        if (move.PlayerId != playerId)
            return JsonResult(HttpStatusCode.BadRequest,
                new ErrorResponse("Move playerId does not match query parameter."));

        GameState state;
        try
        {
            state = await _store.LoadStateAsync(id);
        }
        catch (InvalidOperationException)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' not found."));
        }

        var (newState, result) = GameEngine.Apply(state, move);

        if (result is Error err)
            return JsonResult(HttpStatusCode.BadRequest, new SubmitMoveResponse(false, err.Message, null));

        try
        {
            await _store.SaveStateAsync(id, newState);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return JsonResult(HttpStatusCode.Conflict,
                new ErrorResponse("Game state was modified concurrently. Please reload and retry."));
        }

        var filtered = GameStateView.FilterForPlayer(newState, playerId);
        return JsonResult(HttpStatusCode.OK, new SubmitMoveResponse(true, null, filtered));
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
