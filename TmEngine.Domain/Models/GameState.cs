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

    /// <summary>Remaining prelude cards not dealt during setup (for Valley Trust first action).</summary>
    public ImmutableList<string> PreludeDeck { get; init; } = [];

    // ── Setup State (only during Setup phase) ──

    public SetupState? Setup { get; init; }

    // ── Research State (only during Research phase) ──

    public ResearchState? Research { get; init; }

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

    public bool AllParametersMaxed
    {
        get
        {
            var map = MapDefinitions.GetMap(Map);
            return Oxygen >= map.MaxOxygen &&
                   Temperature >= map.MaxTemperature &&
                   OceansPlaced >= map.MaxOceans;
        }
    }

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
/// Tracks the setup phase: what was dealt to each player and what they've chosen.
/// All players submit simultaneously; once all have submitted, choices are applied in player order.
/// </summary>
public sealed record SetupState
{
    /// <summary>Corporation IDs dealt to each player. Index = player index.</summary>
    public required ImmutableList<ImmutableList<string>> DealtCorporations { get; init; }

    /// <summary>Prelude IDs dealt to each player (empty lists if Prelude not enabled).</summary>
    public required ImmutableList<ImmutableList<string>> DealtPreludes { get; init; }

    /// <summary>Project card IDs dealt to each player (10 cards each).</summary>
    public required ImmutableList<ImmutableList<string>> DealtCards { get; init; }

    /// <summary>Submitted setup moves per player (null = not yet submitted).</summary>
    public required ImmutableList<Moves.SetupMove?> SubmittedMoves { get; init; }
}

/// <summary>
/// Tracks the research phase: cards dealt to each player, waiting for buy decisions.
/// </summary>
public sealed record ResearchState
{
    /// <summary>Cards dealt/drafted to each player, available for purchase. Index = player index.</summary>
    public required ImmutableList<ImmutableList<string>> AvailableCards { get; init; }

    /// <summary>Which players have submitted their buy decision.</summary>
    public required ImmutableList<bool> Submitted { get; init; }
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
