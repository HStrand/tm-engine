using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Engine;

public class GameEngineTests
{
    private static GameState CreateTestGame(int playerCount = 2)
    {
        var options = new GameSetupOptions(
            PlayerCount: playerCount,
            Map: MapName.Tharsis,
            CorporateEra: true,
            DraftVariant: false,
            PreludeExpansion: false);

        var state = GameEngine.Setup(options, seed: 42);

        // Give players some resources for testing
        for (int i = 0; i < playerCount; i++)
        {
            state = state.UpdatePlayer(i, p => p with
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

        return state;
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
            new UseStandardProjectMove(0, StandardProject.PowerPlant));

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
            new UseStandardProjectMove(0, StandardProject.Asteroid));

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
            new UseStandardProjectMove(0, StandardProject.Aquifer, Location: oceanHex));

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
            new UseStandardProjectMove(0, StandardProject.City, Location: landHex));

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
            new UseStandardProjectMove(0, StandardProject.City, Location: new HexCoord(5, 3)));

        // Player 0 passes, now player 1's turn
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Player 1 tries adjacent hex (6,3 is adjacent to 5,3 on row 3 odd)
        var (_, result) = GameEngine.Apply(s2,
            new UseStandardProjectMove(1, StandardProject.City, Location: new HexCoord(6, 3)));

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
            new UseStandardProjectMove(0, StandardProject.SellPatents,
                CardsToDiscard: ["card1", "card2"]));

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
    public void Temperature_At0C_GainsHeatProduction()
    {
        var state = CreateTestGame() with { Temperature = -2 };
        var initialHeatProd = state.Players[0].Production.Heat;

        var (newState, _) = GameEngine.Apply(state, new ConvertHeatMove(0));

        // Temperature goes from -2 to 0, bonus heat production
        Assert.Equal(0, newState.Temperature);
        Assert.Equal(initialHeatProd + 1, newState.Players[0].Production.Heat);
    }

    [Fact]
    public void Temperature_AtMinus24_TriggersOceanPending()
    {
        var state = CreateTestGame() with { Temperature = -26 };

        var (newState, _) = GameEngine.Apply(state, new ConvertHeatMove(0));

        Assert.Equal(-24, newState.Temperature);
        Assert.IsType<PlaceTilePending>(newState.PendingAction);
        var pending = (PlaceTilePending)newState.PendingAction;
        Assert.Equal(TileType.Ocean, pending.TileType);
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
            new UseStandardProjectMove(0, StandardProject.Aquifer, Location: oceanHex));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Player 1 places city adjacent to ocean
        // (5,1) is adjacent to (4,1) on row 1
        var cityHex = new HexCoord(5, 1);
        var mcBefore = s2.Players[1].Resources.MegaCredits;

        var (s3, _) = GameEngine.Apply(s2,
            new UseStandardProjectMove(1, StandardProject.City, Location: cityHex));

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
}
