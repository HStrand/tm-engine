using System.Collections.Immutable;

namespace TmEngine.Domain.Models;

/// <summary>
/// The canonical, JSON-serializable state of a Terraforming Mars game.
/// Every field needed to determine the next legal moves and apply state transitions.
/// </summary>
public sealed record GameState
{
    // ── Identity & Config ──────────────────────────────────────

    public required string GameId { get; init; }
    public required MapName Map { get; init; }
    public required bool CorporateEra { get; init; }
    public required bool DraftVariant { get; init; }
    public required bool PreludeExpansion { get; init; }

    // ── Phase & Turn ───────────────────────────────────────────

    public required GamePhase Phase { get; init; }
    public required int Generation { get; init; }

    /// <summary>Index into Players for the currently active player.</summary>
    public required int ActivePlayerIndex { get; init; }

    /// <summary>Index into Players for the first player this generation.</summary>
    public required int FirstPlayerIndex { get; init; }

    // ── Global Parameters ──────────────────────────────────────

    /// <summary>Oxygen level, 0–14%.</summary>
    public required int Oxygen { get; init; }

    /// <summary>Temperature in °C, -30 to +8, step 2.</summary>
    public required int Temperature { get; init; }

    /// <summary>Number of ocean tiles placed, 0–9.</summary>
    public required int OceansPlaced { get; init; }

    // ── Players ────────────────────────────────────────────────

    public required ImmutableList<PlayerState> Players { get; init; }

    // ── Board ──────────────────────────────────────────────────

    public required ImmutableDictionary<HexCoord, PlacedTile> PlacedTiles { get; init; }
    public required ImmutableList<MilestoneClaim> ClaimedMilestones { get; init; }
    public required ImmutableList<AwardFunding> FundedAwards { get; init; }

    // ── Card Piles ─────────────────────────────────────────────

    /// <summary>Draw pile, top of deck = index 0.</summary>
    public required ImmutableList<string> DrawPile { get; init; }

    public required ImmutableList<string> DiscardPile { get; init; }

    // ── Draft State (only during Research phase with Draft variant) ──

    public DraftState? Draft { get; init; }

    // ── Sub-Move ───────────────────────────────────────────────

    /// <summary>
    /// If non-null, the active player must resolve this before any other action.
    /// </summary>
    public PendingAction? PendingAction { get; init; }

    // ── Audit ──────────────────────────────────────────────────

    public required int MoveNumber { get; init; }
    public required ImmutableList<string> Log { get; init; }

    // ── Helpers ─────────────────────────────────────────────────

    public PlayerState ActivePlayer => Players[ActivePlayerIndex];

    public bool IsGameOver => Phase == GamePhase.GameEnd;

    public bool AllParametersMaxed =>
        Oxygen >= Constants.MaxOxygen &&
        Temperature >= Constants.MaxTemperature &&
        OceansPlaced >= Constants.MaxOceans;

    public PlayerState GetPlayer(int playerId) =>
        Players.First(p => p.PlayerId == playerId);

    public int GetPlayerIndex(int playerId) =>
        Players.FindIndex(p => p.PlayerId == playerId);

    public GameState UpdatePlayer(int playerId, Func<PlayerState, PlayerState> update)
    {
        var index = GetPlayerIndex(playerId);
        return this with { Players = Players.SetItem(index, update(Players[index])) };
    }

    public GameState AppendLog(string message) =>
        this with { Log = Log.Add(message) };
}

/// <summary>
/// Tracks draft state during the Research phase when using the Draft variant.
/// </summary>
public sealed record DraftState
{
    /// <summary>Cards currently in each player's draft hand, waiting to be picked from.</summary>
    public required ImmutableList<ImmutableList<string>> DraftHands { get; init; }

    /// <summary>Cards each player has drafted so far this round.</summary>
    public required ImmutableList<ImmutableList<string>> DraftedCards { get; init; }

    /// <summary>Current draft round (0–3, 4 rounds total).</summary>
    public required int DraftRound { get; init; }

    /// <summary>True if cards pass left (clockwise) this generation.</summary>
    public required bool PassLeft { get; init; }
}
