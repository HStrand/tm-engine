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
            ActivePlayerIndex = 0,
            FirstPlayerIndex = 0,
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
            Log = [],
        };
    }

    // ── Pass & Turn Flow ───────────────────────────────────────

    [Fact]
    public void Pass_SwitchesToNextPlayer()
    {
        var state = CreateTestGame();
        Assert.Equal(0, state.ActivePlayerIndex);

        var (newState, result) = GameEngine.Apply(state, new PassMove(0));

        Assert.True(result.IsSuccess);
        Assert.Equal(1, newState.ActivePlayerIndex);
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
        Assert.Equal(0, state.ActivePlayerIndex);

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
        Assert.Equal(0, s1.ActivePlayerIndex); // Still player 0's turn

        // Player 0, action 2: convert heat again
        var (s2, r2) = GameEngine.Apply(s1, new ConvertHeatMove(0));
        Assert.True(r2.IsSuccess);
        Assert.Equal(1, s2.ActivePlayerIndex); // Now player 1's turn
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
    public void ConvertHeat_FailsWhenTemperatureMaxed()
    {
        var state = CreateTestGame() with { Temperature = Constants.DefaultMaxTemperature };

        var (_, result) = GameEngine.Apply(state, new ConvertHeatMove(0));
        Assert.True(result.IsError);
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
        var (s3, _) = GameEngine.Apply(s2, new PassMove(s2.ActivePlayer.PlayerId));
        var (s4, _) = GameEngine.Apply(s3, new PassMove(s3.ActivePlayer.PlayerId));
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

    // ── Audit Log ──────────────────────────────────────────────

    [Fact]
    public void Moves_AreLoggedSequentially()
    {
        var state = CreateTestGame();

        var (s1, _) = GameEngine.Apply(state, new ConvertHeatMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        Assert.Equal(2, s2.Log.Count);
        Assert.Contains("converts heat", s2.Log[0]);
        Assert.Contains("passes", s2.Log[1]);
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
        Assert.Equal(0, s1.ActivePlayerIndex); // still player 0's turn
        Assert.Equal(1, s1.Players[0].ActionsThisTurn);

        // Player 0 action 2: convert heat
        var (s2, r2) = GameEngine.Apply(s1, new ConvertHeatMove(0));
        Assert.True(r2.IsSuccess);
        Assert.Equal(1, s2.ActivePlayerIndex); // should advance to player 1
        Assert.Equal(0, s2.Players[1].ActionsThisTurn); // player 1 reset

        // Player 1 action 1: convert heat
        var (s3, r3) = GameEngine.Apply(s2, new ConvertHeatMove(1));
        Assert.True(r3.IsSuccess);
        Assert.Equal(1, s3.ActivePlayerIndex);
        Assert.Equal(1, s3.Players[1].ActionsThisTurn);

        // Player 1 action 2: convert heat
        var (s4, r4) = GameEngine.Apply(s3, new ConvertHeatMove(1));
        Assert.True(r4.IsSuccess);
        Assert.Equal(0, s4.ActivePlayerIndex); // should advance back to player 0
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
        Assert.Equal(0, s1b.ActivePlayerIndex);
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
        Assert.Equal(0, s1.ActivePlayerIndex);

        // Player 0 action 2: convert heat (-2 -> 0, triggers ocean bonus)
        var (s2, r2) = GameEngine.Apply(s1, new ConvertHeatMove(0));
        Assert.True(r2.IsSuccess);
        Assert.NotNull(s2.PendingAction);
        Assert.IsType<PlaceTilePending>(s2.PendingAction);
        // Active player should still be 0 until pending resolved
        Assert.Equal(0, s2.ActivePlayerIndex);

        // Resolve ocean placement
        var pending = (PlaceTilePending)s2.PendingAction;
        var (s3, r3) = GameEngine.Apply(s2, new PlaceTileMove(0, pending.ValidLocations[0]));
        Assert.True(r3.IsSuccess);
        Assert.Null(s3.PendingAction);
        // NOW should advance to player 1 (2 actions completed + pending resolved)
        Assert.Equal(1, s3.ActivePlayerIndex);
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

        // Plants removed (auto-applied to only opponent)
        Assert.Equal(7, s2.GetPlayer(1).Resources.Plants);

        // Last remaining effect (ocean) auto-executes → PlaceTilePending
        Assert.NotNull(s2.PendingAction);
        Assert.IsType<PlaceTilePending>(s2.PendingAction);

        // Place the ocean
        var oceanPending = (PlaceTilePending)s2.PendingAction;
        var (s3, _) = GameEngine.Apply(s2, new PlaceTileMove(0, oceanPending.ValidLocations[0]));
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

        // Plants should have been removed after ocean
        Assert.Equal(7, s3.GetPlayer(1).Resources.Plants);
        Assert.Null(s3.PendingAction);
    }
}
