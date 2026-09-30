using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using tm_engine.Storage;
using TmEngine.BgaImport;

namespace tm_engine.Api;

/// <summary>
/// Creates games from positions in Board Game Arena replays, so a real game can be played on with different moves.
/// </summary>
public class ImportFunction
{
    private readonly IGameStore _store;
    private readonly IBgaLogSource _logs;
    private readonly RequestLimiter _limiter;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly ILogger<ImportFunction> _logger;

    // Per client and day. Looking at a replay costs one fetch; each "Play from here" costs one fetch and one import.
    private static readonly int FetchLimit = RequestLimiter.LimitSetting("RegistryFetchDailyLimit", 300);
    private static readonly int ImportLimit = RequestLimiter.LimitSetting("ImportDailyLimit", 60);

    public ImportFunction(IGameStore store, IBgaLogSource logs, RequestLimiter limiter, JsonSerializerSettings jsonSettings, ILogger<ImportFunction> logger)
    {
        _store = store;
        _logs = logs;
        _limiter = limiter;
        _jsonSettings = jsonSettings;
        _logger = logger;
    }

    /// <summary>Whether a scraped game can be played on from, and after which moves.</summary>
    [FunctionName("GetBgaForkPoints")]
    public async Task<IActionResult> GetForkPoints(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "import/bga/{tableId}/fork-points")] HttpRequest req,
        string tableId)
    {
        if (!_limiter.Allow(req, "fetch", FetchLimit))
            return TooManyRequests();

        var (log, error) = await FetchLog(tableId);
        if (log == null)
            return error!;

        var result = BgaImporter.FindForkPoints(log);
        return JsonResult(HttpStatusCode.OK, new ForkPointsResponse(result.Eligible, result.ForkPoints, result.Issues));
    }

    /// <summary>Create a game from a position in a scraped game, fetching the log by table id.</summary>
    [FunctionName("ImportBgaGame")]
    public async Task<IActionResult> ImportBgaGame(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "games/import/bga")] HttpRequest req)
    {
        if (!_limiter.Allow(req, "fetch", FetchLimit) || !_limiter.Allow(req, "import", ImportLimit))
            return TooManyRequests();

        var body = await new StreamReader(req.Body).ReadToEndAsync();
        ImportBgaRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<ImportBgaRequest>(body, _jsonSettings);
        }
        catch (JsonException ex)
        {
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse(ex.Message));
        }
        if (request == null || string.IsNullOrEmpty(request.TableId))
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("tableId and moveIndex are required."));

        var (log, error) = await FetchLog(request.TableId);
        if (log == null)
            return error!;

        return await ImportAndStore(log, request.MoveIndex, request.Seed, request.Snap ?? true);
    }

    /// <summary>
    /// Create a game from a position in a game log supplied in the request body.
    /// For tooling and local development, where the registry is not reachable.
    /// </summary>
    [FunctionName("ImportBgaLog")]
    public async Task<IActionResult> ImportBgaLog(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "games/import/bga-log")] HttpRequest req)
    {
        if (!int.TryParse(req.Query["moveIndex"], out var moveIndex))
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Query parameter 'moveIndex' is required."));
        int? seed = int.TryParse(req.Query["seed"], out var parsedSeed) ? parsedSeed : null;
        var snap = req.Query["snap"] != "false";

        BgaGameLog log;
        try
        {
            log = BgaGameLog.Parse(await new StreamReader(req.Body).ReadToEndAsync());
        }
        catch (System.Text.Json.JsonException ex)
        {
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse($"The body is not a game log: {ex.Message}"));
        }

        return await ImportAndStore(log, moveIndex, seed, snap);
    }

    /// <summary>Who sits where and which replay an imported game came from.</summary>
    [FunctionName("GetGameMeta")]
    public async Task<IActionResult> GetGameMeta(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{id}/meta")] HttpRequest req,
        string id)
    {
        var meta = await _store.LoadMetaAsync(id);
        return meta == null
            ? JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' has no import metadata."))
            : JsonResult(HttpStatusCode.OK, meta);
    }

    private async Task<IActionResult> ImportAndStore(BgaGameLog log, int moveIndex, int? seed, bool snap)
    {
        var sw = Stopwatch.StartNew();

        var options = new BgaImportOptions(moveIndex, seed ?? Random.Shared.Next());
        var result = snap ? BgaImporter.ImportAtOrBefore(log, options) : BgaImporter.Import(log, options);
        if (!result.Success)
            return JsonResult(HttpStatusCode.UnprocessableEntity, new ImportResponse(null, result.Fork, null, result.Issues));

        await _store.CreateGameAsync(result.State!);
        await _store.SaveMetaAsync(result.State!.GameId, result.Meta!);

        _logger.LogInformation("Imported BGA table {TableId} at move index {MoveIndex} as game {GameId} in {ElapsedMs}ms",
            result.Meta!.Source.TableId, result.Fork!.MoveIndex, result.State.GameId, sw.ElapsedMilliseconds);
        return JsonResult(HttpStatusCode.Created, new ImportResponse(result.State.GameId, result.Fork, result.Meta, result.Issues));
    }

    private async Task<(BgaGameLog? Log, IActionResult? Error)> FetchLog(string tableId)
    {
        try
        {
            var log = await _logs.FetchAsync(tableId);
            return log == null
                ? (null, JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"No scraped game found for table '{tableId}'.")))
                : (log, null);
        }
        catch (InvalidOperationException ex)
        {
            return (null, JsonResult(HttpStatusCode.ServiceUnavailable, new ErrorResponse(ex.Message)));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            _logger.LogWarning(ex, "Fetching the game log for table {TableId} failed", tableId);
            return (null, JsonResult(HttpStatusCode.BadGateway, new ErrorResponse("The game log could not be read from the registry.")));
        }
    }

    private ContentResult TooManyRequests() =>
        JsonResult(HttpStatusCode.TooManyRequests, new ErrorResponse("That is enough new games for today; try again tomorrow."));

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
