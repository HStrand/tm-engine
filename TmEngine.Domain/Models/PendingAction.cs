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
/// Player must select a corporation (during setup).
/// </summary>
public sealed record SelectCorporationPending(
    ImmutableArray<string> AvailableCorporationIds) : PendingAction;

/// <summary>
/// Player must select preludes to keep (during setup).
/// </summary>
public sealed record SelectPreludesPending(
    ImmutableArray<string> AvailablePreludeIds,
    int KeepCount) : PendingAction;
