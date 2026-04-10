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

public sealed record ErrorResponse(string Error);

// ── Player Status ──────────────────────────────────────────

public sealed record PlayerStatusResponse(
    ImmutableList<PlayerStatusDto> Players);

public sealed record PlayerStatusDto(
    int PlayerId,
    string Corporation,
    PlayerPointsDto Points,
    PlayerTilesDto Tiles,
    ResourceSet Resources,
    ProductionSet Production,
    PlayerTagsDto Tags);

public sealed record PlayerPointsDto(
    int Total,
    int TR,
    int VP,
    int Milestones,
    int Awards,
    int Greeneries,
    int Cities);

public sealed record PlayerTilesDto(
    int Greeneries,
    int Cities,
    int SpecialTiles);

public sealed record PlayerTagsDto(
    int Building,
    int Space,
    int Power,
    int Science,
    int Jovian,
    int Earth,
    int Plant,
    int Microbe,
    int Animal,
    int City,
    int Event,
    int Wild);
