using System;
using System.Diagnostics;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using tm_engine.Storage;
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

        var players = PlayerStatusBuilder.Build(state);

        _logger.LogInformation("GetPlayerStatus completed in {ElapsedMs}ms", sw.ElapsedMilliseconds);
        return JsonResult(HttpStatusCode.OK, new PlayerStatusResponse(players));
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
