using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using tm_engine.Storage;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

public class GamesFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly ILogger<GamesFunction> _logger;

    public GamesFunction(IGameStore store, JsonSerializerSettings jsonSettings, ILogger<GamesFunction> logger)
    {
        _store = store;
        _jsonSettings = jsonSettings;
        _logger = logger;
    }

    [FunctionName("CreateGame")]
    public async Task<IActionResult> CreateGame(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "games")] HttpRequest req)
    {
        var sw = Stopwatch.StartNew();

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        if (string.IsNullOrEmpty(body))
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Request body is required."));

        var request = JsonConvert.DeserializeObject<CreateGameRequest>(body, _jsonSettings);
        if (request == null)
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Invalid request body."));

        var seed = request.Seed ?? Random.Shared.Next();
        var options = new GameSetupOptions(
            request.PlayerCount,
            request.Map,
            request.CorporateEra,
            request.DraftVariant,
            request.PreludeExpansion);

        var state = GameEngine.Setup(options, seed);
        await _store.CreateGameAsync(state);

        _logger.LogInformation("CreateGame completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return JsonResult(HttpStatusCode.Created, new CreateGameResponse(state.GameId));
    }

    [FunctionName("GetGame")]
    public async Task<IActionResult> GetGame(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "games/{id}")] HttpRequest req,
        string id)
    {
        var sw = Stopwatch.StartNew();

        var playerIdStr = req.Query["playerId"];
        int? playerId = !string.IsNullOrEmpty(playerIdStr) ? int.Parse(playerIdStr) : null;

        GameState state;
        try
        {
            state = await _store.LoadStateAsync(id);
        }
        catch (InvalidOperationException)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' not found."));
        }

        var filtered = GameStateView.FilterForPlayer(state, playerId);
        var cardNames = CardNameResolver.FromGameState(filtered);
        _logger.LogInformation("GetGame completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return JsonResult(HttpStatusCode.OK, new GameStateResponse(filtered, cardNames));
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
