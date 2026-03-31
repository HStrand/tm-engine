using System;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Newtonsoft.Json;
using tm_engine.Storage;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

public class HistoryFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;

    public HistoryFunction(IGameStore store, JsonSerializerSettings jsonSettings)
    {
        _store = store;
        _jsonSettings = jsonSettings;
    }

    [FunctionName("GetHistory")]
    public async Task<IActionResult> GetHistory(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "games/{id}/history")] HttpRequest req,
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

        return JsonResult(HttpStatusCode.OK, new HistoryResponse(state.Log));
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
