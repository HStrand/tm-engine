using System.Collections.Immutable;
using TmEngine.Domain.Cards.Effects;

namespace TmEngine.Domain.Models;

/// <summary>
/// Represents a sub-move that the active player must resolve before the turn continues.
/// When a move triggers an effect requiring a player decision, a PendingAction is set
/// on the GameState and the player must submit the appropriate resolution move.
/// </summary>
public abstract record PendingAction;

/// <summary>
/// Player must place a tile at one of the valid locations.
/// </summary>
public sealed record PlaceTilePending(
    TileType TileType,
    ImmutableArray<HexCoord> ValidLocations,
    int RemoveAdjacentMC = 0) : PendingAction;

/// <summary>
/// Player must choose which opponent (or self) to remove resources from.
/// </summary>
public sealed record RemoveResourcePending(
    ResourceType Resource,
    int Amount,
    ImmutableArray<int> ValidTargetPlayerIds,
    bool IsOptional = false) : PendingAction;

/// <summary>
/// Player must choose a card to add resources to.
/// </summary>
public sealed record AddCardResourcePending(
    CardResourceType ResourceType,
    int Amount,
    ImmutableArray<string> ValidCardIds) : PendingAction;

/// <summary>
/// Player must choose which card to remove resources from.
/// </summary>
public sealed record RemoveCardResourcePending(
    CardResourceType ResourceType,
    int Amount,
    ImmutableArray<string> ValidCardIds) : PendingAction;

/// <summary>
/// Player must choose cards to keep from a drawn set. Unchosen cards are discarded.
/// </summary>
public sealed record DrawKeepPending(
    ImmutableArray<string> DrawnCardIds,
    int KeepCount,
    int CostPerCard = 0) : PendingAction;

/// <summary>
/// Player must choose between multiple options (e.g., "gain 3 plants OR add 2 animals").
/// </summary>
public sealed record ChooseOptionPending(
    string Description,
    ImmutableArray<string> Options,
    string? SourceCardId = null,
    ImmutableArray<int>? ValidOptionIndices = null) : PendingAction;

/// <summary>
/// Player must choose which opponent to reduce production for.
/// </summary>
public sealed record ReduceProductionPending(
    ResourceType Resource,
    int Amount,
    ImmutableArray<int> ValidTargetPlayerIds) : PendingAction;

/// <summary>
/// Player must select cards to discard from hand.
/// </summary>
public sealed record DiscardCardsPending(
    int Count) : PendingAction;

/// <summary>
/// Player must select cards to buy from dealt/drafted cards (during research or setup).
/// </summary>
public sealed record BuyCardsPending(
    ImmutableArray<string> AvailableCardIds) : PendingAction;

/// <summary>
/// Player must choose a hex to claim (Land Claim). Only valid non-reserved, unoccupied, unclaimed land hexes.
/// </summary>
public sealed record ClaimLandPending(
    ImmutableArray<HexCoord> ValidLocations) : PendingAction;

/// <summary>
/// Player must play a card from their hand with special rules
/// (e.g., Ecology Experts: ignore global parameter requirements, Eccentric Sponsor: 25 MC discount).
/// </summary>
public sealed record PlayCardFromHandPending(
    string Description,
    bool IgnoreGlobalRequirements = false,
    int CostDiscount = 0) : PendingAction;

/// <summary>
/// Player must choose one card to play from a set of options (e.g., Valley Trust first action).
/// The chosen card's effects are applied immediately; the rest are discarded.
/// </summary>
public sealed record ChooseCardToPlayPending(
    string Description,
    ImmutableArray<string> CardIds) : PendingAction;

/// <summary>
/// Mars University: player may discard a card from hand to draw a new one, or pass.
/// Resolved by DiscardCardsMove (with 1 card) to cycle, or PassMove to skip.
/// </summary>
public sealed record MarsUniversityPending : PendingAction;

/// <summary>
/// Player must choose which triggered effect to resolve next when multiple fire at once.
/// Each entry identifies a triggered effect by its owner card ID and effect index.
/// </summary>
public sealed record ChooseTriggerOrderPending(
    ImmutableArray<TriggerQueueEntry> Triggers,
    ImmutableArray<string> Descriptions) : PendingAction;

/// <summary>
/// Identifies a triggered effect waiting to be resolved.
/// There are two kinds of entries:
/// <list type="bullet">
///   <item><b>Triggered effect</b> — a "When you play X" effect on an in-play card.
///     OwnerCardId is the card with the trigger (e.g., Mars University),
///     Trigger is the condition that fired (e.g., PlayScienceTag),
///     and TriggeringCardId is the card that was just played.</item>
///   <item><b>On-play effects</b> (IsOnPlayEntry = true) — the played card's own
///     on-play effects that were deferred for ordering. DeferredEffectIndices stores
///     which on-play effects still need executing.</item>
/// </list>
/// </summary>
public sealed record TriggerQueueEntry(
    /// <summary>The card that owns the triggered effect (e.g., Mars University), or the played card for on-play entries.</summary>
    string OwnerCardId,
    /// <summary>Which trigger condition fired (e.g., PlayScienceTag). Null for on-play entries.</summary>
    TriggerCondition? Trigger,
    /// <summary>The card that was just played and caused this trigger to fire.</summary>
    string? TriggeringCardId)
{
    /// <summary>True if this entry represents deferred on-play effects rather than a triggered effect.</summary>
    public bool IsOnPlayEntry => Trigger == null;

    /// <summary>
    /// For on-play entries only: which of the card's on-play effects still need executing.
    /// </summary>
    public ImmutableArray<int> DeferredEffectIndices { get; init; } = [];
}

/// <summary>
/// Player must choose one of their building-tagged cards to copy production from.
/// E.g., Robotic Workforce.
/// </summary>
public sealed record CopyProductionPending(
    ImmutableArray<string> ValidCardIds) : PendingAction;

/// <summary>
/// Player must submit their combined setup choices (corporation + preludes + initial cards).
/// </summary>
public sealed record SetupPending(
    ImmutableArray<string> AvailableCorporationIds,
    ImmutableArray<string> AvailablePreludeIds,
    ImmutableArray<string> AvailableProjectCardIds) : PendingAction;

/// <summary>
/// Player must choose which effect to resolve next from a card's remaining effects.
/// </summary>
public sealed record ChooseEffectOrderPending(
    string SourceCardId,
    string EffectSource,
    ImmutableArray<int> RemainingEffectIndices,
    ImmutableArray<string> EffectDescriptions) : PendingAction;

/// <summary>
/// Tracks remaining card effects that need to execute after the current PendingAction resolves.
/// Uses card ID + effect indices so it survives serialization without storing abstract Effect objects.
/// </summary>
public sealed record PendingEffectQueue(
    string SourceCardId,
    ImmutableArray<int> RemainingEffectIndices,
    string EffectSource);
