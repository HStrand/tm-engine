using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

public sealed record CreateGameRequest(
    int PlayerCount,
    MapName Map,
    bool CorporateEra,
    bool DraftVariant,
    bool PreludeExpansion,
    int? Seed = null);

public sealed record CreateGameResponse(string GameId);

public sealed record GameStateResponse(
    GameState State,
    ImmutableDictionary<string, string> CardNames);

public sealed record SubmitMoveResponse(
    bool Success,
    string? Error,
    GameState? State,
    ImmutableDictionary<string, string>? CardNames);

public sealed record LegalMovesResponse(
    TmEngine.Domain.Engine.AvailableMoves Moves,
    ImmutableDictionary<string, string> CardNames);

public sealed record HistoryResponse(ImmutableList<string> Log);

public sealed record ErrorResponse(string Error);
