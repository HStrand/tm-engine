using System.Collections.Immutable;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Engine;

/// <summary>
/// Pure functions for board-related game logic: tile placement rules,
/// adjacency calculations, and placement bonus resolution.
/// </summary>
public static class BoardLogic
{
    // ── Valid Tile Placements ───────────────────────────────────

    /// <summary>
    /// Returns all valid hex coordinates where the given tile type can be placed.
    /// </summary>
    public static ImmutableArray<HexCoord> GetValidTilePlacements(
        GameState state, TileType tileType, int playerId)
    {
        return tileType switch
        {
            TileType.Ocean => GetValidOceanPlacements(state),
            TileType.Greenery => GetValidGreeneryPlacements(state, playerId),
            TileType.City => GetValidCityPlacements(state),
            TileType.Capital => GetValidCapitalPlacements(state),
            TileType.LavaFlows => GetValidLavaFlowsPlacements(state),
            TileType.MoholeArea => GetValidOceanPlacements(state), // placed on ocean-reserved area
            TileType.NuclearZone => GetValidLandPlacements(state),
            TileType.NaturalPreserve => GetValidIsolatedPlacements(state),
            TileType.IndustrialCenter => GetValidAdjacentToCityPlacements(state),
            TileType.CommercialDistrict => GetValidLandPlacements(state),
            TileType.EcologicalZone => GetValidAdjacentToGreeneryPlacements(state, playerId),
            TileType.RestrictedArea => GetValidLandPlacements(state),
            TileType.MiningArea => GetValidMiningAreaPlacements(state, playerId),
            TileType.MiningRights => GetValidMiningRightsPlacements(state),
            _ => GetValidLandPlacements(state),
        };
    }

    /// <summary>
    /// Returns valid ocean-reserved hexes that are not yet occupied.
    /// </summary>
    public static ImmutableArray<HexCoord> GetValidOceanPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved && !state.PlacedTiles.ContainsKey(coord))
                builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns valid greenery placements. Greeneries MUST be placed adjacent to
    /// the player's existing tiles if possible. If no such spot exists, any open
    /// land hex is valid.
    /// </summary>
    public static ImmutableArray<HexCoord> GetValidGreeneryPlacements(GameState state, int playerId)
    {
        var map = MapDefinitions.GetMap(state.Map);

        // First, find all open land hexes adjacent to the player's tiles
        var adjacentToOwn = ImmutableArray.CreateBuilder<HexCoord>();
        var allLand = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;

            allLand.Add(coord);

            if (IsAdjacentToPlayerTile(state, coord, playerId))
                adjacentToOwn.Add(coord);
        }

        // Must place adjacent to own tile if possible
        return adjacentToOwn.Count > 0 ? adjacentToOwn.ToImmutable() : allLand.ToImmutable();
    }

    /// <summary>
    /// Returns valid city placements. Cities cannot be placed adjacent to other cities.
    /// Excludes reserved-name hexes (Noctis City has its own placement).
    /// </summary>
    public static ImmutableArray<HexCoord> GetValidCityPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;
            if (IsAdjacentToCity(state, coord))
                continue;

            builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Capital city: must be adjacent to at least 2 ocean tiles (per card text).
    /// Also follows normal city rules (no adjacent cities).
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidCapitalPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;
            if (IsAdjacentToCity(state, coord))
                continue;

            builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Lava Flows: must be placed on a volcanic area (map-specific).
    /// On Hellas (no volcanos), can be placed on any non-ocean area.
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidLavaFlowsPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);

        if (map.VolcanicAreas.IsEmpty)
        {
            // Hellas: no volcanos, Lava Flows can go anywhere
            return GetValidLandPlacements(state);
        }

        var builder = ImmutableArray.CreateBuilder<HexCoord>();
        foreach (var coord in map.VolcanicAreas)
        {
            if (!state.PlacedTiles.ContainsKey(coord))
                builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Tiles that must be placed next to no other tile (Nuclear Zone, Natural Preserve).
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidIsolatedPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;

            // Must not be adjacent to any placed tile
            bool hasAdjacentTile = false;
            foreach (var adj in coord.GetAdjacentCoords())
            {
                if (state.PlacedTiles.ContainsKey(adj))
                {
                    hasAdjacentTile = true;
                    break;
                }
            }

            if (!hasAdjacentTile)
                builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Tiles that must be placed adjacent to a city tile (Industrial Center, Commercial District).
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidAdjacentToCityPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;

            if (IsAdjacentToCity(state, coord))
                builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Ecological Zone: must be placed adjacent to any greenery tile.
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidAdjacentToGreeneryPlacements(GameState state, int playerId)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;

            foreach (var adj in coord.GetAdjacentCoords())
            {
                if (state.PlacedTiles.TryGetValue(adj, out var tile) && tile.Type == TileType.Greenery)
                {
                    builder.Add(coord);
                    break;
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Mining Area: must be placed on a hex with steel or titanium bonus,
    /// adjacent to another of the player's tiles.
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidMiningAreaPlacements(GameState state, int playerId)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;

            if (!HasMiningBonus(hex))
                continue;

            if (IsAdjacentToPlayerTile(state, coord, playerId))
                builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Mining Rights: must be placed on a hex with steel or titanium bonus.
    /// </summary>
    private static ImmutableArray<HexCoord> GetValidMiningRightsPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;

            if (HasMiningBonus(hex))
                builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Returns all valid non-ocean, non-reserved hexes.
    /// </summary>
    public static ImmutableArray<HexCoord> GetValidLandPlacements(GameState state)
    {
        var map = MapDefinitions.GetMap(state.Map);
        var builder = ImmutableArray.CreateBuilder<HexCoord>();

        foreach (var (coord, hex) in map.Hexes)
        {
            if (hex.Type == HexType.OceanReserved || state.PlacedTiles.ContainsKey(coord))
                continue;
            if (hex.Type == HexType.Named && hex.ReservedFor != null)
                continue;

            builder.Add(coord);
        }

        return builder.ToImmutable();
    }

    // ── Adjacency Queries ──────────────────────────────────────

    /// <summary>
    /// Count ocean tiles adjacent to the given hex.
    /// </summary>
    public static int CountAdjacentOceans(GameState state, HexCoord coord)
    {
        int count = 0;
        foreach (var adj in coord.GetAdjacentCoords())
        {
            if (state.PlacedTiles.TryGetValue(adj, out var tile) && tile.Type == TileType.Ocean)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Count greenery tiles adjacent to the given hex (regardless of owner).
    /// </summary>
    public static int CountAdjacentGreeneries(GameState state, HexCoord coord)
    {
        int count = 0;
        foreach (var adj in coord.GetAdjacentCoords())
        {
            if (state.PlacedTiles.TryGetValue(adj, out var tile) && tile.Type == TileType.Greenery)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Check if a hex is adjacent to any city tile (City or Capital).
    /// </summary>
    public static bool IsAdjacentToCity(GameState state, HexCoord coord)
    {
        foreach (var adj in coord.GetAdjacentCoords())
        {
            if (state.PlacedTiles.TryGetValue(adj, out var tile) &&
                (tile.Type == TileType.City || tile.Type == TileType.Capital))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Check if a hex is adjacent to any tile owned by the given player.
    /// </summary>
    public static bool IsAdjacentToPlayerTile(GameState state, HexCoord coord, int playerId)
    {
        foreach (var adj in coord.GetAdjacentCoords())
        {
            if (state.PlacedTiles.TryGetValue(adj, out var tile) && tile.OwnerId == playerId)
                return true;
        }
        return false;
    }

    // ── Placement Bonus Resolution ─────────────────────────────

    /// <summary>
    /// Apply placement bonuses for the given hex to the player.
    /// Returns updated state with resources added and any pending actions.
    /// </summary>
    public static GameState ApplyPlacementBonuses(GameState state, int playerId, HexCoord location)
    {
        var map = MapDefinitions.GetMap(state.Map);
        if (!map.Hexes.TryGetValue(location, out var hex))
            return state;

        // Hex-printed bonuses
        bool gainedMineral = false;

        foreach (var bonus in hex.Bonuses)
        {
            state = bonus switch
            {
                PlacementBonus.Steel => state.UpdatePlayer(playerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Steel, 1) }),
                PlacementBonus.Titanium => state.UpdatePlayer(playerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Titanium, 1) }),
                PlacementBonus.Plants => state.UpdatePlayer(playerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Plants, 1) }),
                PlacementBonus.Heat => state.UpdatePlayer(playerId, p => p with
                    { Resources = p.Resources.Add(ResourceType.Heat, 1) }),
                PlacementBonus.Cards => DrawCardForPlayer(state, playerId),
                PlacementBonus.Ocean => state, // Hellas south pole: handled by special logic
                _ => state,
            };

            if (bonus is PlacementBonus.Steel or PlacementBonus.Titanium)
                gainedMineral = true;
        }

        // Fire triggered effect once per tile placement if any mineral was gained (Mining Guild)
        if (gainedMineral)
            state = TriggerSystem.FireTrigger(state, playerId, TriggerCondition.GainMineralPlacementBonus);

        // Ocean adjacency bonus: 2 MC per adjacent ocean tile
        var adjacentOceans = CountAdjacentOceans(state, location);
        if (adjacentOceans > 0)
        {
            var mcBonus = adjacentOceans * Constants.OceanAdjacencyBonus;
            state = state.UpdatePlayer(playerId, p => p with
            {
                Resources = p.Resources.Add(ResourceType.MegaCredits, mcBonus),
            });
        }

        return state;
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static bool HasMiningBonus(HexDefinition hex) =>
        hex.Bonuses.Contains(PlacementBonus.Steel) || hex.Bonuses.Contains(PlacementBonus.Titanium);

    private static GameState DrawCardForPlayer(GameState state, int playerId)
    {
        if (state.DrawPile.IsEmpty)
            return state; // TODO: reshuffle discard pile when draw pile is empty

        var cardId = state.DrawPile[0];
        state = state with { DrawPile = state.DrawPile.RemoveAt(0) };
        state = state.UpdatePlayer(playerId, p => p with { Hand = p.Hand.Add(cardId) });
        return state;
    }
}
