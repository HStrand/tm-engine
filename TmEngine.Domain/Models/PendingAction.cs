using System.Collections.Immutable;

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
    ImmutableArray<HexCoord> ValidLocations) : PendingAction;

/// <summary>
/// Player must choose which opponent (or self) to remove resources from.
/// </summary>
public sealed record RemoveResourcePending(
    ResourceType Resource,
    int Amount,
    ImmutableArray<int> ValidTargetPlayerIds) : PendingAction;

/// <summary>
/// Player must choose a card to add resources to.
/// </summary>
public sealed record AddCardResourcePending(
    CardResourceType ResourceType,
    int Amount,
    ImmutableArray<string> ValidCardIds) : PendingAction;

/// <summary>
/// Player must choose between multiple options (e.g., "gain 3 plants OR add 2 animals").
/// </summary>
public sealed record ChooseOptionPending(
    string Description,
    ImmutableArray<string> Options) : PendingAction;

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
/// Player must submit their combined setup choices (corporation + preludes + initial cards).
/// </summary>
public sealed record SetupPending(
    ImmutableArray<string> AvailableCorporationIds,
    ImmutableArray<string> AvailablePreludeIds,
    ImmutableArray<string> AvailableProjectCardIds) : PendingAction;
