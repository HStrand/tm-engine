using System.Collections.Immutable;
using System.Text.Json;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Tests.Models;

public class GameStateTests
{
    [Fact]
    public void PlayerState_CreateInitial_HasCorrectDefaults()
    {
        var player = PlayerState.CreateInitial(playerId: 1, startingTR: 20);

        Assert.Equal(1, player.PlayerId);
        Assert.Equal(20, player.TerraformRating);
        Assert.Equal(0, player.Resources.MegaCredits);
        Assert.Equal(0, player.Production.MegaCredits);
        Assert.Empty(player.Hand);
        Assert.Empty(player.PlayedCards);
        Assert.False(player.Passed);
    }

    [Fact]
    public void ResourceSet_Add_WorksCorrectly()
    {
        var resources = ResourceSet.Zero
            .Add(ResourceType.MegaCredits, 42)
            .Add(ResourceType.Steel, 5);

        Assert.Equal(42, resources.MegaCredits);
        Assert.Equal(5, resources.Steel);
        Assert.Equal(0, resources.Titanium);
    }

    [Fact]
    public void ResourceSet_PlusOperator_SumsBothSets()
    {
        var a = new ResourceSet(MegaCredits: 10, Steel: 3);
        var b = new ResourceSet(MegaCredits: 5, Titanium: 2);

        var sum = a + b;

        Assert.Equal(15, sum.MegaCredits);
        Assert.Equal(3, sum.Steel);
        Assert.Equal(2, sum.Titanium);
    }

    [Fact]
    public void ProductionSet_CanHaveNegativeMC()
    {
        var production = ProductionSet.Zero with { MegaCredits = -5 };
        Assert.Equal(-5, production.MegaCredits);
    }

    [Fact]
    public void HexCoord_GetAdjacentCoords_ReturnsExactlySix()
    {
        var coord = new HexCoord(5, 5);
        var adjacent = coord.GetAdjacentCoords();
        Assert.Equal(6, adjacent.Length);
    }

    [Fact]
    public void HexCoord_Adjacency_OddRow_CorrectOffsets()
    {
        // Row 3 is odd, so offset right
        var coord = new HexCoord(5, 3);
        var adjacent = coord.GetAdjacentCoords();

        Assert.Contains(new HexCoord(4, 3), adjacent); // left
        Assert.Contains(new HexCoord(6, 3), adjacent); // right
        Assert.Contains(new HexCoord(5, 2), adjacent); // upper-same
        Assert.Contains(new HexCoord(6, 2), adjacent); // upper-offset (odd row -> +1)
        Assert.Contains(new HexCoord(5, 4), adjacent); // lower-same
        Assert.Contains(new HexCoord(6, 4), adjacent); // lower-offset (odd row -> +1)
    }

    [Fact]
    public void HexCoord_Adjacency_EvenRow_CorrectOffsets()
    {
        // Row 4 is even, so offset left
        var coord = new HexCoord(5, 4);
        var adjacent = coord.GetAdjacentCoords();

        Assert.Contains(new HexCoord(4, 4), adjacent); // left
        Assert.Contains(new HexCoord(6, 4), adjacent); // right
        Assert.Contains(new HexCoord(5, 3), adjacent); // upper-same
        Assert.Contains(new HexCoord(4, 3), adjacent); // upper-offset (even row -> -1)
        Assert.Contains(new HexCoord(5, 5), adjacent); // lower-same
        Assert.Contains(new HexCoord(4, 5), adjacent); // lower-offset (even row -> -1)
    }

    [Fact]
    public void MapDefinitions_Tharsis_HasCorrectHexCount()
    {
        var map = MapDefinitions.Tharsis;
        // Tharsis: 6+7+7+8+9+8+7+6+6 = 64 hexes
        Assert.Equal(64, map.Hexes.Count);
    }

    [Fact]
    public void MapDefinitions_Tharsis_Has12OceanHexes()
    {
        var map = MapDefinitions.Tharsis;
        var oceanCount = map.Hexes.Values.Count(h => h.Type == HexType.OceanReserved);
        Assert.Equal(12, oceanCount);
    }

    [Fact]
    public void MapDefinitions_Tharsis_Has5NamedLocations()
    {
        var map = MapDefinitions.Tharsis;
        var namedCount = map.Hexes.Values.Count(h => h.Type == HexType.Named);
        Assert.Equal(5, namedCount);
    }

    [Fact]
    public void MapDefinitions_Tharsis_Has5Milestones()
    {
        Assert.Equal(5, MapDefinitions.Tharsis.MilestoneNames.Length);
    }

    [Fact]
    public void MapDefinitions_Tharsis_Has5Awards()
    {
        Assert.Equal(5, MapDefinitions.Tharsis.AwardNames.Length);
    }

    [Fact]
    public void MapDefinitions_Hellas_HasSouthPole()
    {
        var map = MapDefinitions.Hellas;
        var southPole = map.Hexes[new HexCoord(5, 9)];
        Assert.Equal("South Pole", southPole.ReservedFor);
        Assert.Contains(PlacementBonus.Ocean, southPole.Bonuses);
    }

    [Fact]
    public void MapDefinitions_Elysium_Has4VolcanicAreas()
    {
        Assert.Equal(4, MapDefinitions.Elysium.VolcanicAreas.Length);
    }

    [Fact]
    public void MapDefinitions_Hellas_HasNoVolcanicAreas()
    {
        Assert.Empty(MapDefinitions.Hellas.VolcanicAreas);
    }

    [Fact]
    public void MapDefinitions_AllMaps_HaveCorrectNames()
    {
        Assert.Equal(MapName.Tharsis, MapDefinitions.Tharsis.Name);
        Assert.Equal(MapName.Hellas, MapDefinitions.Hellas.Name);
        Assert.Equal(MapName.Elysium, MapDefinitions.Elysium.Name);
    }

    [Fact]
    public void GameState_AllParametersMaxed_DetectsCorrectly()
    {
        var state = CreateMinimalGameState() with
        {
            Oxygen = Constants.MaxOxygen,
            Temperature = Constants.MaxTemperature,
            OceansPlaced = Constants.MaxOceans,
        };

        Assert.True(state.AllParametersMaxed);
    }

    [Fact]
    public void GameState_AllParametersMaxed_FalseWhenNotMaxed()
    {
        var state = CreateMinimalGameState();
        Assert.False(state.AllParametersMaxed);
    }

    [Fact]
    public void GameState_UpdatePlayer_ModifiesCorrectPlayer()
    {
        var state = CreateMinimalGameState();
        var updated = state.UpdatePlayer(0, p => p with { TerraformRating = 25 });

        Assert.Equal(25, updated.Players[0].TerraformRating);
        Assert.Equal(20, updated.Players[1].TerraformRating);
    }

    [Fact]
    public void GameState_IsJsonSerializable()
    {
        var state = CreateMinimalGameState();
        var json = JsonSerializer.Serialize(state);
        Assert.NotEmpty(json);
        Assert.Contains("GameId", json);
    }

    private static GameState CreateMinimalGameState() => new()
    {
        GameId = "test-game",
        Map = MapName.Tharsis,
        CorporateEra = true,
        DraftVariant = false,
        PreludeExpansion = false,
        Phase = GamePhase.Action,
        Generation = 1,
        ActivePlayerIndex = 0,
        FirstPlayerIndex = 0,
        Oxygen = 0,
        Temperature = -30,
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
