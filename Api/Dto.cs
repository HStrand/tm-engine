using System.Collections.Immutable;
using Newtonsoft.Json;
using TmEngine.Domain.Models;

namespace tm_engine.Api;

[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
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
    ImmutableDictionary<string, string> CardNames,
    int DrawPileCount,
    int DiscardPileCount,
    ImmutableDictionary<int, int> HandCounts);

public sealed record SubmitMoveResponse(
    bool Success,
    string? Error,
    GameState? State,
    ImmutableDictionary<string, string>? CardNames);

public sealed record LegalMovesResponse(
    TmEngine.Domain.Engine.AvailableMoves Moves,
    ImmutableDictionary<string, string> CardNames);

public sealed record ErrorResponse(string Error);

// ── BGA Import ─────────────────────────────────────────────

/// <summary>
/// Play on from a position in a BGA replay. With Snap, MoveIndex is moved back to the nearest position
/// that can be imported; without it, MoveIndex must be one of the game's fork points.
/// </summary>
[JsonObject(MissingMemberHandling = MissingMemberHandling.Error)]
public sealed record ImportBgaRequest(
    string TableId,
    int MoveIndex,
    int? Seed = null,
    bool? Snap = null);

public sealed record ImportResponse(
    string? GameId,
    TmEngine.BgaImport.ForkPoint? Fork,
    TmEngine.BgaImport.GameMeta? Meta,
    ImmutableList<TmEngine.BgaImport.ImportIssue> Issues);

public sealed record ForkPointsResponse(
    bool Eligible,
    ImmutableList<TmEngine.BgaImport.ForkPoint> ForkPoints,
    ImmutableList<TmEngine.BgaImport.ImportIssue> Issues);

// ── Hot-seat table ─────────────────────────────────────────

/// <summary>
/// Everything one person controlling every seat needs to show the game and offer the next move:
/// the state with all hands visible, derived per-player figures, and each seat's legal moves.
/// </summary>
public sealed record TableResponse(
    GameState State,
    ImmutableDictionary<string, string> CardNames,
    ImmutableList<PlayerStatusDto> Players,
    /// <summary>Player id -> legal moves, for the players who can act. Empty when viewing a past move.</summary>
    ImmutableDictionary<int, TmEngine.Domain.Engine.AvailableMoves> LegalMoves,
    int DrawPileCount,
    int DiscardPileCount,
    /// <summary>The game's latest move number; greater than State.MoveNumber when viewing a past move.</summary>
    int LatestMoveNumber,
    TmEngine.BgaImport.GameMeta? Meta);

/// <summary>The moves played so far. CardNames covers every card, since moves name cards that have since left play.</summary>
public sealed record HistoryResponse(ImmutableList<HistoryEntry> Moves, ImmutableDictionary<string, string> CardNames);

public sealed record HistoryEntry(int MoveNumber, int Generation, TmEngine.Domain.Moves.Move Move);

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
    PlayerTagsDto Tags,
    /// <summary>What the player can put towards a cost in MC: their MC, plus heat if they may pay with it (Helion).</summary>
    int SpendableMegaCredits);

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
