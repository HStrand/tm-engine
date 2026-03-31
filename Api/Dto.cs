using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

public sealed record CreateGameRequest(
    int PlayerCount,
    MapName Map,
    bool CorporateEra,
    bool DraftVariant,
    bool PreludeExpansion);

public sealed record CreateGameResponse(string GameId);

public sealed record SubmitMoveResponse(bool Success, string? Error, GameState? State);

public sealed record HistoryResponse(ImmutableList<string> Log);

public sealed record ErrorResponse(string Error);
