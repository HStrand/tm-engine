using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Tests.Engine;

public class VastitasBorealisTests
{
    private static readonly HexCoord Centre = new(5, 5);

    private static GameState CreateTestState()
    {
        return new GameState
        {
            GameId = "test",
            Map = MapName.VastitasBorealis,
            CorporateEra = true,
            DraftVariant = false,
            PreludeExpansion = false,
            Phase = GamePhase.Action,
            Generation = 1,
            ActivePlayerId = 0,
            FirstPlayerId = 0,
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
        };
    }

    private static GameState WithMoney(GameState state, int megaCredits) =>
        state.UpdatePlayer(0, p => p with { Resources = p.Resources with { MegaCredits = megaCredits } });

    private static GameState WithTiles(GameState state, int owner, TileType type, params HexCoord[] hexes) =>
        state with { PlacedTiles = state.PlacedTiles.AddRange(hexes.Select(h => KeyValuePair.Create(h, new PlacedTile(type, owner, h)))) };

    private static GameState WithOceans(GameState state, params HexCoord[] hexes) =>
        state with { PlacedTiles = state.PlacedTiles.AddRange(hexes.Select(h => KeyValuePair.Create(h, new PlacedTile(TileType.Ocean, null, h)))) };

    // ── The board ──────────────────────────────────────────────

    [Fact]
    public void Board_HasTheStandardShape_TwelveOceanSpaces_AndFourVolcanoes()
    {
        var map = MapDefinitions.GetMap(MapName.VastitasBorealis);

        Assert.Equal(61, map.Hexes.Count);
        Assert.Equal(12, map.Hexes.Values.Count(h => h.Type == HexType.OceanReserved));
        Assert.Equal(4, map.VolcanicAreas.Length);
        Assert.Equal(["Agronomist", "Engineer", "Spacefarer", "Geologist", "Farmer"], map.MilestoneNames);
        Assert.Equal(["Traveller", "Landscaper", "Highlander", "Promoter", "Blacksmith"], map.AwardNames);
    }

    [Fact]
    public void Board_OnlyTheCentreHexHasTheTemperatureBonus()
    {
        var map = MapDefinitions.VastitasBorealis;

        var withBonus = map.Hexes.Values.Where(h => h.Bonuses.Contains(PlacementBonus.Temperature)).Select(h => h.Coord);

        Assert.Equal([Centre], withBonus);
        Assert.Equal(HexType.Land, map.Hexes[Centre].Type);
    }

    // ── The centre hex ─────────────────────────────────────────

    [Fact]
    public void PlacingOnTheCentreHex_CostsFourMore_AndRaisesTheTemperature()
    {
        var state = CreateTestState().UpdatePlayer(0, p => p with
        {
            Resources = p.Resources with { MegaCredits = 50 },
        });

        var (newState, result) = GameEngine.Apply(state, new Moves.GreeneryMove(0, Centre));
        Assert.True(result.IsSuccess, $"Expected success but got: {result}");

        Assert.Equal(TileType.Greenery, newState.PlacedTiles[Centre].Type);
        Assert.Equal(50 - Constants.GreeneryCost - 4, newState.Players[0].Resources.MegaCredits);
        Assert.Equal(Constants.MinTemperature + Constants.TemperatureStep, newState.Temperature);
        // One step for the greenery's oxygen, one for the temperature.
        Assert.Equal(state.Players[0].TerraformRating + 2, newState.Players[0].TerraformRating);
        Assert.Null(newState.PendingAction);
    }

    [Fact]
    public void CentreHex_IsNotOffered_WhenThePlayerCannotAffordTheExtraFour()
    {
        var state = CreateTestState().UpdatePlayer(0, p => p with
        {
            Resources = p.Resources with { MegaCredits = Constants.GreeneryCost + 3 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);

        var greenery = moves.Actions!.StandardProjects.First(sp => sp.Project == StandardProject.Greenery);
        Assert.DoesNotContain(Centre, greenery.ValidLocations);
        Assert.NotEmpty(greenery.ValidLocations);
    }

    [Fact]
    public void CentreHex_StillCostsFour_WhenTheTemperatureIsAtItsMaximum()
    {
        // BGA charges the 4 M€ at +8°C too (table 831406044, move 723).
        var state = WithMoney(CreateTestState(), 20) with { Temperature = Constants.DefaultMaxTemperature };

        var newState = GlobalParameters.PlaceTileOnBoard(state, TileType.City, 0, Centre);

        Assert.Equal(20 - 4, newState.Players[0].Resources.MegaCredits);
        Assert.Equal(Constants.DefaultMaxTemperature, newState.Temperature);
        Assert.Equal(state.Players[0].TerraformRating, newState.Players[0].TerraformRating);
    }

    [Fact]
    public void CentreHex_PaysOutOceanAdjacencyBeforeTheFourIsCharged()
    {
        // (6,5) is an ocean space next to the centre.
        var state = WithOceans(WithMoney(CreateTestState(), 20), new HexCoord(6, 5));

        var newState = GlobalParameters.PlaceTileOnBoard(state, TileType.City, 0, Centre);

        Assert.Equal(20 + Constants.OceanAdjacencyBonus - 4, newState.Players[0].Resources.MegaCredits);
    }

    [Fact]
    public void OtherHexes_CostNothingExtra()
    {
        var state = CreateTestState();

        Assert.Equal(4, BoardLogic.GetExtraPlacementCost(state, Centre));
        Assert.Equal(0, BoardLogic.GetExtraPlacementCost(state, new HexCoord(4, 5)));
    }

    // ── Milestones ─────────────────────────────────────────────

    [Fact]
    public void Geologist_CountsTilesOnAndNextToVolcanoes()
    {
        var albaMons = new HexCoord(2, 4);
        var nextToAlbaMons = albaMons.GetAdjacentCoords().First(c => MapDefinitions.VastitasBorealis.Hexes.ContainsKey(c));
        var farAway = new HexCoord(6, 8);
        var state = WithTiles(CreateTestState(), 0, TileType.Greenery, albaMons, nextToAlbaMons, farAway);
        state = WithTiles(state, 1, TileType.City, new HexCoord(5, 1)); // the other player's, on Hecates Tholus

        Assert.Equal(2, MilestoneAndAwardLogic.GetMilestoneScore(state, state.GetPlayer(0), "Geologist"));
        Assert.Equal(1, MilestoneAndAwardLogic.GetMilestoneScore(state, state.GetPlayer(1), "Geologist"));
        Assert.Equal(3, MilestoneAndAwardLogic.GetMilestoneThreshold("Geologist"));
    }

    [Fact]
    public void Engineer_AddsEnergyAndHeatProduction()
    {
        var state = CreateTestState().UpdatePlayer(0, p => p with
        {
            Production = p.Production with { Energy = 4, Heat = 6, Steel = 9 },
        });

        Assert.Equal(10, MilestoneAndAwardLogic.GetMilestoneScore(state, state.GetPlayer(0), "Engineer"));
        Assert.Null(MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Engineer"));
        Assert.NotNull(MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 1, "Engineer"));
    }

    [Fact]
    public void Farmer_CountsAnimalsAndMicrobes_NotOtherCardResources()
    {
        // 033 Regolith Eaters holds microbes, 024 Predators animals, 005 Search For Life science.
        var state = CreateTestState().UpdatePlayer(0, p => p with
        {
            PlayedCards = ["033", "024", "005"],
            CardResources = ImmutableDictionary<string, int>.Empty.Add("033", 3).Add("024", 2).Add("005", 4),
        });

        Assert.Equal(5, MilestoneAndAwardLogic.GetMilestoneScore(state, state.GetPlayer(0), "Farmer"));
        Assert.Equal(5, MilestoneAndAwardLogic.GetMilestoneThreshold("Farmer"));
    }

    [Theory]
    [InlineData("Agronomist", 4)]
    [InlineData("Spacefarer", 4)]
    public void TagMilestones_NeedFourTags(string milestone, int threshold)
    {
        Assert.Equal(threshold, MilestoneAndAwardLogic.GetMilestoneThreshold(milestone));
    }

    // ── Awards ─────────────────────────────────────────────────

    [Fact]
    public void Landscaper_ScoresTheLargestConnectedGroupOfOwnTiles()
    {
        // Three in a row on row 8, and two together on row 2; the other player's tile joins nothing.
        var state = WithTiles(CreateTestState(), 0, TileType.Greenery,
            new HexCoord(3, 8), new HexCoord(4, 8), new HexCoord(5, 8),
            new HexCoord(5, 2), new HexCoord(6, 2));
        state = WithTiles(state, 1, TileType.City, new HexCoord(6, 8));

        Assert.Equal(3, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(0), "Landscaper"));
        Assert.Equal(1, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(1), "Landscaper"));
    }

    [Fact]
    public void Highlander_ScoresOwnTilesWithNoOceanNextToThem()
    {
        var state = WithOceans(CreateTestState(), new HexCoord(6, 5));
        state = WithTiles(state, 0, TileType.Greenery, Centre, new HexCoord(3, 8), new HexCoord(4, 8));

        // The centre is next to the ocean on (6,5); the two on row 8 are not next to any.
        Assert.Equal(2, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(0), "Highlander"));
    }

    [Fact]
    public void Blacksmith_AddsSteelAndTitaniumProduction()
    {
        var state = CreateTestState().UpdatePlayer(0, p => p with
        {
            Production = p.Production with { Steel = 3, Titanium = 2, Energy = 7 },
        });

        Assert.Equal(5, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(0), "Blacksmith"));
    }

    [Fact]
    public void Promoter_ScoresEventsPlayed()
    {
        var state = CreateTestState().UpdatePlayer(0, p => p with { PlayedEvents = ["009", "010"] });

        Assert.Equal(2, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(0), "Promoter"));
    }
}
