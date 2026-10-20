using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure;
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
using TmEngine.Domain.Moves;

namespace tm_engine.Api;

/// <summary>
/// Hot-seat play: one person controls every seat, so all hands are shown and each response carries what is
/// needed to render the table and offer the next move in a single round trip.
/// </summary>
public class TableFunction
{
    private readonly IGameStore _store;
    private readonly JsonSerializerSettings _jsonSettings;
    private readonly ILogger<TableFunction> _logger;

    public TableFunction(IGameStore store, JsonSerializerSettings jsonSettings, ILogger<TableFunction> logger)
    {
        _store = store;
        _jsonSettings = jsonSettings;
        _logger = logger;
    }

    /// <summary>The table as it stands, or with at=N as it stood after move N.</summary>
    [FunctionName("GetTable")]
    public async Task<IActionResult> GetTable(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{id}/table")] HttpRequest req,
        string id)
    {
        try
        {
            var latest = await _store.LoadStateAsync(id);
            var state = int.TryParse(req.Query["at"], out var at) && at != latest.MoveNumber
                ? await _store.LoadStateAtAsync(id, at)
                : latest;

            return JsonResult(HttpStatusCode.OK, BuildTable(state, latest.MoveNumber, await _store.LoadMetaAsync(id)));
        }
        catch (InvalidOperationException ex)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse(ex.Message));
        }
    }

    /// <summary>Apply a move for whichever seat it names and return the resulting table.</summary>
    [FunctionName("SubmitTableMove")]
    public async Task<IActionResult> SubmitMove(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "games/{id}/table/moves")] HttpRequest req,
        string id)
    {
        Move? move;
        try
        {
            move = JsonConvert.DeserializeObject<Move>(await new StreamReader(req.Body).ReadToEndAsync(), _jsonSettings);
        }
        catch (JsonException ex)
        {
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse(ex.Message));
        }
        if (move == null)
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Invalid move."));

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
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse(err.Message));

        try
        {
            await _store.SaveStateAsync(id, newState);
        }
        catch (RequestFailedException ex) when (ex.Status == 409)
        {
            return JsonResult(HttpStatusCode.Conflict,
                new ErrorResponse("Game state was modified concurrently. Please reload and retry."));
        }

        _logger.LogInformation("Game {GameId} move {MoveNumber}: {Move}", id, newState.MoveNumber, move.GetType().Name);
        return JsonResult(HttpStatusCode.OK, BuildTable(newState, newState.MoveNumber, await _store.LoadMetaAsync(id)));
    }

    /// <summary>
    /// Take back the latest move and return the table as it stood before it. The request names the move, which
    /// must be the latest one; the position the game started from cannot be taken back.
    /// </summary>
    [FunctionName("UndoTableMove")]
    public async Task<IActionResult> Undo(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "games/{id}/table/undo")] HttpRequest req,
        string id)
    {
        UndoRequest? request;
        try
        {
            request = JsonConvert.DeserializeObject<UndoRequest>(await new StreamReader(req.Body).ReadToEndAsync(), _jsonSettings);
        }
        catch (JsonException ex)
        {
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse(ex.Message));
        }
        if (request == null)
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("Say which move to take back."));

        GameState latest;
        try
        {
            latest = await _store.LoadStateAsync(id);
        }
        catch (InvalidOperationException)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse($"Game '{id}' not found."));
        }

        if (request.MoveNumber != latest.MoveNumber)
            return JsonResult(HttpStatusCode.Conflict,
                new ErrorResponse($"Move {request.MoveNumber} is not the latest move; the game is at move {latest.MoveNumber}. Reload and retry."));
        if (latest.LastMove == null)
            return JsonResult(HttpStatusCode.BadRequest, new ErrorResponse("There is no move to take back."));

        var state = await _store.RewindAsync(id, latest.MoveNumber - 1);
        _logger.LogInformation("Game {GameId} move {MoveNumber} taken back", id, latest.MoveNumber);
        return JsonResult(HttpStatusCode.OK, BuildTable(state, state.MoveNumber, await _store.LoadMetaAsync(id)));
    }

    /// <summary>The moves played so far, for showing a move list.</summary>
    [FunctionName("GetGameHistory")]
    public async Task<IActionResult> GetHistory(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "games/{id}/history")] HttpRequest req,
        string id)
    {
        IReadOnlyList<GameState> states;
        try
        {
            states = await _store.LoadAllStatesAsync(id);
        }
        catch (InvalidOperationException ex)
        {
            return JsonResult(HttpStatusCode.NotFound, new ErrorResponse(ex.Message));
        }

        var moves = states
            .Where(s => s.LastMove != null)
            .Select(s => new HistoryEntry(s.MoveNumber, s.Generation, s.LastMove!))
            .ToImmutableList();
        var cardNames = CardRegistry.All.ToImmutableDictionary(c => c.Key, c => c.Value.Definition.Name);
        return JsonResult(HttpStatusCode.OK, new HistoryResponse(moves, cardNames));
    }

    private static TableResponse BuildTable(GameState state, int latestMoveNumber, TmEngine.BgaImport.GameMeta? meta)
    {
        // Moves can only be made from the latest state, so a past one offers none.
        var legalMoves = state.MoveNumber == latestMoveNumber
            ? state.Players
                .Select(p => (p.PlayerId, Moves: LegalMoveGenerator.GetLegalMoves(state, p.PlayerId)))
                .Where(x => !x.Moves.WaitingForOtherPlayer && !x.Moves.GameOver)
                .ToImmutableDictionary(x => x.PlayerId, x => x.Moves)
            : ImmutableDictionary<int, AvailableMoves>.Empty;

        var cardNames = CardNameResolver.FromGameState(state);
        foreach (var moves in legalMoves.Values)
            cardNames = cardNames.SetItems(CardNameResolver.FromAvailableMoves(moves));

        return new TableResponse(
            GameStateView.FilterForHotSeat(state),
            cardNames,
            PlayerStatusBuilder.Build(state),
            legalMoves,
            state.DrawPile.Count,
            state.DiscardPile.Count,
            latestMoveNumber,
            meta);
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
