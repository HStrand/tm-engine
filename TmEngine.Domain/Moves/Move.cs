using System.Collections.Immutable;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Moves;

/// <summary>
/// Base type for all moves (commands) that can be applied to a GameState.
/// Each move is an explicit, auditable, deterministic state transition.
/// </summary>
public abstract record Move(int PlayerId);

// ═══════════════════════════════════════════════════════════
//  SETUP MOVE
// ═══════════════════════════════════════════════════════════

/// <summary>
/// Single combined setup move: player locks in their corporation, preludes (if applicable),
/// and initial card purchases all at once. This matches how players actually decide —
/// simultaneously examining all options before revealing.
/// PreludeIds is empty when the Prelude expansion is not in use.
/// </summary>
public sealed record SetupMove(
    int PlayerId,
    string CorporationId,
    ImmutableArray<string> PreludeIds,
    ImmutableArray<string> CardIdsToBuy) : Move(PlayerId);

// ═══════════════════════════════════════════════════════════
//  RESEARCH PHASE MOVES
// ═══════════════════════════════════════════════════════════

/// <summary>Player drafts one card from their current draft hand (Draft variant).</summary>
public sealed record DraftCardMove(int PlayerId, string CardId) : Move(PlayerId);

/// <summary>Player buys cards from dealt/drafted selection (3 MC each).</summary>
public sealed record BuyCardsMove(int PlayerId, ImmutableArray<string> CardIds) : Move(PlayerId);

// ═══════════════════════════════════════════════════════════
//  ACTION PHASE MOVES
// ═══════════════════════════════════════════════════════════

/// <summary>Player plays a project card from hand.</summary>
public sealed record PlayCardMove(
    int PlayerId,
    string CardId,
    PaymentInfo Payment) : Move(PlayerId);

// ── Standard Projects ──────────────────────────────────────

/// <summary>Player sells cards from hand for 1 MC each.</summary>
public sealed record SellPatentsMove(int PlayerId, ImmutableArray<string> CardIds) : Move(PlayerId);

/// <summary>Player pays to increase energy production 1 step.</summary>
public sealed record PowerPlantMove(int PlayerId) : Move(PlayerId);

/// <summary>Player pays to raise temperature 1 step.</summary>
public sealed record AsteroidMove(int PlayerId) : Move(PlayerId);

/// <summary>Player pays to place an ocean tile.</summary>
public sealed record AquiferMove(int PlayerId, HexCoord Location) : Move(PlayerId);

/// <summary>Player pays to place a greenery tile and raise oxygen.</summary>
public sealed record GreeneryMove(int PlayerId, HexCoord Location) : Move(PlayerId);

/// <summary>Player pays to place a city tile and increase MC production.</summary>
public sealed record CityMove(int PlayerId, HexCoord Location) : Move(PlayerId);

/// <summary>Player uses the action on a blue (active) card.</summary>
public sealed record UseCardActionMove(int PlayerId, string CardId) : Move(PlayerId);

/// <summary>Player claims a milestone.</summary>
public sealed record ClaimMilestoneMove(int PlayerId, string MilestoneName) : Move(PlayerId);

/// <summary>Player funds an award.</summary>
public sealed record FundAwardMove(int PlayerId, string AwardName) : Move(PlayerId);

/// <summary>Player converts 8 plants into a greenery tile.</summary>
public sealed record ConvertPlantsMove(int PlayerId, HexCoord Location) : Move(PlayerId);

/// <summary>Player converts 8 heat into a temperature raise.</summary>
public sealed record ConvertHeatMove(int PlayerId) : Move(PlayerId);

/// <summary>Player passes (takes no more actions this generation).</summary>
public sealed record PassMove(int PlayerId) : Move(PlayerId);

/// <summary>Player skips their remaining action(s) this turn without passing for the generation.</summary>
public sealed record EndTurnMove(int PlayerId) : Move(PlayerId);

/// <summary>
/// Player performs their corporation's mandatory first action (gen 1 only).
/// Counts as 1 of their 2 actions for the turn.
/// </summary>
public sealed record PerformFirstActionMove(int PlayerId) : Move(PlayerId);

/// <summary>
/// Player plays a prelude card during the PreludePlacement phase.
/// The prelude must be one of the player's remaining preludes.
/// </summary>
public sealed record PlayPreludeMove(int PlayerId, string PreludeId) : Move(PlayerId);

// ═══════════════════════════════════════════════════════════
//  SUB-MOVE RESOLUTION MOVES
// ═══════════════════════════════════════════════════════════

/// <summary>Player places a tile to resolve a pending PlaceTilePending.</summary>
public sealed record PlaceTileMove(int PlayerId, HexCoord Location) : Move(PlayerId);

/// <summary>Player chooses a target player (e.g., for removing resources).</summary>
public sealed record ChooseTargetPlayerMove(int PlayerId, int TargetPlayerId) : Move(PlayerId);

/// <summary>Player selects a card to add/remove resources on.</summary>
public sealed record SelectCardMove(int PlayerId, string CardId) : Move(PlayerId);

/// <summary>Player chooses one of several options (e.g., "gain 3 plants OR add 2 animals").</summary>
public sealed record ChooseOptionMove(int PlayerId, int OptionIndex) : Move(PlayerId);

/// <summary>Player discards cards from hand to resolve a DiscardCardsPending.</summary>
public sealed record DiscardCardsMove(int PlayerId, ImmutableArray<string> CardIds) : Move(PlayerId);

/// <summary>
/// Player chooses which effect to resolve next from a ChooseEffectOrderPending.
/// EffectIndex is the original index into the card's effect array, or -1 to auto-execute all remaining.
/// </summary>
public sealed record ChooseEffectOrderMove(int PlayerId, int EffectIndex) : Move(PlayerId);
