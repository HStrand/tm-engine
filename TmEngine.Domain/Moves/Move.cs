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

/// <summary>Player uses one of the 6 standard projects.</summary>
public sealed record UseStandardProjectMove(
    int PlayerId,
    StandardProject Project,
    /// <summary>Cards to discard (only for SellPatents).</summary>
    ImmutableArray<string> CardsToDiscard = default,
    /// <summary>Location for tile placement (Aquifer, Greenery, City).</summary>
    HexCoord? Location = null) : Move(PlayerId);

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
