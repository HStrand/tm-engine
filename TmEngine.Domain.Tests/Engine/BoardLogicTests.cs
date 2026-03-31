using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Tests.Engine;

public class BoardLogicTests
{
    private static GameState CreateTestState(MapName map = MapName.Tharsis)
    {
        return new GameState
        {
            GameId = "test",
            Map = map,
            CorporateEra = true,
            DraftVariant = false,
            PreludeExpansion = false,
            Phase = GamePhase.Action,
            Generation = 1,
            ActivePlayerIndex = 0,
            FirstPlayerIndex = 0,
            Oxygen = 0,
            Temperature = Constants.MinTemperature,
            OceansPlaced = 0,
            Players =
            [
                PlayerState.CreateInitial(0, 20),
                PlayerState.CreateInitial(1, 20),
            ],
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = [],
            DiscardPile = [],
            MoveNumber = 0,
            Log = [],
        };
    }

    // ── Ocean Placements ───────────────────────────────────────

    [Fact]
    public void GetValidOceanPlacements_ReturnsOnlyOceanReservedHexes()
    {
        var state = CreateTestState();
        var placements = BoardLogic.GetValidOceanPlacements(state);

        var map = MapDefinitions.Tharsis;
        Assert.Equal(12, placements.Length); // Tharsis has 12 ocean-reserved hexes

        foreach (var coord in placements)
        {
            Assert.Equal(HexType.OceanReserved, map.Hexes[coord].Type);
        }
    }

    [Fact]
    public void GetValidOceanPlacements_ExcludesOccupiedHexes()
    {
        var state = CreateTestState();
        var oceanHex = new HexCoord(4, 1);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(oceanHex, new PlacedTile(TileType.Ocean, null, oceanHex)),
        };

        var placements = BoardLogic.GetValidOceanPlacements(state);
        Assert.Equal(11, placements.Length);
        Assert.DoesNotContain(oceanHex, placements);
    }

    // ── Greenery Placements ────────────────────────────────────

    [Fact]
    public void GetValidGreeneryPlacements_NoOwnTiles_ReturnsAllLand()
    {
        var state = CreateTestState();
        var placements = BoardLogic.GetValidGreeneryPlacements(state, playerId: 0);

        // Should be all non-ocean, non-reserved hexes
        var map = MapDefinitions.Tharsis;
        var expectedCount = map.Hexes.Values.Count(h =>
            h.Type != HexType.OceanReserved &&
            !(h.Type == HexType.Named && h.ReservedFor != null));

        Assert.Equal(expectedCount, placements.Length);
    }

    [Fact]
    public void GetValidGreeneryPlacements_WithOwnTile_OnlyAdjacentToOwn()
    {
        var state = CreateTestState();
        var ownTile = new HexCoord(5, 5);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(ownTile, new PlacedTile(TileType.City, 0, ownTile)),
        };

        var placements = BoardLogic.GetValidGreeneryPlacements(state, playerId: 0);

        // All placements should be adjacent to (5,5)
        var adjacent = ownTile.GetAdjacentCoords();
        foreach (var coord in placements)
        {
            Assert.Contains(coord, adjacent);
        }

        // Should not include the tile itself or ocean hexes
        Assert.DoesNotContain(ownTile, placements);
    }

    [Fact]
    public void GetValidGreeneryPlacements_AllAdjacentOccupied_FallsBackToAllLand()
    {
        var state = CreateTestState();
        var center = new HexCoord(5, 5);

        // Place player's tile and fill all adjacent hexes
        var tiles = ImmutableDictionary.CreateBuilder<HexCoord, PlacedTile>();
        tiles.Add(center, new PlacedTile(TileType.City, 0, center));
        foreach (var adj in center.GetAdjacentCoords())
        {
            var map = MapDefinitions.Tharsis;
            if (map.Hexes.ContainsKey(adj))
                tiles.Add(adj, new PlacedTile(TileType.Greenery, 1, adj));
        }

        state = state with { PlacedTiles = tiles.ToImmutable() };

        var placements = BoardLogic.GetValidGreeneryPlacements(state, playerId: 0);

        // No adjacent open hexes → falls back to all available land
        Assert.True(placements.Length > 0);
    }

    // ── City Placements ────────────────────────────────────────

    [Fact]
    public void GetValidCityPlacements_ExcludesAdjacentToCities()
    {
        var state = CreateTestState();
        var cityHex = new HexCoord(5, 5);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(cityHex, new PlacedTile(TileType.City, 0, cityHex)),
        };

        var placements = BoardLogic.GetValidCityPlacements(state);

        // City hex itself should not be in placements
        Assert.DoesNotContain(cityHex, placements);

        // Adjacent hexes should not be in placements
        foreach (var adj in cityHex.GetAdjacentCoords())
        {
            Assert.DoesNotContain(adj, placements);
        }
    }

    [Fact]
    public void GetValidCityPlacements_ExcludesReservedHexes()
    {
        var state = CreateTestState();
        var placements = BoardLogic.GetValidCityPlacements(state);

        // Noctis City (3,5) is reserved — should not be in valid city placements
        Assert.DoesNotContain(new HexCoord(3, 5), placements);
    }

    // ── Special Tile Placements ────────────────────────────────

    [Fact]
    public void GetValidLavaFlows_Tharsis_OnlyVolcanicAreas()
    {
        var state = CreateTestState(MapName.Tharsis);
        var placements = BoardLogic.GetValidTilePlacements(state, TileType.LavaFlows, 0);

        Assert.Equal(4, placements.Length);
        Assert.Contains(new HexCoord(4, 2), placements);  // Tharsis Tholus
        Assert.Contains(new HexCoord(2, 3), placements);  // Ascraeus Mons
        Assert.Contains(new HexCoord(2, 4), placements);  // Pavonis Mons
        Assert.Contains(new HexCoord(1, 5), placements);  // Arsia Mons
    }

    [Fact]
    public void GetValidLavaFlows_Hellas_AnyLandHex()
    {
        var state = CreateTestState(MapName.Hellas);
        var placements = BoardLogic.GetValidTilePlacements(state, TileType.LavaFlows, 0);

        // Hellas has no volcanos, so Lava Flows can go on any land hex
        Assert.True(placements.Length > 4);
    }

    [Fact]
    public void GetValidLavaFlows_Elysium_OnlyVolcanicAreas()
    {
        var state = CreateTestState(MapName.Elysium);
        var placements = BoardLogic.GetValidTilePlacements(state, TileType.LavaFlows, 0);

        Assert.Equal(4, placements.Length);
        Assert.Contains(new HexCoord(3, 2), placements);  // Hecates Tholus
        Assert.Contains(new HexCoord(2, 3), placements);  // Elysium Mons
        Assert.Contains(new HexCoord(8, 3), placements);  // Olympus Mons
        Assert.Contains(new HexCoord(9, 5), placements);  // Arsia Mons
    }

    [Fact]
    public void GetValidPlacements_NuclearZone_AnyLandHex()
    {
        var state = CreateTestState();
        var existingTile = new HexCoord(5, 5);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(existingTile, new PlacedTile(TileType.City, 0, existingTile)),
        };

        var placements = BoardLogic.GetValidTilePlacements(state, TileType.NuclearZone, 0);

        // Should include hexes adjacent to existing tile (any land hex is valid)
        Assert.True(placements.Length > 0);
        Assert.DoesNotContain(existingTile, placements); // occupied hex excluded
    }

    [Fact]
    public void GetValidPlacements_NaturalPreserve_MustBeIsolated()
    {
        var state = CreateTestState();
        var existingTile = new HexCoord(5, 5);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(existingTile, new PlacedTile(TileType.City, 0, existingTile)),
        };

        var placements = BoardLogic.GetValidTilePlacements(state, TileType.NaturalPreserve, 0);

        // No placement should be adjacent to the existing tile
        foreach (var coord in placements)
        {
            Assert.DoesNotContain(coord, existingTile.GetAdjacentCoords());
        }
    }

    [Fact]
    public void GetValidAdjacentToCity_IndustrialCenter_MustBeAdjacentToCity()
    {
        var state = CreateTestState();
        var cityHex = new HexCoord(5, 5);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(cityHex, new PlacedTile(TileType.City, 0, cityHex)),
        };

        var placements = BoardLogic.GetValidTilePlacements(state, TileType.IndustrialCenter, 0);

        // All placements must be adjacent to the city
        foreach (var coord in placements)
        {
            Assert.True(BoardLogic.IsAdjacentToCity(state, coord));
        }

        Assert.True(placements.Length > 0);
    }

    // ── Adjacency Helpers ──────────────────────────────────────

    [Fact]
    public void CountAdjacentOceans_CorrectCount()
    {
        var state = CreateTestState();
        // (4,1) and (6,1) are both ocean-reserved on Tharsis
        var ocean1 = new HexCoord(4, 1);
        var ocean2 = new HexCoord(6, 1);

        state = state with
        {
            PlacedTiles = state.PlacedTiles
                .Add(ocean1, new PlacedTile(TileType.Ocean, null, ocean1))
                .Add(ocean2, new PlacedTile(TileType.Ocean, null, ocean2)),
        };

        // (5,1) on row 1 (odd) is adjacent to both (4,1) and (6,1)
        var count = BoardLogic.CountAdjacentOceans(state, new HexCoord(5, 1));
        Assert.Equal(2, count);
    }

    [Fact]
    public void IsAdjacentToPlayerTile_DetectsOwnership()
    {
        var state = CreateTestState();
        var playerTile = new HexCoord(5, 5);

        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(playerTile, new PlacedTile(TileType.City, 0, playerTile)),
        };

        // Adjacent hex should detect player 0's tile
        Assert.True(BoardLogic.IsAdjacentToPlayerTile(state, new HexCoord(4, 5), 0));
        Assert.False(BoardLogic.IsAdjacentToPlayerTile(state, new HexCoord(4, 5), 1));

        // Non-adjacent hex
        Assert.False(BoardLogic.IsAdjacentToPlayerTile(state, new HexCoord(8, 8), 0));
    }

    // ── Placement Bonuses ──────────────────────────────────────

    [Fact]
    public void ApplyPlacementBonuses_SteelBonus_AddsSteel()
    {
        var state = CreateTestState();
        state = state.UpdatePlayer(0, p => p with { Resources = ResourceSet.Zero });

        // (3,1) on Tharsis has 2 steel bonuses
        var hex = new HexCoord(3, 1);
        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(hex, new PlacedTile(TileType.Greenery, 0, hex)),
        };

        state = BoardLogic.ApplyPlacementBonuses(state, 0, hex);
        Assert.Equal(2, state.Players[0].Resources.Steel);
    }

    [Fact]
    public void ApplyPlacementBonuses_OceanAdjacency_GainsMC()
    {
        var state = CreateTestState();
        var ocean = new HexCoord(4, 1);
        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(ocean, new PlacedTile(TileType.Ocean, null, ocean)),
        };

        state = state.UpdatePlayer(0, p => p with { Resources = ResourceSet.Zero });

        // (5,1) is adjacent to ocean (4,1) on row 1, and has no hex bonus
        var adj = new HexCoord(5, 1);
        state = BoardLogic.ApplyPlacementBonuses(state, 0, adj);

        Assert.Equal(Constants.OceanAdjacencyBonus, state.Players[0].Resources.MegaCredits);
    }

    [Fact]
    public void ApplyPlacementBonuses_CardDraw_DrawsFromPile()
    {
        var state = CreateTestState() with
        {
            DrawPile = ImmutableList.Create("card1", "card2", "card3"),
        };
        state = state.UpdatePlayer(0, p => p with { Hand = ImmutableList<string>.Empty });

        // (6,1) on Tharsis has a Cards bonus
        var hex = new HexCoord(6, 1);
        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(hex, new PlacedTile(TileType.Greenery, 0, hex)),
        };

        state = BoardLogic.ApplyPlacementBonuses(state, 0, hex);

        Assert.Single(state.Players[0].Hand);
        Assert.Equal("card1", state.Players[0].Hand[0]);
        Assert.Equal(2, state.DrawPile.Count);
    }

    // ── Hellas South Pole ──────────────────────────────────────

    [Fact]
    public void Hellas_SouthPole_HasOceanBonus()
    {
        var map = MapDefinitions.Hellas;
        var southPole = map.Hexes[new HexCoord(5, 9)];

        Assert.Contains(PlacementBonus.Ocean, southPole.Bonuses);
        Assert.Equal("South Pole", southPole.ReservedFor);
    }
}
