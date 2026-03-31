using System.Collections.Immutable;

namespace TmEngine.Domain.Models;

/// <summary>
/// Coordinate on the hex grid using offset coordinates (odd-row offset right).
/// </summary>
public readonly record struct HexCoord(int Col, int Row)
{
    public override string ToString() => $"({Col},{Row})";

    /// <summary>
    /// Returns the 6 adjacent hex coordinates using odd-row offset right convention.
    /// </summary>
    public ImmutableArray<HexCoord> GetAdjacentCoords()
    {
        bool oddRow = Row % 2 == 1;
        int offset = oddRow ? 1 : -1;

        return
        [
            new HexCoord(Col - 1, Row),         // left
            new HexCoord(Col + 1, Row),         // right
            new HexCoord(Col, Row - 1),         // upper-same
            new HexCoord(Col + (oddRow ? 1 : -1), Row - 1), // upper-offset
            new HexCoord(Col, Row + 1),         // lower-same
            new HexCoord(Col + (oddRow ? 1 : -1), Row + 1), // lower-offset
        ];
    }
}

/// <summary>
/// Defines a hex tile on the map — its type, bonuses, and any reservation.
/// </summary>
public sealed record HexDefinition(
    HexCoord Coord,
    HexType Type,
    ImmutableArray<PlacementBonus> Bonuses,
    string? ReservedFor = null);

/// <summary>
/// A tile that has been placed on the board.
/// </summary>
public sealed record PlacedTile(
    TileType Type,
    int? OwnerId,   // null for ocean tiles (unowned)
    HexCoord Location);

/// <summary>
/// An off-map city tile (e.g., Phobos Space Haven, Ganymede Colony).
/// These count as cities for scoring and tag purposes but are not on the hex grid.
/// </summary>
public sealed record OffMapTile(
    string Name,
    TileType Type,
    int OwnerId);

/// <summary>
/// A claimed milestone.
/// </summary>
public sealed record MilestoneClaim(string MilestoneName, int PlayerId);

/// <summary>
/// A funded award.
/// </summary>
public sealed record AwardFunding(string AwardName, int PlayerId);
