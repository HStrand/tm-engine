using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Engine;

public class GameEngineTests
{
    private static GameState CreateTestGame(int playerCount = 2)
    {
        var players = ImmutableList.CreateBuilder<PlayerState>();
        for (int i = 0; i < playerCount; i++)
        {
            players.Add(PlayerState.CreateInitial(i, 20) with
            {
                Resources = new ResourceSet(
                    MegaCredits: 100,
                    Steel: 10,
                    Titanium: 10,
                    Plants: 20,
                    Energy: 10,
                    Heat: 20),
            });
        }

        return new GameState
        {
            GameId = "test",
            Map = MapName.Tharsis,
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
            Players = players.ToImmutable(),
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = [],
            DiscardPile = [],
            MoveNumber = 0,

        };
    }

    // ── Pass & Turn Flow ───────────────────────────────────────

    [Fact]
    public void Pass_SwitchesToNextPlayer()
    {
        var state = CreateTestGame();
        Assert.Equal(0, state.ActivePlayerId);

        var (newState, result) = GameEngine.Apply(state, new PassMove(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, newState.ActivePlayerId);
        Assert.True(newState.Players[0].Passed);
    }

    [Fact]
    public void AllPlayersPass_TriggersProductionPhase()
    {
        var state = CreateTestGame();

        // Player 0 passes
        var (s1, r1) = GameEngine.Apply(state, new PassMove(0));
        Assert.True(r1.IsSuccess);

        // Player 1 passes
        var (s2, r2) = GameEngine.Apply(s1, new PassMove(1));
        Assert.True(r2.IsSuccess);

        // After production, should be in Research phase for generation 2
        Assert.Equal(GamePhase.Research, s2.Phase);
        Assert.Equal(2, s2.Generation);
    }

    [Fact]
    public void WrongPlayer_IsRejected()
    {
        var state = CreateTestGame();
        Assert.Equal(0, state.ActivePlayerId);

        var (_, result) = GameEngine.Apply(state, new PassMove(1));

        Assert.True(result.IsError);
    }

    [Fact]
    public void PlayerCanTake2Actions_ThenTurnAdvances()
    {
        var state = CreateTestGame();

        // Player 0, action 1: convert heat
        var (s1, r1) = GameEngine.Apply(state, new ConvertHeatMove(0));
        Assert.True(r1.IsSuccess);
        Assert.Equal(0, s1.ActivePlayerId); // Still player 0's turn

        // Player 0, action 2: convert heat again
        var (s2, r2) = GameEngine.Apply(s1, new ConvertHeatMove(0));
        Assert.True(r2.IsSuccess);
        Assert.Equal(1, s2.ActivePlayerId); // Now player 1's turn
    }

    // ── Convert Heat ───────────────────────────────────────────

    [Fact]
    public void ConvertHeat_Spends8Heat_RaisesTemperature()
    {
        var state = CreateTestGame();
        var initialTemp = state.Temperature;
        var initialHeat = state.Players[0].Resources.Heat;
        var initialTR = state.Players[0].TerraformRating;

        var (newState, result) = GameEngine.Apply(state, new ConvertHeatMove(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(initialTemp + Constants.TemperatureStep, newState.Temperature);
        Assert.Equal(initialHeat - Constants.HeatPerTemperature, newState.Players[0].Resources.Heat);
        Assert.Equal(initialTR + 1, newState.Players[0].TerraformRating);
    }

    [Fact]
    public void ConvertHeat_FailsWithInsufficientHeat()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Resources = p.Resources with { Heat = 3 },
        });

        var (_, result) = GameEngine.Apply(state, new ConvertHeatMove(0));
        Assert.True(result.IsError);
    }

    [Fact]
    public void ConvertHeat_WhenTemperatureMaxed_SpendsHeatButNoEffect()
    {
        var state = CreateTestGame() with { Temperature = Constants.DefaultMaxTemperature };
        var initialHeat = state.Players[0].Resources.Heat;
        var initialTR = state.Players[0].TerraformRating;

        var (newState, result) = GameEngine.Apply(state, new ConvertHeatMove(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(Constants.DefaultMaxTemperature, newState.Temperature);
        Assert.Equal(initialHeat - Constants.HeatPerTemperature, newState.Players[0].Resources.Heat);
        Assert.Equal(initialTR, newState.Players[0].TerraformRating);
    }

    // ── Convert Plants ─────────────────────────────────────────

    [Fact]
    public void ConvertPlants_Spends8Plants_PlacesGreenery_RaisesOxygen()
    {
        var state = CreateTestGame();
        var initialO2 = state.Oxygen;
        var initialPlants = state.Players[0].Resources.Plants;
        var initialTR = state.Players[0].TerraformRating;

        // Pick a valid land hex on Tharsis (no bonus)
        var location = new HexCoord(5, 3);

        var (newState, result) = GameEngine.Apply(state, new ConvertPlantsMove(0, location));

        Assert.True(result.IsSuccess);
        Assert.Equal(initialO2 + 1, newState.Oxygen);
        Assert.Equal(initialPlants - Constants.PlantsPerGreenery, newState.Players[0].Resources.Plants);
        Assert.Equal(initialTR + 1, newState.Players[0].TerraformRating);
        Assert.True(newState.PlacedTiles.ContainsKey(location));
        Assert.Equal(TileType.Greenery, newState.PlacedTiles[location].Type);
    }

    [Fact]
    public void ConvertPlants_FailsOnOceanHex()
    {
        var state = CreateTestGame();
        var oceanHex = new HexCoord(4, 1); // Ocean-reserved on Tharsis

        var (_, result) = GameEngine.Apply(state, new ConvertPlantsMove(0, oceanHex));
        Assert.True(result.IsError);
    }

    // ── Standard Projects ──────────────────────────────────────

    [Fact]
    public void PowerPlant_Spends11MC_IncreasesEnergyProduction()
    {
        var state = CreateTestGame();
        var initialMC = state.Players[0].Resources.MegaCredits;
        var initialEnergyProd = state.Players[0].Production.Energy;

        var (newState, result) = GameEngine.Apply(state,
            new PowerPlantMove(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(initialMC - Constants.PowerPlantCost, newState.Players[0].Resources.MegaCredits);
        Assert.Equal(initialEnergyProd + 1, newState.Players[0].Production.Energy);
    }

    [Fact]
    public void Asteroid_Spends14MC_RaisesTemperature()
    {
        var state = CreateTestGame();
        var initialTemp = state.Temperature;

        var (newState, result) = GameEngine.Apply(state,
            new AsteroidMove(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(initialTemp + Constants.TemperatureStep, newState.Temperature);
    }

    [Fact]
    public void Aquifer_PlacesOcean_RaisesTR()
    {
        var state = CreateTestGame();
        var initialTR = state.Players[0].TerraformRating;
        var oceanHex = new HexCoord(4, 1); // Ocean hex on Tharsis

        var (newState, result) = GameEngine.Apply(state,
            new AquiferMove(0, oceanHex));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, newState.OceansPlaced);
        Assert.Equal(initialTR + 1, newState.Players[0].TerraformRating);
        Assert.Equal(TileType.Ocean, newState.PlacedTiles[oceanHex].Type);
        Assert.Null(newState.PlacedTiles[oceanHex].OwnerId); // Oceans are unowned
    }

    [Fact]
    public void City_PlacesCity_IncreasesMCProduction()
    {
        var state = CreateTestGame();
        var initialMCProd = state.Players[0].Production.MegaCredits;
        var landHex = new HexCoord(5, 3);

        var (newState, result) = GameEngine.Apply(state,
            new CityMove(0, landHex));

        Assert.True(result.IsSuccess);
        Assert.Equal(initialMCProd + 1, newState.Players[0].Production.MegaCredits);
        Assert.Equal(TileType.City, newState.PlacedTiles[landHex].Type);
        Assert.Equal(0, newState.PlacedTiles[landHex].OwnerId);
    }

    [Fact]
    public void City_CannotPlaceAdjacentToCity()
    {
        var state = CreateTestGame();

        // Place first city (player 0, action 1)
        var (s1, _) = GameEngine.Apply(state,
            new CityMove(0, new HexCoord(5, 3)));

        // Player 0 passes, now player 1's turn
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Player 1 tries adjacent hex (6,3 is adjacent to 5,3 on row 3 odd)
        var (_, result) = GameEngine.Apply(s2,
            new CityMove(1, new HexCoord(6, 3)));

        Assert.True(result.IsError);
    }

    [Fact]
    public void SellPatents_DiscardsCards_GainsMC()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("card1", "card2", "card3"),
        });

        var (newState, result) = GameEngine.Apply(state,
            new SellPatentsMove(0, ["card1", "card2"]));

        Assert.True(result.IsSuccess);
        Assert.Equal(102, newState.Players[0].Resources.MegaCredits); // 100 + 2
        Assert.Single(newState.Players[0].Hand); // 1 card left
    }

    // ── Plant Conversion ────────────────────────────────────────

    [Fact]
    public void ConvertPlants_Ecoline_CanConvertWith7Plants()
    {
        // Ecoline has PlantConversionModifierEffect(7) — only needs 7 plants
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            CorporationId = "CORP02", // Ecoline
            Resources = p.Resources with { Plants = 7 },
        });

        var greeneryLocations = BoardLogic.GetValidGreeneryPlacements(state, 0);
        var greeneryHex = greeneryLocations[0];
        var (newState, result) = GameEngine.Apply(state, new ConvertPlantsMove(0, greeneryHex));

        Assert.True(result.IsSuccess, $"Expected success but got error: {result}");
        Assert.Equal(0, newState.Players[0].Resources.Plants);
        Assert.Equal(TileType.Greenery, newState.PlacedTiles[greeneryHex].Type);
    }

    [Fact]
    public void ConvertPlants_Ecoline_LegalMovesAvailableWith7Plants()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            CorporationId = "CORP02", // Ecoline
            Resources = p.Resources with { Plants = 7 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.True(moves.Actions!.CanConvertPlants);
    }

    // ── Triggered Effects ──────────────────────────────────────

    [Fact]
    public void InterplanetaryCinematics_Gains2MC_WhenPlayingEvent()
    {
        // CORP05 effect: "When you play an event, gain 2 MC"
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            CorporationId = "CORP05",
            Hand = p.Hand.Add("036"), // Release of Inert Gases (Event, cost 14)
        });

        var initialMC = state.Players[0].Resources.MegaCredits;
        var (newState, result) = GameEngine.Apply(state,
            new PlayCardMove(0, "036", new PaymentInfo(14, 0, 0, 0)));

        Assert.True(result.IsSuccess, $"Expected success but got: {result}");
        // Should have spent 14 MC and gained 2 MC from the triggered effect
        Assert.Equal(initialMC - 14 + 2, newState.Players[0].Resources.MegaCredits);
    }

    [Fact]
    public void InterplanetaryCinematics_Gains2MC_WhenPlayingEventWithPendingAction()
    {
        // Virus (050) is an Event+Microbe card that creates a pending action (ChooseEffect).
        // CORP05's trigger should still fire even when the card's effects create a pending action.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            CorporationId = "CORP05",
            Hand = p.Hand.Add("050"), // Virus (Event, cost 1)
        });

        var initialMC = state.Players[0].Resources.MegaCredits;
        var (newState, result) = GameEngine.Apply(state,
            new PlayCardMove(0, "050", new PaymentInfo(1, 0, 0, 0)));

        Assert.True(result.IsSuccess, $"Expected success but got: {result}");
        // Should have spent 1 MC and gained 2 MC from the triggered effect, even with pending action
        Assert.Equal(initialMC - 1 + 2, newState.Players[0].Resources.MegaCredits);
    }

    // ── Milestones & Awards ────────────────────────────────────

    [Fact]
    public void ClaimMilestone_Costs8MC_RecordsClaim()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with { TerraformRating = 35 });

        var (newState, result) = GameEngine.Apply(state,
            new ClaimMilestoneMove(0, "Terraformer"));

        Assert.True(result.IsSuccess);
        Assert.Equal(92, newState.Players[0].Resources.MegaCredits); // 100 - 8
        Assert.Single(newState.ClaimedMilestones);
        Assert.Equal("Terraformer", newState.ClaimedMilestones[0].MilestoneName);
    }

    [Fact]
    public void ClaimMilestone_CannotClaimSameTwice()
    {
        var state = CreateTestGame();
        var (s1, _) = GameEngine.Apply(state, new ClaimMilestoneMove(0, "Terraformer"));
        // Skip to player 1's turn
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Player 1 tries same milestone - note we need to advance past player 0
        // Actually after claiming (1 action) player 0 still has actions. Let me adjust.
        // After claiming, player 0 has 1 action used. Let them pass.
        var s3 = s1; // player 0 has 1 action used
        var (s4, _) = GameEngine.Apply(s3, new PassMove(0)); // player 0 passes, now player 1

        var (_, result) = GameEngine.Apply(s4, new ClaimMilestoneMove(1, "Terraformer"));
        Assert.True(result.IsError);
    }

    [Fact]
    public void FundAward_CostsEscalate()
    {
        var state = CreateTestGame();

        // First award: 8 MC
        var (s1, r1) = GameEngine.Apply(state, new FundAwardMove(0, "Landlord"));
        Assert.True(r1.IsSuccess);
        Assert.Equal(92, s1.Players[0].Resources.MegaCredits);

        // Pass to player 1
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Second award: 14 MC (by player 1)
        var (s3, r3) = GameEngine.Apply(s2, new FundAwardMove(1, "Banker"));
        Assert.True(r3.IsSuccess);
        Assert.Equal(86, s3.Players[1].Resources.MegaCredits);
    }

    [Fact]
    public void FundAward_Max3Allowed()
    {
        var state = CreateTestGame() with
        {
            FundedAwards =
            [
                new AwardFunding("Landlord", 0),
                new AwardFunding("Banker", 1),
                new AwardFunding("Scientist", 0),
            ],
        };

        var (_, result) = GameEngine.Apply(state, new FundAwardMove(0, "Thermalist"));
        Assert.True(result.IsError);
    }

    // ── Temperature Bonus Effects ──────────────────────────────

    [Fact]
    public void Temperature_At0C_TriggersOceanBonus()
    {
        var state = CreateTestGame() with { Temperature = -2 };

        var (newState, _) = GameEngine.Apply(state, new ConvertHeatMove(0));

        // Temperature goes from -2 to 0, triggers ocean placement
        Assert.Equal(0, newState.Temperature);
        Assert.NotNull(newState.PendingAction);
        Assert.IsType<PlaceTilePending>(newState.PendingAction);
        Assert.Equal(TileType.Ocean, ((PlaceTilePending)newState.PendingAction).TileType);
    }

    [Fact]
    public void Temperature_AtMinus24_GrantsHeatProduction()
    {
        var state = CreateTestGame() with { Temperature = -26 };

        var (newState, _) = GameEngine.Apply(state, new ConvertHeatMove(0));

        Assert.Equal(-24, newState.Temperature);
        Assert.Equal(state.Players[0].Production.Heat + 1, newState.Players[0].Production.Heat);
        Assert.Null(newState.PendingAction);
    }

    [Fact]
    public void Temperature_AtMinus20_GrantsHeatProduction()
    {
        var state = CreateTestGame() with { Temperature = -22 };

        var (newState, _) = GameEngine.Apply(state, new ConvertHeatMove(0));

        Assert.Equal(-20, newState.Temperature);
        Assert.Equal(state.Players[0].Production.Heat + 1, newState.Players[0].Production.Heat);
        Assert.Null(newState.PendingAction);
    }

    [Fact]
    public void Temperature_MultiStep_HitsBothHeatProductionBonuses()
    {
        // Huge Asteroid raises temp 3 steps: -30 -> -28 -> -26 -> -24
        // Should hit the -24 bonus
        var state = CreateTestGame() with { Temperature = -30 };
        var initialHeatProd = state.Players[0].Production.Heat;

        // Give player card "P15" (Huge Asteroid prelude: +3 temp, -5 MC)
        // Simulate by directly calling RaiseTemperature 3 times
        for (int i = 0; i < 3; i++)
            state = GlobalParameters.RaiseTemperature(state, 0);

        Assert.Equal(-24, state.Temperature);
        Assert.Equal(initialHeatProd + 1, state.GetPlayer(0).Production.Heat);
    }

    [Fact]
    public void Temperature_MultiStep_PassingThroughBothBonuses()
    {
        // Starting at -26, raise 4 steps: -26 -> -24 -> -22 -> -20 -> -18
        // Should hit both -24 and -20 bonuses = +2 heat production
        var state = CreateTestGame() with { Temperature = -26 };
        var initialHeatProd = state.Players[0].Production.Heat;

        for (int i = 0; i < 4; i++)
            state = GlobalParameters.RaiseTemperature(state, 0);

        Assert.Equal(-18, state.Temperature);
        Assert.Equal(initialHeatProd + 2, state.GetPlayer(0).Production.Heat);
    }

    // ── Oxygen Bonus ───────────────────────────────────────────

    [Fact]
    public void Oxygen_At8Percent_AlsoRaisesTemperature()
    {
        var state = CreateTestGame() with { Oxygen = 7 };
        var initialTemp = state.Temperature;
        var location = new HexCoord(5, 3);

        var (newState, _) = GameEngine.Apply(state, new ConvertPlantsMove(0, location));

        Assert.Equal(8, newState.Oxygen);
        Assert.Equal(initialTemp + Constants.TemperatureStep, newState.Temperature);
        // Player gets TR for both oxygen (+1) and temperature (+1) = +2
        Assert.Equal(state.Players[0].TerraformRating + 2, newState.Players[0].TerraformRating);
    }

    // ── Ocean Adjacency Bonus ──────────────────────────────────

    [Fact]
    public void PlacingTileNextToOcean_Gains2MC()
    {
        var state = CreateTestGame();

        // Place an ocean
        var oceanHex = new HexCoord(4, 1);
        var (s1, _) = GameEngine.Apply(state,
            new AquiferMove(0, oceanHex));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Player 1 places city adjacent to ocean
        // (5,1) is adjacent to (4,1) on row 1
        var cityHex = new HexCoord(5, 1);
        var mcBefore = s2.Players[1].Resources.MegaCredits;

        var (s3, _) = GameEngine.Apply(s2,
            new CityMove(1, cityHex));

        // Should gain 2 MC from ocean adjacency (minus 25 MC city cost)
        var expected = mcBefore - Constants.CityCost + Constants.OceanAdjacencyBonus;
        Assert.Equal(expected, s3.Players[1].Resources.MegaCredits);
    }

    // ── Production Phase ───────────────────────────────────────

    [Fact]
    public void ProductionPhase_EnergyConvertsToHeat()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Resources = new ResourceSet(MegaCredits: 0, Energy: 5, Heat: 3),
        });

        // Both pass → production
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        // Energy (5) → Heat, plus Heat production
        Assert.Equal(0, s2.Players[0].Resources.Energy); // Except production adds back
        // Heat = old heat(3) + old energy(5) + heat production(0) = 8
        // But production also adds energy production back to energy
        Assert.Equal(8, s2.Players[0].Resources.Heat);
    }

    [Fact]
    public void ProductionPhase_MCIncomeIsTRPlusProduction()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            TerraformRating = 25,
            Production = new ProductionSet(MegaCredits: 3),
            Resources = ResourceSet.Zero,
        });

        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        // MC = TR(25) + MC production(3) = 28
        Assert.Equal(28, s2.Players[0].Resources.MegaCredits);
    }

    // ── Full Game Loop ─────────────────────────────────────────

    [Fact]
    public void FullLoop_3Generations_PassOnly()
    {
        var state = CreateTestGame();
        Assert.Equal(1, state.Generation);
        Assert.Equal(GamePhase.Action, state.Phase);

        // Gen 1: both pass
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));
        Assert.Equal(2, s2.Generation);
        Assert.Equal(GamePhase.Research, s2.Phase);

        // Simulate buying 0 cards to advance to action phase
        // For now, manually set phase since BuyCards doesn't auto-advance yet
        s2 = s2 with { Phase = GamePhase.Action };
        s2 = PhaseManager.StartActionPhase(s2);

        // Gen 2: both pass
        var (s3, _) = GameEngine.Apply(s2, new PassMove(s2.GetActivePlayer().PlayerId));
        var (s4, _) = GameEngine.Apply(s3, new PassMove(s3.GetActivePlayer().PlayerId));
        Assert.Equal(3, s4.Generation);
    }

    // ── Game End ───────────────────────────────────────────────

    [Fact]
    public void GameEnd_WhenAllParametersMaxed_AfterProduction()
    {
        var state = CreateTestGame() with
        {
            Oxygen = Constants.DefaultMaxOxygen,
            Temperature = Constants.DefaultMaxTemperature,
            OceansPlaced = Constants.DefaultMaxOceans,
        };

        // Both pass → production → game end check
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        // Should be in final greenery conversion
        Assert.Equal(GamePhase.FinalGreeneryConversion, s2.Phase);
    }

    [Fact]
    public void FinalGreeneryConversion_AllPass_EndsGame()
    {
        var state = CreateTestGame() with
        {
            Phase = GamePhase.FinalGreeneryConversion,
        };
        state = state.UpdatePlayer(0, p => p with { Resources = p.Resources with { Plants = 0 } });
        state = state.UpdatePlayer(1, p => p with { Resources = p.Resources with { Plants = 0 } });

        var (s1, r1) = GameEngine.Apply(state, new PassMove(0));
        Assert.True(r1.IsSuccess);

        var (s2, r2) = GameEngine.Apply(s1, new PassMove(1));
        Assert.True(r2.IsSuccess);
        Assert.Equal(GamePhase.GameEnd, s2.Phase);
    }

    [Fact]
    public void BothPlayersTake2Actions_ActivePlayerCyclesCorrectly()
    {
        var state = CreateTestGame();

        // Start temperature past the ocean bonus thresholds to avoid pending actions
        state = state with { Temperature = -20 };

        // Player 0 action 1: convert heat
        var (s1, r1) = GameEngine.Apply(state, new ConvertHeatMove(0));
        Assert.True(r1.IsSuccess);
        Assert.Equal(0, s1.ActivePlayerId); // still player 0's turn
        Assert.Equal(1, s1.Players[0].ActionsThisTurn);

        // Player 0 action 2: convert heat
        var (s2, r2) = GameEngine.Apply(s1, new ConvertHeatMove(0));
        Assert.True(r2.IsSuccess);
        Assert.Equal(1, s2.ActivePlayerId); // should advance to player 1
        Assert.Equal(0, s2.Players[1].ActionsThisTurn); // player 1 reset

        // Player 1 action 1: convert heat
        var (s3, r3) = GameEngine.Apply(s2, new ConvertHeatMove(1));
        Assert.True(r3.IsSuccess);
        Assert.Equal(1, s3.ActivePlayerId);
        Assert.Equal(1, s3.Players[1].ActionsThisTurn);

        // Player 1 action 2: convert heat
        var (s4, r4) = GameEngine.Apply(s3, new ConvertHeatMove(1));
        Assert.True(r4.IsSuccess);
        Assert.Equal(0, s4.ActivePlayerId); // should advance back to player 0
        Assert.Equal(0, s4.Players[0].ActionsThisTurn); // player 0 reset

        // Player 0 should now have legal moves
        var moves = LegalMoveGenerator.GetLegalMoves(s4, 0);
        Assert.False(moves.WaitingForOtherPlayer);
        Assert.NotNull(moves.Actions);
        Assert.True(moves.Actions!.CanPass);
    }

    [Fact]
    public void PendingActionAfter1stAction_ResolvesAndContinuesTurn()
    {
        var state = CreateTestGame();

        // Set temp at -2 so converting heat hits 0°C (ocean bonus pending)
        state = state with { Temperature = -2 };

        // Player 0 action 1: convert heat (-2 -> 0, triggers ocean bonus pending)
        var (s1, r1) = GameEngine.Apply(state, new ConvertHeatMove(0));
        Assert.True(r1.IsSuccess);
        Assert.Equal(1, s1.Players[0].ActionsThisTurn);
        Assert.NotNull(s1.PendingAction);
        Assert.IsType<PlaceTilePending>(s1.PendingAction);

        // Resolve the ocean placement
        var oceanPending = (PlaceTilePending)s1.PendingAction;
        var (s1b, r1b) = GameEngine.Apply(s1, new PlaceTileMove(0, oceanPending.ValidLocations[0]));
        Assert.True(r1b.IsSuccess);
        Assert.Null(s1b.PendingAction);
        // Still player 0's turn (only 1 action taken)
        Assert.Equal(0, s1b.ActivePlayerId);
        Assert.Equal(1, s1b.Players[0].ActionsThisTurn);
    }

    [Fact]
    public void PendingActionOn2ndAction_AdvancesAfterResolution()
    {
        var state = CreateTestGame();

        // Set temp at -4: action 1 -> -2 (no bonus), action 2 -> 0 (ocean bonus!)
        state = state with { Temperature = -4 };

        // Player 0 action 1: convert heat (-4 -> -2, no bonus)
        var (s1, r1) = GameEngine.Apply(state, new ConvertHeatMove(0));
        Assert.True(r1.IsSuccess);
        Assert.Null(s1.PendingAction);
        Assert.Equal(0, s1.ActivePlayerId);

        // Player 0 action 2: convert heat (-2 -> 0, triggers ocean bonus)
        var (s2, r2) = GameEngine.Apply(s1, new ConvertHeatMove(0));
        Assert.True(r2.IsSuccess);
        Assert.NotNull(s2.PendingAction);
        Assert.IsType<PlaceTilePending>(s2.PendingAction);
        // Active player should still be 0 until pending resolved
        Assert.Equal(0, s2.ActivePlayerId);

        // Resolve ocean placement
        var pending = (PlaceTilePending)s2.PendingAction;
        var (s3, r3) = GameEngine.Apply(s2, new PlaceTileMove(0, pending.ValidLocations[0]));
        Assert.True(r3.IsSuccess);
        Assert.Null(s3.PendingAction);
        // NOW should advance to player 1 (2 actions completed + pending resolved)
        Assert.Equal(1, s3.ActivePlayerId);
        Assert.Equal(0, s3.Players[1].ActionsThisTurn);

        // Player 1 should have legal moves
        var moves = LegalMoveGenerator.GetLegalMoves(s3, 1);
        Assert.False(moves.WaitingForOtherPlayer);
        Assert.NotNull(moves.Actions);
    }

    // ── ChooseEffect / Hired Raiders ──────────────────────────

    [Fact]
    public void HiredRaiders_StealMC_RemovesFromOpponent()
    {
        // Card 124: Hired Raiders — ChooseEffect with two options:
        //   0: Steal up to 2 steel from any player
        //   1: Steal up to 3 MC from any player

        var state = CreateTestGame();

        // Give player 0 the card and set up known resources
        state = state with
        {
            Players = state.Players
                .SetItem(0, state.Players[0] with
                {
                    Hand = state.Players[0].Hand.Add("124"),
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                })
                .SetItem(1, state.Players[1] with
                {
                    Resources = new ResourceSet(MegaCredits: 30, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                }),
        };

        // Play Hired Raiders (cost 1, event tag)
        var (s1, r1) = GameEngine.Apply(state, new PlayCardMove(0, "124", new PaymentInfo(MegaCredits: 1)));
        Assert.IsType<Success>(r1);

        // Should have a ChooseOptionPending
        Assert.NotNull(s1.PendingAction);
        Assert.IsType<ChooseOptionPending>(s1.PendingAction);
        var choosePending = (ChooseOptionPending)s1.PendingAction;
        Assert.Equal(2, choosePending.Options.Length);

        // Choose option 1: "Steal up to 3 MC"
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1));
        Assert.IsType<Success>(r2);

        // In a 2-player game, only one valid target (player 1), so steal is auto-applied
        // Player 1 should have lost 3 MC: 30 - 3 = 27
        var opponent = s2.GetPlayer(1);
        Assert.Equal(27, opponent.Resources.MegaCredits);

        // Player 0 should have gained 3 MC: 50 - 1 (card cost) + 3 (stolen) = 52
        var player = s2.GetPlayer(0);
        Assert.Equal(52, player.Resources.MegaCredits);

        // Pending action should be cleared
        Assert.Null(s2.PendingAction);
    }

    [Fact]
    public void HiredRaiders_StealSteel_RemovesFromOpponent()
    {
        var state = CreateTestGame();

        state = state with
        {
            Players = state.Players
                .SetItem(0, state.Players[0] with
                {
                    Hand = state.Players[0].Hand.Add("124"),
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                })
                .SetItem(1, state.Players[1] with
                {
                    Resources = new ResourceSet(MegaCredits: 30, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                }),
        };

        var (s1, r1) = GameEngine.Apply(state, new PlayCardMove(0, "124", new PaymentInfo(MegaCredits: 1)));
        Assert.IsType<Success>(r1);
        Assert.IsType<ChooseOptionPending>(s1.PendingAction);

        // Choose option 0: "Steal up to 2 steel"
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 0));
        Assert.IsType<Success>(r2);

        // Player 1 should have lost 2 steel: 5 - 2 = 3
        var opponent = s2.GetPlayer(1);
        Assert.Equal(3, opponent.Resources.Steel);

        // Player 0 should have gained 2 steel: 5 + 2 = 7
        var player = s2.GetPlayer(0);
        Assert.Equal(7, player.Resources.Steel);

        Assert.Null(s2.PendingAction);
    }

    [Fact]
    public void HiredRaiders_StealMC_CappedAtOpponentAmount()
    {
        var state = CreateTestGame();

        // Opponent only has 1 MC
        state = state with
        {
            Players = state.Players
                .SetItem(0, state.Players[0] with
                {
                    Hand = state.Players[0].Hand.Add("124"),
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                })
                .SetItem(1, state.Players[1] with
                {
                    Resources = new ResourceSet(MegaCredits: 1, Steel: 0, Titanium: 0, Plants: 0, Energy: 0, Heat: 0),
                }),
        };

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "124", new PaymentInfo(MegaCredits: 1)));
        var (s2, _) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1)); // Steal up to 3 MC

        // Should only remove 1 MC (capped at what opponent has)
        Assert.Equal(0, s2.GetPlayer(1).Resources.MegaCredits);

        // Player 0 should gain only 1 MC (what was actually stolen): 50 - 1 (cost) + 1 = 50
        Assert.Equal(50, s2.GetPlayer(0).Resources.MegaCredits);

        Assert.Null(s2.PendingAction);
    }

    // ── Effect Queue (Comet etc.) ─────────────────────────────

    [Fact]
    public void Comet_PresentsEffectOrderChoice()
    {
        // Card 010: Comet — Raise temp 1, place ocean, remove up to 3 plants from any
        // RaiseTemp is auto-executed. PlaceOcean and RemovePlants are orderable.
        var state = CreateTestGame();
        state = state with
        {
            Players = state.Players
                .SetItem(0, state.Players[0] with
                {
                    Hand = state.Players[0].Hand.Add("010"),
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                })
                .SetItem(1, state.Players[1] with
                {
                    Resources = new ResourceSet(MegaCredits: 30, Steel: 5, Titanium: 5, Plants: 10, Energy: 5, Heat: 5),
                }),
        };

        // Play Comet (cost 23, space tag)
        var (s1, r1) = GameEngine.Apply(state, new PlayCardMove(0, "010", new PaymentInfo(MegaCredits: 23)));
        Assert.IsType<Success>(r1);

        // Temperature should have been auto-raised (not orderable)
        Assert.Equal(state.Temperature + Constants.TemperatureStep, s1.Temperature);

        // Should have a ChooseEffectOrderPending with 2 orderable effects
        Assert.NotNull(s1.PendingAction);
        Assert.IsType<ChooseEffectOrderPending>(s1.PendingAction);
        var orderPending = (ChooseEffectOrderPending)s1.PendingAction;
        Assert.Equal(2, orderPending.RemainingEffectIndices.Length);
        Assert.Equal(2, orderPending.EffectDescriptions.Length);
    }

    [Fact]
    public void Comet_RemovePlantsFirst_ThenOcean()
    {
        var state = CreateTestGame();
        state = state with
        {
            Players = state.Players
                .SetItem(0, state.Players[0] with
                {
                    Hand = state.Players[0].Hand.Add("010"),
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                })
                .SetItem(1, state.Players[1] with
                {
                    Resources = new ResourceSet(MegaCredits: 30, Steel: 5, Titanium: 5, Plants: 10, Energy: 5, Heat: 5),
                }),
        };

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "010", new PaymentInfo(MegaCredits: 23)));
        var orderPending = (ChooseEffectOrderPending)s1.PendingAction!;

        // Find the RemoveResource effect index (the one that describes plants)
        int removeIdx = -1, oceanIdx = -1;
        for (int i = 0; i < orderPending.EffectDescriptions.Length; i++)
        {
            if (orderPending.EffectDescriptions[i].Contains("Plants", StringComparison.OrdinalIgnoreCase))
                removeIdx = orderPending.RemainingEffectIndices[i];
            if (orderPending.EffectDescriptions[i].Contains("ocean", StringComparison.OrdinalIgnoreCase))
                oceanIdx = orderPending.RemainingEffectIndices[i];
        }
        Assert.NotEqual(-1, removeIdx);
        Assert.NotEqual(-1, oceanIdx);

        // Choose to remove plants first
        var (s2, r2) = GameEngine.Apply(s1, new ChooseEffectOrderMove(0, removeIdx));
        Assert.IsType<Success>(r2);

        // Remove plants is now optional — pending action to choose target
        Assert.IsType<RemoveResourcePending>(s2.PendingAction);
        var removePending = (RemoveResourcePending)s2.PendingAction!;
        Assert.True(removePending.IsOptional);

        // Choose to remove from player 1
        var (s2b, _) = GameEngine.Apply(s2, new ChooseTargetPlayerMove(0, 1));
        Assert.Equal(7, s2b.GetPlayer(1).Resources.Plants);

        // Last remaining effect (ocean) auto-executes → PlaceTilePending
        Assert.IsType<PlaceTilePending>(s2b.PendingAction);

        // Place the ocean
        var oceanPending = (PlaceTilePending)s2b.PendingAction;
        var (s3, _) = GameEngine.Apply(s2b, new PlaceTileMove(0, oceanPending.ValidLocations[0]));
        Assert.Null(s3.PendingAction);
        Assert.Equal(1, s3.OceansPlaced);
    }

    [Fact]
    public void Comet_AutoResolve_ExecutesAllEffects()
    {
        var state = CreateTestGame();
        state = state with
        {
            Players = state.Players
                .SetItem(0, state.Players[0] with
                {
                    Hand = state.Players[0].Hand.Add("010"),
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 5, Energy: 5, Heat: 5),
                })
                .SetItem(1, state.Players[1] with
                {
                    Resources = new ResourceSet(MegaCredits: 30, Steel: 5, Titanium: 5, Plants: 10, Energy: 5, Heat: 5),
                }),
        };

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "010", new PaymentInfo(MegaCredits: 23)));
        Assert.IsType<ChooseEffectOrderPending>(s1.PendingAction);

        // Choose auto-resolve (-1)
        var (s2, r2) = GameEngine.Apply(s1, new ChooseEffectOrderMove(0, -1));
        Assert.IsType<Success>(r2);

        // Ocean placement should be pending (first orderable effect in default order)
        Assert.IsType<PlaceTilePending>(s2.PendingAction);
        var oceanPending = (PlaceTilePending)s2.PendingAction;

        // Place ocean
        var (s3, _) = GameEngine.Apply(s2, new PlaceTileMove(0, oceanPending.ValidLocations[0]));

        // Remove plants is now optional — pending to choose target
        Assert.IsType<RemoveResourcePending>(s3.PendingAction);
        var (s4, _) = GameEngine.Apply(s3, new ChooseTargetPlayerMove(0, 1));
        Assert.Equal(7, s4.GetPlayer(1).Resources.Plants);
        Assert.Null(s4.PendingAction);
    }

    // ── Urbanized Area ─────────────────────────────────────────

    [Fact]
    public void UrbanizedArea_IsPlayable_OnHellas_WithCitiesAt6_7And8_8()
    {
        // Mirrors a real CLI-reported scenario: Hellas map, Vitor, 37 MC, 8 steel,
        // energy prod 3, cities at (6,7) P0 and (8,8) P0, Urbanized Area in hand.
        // Hexes (7,7) and (7,8) are adjacent to both cities, so the card must be playable.
        var state = CreateTestGame() with { Map = MapName.Hellas };

        state = state with
        {
            PlacedTiles = state.PlacedTiles
                .Add(new HexCoord(6, 7), new PlacedTile(TileType.City, 0, new HexCoord(6, 7)))
                .Add(new HexCoord(8, 8), new PlacedTile(TileType.City, 0, new HexCoord(8, 8))),
        };

        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("120"),
            Resources = new ResourceSet(MegaCredits: 37, Steel: 8, Titanium: 4, Plants: 3, Energy: 3, Heat: 0),
            Production = p.Production with { Energy = 3 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);
        Assert.Contains(moves.Actions!.PlayableCards, c => c.CardId == "120");
    }

    [Fact]
    public void UrbanizedArea_IsPlayable_WhenHexIsAdjacentToTwoCities()
    {
        // Urbanized Area (120) must be placed adjacent to ≥2 cities, which inherently
        // means it's adjacent to cities — the normal "no adjacent cities" rule for
        // cities must be overridden for this constraint.
        //
        // Scenario: place cities at (3,3) and (5,3). Hex (4,3) is adjacent to both.
        var state = CreateTestGame();

        // Place cities at (3,3) and (5,3), both owned by player 1 (other player)
        state = state with
        {
            PlacedTiles = state.PlacedTiles
                .Add(new HexCoord(3, 3), new PlacedTile(TileType.City, 1, new HexCoord(3, 3)))
                .Add(new HexCoord(5, 3), new PlacedTile(TileType.City, 1, new HexCoord(5, 3))),
        };

        // Put Urbanized Area in player 0's hand and give them energy production to reduce
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("120"),
            Production = p.Production with { Energy = 1 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);

        // Urbanized Area should appear in the list of playable cards
        Assert.Contains(moves.Actions!.PlayableCards, c => c.CardId == "120");
    }

    // ── Equatorial Magnetizer ──────────────────────────────────

    [Fact]
    public void EquatorialMagnetizer_NotUsable_WhenEnergyProductionIsZero()
    {
        // Equatorial Magnetizer (015) action: -1 energy prod, +1 TR.
        // Must not be usable when the player has 0 energy production, since
        // production can't go below 0.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("015"),
            Production = p.Production with { Energy = 0 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);

        // Should not be in the list of usable card actions
        Assert.DoesNotContain(moves.Actions!.UsableCardActions, a => a.CardId == "015");
    }

    [Fact]
    public void EquatorialMagnetizer_UseCardActionMove_Rejected_WhenEnergyProductionIsZero()
    {
        // Submitting the move directly should also be rejected by the validator.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("015"),
            Production = p.Production with { Energy = 0 },
        });

        var (_, result) = GameEngine.Apply(state, new UseCardActionMove(0, "015"));
        Assert.True(result.IsError, "Expected UseCardActionMove to be rejected when energy production is 0.");
    }

    // ── Aquifer Pumping (187) ─────────────────────────────────

    [Fact]
    public void AquiferPumping_AcceptsSteelPayment()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("187"),
            Resources = p.Resources with { MegaCredits = 4, Steel = 2 },
        });

        // 4 MC + 2 steel (worth 2 MC each = 4 MC) = 8 total
        var payment = new PaymentInfo(MegaCredits: 4, Steel: 2);
        var (newState, result) = GameEngine.Apply(state, new UseCardActionMove(0, "187", payment));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, newState.Players[0].Resources.MegaCredits);
        Assert.Equal(0, newState.Players[0].Resources.Steel);
    }

    [Fact]
    public void AquiferPumping_RejectsInsufficientPayment()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("187"),
            Resources = p.Resources with { MegaCredits = 3, Steel = 2 },
        });

        // 3 MC + 2 steel (worth 4 MC) = 7 total, need 8
        var payment = new PaymentInfo(MegaCredits: 3, Steel: 2);
        var (_, result) = GameEngine.Apply(state, new UseCardActionMove(0, "187", payment));

        Assert.True(result.IsError);
    }

    [Fact]
    public void AquiferPumping_AllowsSteelInLegalMoves()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("187"),
            Resources = p.Resources with { MegaCredits = 4, Steel = 2 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        var action = moves.Actions!.UsableCardActions.FirstOrDefault(a => a.CardId == "187");
        Assert.NotNull(action);
        Assert.True(action!.AllowSteel);
        Assert.Equal(8, action.MCCost);
    }

    [Fact]
    public void AquiferPumping_PureMCPaymentWhenNoPaymentSpecified()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("187"),
            Resources = p.Resources with { MegaCredits = 10, Steel = 5 },
        });

        // No payment specified — defaults to full MC payment
        var (newState, result) = GameEngine.Apply(state, new UseCardActionMove(0, "187"));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, newState.Players[0].Resources.MegaCredits);
        Assert.Equal(5, newState.Players[0].Resources.Steel);
    }

    // ── Flooding (188) ──────────────────────────────────────────

    [Fact]
    public void Flooding_PlacesOcean_ThenOffersOptionalMCRemoval()
    {
        // Place a greenery owned by player 1 adjacent to an ocean-reserved hex
        var adjacentHex = new HexCoord(5, 0); // adjacent to ocean hex (4,1) on Tharsis
        var state = CreateTestGame();
        state = state with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(adjacentHex, new PlacedTile(TileType.Greenery, 1, adjacentHex)),
        };
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("188"),
        });

        // Play Flooding — first it should create a PlaceTilePending for the ocean
        var (s1, r1) = GameEngine.Apply(state, new PlayCardMove(0, "188", new PaymentInfo(MegaCredits: 7)));
        Assert.True(r1.IsSuccess);
        Assert.IsType<PlaceTilePending>(s1.PendingAction);

        // Place the ocean adjacent to player 1's tile
        var oceanHex = new HexCoord(4, 1);
        var (s2, r2) = GameEngine.Apply(s1, new PlaceTileMove(0, oceanHex));
        Assert.True(r2.IsSuccess);

        // Should now have a RemoveResourcePending that is optional
        Assert.IsType<RemoveResourcePending>(s2.PendingAction);
        var pending = (RemoveResourcePending)s2.PendingAction!;
        Assert.True(pending.IsOptional);
        Assert.Contains(1, pending.ValidTargetPlayerIds);
    }

    [Fact]
    public void Flooding_CanChooseToRemoveMCFromAdjacentOwner()
    {
        var adjacentHex = new HexCoord(5, 0);
        var state = CreateTestGame();
        state = state with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(adjacentHex, new PlacedTile(TileType.Greenery, 1, adjacentHex)),
        };
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("188"),
        });
        var initialMC = state.Players[1].Resources.MegaCredits;

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "188", new PaymentInfo(MegaCredits: 7)));
        var (s2, _) = GameEngine.Apply(s1, new PlaceTileMove(0, new HexCoord(4, 1)));

        // Choose to remove 4 MC from player 1
        var (s3, r3) = GameEngine.Apply(s2, new ChooseTargetPlayerMove(0, 1));
        Assert.True(r3.IsSuccess);
        Assert.Equal(initialMC - 4, s3.Players[1].Resources.MegaCredits);
    }

    [Fact]
    public void Flooding_CanSkipMCRemoval()
    {
        var adjacentHex = new HexCoord(5, 0);
        var state = CreateTestGame();
        state = state with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(adjacentHex, new PlacedTile(TileType.Greenery, 1, adjacentHex)),
        };
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("188"),
        });
        var initialMC = state.Players[1].Resources.MegaCredits;

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "188", new PaymentInfo(MegaCredits: 7)));
        var (s2, _) = GameEngine.Apply(s1, new PlaceTileMove(0, new HexCoord(4, 1)));

        // Skip — pass to decline
        var (s3, r3) = GameEngine.Apply(s2, new PassMove(0));
        Assert.True(r3.IsSuccess);
        Assert.Equal(initialMC, s3.Players[1].Resources.MegaCredits);
    }

    [Fact]
    public void Flooding_CanSkipWhenAdjacentToOwnTile()
    {
        // Place a tile owned by the active player (player 0) adjacent to the ocean
        var adjacentHex = new HexCoord(5, 0);
        var state = CreateTestGame();
        state = state with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(adjacentHex, new PlacedTile(TileType.Greenery, 0, adjacentHex)),
        };
        state = state.UpdatePlayer(0, p => p with { Hand = p.Hand.Add("188") });
        var initialMC = state.Players[0].Resources.MegaCredits;

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "188", new PaymentInfo(MegaCredits: 7)));
        var (s2, _) = GameEngine.Apply(s1, new PlaceTileMove(0, new HexCoord(4, 1)));

        // Should have optional pending with player 0 as target
        Assert.IsType<RemoveResourcePending>(s2.PendingAction);
        var pending = (RemoveResourcePending)s2.PendingAction!;
        Assert.True(pending.IsOptional);
        Assert.Contains(0, pending.ValidTargetPlayerIds);

        // Player can skip — they shouldn't be forced to lose their own MC
        var (s3, r3) = GameEngine.Apply(s2, new PassMove(0));
        Assert.True(r3.IsSuccess);
        Assert.Null(s3.PendingAction);
    }

    [Fact]
    public void Flooding_NoPromptWhenNoAdjacentTiles()
    {
        // No tiles on the board — ocean placed with nothing adjacent
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with { Hand = p.Hand.Add("188") });

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "188", new PaymentInfo(MegaCredits: 7)));
        var (s2, r2) = GameEngine.Apply(s1, new PlaceTileMove(0, new HexCoord(4, 1)));
        Assert.True(r2.IsSuccess);

        // No adjacent tiles → no pending, action should be complete
        Assert.Null(s2.PendingAction);
    }

    // ── Energy Saving (189) ─────────────────────────────────────

    [Fact]
    public void EnergySaving_CountsOffMapCities()
    {
        // Place Ganymede Colony and Phobos Space Haven as off-map cities,
        // plus one on-map city — Energy Saving should count all three.
        var state = CreateTestGame();
        state = state with
        {
            PlacedTiles = state.PlacedTiles
                .Add(new HexCoord(4, 4), new PlacedTile(TileType.City, 1, new HexCoord(4, 4))),
            OffMapTiles = state.OffMapTiles
                .Add(new OffMapTile("Ganymede Colony", TileType.City, 0))
                .Add(new OffMapTile("Phobos Space Haven", TileType.City, 1)),
        };
        state = state.UpdatePlayer(0, p => p with { Hand = p.Hand.Add("189") });

        var initialEnergyProd = state.Players[0].Production.Energy;

        var (newState, result) = GameEngine.Apply(state, new PlayCardMove(0, "189", new PaymentInfo(MegaCredits: 15)));

        Assert.True(result.IsSuccess);
        Assert.Equal(initialEnergyProd + 3, newState.Players[0].Production.Energy);
    }

    // ── GHG Producing Bacteria (034) ──────────────────────────────

    [Fact]
    public void GHGBacteria_Action_AddMicrobe_AutoSelectsWhenNotEnoughToRemove()
    {
        // With 0 microbes, only "add 1 microbe" is valid — auto-selected, no choice needed
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("034"),
            CardResources = p.CardResources.Add("034", 0),
        });

        var (newState, result) = GameEngine.Apply(state, new UseCardActionMove(0, "034"));
        Assert.True(result.IsSuccess);
        Assert.Null(newState.PendingAction);
        Assert.Equal(1, newState.GetPlayer(0).CardResources["034"]);
    }

    [Fact]
    public void GHGBacteria_Action_PresentsChoice_WhenEnoughMicrobes()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("034"),
            CardResources = p.CardResources.Add("034", 2),
        });

        var (s1, r1) = GameEngine.Apply(state, new UseCardActionMove(0, "034"));
        Assert.True(r1.IsSuccess);
        var pending = Assert.IsType<ChooseOptionPending>(s1.PendingAction);
        Assert.Equal(2, pending.ValidOptionIndices!.Value.Length);

        // Choose option 0: add 1 microbe
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 0));
        Assert.True(r2.IsSuccess);
        Assert.Equal(3, s2.GetPlayer(0).CardResources["034"]);
    }

    [Fact]
    public void GHGBacteria_Action_Remove2Microbes_RaisesTemperature()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("034"),
            CardResources = p.CardResources.Add("034", 3),
        });
        var initialTemp = state.Temperature;
        var initialTR = state.Players[0].TerraformRating;

        var (s1, _) = GameEngine.Apply(state, new UseCardActionMove(0, "034"));
        // Choose option 1: remove 2 microbes, raise temperature
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1));
        Assert.True(r2.IsSuccess);
        Assert.Equal(1, s2.GetPlayer(0).CardResources["034"]);
        Assert.Equal(initialTemp + Constants.TemperatureStep, s2.Temperature);
        Assert.Equal(initialTR + 1, s2.GetPlayer(0).TerraformRating);
    }

    [Fact]
    public void GHGBacteria_Action_Remove2Rejected_WhenNotEnoughMicrobes()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("034"),
            CardResources = p.CardResources.Add("034", 1),
        });

        var (s1, _) = GameEngine.Apply(state, new UseCardActionMove(0, "034"));
        // Try option 1 with only 1 microbe — should be rejected
        var (_, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1));
        Assert.True(r2.IsError);
    }

    [Fact]
    public void GHGBacteria_Action_Remove2Allowed_WhenTempMaxed()
    {
        var state = CreateTestGame() with { Temperature = Constants.DefaultMaxTemperature };
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("034"),
            CardResources = p.CardResources.Add("034", 2),
        });

        var (s1, _) = GameEngine.Apply(state, new UseCardActionMove(0, "034"));
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1));
        Assert.True(r2.IsSuccess);
        // Microbes removed, temp stays maxed, no TR gain
        Assert.Equal(0, s2.GetPlayer(0).CardResources["034"]);
        Assert.Equal(Constants.DefaultMaxTemperature, s2.Temperature);
    }

    // ── Regolith Eaters (033) ───────────────────────────────────

    [Fact]
    public void RegolithEaters_Action_Remove2Microbes_RaisesOxygen()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("033"),
            CardResources = p.CardResources.Add("033", 4),
        });
        var initialO2 = state.Oxygen;
        var initialTR = state.Players[0].TerraformRating;

        var (s1, _) = GameEngine.Apply(state, new UseCardActionMove(0, "033"));
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1));
        Assert.True(r2.IsSuccess);
        Assert.Equal(2, s2.GetPlayer(0).CardResources["033"]);
        Assert.Equal(initialO2 + 1, s2.Oxygen);
        Assert.Equal(initialTR + 1, s2.GetPlayer(0).TerraformRating);
    }

    [Fact]
    public void RegolithEaters_Action_Remove2Allowed_WhenOxygenMaxed()
    {
        var state = CreateTestGame() with { Oxygen = Constants.DefaultMaxOxygen };
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("033"),
            CardResources = p.CardResources.Add("033", 2),
        });

        var (s1, _) = GameEngine.Apply(state, new UseCardActionMove(0, "033"));
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 1));
        Assert.True(r2.IsSuccess);
        Assert.Equal(0, s2.GetPlayer(0).CardResources["033"]);
        Assert.Equal(Constants.DefaultMaxOxygen, s2.Oxygen);
    }

    // ── Insulation (152) ────────────────���─────────────────────────

    [Fact]
    public void Insulation_ConvertsHeatProdToMCProd()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("152"),
            Production = p.Production with { Heat = 5, MegaCredits = 0 },
        });

        var (s1, r1) = GameEngine.Apply(state, new PlayCardMove(0, "152", new PaymentInfo(MegaCredits: 2)));
        Assert.True(r1.IsSuccess);
        var pending = Assert.IsType<ChooseOptionPending>(s1.PendingAction);
        Assert.Equal(6, pending.Options.Length); // 0 through 5

        // Choose to convert 3 steps
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 3));
        Assert.True(r2.IsSuccess);
        Assert.Equal(2, s2.GetPlayer(0).Production.Heat);
        Assert.Equal(3, s2.GetPlayer(0).Production.MegaCredits);
    }

    [Fact]
    public void Insulation_ZeroSteps_NoChange()
    {
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("152"),
            Production = p.Production with { Heat = 3, MegaCredits = 2 },
        });

        var (s1, _) = GameEngine.Apply(state, new PlayCardMove(0, "152", new PaymentInfo(MegaCredits: 2)));
        var (s2, r2) = GameEngine.Apply(s1, new ChooseOptionMove(0, 0));
        Assert.True(r2.IsSuccess);
        Assert.Equal(3, s2.GetPlayer(0).Production.Heat);
        Assert.Equal(2, s2.GetPlayer(0).Production.MegaCredits);
    }

    // ── Predators (024) ───────────��────────────────��─────────────

    [Fact]
    public void Predators_Action_RemovesAnimalFromAnotherCard()
    {
        // Player 0 has Predators (024) and Birds (072) in play, each with 1 animal.
        // Using Predators should remove 1 animal from Birds and add 1 to Predators.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("024").Add("072"),
            CardResources = p.CardResources.Add("024", 1).Add("072", 1),
        });

        var (newState, result) = GameEngine.Apply(state, new UseCardActionMove(0, "024"));
        Assert.True(result.IsSuccess);

        // Predators should gain 1 animal (1 → 2)
        Assert.Equal(2, newState.GetPlayer(0).CardResources["024"]);
        // Birds should lose 1 animal (1 → 0)
        Assert.Equal(0, newState.GetPlayer(0).CardResources["072"]);
    }

    [Fact]
    public void Predators_Action_CannotBeUsed_WhenNoValidTargets()
    {
        // Player 0 has Predators with 1 animal but no other animal cards in play
        // (on any player). The action should not be available.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("024"),
            CardResources = p.CardResources.Add("024", 3),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.DoesNotContain(moves.Actions!.UsableCardActions, a => a.CardId == "024");
    }

    [Fact]
    public void Predators_Action_CanTargetOpponentAnimalCards()
    {
        // Player 1 has Birds with 2 animals. Player 0 has Predators with 0 animals.
        // Player 0 should be able to use Predators to steal from Player 1's Birds.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("024"),
            CardResources = p.CardResources.Add("024", 0),
        });
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Add("072"),
            CardResources = p.CardResources.Add("072", 2),
        });

        var (newState, result) = GameEngine.Apply(state, new UseCardActionMove(0, "024"));
        Assert.True(result.IsSuccess);

        // Birds on player 1 should lose 1 animal (2 → 1)
        Assert.Equal(1, newState.GetPlayer(1).CardResources["072"]);
        // Predators on player 0 should gain 1 animal (0 → 1)
        Assert.Equal(1, newState.GetPlayer(0).CardResources["024"]);
    }

    // ── Herbivores ─────────────────────────────────────────────

    [Fact]
    public void Herbivores_AddsOneAnimalToItself_OnPlay()
    {
        // Herbivores (147) on-play: "Add 1 Animal to this card. Decrease any
        // Plant production 1 step." Requires 8% oxygen.
        var state = CreateTestGame() with { Oxygen = 8 };
        // Give player 1 some plant production so the ReduceAnyProductionEffect
        // has a target and doesn't fail / create a pending action.
        state = state.UpdatePlayer(1, p => p with
        {
            Production = p.Production with { Plants = 2 },
        });
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("147"),
        });

        var (newState, result) = GameEngine.Apply(state,
            new PlayCardMove(0, "147", new PaymentInfo(MegaCredits: 12)));

        Assert.True(result.IsSuccess, $"Expected success but got: {result}");
        Assert.Contains("147", newState.Players[0].PlayedCards);
        Assert.Equal(1, newState.Players[0].CardResources.GetValueOrDefault("147", 0));
    }

    [Fact]
    public void Herbivores_GainsAnimal_WhenPlayerPlacesGreenery()
    {
        // Herbivores already in play with 1 animal. When the player places a
        // greenery (e.g., via the Greenery standard project), the WhenYouEffect
        // trigger should fire and add another animal to Herbivores.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("147"),
            CardResources = p.CardResources.SetItem("147", 1),
        });

        // Place a greenery via the standard project at any valid location
        var greeneryLocations = BoardLogic.GetValidGreeneryPlacements(state, 0);
        Assert.NotEmpty(greeneryLocations);
        var (newState, result) = GameEngine.Apply(state, new GreeneryMove(0, greeneryLocations[0]));

        Assert.True(result.IsSuccess, $"Expected success but got: {result}");
        Assert.Equal(2, newState.Players[0].CardResources.GetValueOrDefault("147", 0));
    }

    // ── Wild tag requirement counting ──────────────────────────

    [Fact]
    public void WildTag_CountsTowardScienceTagRequirement()
    {
        // Mass Converter (094) requires 5 science tags. Player has 4 science tags
        // from played cards plus 1 wild tag, which should satisfy the requirement.
        //
        // Cards used:
        //   "090" Research — 2 Science tags
        //   "155" Designed Microorganisms — 1 Science, 1 Microbe
        //   "071" Advanced Alloys — 1 Science
        //   "P40" Research Coordination — 1 Wild
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("094"),
            PlayedCards = ImmutableList.Create("090", "155", "071", "P40"),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);

        // Mass Converter should be playable: 4 science tags + 1 wild >= 5
        Assert.Contains(moves.Actions!.PlayableCards, c => c.CardId == "094");
    }

    [Fact]
    public void WildTag_NotNeeded_WhenSpecificTagAlreadySatisfiesRequirement()
    {
        // Player has exactly 5 science tags and no wild tags — should still be playable.
        // Research (090)=2 + Designed Microorganisms (155)=1 + Advanced Alloys (071)=1
        // + Lagrange Observatory (196)=1 → 5 science total
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("094"),
            PlayedCards = ImmutableList.Create("090", "155", "071", "196"),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.Contains(moves.Actions!.PlayableCards, c => c.CardId == "094");
    }

    [Fact]
    public void WildTag_NotEnough_WhenTotalBelowRequirement()
    {
        // 3 science (Research=2, Designed Microorganisms=1) + 1 wild = 4 total, short of 5.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("094"),
            PlayedCards = ImmutableList.Create("090", "155", "P40"),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.DoesNotContain(moves.Actions!.PlayableCards, c => c.CardId == "094");
    }

    [Fact]
    public void MultipleWildTags_EachCountsIndividuallyTowardRequirement()
    {
        // 3 science tags + 2 wild tags = 5 total, satisfies Mass Converter's requirement.
        //   "090" Research — 2 Science
        //   "155" Designed Microorganisms — 1 Science
        //   "P28" Research Network — 1 Wild (prelude)
        //   "P40" Research Coordination — 1 Wild
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("094"),
            PlayedCards = ImmutableList.Create("090", "155", "P28", "P40"),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);
        Assert.Contains(moves.Actions!.PlayableCards, c => c.CardId == "094");
    }

    // ── Colonizer Training Camp ────────────────────────────────

    [Fact]
    public void ColonizerTrainingCamp_IsPlayable_WhenOxygenAtMost5Percent()
    {
        // 001: cost 8, requires max_oxygen 5, gives 2 VP.
        var state = CreateTestGame() with { Oxygen = 5 };
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("001"),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);
        Assert.Contains(moves.Actions!.PlayableCards, c => c.CardId == "001");
    }

    [Fact]
    public void ColonizerTrainingCamp_NotPlayable_WhenOxygenAbove5Percent()
    {
        var state = CreateTestGame() with { Oxygen = 6 };
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("001"),
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);
        Assert.DoesNotContain(moves.Actions!.PlayableCards, c => c.CardId == "001");
    }

    [Fact]
    public void ColonizerTrainingCamp_PlaysSuccessfully_AndScores2VP()
    {
        var state = CreateTestGame() with { Oxygen = 3 };
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("001"),
        });

        var (newState, result) = GameEngine.Apply(state,
            new PlayCardMove(0, "001", new PaymentInfo(MegaCredits: 8)));

        Assert.True(result.IsSuccess, $"Expected success but got: {result}");
        Assert.Contains("001", newState.Players[0].PlayedCards);
        Assert.Equal(100 - 8, newState.Players[0].Resources.MegaCredits);

        // Scoring: 2 VP from the card
        var scores = Scoring.CalculateFinalScores(newState);
        var player0Score = scores.First(s => s.PlayerId == 0);
        Assert.Equal(2, player0Score.CardPoints);
    }

    [Fact]
    public void EquatorialMagnetizer_IsUsable_WhenEnergyProductionIsAtLeastOne()
    {
        // Sanity check: with +1 energy production, the action should still be usable.
        var state = CreateTestGame();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = ImmutableList.Create("015"),
            Production = p.Production with { Energy = 1 },
        });

        var moves = LegalMoveGenerator.GetLegalMoves(state, 0);
        Assert.NotNull(moves.Actions);
        Assert.Contains(moves.Actions!.UsableCardActions, a => a.CardId == "015");
    }
}
