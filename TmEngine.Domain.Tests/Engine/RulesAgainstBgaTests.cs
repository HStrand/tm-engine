using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Engine;

/// <summary>
/// Rules the engine got wrong until imported Board Game Arena positions showed the difference.
/// </summary>
public class RulesAgainstBgaTests
{
    private const string Capital = "008";
    private const string CommercialDistrict = "085";
    private const string ImmigrationShuttles = "198";
    private const string RadSuits = "186";
    private const string ResearchCoordination = "P40"; // one wild tag
    private const string Helion = "CORP03";
    private const string Credicor = "CORP01";
    private const string Fish = "052";
    private const string ViralEnhancers = "074";

    private static GameState CreateState(Func<PlayerState, PlayerState>? player0 = null) => new()
    {
        GameId = "test",
        Map = MapName.Tharsis,
        CorporateEra = true,
        DraftVariant = false,
        PreludeExpansion = true,
        Phase = GamePhase.Action,
        Generation = 5,
        ActivePlayerId = 0,
        FirstPlayerId = 0,
        Oxygen = 5,
        Temperature = -10,
        OceansPlaced = 4,
        Players =
        [
            (player0 ?? (p => p))(PlayerState.CreateInitial(0, 20) with { CorporationId = Credicor }),
            PlayerState.CreateInitial(1, 20) with { CorporationId = Credicor },
        ],
        PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
        ClaimedMilestones = [],
        FundedAwards = [],
        DrawPile = [],
        DiscardPile = [],
        MoveNumber = 0,
    };

    private static GameState WithTiles(GameState state, params PlacedTile[] tiles) =>
        state with { PlacedTiles = tiles.ToImmutableDictionary(t => t.Location) };

    private static int CardPoints(GameState state, string cardId) =>
        Scoring.CalculateCardPoints(state, state.GetPlayer(0), cardId);

    // ── Cards scored from the board ────────────────────────────

    [Fact]
    public void Capital_ScoresOnePointPerAdjacentOcean()
    {
        // (5,5) is next to (4,5) and (6,5) in its own row.
        var state = WithTiles(CreateState(p => p with { PlayedCards = [Capital] }),
            new PlacedTile(TileType.Capital, 0, new HexCoord(5, 5)),
            new PlacedTile(TileType.Ocean, null, new HexCoord(4, 5)),
            new PlacedTile(TileType.Ocean, null, new HexCoord(6, 5)),
            new PlacedTile(TileType.Ocean, null, new HexCoord(8, 2)));

        Assert.Equal(2, CardPoints(state, Capital));
        Assert.Equal(2, Scoring.CalculateFinalScores(state).First(s => s.PlayerId == 0).CardPoints);
    }

    [Fact]
    public void CommercialDistrict_ScoresOnePointPerAdjacentCity_WhoeverOwnsIt()
    {
        var state = WithTiles(CreateState(p => p with { PlayedCards = [CommercialDistrict] }),
            new PlacedTile(TileType.CommercialDistrict, 0, new HexCoord(5, 5)),
            new PlacedTile(TileType.City, 0, new HexCoord(4, 5)),
            new PlacedTile(TileType.City, 1, new HexCoord(6, 5)),
            new PlacedTile(TileType.City, 1, new HexCoord(8, 2)),
            new PlacedTile(TileType.Greenery, 0, new HexCoord(5, 4)));

        Assert.Equal(2, CardPoints(state, CommercialDistrict));
    }

    [Fact]
    public void CommercialDistrict_ScoresNothing_WithNoCityNextToIt()
    {
        var state = WithTiles(CreateState(p => p with { PlayedCards = [CommercialDistrict] }),
            new PlacedTile(TileType.CommercialDistrict, 0, new HexCoord(5, 5)),
            new PlacedTile(TileType.City, 1, new HexCoord(8, 2)));

        Assert.Equal(0, CardPoints(state, CommercialDistrict));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(3, 1)]
    [InlineData(5, 1)]
    [InlineData(6, 2)]
    public void ImmigrationShuttles_ScoresOnePointForEveryThirdCityInPlay(int citiesInPlay, int expectedPoints)
    {
        // Cities of both players count, and so does the off-map one below.
        var tiles = Enumerable.Range(0, citiesInPlay - 1)
            .Select(i => new PlacedTile(i % 2 == 0 ? TileType.City : TileType.Capital, i % 2, new HexCoord(3 + i, 1)))
            .ToArray();
        var state = WithTiles(CreateState(p => p with { PlayedCards = [ImmigrationShuttles] }), tiles) with
        {
            OffMapTiles = [new OffMapTile("Phobos Space Haven", TileType.City, 1)],
        };

        Assert.Equal(expectedPoints, CardPoints(state, ImmigrationShuttles));
    }

    // ── Cities in play ─────────────────────────────────────────

    [Fact]
    public void RadSuits_CanBePlayed_WhenTheTwoCitiesInPlayBelongToSomeoneElse()
    {
        var state = WithTiles(CreateState(p => p with { Hand = [RadSuits], Resources = new ResourceSet(MegaCredits: 20) }),
            new PlacedTile(TileType.City, 1, new HexCoord(4, 5))) with
        {
            OffMapTiles = [new OffMapTile("Ganymede Colony", TileType.City, 1)],
        };

        var playable = LegalMoveGenerator.GetLegalMoves(state, 0).Actions!.PlayableCards;
        var (_, result) = GameEngine.Apply(state, new PlayCardMove(0, RadSuits, new PaymentInfo(MegaCredits: 6)));

        Assert.Contains(playable, c => c.CardId == RadSuits);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void RadSuits_CannotBePlayed_WithOnlyOneCityInPlay()
    {
        var state = WithTiles(CreateState(p => p with { Hand = [RadSuits], Resources = new ResourceSet(MegaCredits: 20) }),
            new PlacedTile(TileType.City, 0, new HexCoord(4, 5)));

        Assert.DoesNotContain(LegalMoveGenerator.GetLegalMoves(state, 0).Actions!.PlayableCards, c => c.CardId == RadSuits);
    }

    // ── Wild tags and milestones ───────────────────────────────

    // Seven cards with exactly one building tag each.
    private static readonly ImmutableList<string> SevenBuildingCards = ["007", "015", "038", "041", "056", "064", "065"];

    [Fact]
    public void Builder_CountsAWildTagAsABuildingTag()
    {
        var withoutWild = CreateState(p => p with { PlayedCards = SevenBuildingCards, Resources = new ResourceSet(MegaCredits: 8) });
        var withWild = CreateState(p => p with { PlayedCards = SevenBuildingCards.Add(ResearchCoordination), Resources = new ResourceSet(MegaCredits: 8) });
        Assert.Equal(7, withWild.GetPlayer(0).CountTag(Tag.Building, CardRegistry.GetTags));

        Assert.NotNull(MilestoneAndAwardLogic.CheckMilestoneEligibility(withoutWild, 0, "Builder"));
        Assert.Null(MilestoneAndAwardLogic.CheckMilestoneEligibility(withWild, 0, "Builder"));

        Assert.Contains(LegalMoveGenerator.GetLegalMoves(withWild, 0).Actions!.ClaimableMilestones, m => m.Name == "Builder");
        Assert.True(GameEngine.Apply(withWild, new ClaimMilestoneMove(0, "Builder")).Result.IsSuccess);
    }

    [Fact]
    public void RimSettler_CountsAWildTagAsAJovianTag()
    {
        // Colonizer Training Camp and Asteroid Mining Consortium have one Jovian tag each.
        var state = CreateState(p => p with { PlayedCards = ["001", "002", ResearchCoordination] }) with { Map = MapName.Hellas };

        Assert.Equal(3, MilestoneAndAwardLogic.GetMilestoneScore(state, state.GetPlayer(0), "Rim Settler"));
        Assert.Null(MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Rim Settler"));
    }

    [Theory]
    [InlineData("Ecologist", 1)]   // bio tags
    [InlineData("Diversifier", 1)] // a tag type of its own
    public void OtherTagMilestones_CountAWildTag(string milestone, int expected)
    {
        var state = CreateState(p => p with { PlayedCards = [ResearchCoordination], CorporationId = "" });

        Assert.Equal(expected, MilestoneAndAwardLogic.GetMilestoneScore(state, state.GetPlayer(0), milestone));
    }

    [Fact]
    public void Awards_DoNotCountWildTags()
    {
        var state = CreateState(p => p with { PlayedCards = SevenBuildingCards.Add(ResearchCoordination) });

        Assert.Equal(7, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(0), "Contractor"));
        Assert.Equal(0, MilestoneAndAwardLogic.GetAwardScore(state, state.GetPlayer(0), "Scientist"));
    }

    // ── Helion: heat as MC ─────────────────────────────────────

    private static GameState HelionWith(int megaCredits, int heat, string corporation = Helion) =>
        CreateState(p => p with { CorporationId = corporation, Resources = new ResourceSet(MegaCredits: megaCredits, Heat: heat) });

    [Fact]
    public void Helion_PaysForAStandardProject_WithHeatCoveringWhatMegaCreditsCannot()
    {
        var state = HelionWith(megaCredits: 5, heat: 10);

        var option = LegalMoveGenerator.GetLegalMoves(state, 0).Actions!.StandardProjects.Single(p => p.Project == StandardProject.PowerPlant);
        var (after, result) = GameEngine.Apply(state, new PowerPlantMove(0));

        Assert.True(option.Available);
        Assert.True(result.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 0, Heat: 4), after.GetPlayer(0).Resources);
        Assert.Equal(1, after.GetPlayer(0).Production.Energy);
    }

    [Fact]
    public void Helion_SpendsNoHeat_WhenMegaCreditsCoverTheCost()
    {
        var (after, _) = GameEngine.Apply(HelionWith(megaCredits: 20, heat: 10), new PowerPlantMove(0));

        Assert.Equal(new ResourceSet(MegaCredits: 9, Heat: 10), after.GetPlayer(0).Resources);
    }

    [Fact]
    public void OtherCorporations_CannotPayForAStandardProjectWithHeat()
    {
        var state = HelionWith(megaCredits: 5, heat: 10, corporation: Credicor);

        var option = LegalMoveGenerator.GetLegalMoves(state, 0).Actions!.StandardProjects.Single(p => p.Project == StandardProject.PowerPlant);
        var (_, result) = GameEngine.Apply(state, new PowerPlantMove(0));

        Assert.False(option.Available);
        Assert.True(result.IsError);
    }

    [Fact]
    public void Helion_CanPayForCityAndGreeneryProjects_AsInTheRealGamesThatShowedTheGap()
    {
        // 13 MC + 10 heat for a greenery (23); 11 MC + 14 heat for a city (25).
        var greenery = LegalMoveGenerator.GetLegalMoves(
            WithTiles(HelionWith(megaCredits: 13, heat: 10), new PlacedTile(TileType.City, 0, new HexCoord(5, 3))), 0).Actions!;
        var city = LegalMoveGenerator.GetLegalMoves(HelionWith(megaCredits: 11, heat: 14), 0).Actions!;

        Assert.True(greenery.StandardProjects.Single(p => p.Project == StandardProject.Greenery).Available);
        Assert.True(city.StandardProjects.Single(p => p.Project == StandardProject.City).Available);
    }

    [Fact]
    public void Helion_CanClaimAMilestoneAndFundAnAward_WithHeat()
    {
        var state = HelionWith(megaCredits: 2, heat: 14).UpdatePlayer(0, p => p with { TerraformRating = 35 });

        var actions = LegalMoveGenerator.GetLegalMoves(state, 0).Actions!;
        Assert.Contains(actions.ClaimableMilestones, m => m.Name == "Terraformer");
        Assert.NotEmpty(actions.FundableAwards);

        var (after, result) = GameEngine.Apply(state, new ClaimMilestoneMove(0, "Terraformer"));
        Assert.True(result.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 0, Heat: 8), after.GetPlayer(0).Resources);
    }

    // ── Helion: choosing the split ─────────────────────────────

    [Fact]
    public void Helion_CanChooseToSpendHeat_EvenWhenMegaCreditsWouldCoverTheCost()
    {
        var (after, result) = GameEngine.Apply(HelionWith(megaCredits: 20, heat: 10), new PowerPlantMove(0) { Heat = 10 });

        Assert.True(result.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 19, Heat: 0), after.GetPlayer(0).Resources);
    }

    [Fact]
    public void Helion_CanChooseToSpendNoHeat()
    {
        var (after, result) = GameEngine.Apply(HelionWith(megaCredits: 20, heat: 10), new PowerPlantMove(0) { Heat = 0 });

        Assert.True(result.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 9, Heat: 10), after.GetPlayer(0).Resources);
    }

    [Theory]
    [InlineData(5, 10, 0, "Not enough MC")]   // 11 MC left to pay, 5 held
    [InlineData(5, 10, 3, "Not enough MC")]   // 8 MC left to pay, 5 held
    [InlineData(20, 10, 12, "Cannot pay 12 heat towards a cost of 11")]
    [InlineData(20, 4, 6, "Not enough heat")]
    [InlineData(20, 10, -1, "cannot be negative")]
    public void Helion_ChosenSplit_MustAddUp(int megaCredits, int heatHeld, int heatChosen, string expectedError)
    {
        var state = HelionWith(megaCredits, heatHeld);

        var (after, result) = GameEngine.Apply(state, new PowerPlantMove(0) { Heat = heatChosen });

        Assert.Contains(expectedError, Assert.IsType<Error>(result).Message);
        Assert.Equal(state.GetPlayer(0).Resources, after.GetPlayer(0).Resources);
    }

    [Fact]
    public void OtherCorporations_CannotChooseToSpendHeat()
    {
        var state = HelionWith(megaCredits: 20, heat: 10, corporation: Credicor);

        var (_, result) = GameEngine.Apply(state, new PowerPlantMove(0) { Heat = 2 });

        Assert.Contains("requires Helion", Assert.IsType<Error>(result).Message);
    }

    [Fact]
    public void Helion_ChosenHeat_AppliesToMilestonesAwardsAndMegaCreditCardActions()
    {
        var restrictedArea = CardRegistry.All.Values.Single(c => c.Definition.Name == "Restricted Area").Definition.Id; // action: 2 MC
        var state = HelionWith(megaCredits: 30, heat: 20).UpdatePlayer(0, p => p with
        {
            TerraformRating = 35,
            PlayedCards = [restrictedArea],
        }) with { DrawPile = ["001", "002"] };

        var (afterMilestone, milestone) = GameEngine.Apply(state, new ClaimMilestoneMove(0, "Terraformer") { Heat = 8 });
        var (afterAward, award) = GameEngine.Apply(state, new FundAwardMove(0, "Banker") { Heat = 5 });
        var (afterAction, action) = GameEngine.Apply(state, new UseCardActionMove(0, restrictedArea) { Heat = 2 });

        Assert.True(milestone.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 30, Heat: 12), afterMilestone.GetPlayer(0).Resources);
        Assert.True(award.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 27, Heat: 15), afterAward.GetPlayer(0).Resources);
        Assert.True(action.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 30, Heat: 18), afterAction.GetPlayer(0).Resources);
    }

    [Fact]
    public void Helion_ChosenHeat_AppliesToBuyingResearchCards()
    {
        var state = HelionWith(megaCredits: 10, heat: 10) with
        {
            Phase = GamePhase.Research,
            Research = new ResearchState
            {
                AvailableCards = [["001", "002", "003", "004"], []],
                Submitted = [false, false],
            },
        };

        var (after, result) = GameEngine.Apply(state, new BuyCardsMove(0, ["001", "002"]) { Heat = 6 });

        Assert.True(result.IsSuccess);
        Assert.Equal(new ResourceSet(MegaCredits: 10, Heat: 4), after.GetPlayer(0).Resources);
        Assert.Equal(["001", "002"], after.GetPlayer(0).Hand);
    }

    [Fact]
    public void Helion_ChosenHeat_CoversTheSouthPoleOcean()
    {
        // Converting plants onto the Hellas South Pole costs 6 MC more, for the ocean it grants.
        var southPole = new HexCoord(5, 9);
        var state = CreateState(p => p with
        {
            CorporationId = Helion,
            Resources = new ResourceSet(MegaCredits: 0, Plants: 8, Heat: 9),
        }) with { Map = MapName.Hellas };

        var (after, result) = GameEngine.Apply(state, new ConvertPlantsMove(0, southPole) { Heat = 6 });

        Assert.True(result.IsSuccess, (result as Error)?.Message);
        Assert.Equal(0, after.GetPlayer(0).Resources.MegaCredits);
        Assert.Equal(3, after.GetPlayer(0).Resources.Heat);
        Assert.IsType<PlaceTilePending>(after.PendingAction);
    }

    [Fact]
    public void ChosenHeat_IsRefused_OnMovesThatSayHowTheyArePaid()
    {
        var state = HelionWith(megaCredits: 20, heat: 10).UpdatePlayer(0, p => p with { Hand = [RadSuits] });
        var withCities = state with { OffMapTiles = [new("Phobos Space Haven", TileType.City, 1), new("Ganymede Colony", TileType.City, 1)] };

        var (_, result) = GameEngine.Apply(withCities, new PlayCardMove(0, RadSuits, new PaymentInfo(MegaCredits: 6)) { Heat = 2 });

        Assert.Contains("Cannot pay 2 heat towards a cost of 0", Assert.IsType<Error>(result).Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void FixedCost_IncludesTheSouthPoleOcean_ForProjectsPlacedThere(int expectedExtra)
    {
        var state = CreateState() with { Map = expectedExtra > 0 ? MapName.Hellas : MapName.Tharsis };

        Assert.Equal(Constants.GreeneryCost + expectedExtra, Wallet.FixedCost(state, new GreeneryMove(0, new HexCoord(5, 9))));
        Assert.Equal(Constants.CityCost + expectedExtra, Wallet.FixedCost(state, new CityMove(0, new HexCoord(5, 9))));
    }

    [Fact]
    public void Wallet_Pay_LeavesOtherResourcesAlone()
    {
        var player = PlayerState.CreateInitial(0, 20) with
        {
            CorporationId = Helion,
            Resources = new ResourceSet(MegaCredits: 3, Steel: 4, Titanium: 5, Plants: 6, Energy: 7, Heat: 8),
        };

        Assert.Equal(11, Wallet.Spendable(player));
        Assert.Equal(new ResourceSet(MegaCredits: 0, Steel: 4, Titanium: 5, Plants: 6, Energy: 7, Heat: 1), Wallet.Pay(player, 10));
    }

    // ── Land Claim ─────────────────────────────────────────────

    [Fact]
    public void LandClaim_KeepsOtherPlayersOffTheHex_ForEveryKindOfTile()
    {
        // BGA table 924354275: the engine offered a greenery on the hex the opponent had claimed.
        var claimed = new HexCoord(4, 6);
        var state = CreateState() with { ClaimedHexes = ImmutableDictionary<HexCoord, int>.Empty.Add(claimed, 1) };

        foreach (var tile in Enum.GetValues<TileType>().Where(t => t != TileType.Ocean && t != TileType.MoholeArea))
        {
            Assert.DoesNotContain(claimed, BoardLogic.GetValidTilePlacements(state, tile, playerId: 0));
        }
        Assert.Contains(claimed, BoardLogic.GetValidGreeneryPlacements(state, playerId: 1));
        Assert.Contains(claimed, BoardLogic.GetValidCityPlacements(state, playerId: 1));
        Assert.Contains(claimed, BoardLogic.GetValidTilePlacements(state, TileType.NaturalPreserve, playerId: 1));
    }

    // ── Trigger ordering ───────────────────────────────────────

    [Fact]
    public void Fish_StillCutsPlantProduction_WhenViralEnhancersAsksItsQuestion()
    {
        // BGA table 924354275: with Viral Enhancers in play, Fish went straight to the Viral Enhancers
        // choice and nobody lost plant production.
        var state = CreateState(p => p with
        {
            Hand = [Fish],
            PlayedCards = [ViralEnhancers],
            Resources = new ResourceSet(MegaCredits: 20),
            Production = new ProductionSet(Plants: 2),
        }) with { Temperature = 2 };
        state = state.UpdatePlayer(1, p => p with { Production = new ProductionSet(Plants: 3) });

        var (afterPlay, played) = GameEngine.Apply(state, new PlayCardMove(0, Fish, new PaymentInfo(MegaCredits: 9)));
        Assert.True(played.IsSuccess);
        var reduce = Assert.IsType<ReduceProductionPending>(afterPlay.PendingAction);
        Assert.Equal([0, 1], reduce.ValidTargetPlayerIds);

        var (afterTarget, targeted) = GameEngine.Apply(afterPlay, new ChooseTargetPlayerMove(0, 1));
        Assert.True(targeted.IsSuccess);
        Assert.Equal(2, afterTarget.GetPlayer(1).Production.Plants);
        var viral = Assert.IsType<ChooseOptionPending>(afterTarget.PendingAction);
        Assert.Equal(Fish, viral.SourceCardId);

        var (afterChoice, chosen) = GameEngine.Apply(afterTarget, new ChooseOptionMove(0, 1));
        Assert.True(chosen.IsSuccess);
        Assert.Null(afterChoice.PendingAction);
        Assert.Equal(1, afterChoice.GetPlayer(0).CardResources[Fish]);
        Assert.Equal(2, afterChoice.GetPlayer(1).Production.Plants);
    }
}
