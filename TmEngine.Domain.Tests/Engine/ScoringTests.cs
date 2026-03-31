using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Tests.Engine;

public class ScoringTests
{
    private static GameState CreateEndGameState(int playerCount = 3)
    {
        var players = ImmutableList.CreateBuilder<PlayerState>();
        for (int i = 0; i < playerCount; i++)
        {
            players.Add(PlayerState.CreateInitial(i, 20 + i * 5) with
            {
                Resources = new ResourceSet(MegaCredits: 10 + i * 5),
                Production = new ProductionSet(MegaCredits: i + 1),
            });
        }

        return new GameState
        {
            GameId = "test",
            Map = MapName.Tharsis,
            CorporateEra = true,
            DraftVariant = false,
            PreludeExpansion = false,
            Phase = GamePhase.GameEnd,
            Generation = 10,
            ActivePlayerIndex = 0,
            FirstPlayerIndex = 0,
            Oxygen = Constants.DefaultMaxOxygen,
            Temperature = Constants.DefaultMaxTemperature,
            OceansPlaced = Constants.DefaultMaxOceans,
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

    // ── Basic Scoring ──────────────────────────────────────────

    [Fact]
    public void Score_TRIsBaseScore()
    {
        var state = CreateEndGameState();
        var scores = Scoring.CalculateFinalScores(state);

        // Player 2 has TR 30, Player 1 has TR 25, Player 0 has TR 20
        Assert.Equal(2, scores[0].PlayerId); // Highest TR
        Assert.Equal(30, scores[0].TerraformRating);
        Assert.Equal(0, scores[2].PlayerId); // Lowest TR
    }

    [Fact]
    public void Score_MilestonesWorth5VP()
    {
        var state = CreateEndGameState() with
        {
            ClaimedMilestones =
            [
                new MilestoneClaim("Terraformer", 0),
                new MilestoneClaim("Mayor", 1),
            ],
        };

        var scores = Scoring.CalculateFinalScores(state);

        Assert.Equal(5, scores.First(s => s.PlayerId == 0).MilestonePoints);
        Assert.Equal(5, scores.First(s => s.PlayerId == 1).MilestonePoints);
        Assert.Equal(0, scores.First(s => s.PlayerId == 2).MilestonePoints);
    }

    [Fact]
    public void Score_MultipleMilestonesSamePayer()
    {
        var state = CreateEndGameState() with
        {
            ClaimedMilestones =
            [
                new MilestoneClaim("Terraformer", 0),
                new MilestoneClaim("Mayor", 0),
            ],
        };

        var scores = Scoring.CalculateFinalScores(state);
        Assert.Equal(10, scores.First(s => s.PlayerId == 0).MilestonePoints);
    }

    // ── Greenery VP ────────────────────────────────────────────

    [Fact]
    public void Score_GreeneriesWorth1VP()
    {
        var state = CreateEndGameState() with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(5, 3), new PlacedTile(TileType.Greenery, 0, new HexCoord(5, 3)))
                .Add(new HexCoord(6, 3), new PlacedTile(TileType.Greenery, 0, new HexCoord(6, 3)))
                .Add(new HexCoord(7, 3), new PlacedTile(TileType.Greenery, 1, new HexCoord(7, 3))),
        };

        var scores = Scoring.CalculateFinalScores(state);
        Assert.Equal(2, scores.First(s => s.PlayerId == 0).GreeneryPoints);
        Assert.Equal(1, scores.First(s => s.PlayerId == 1).GreeneryPoints);
    }

    // ── City VP ────────────────────────────────────────────────

    [Fact]
    public void Score_CityVP_PerAdjacentGreenery()
    {
        // Place a city at (5,5) with greeneries at (4,5) and (6,5) (both adjacent on even row)
        // Wait, (6,5) is ocean on Tharsis. Use (5,3) area instead.
        // City at (4,3), greeneries at (3,3) and (5,3) — both adjacent on odd row 3
        var state = CreateEndGameState() with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(4, 3), new PlacedTile(TileType.City, 0, new HexCoord(4, 3)))
                .Add(new HexCoord(3, 3), new PlacedTile(TileType.Greenery, 1, new HexCoord(3, 3)))
                .Add(new HexCoord(5, 3), new PlacedTile(TileType.Greenery, 2, new HexCoord(5, 3))),
        };

        var scores = Scoring.CalculateFinalScores(state);

        // Player 0's city is adjacent to 2 greeneries (owned by others, but that doesn't matter)
        Assert.Equal(2, scores.First(s => s.PlayerId == 0).CityPoints);
    }

    [Fact]
    public void Score_CityVP_GreeneryOwnerDoesntMatter()
    {
        // City owned by player 0, adjacent greenery owned by player 1
        var state = CreateEndGameState() with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(4, 3), new PlacedTile(TileType.City, 0, new HexCoord(4, 3)))
                .Add(new HexCoord(3, 3), new PlacedTile(TileType.Greenery, 1, new HexCoord(3, 3))),
        };

        var scores = Scoring.CalculateFinalScores(state);

        // Player 0 gets 1 VP for the adjacent greenery even though player 1 owns it
        Assert.Equal(1, scores.First(s => s.PlayerId == 0).CityPoints);
        // Player 1 gets 1 VP for owning the greenery tile itself
        Assert.Equal(1, scores.First(s => s.PlayerId == 1).GreeneryPoints);
    }

    // ── Awards ─────────────────────────────────────────────────

    [Fact]
    public void Score_Award_FirstPlace5VP()
    {
        var state = CreateEndGameState() with
        {
            FundedAwards = [new AwardFunding("Banker", 0)],
        };
        // Player 0 has MC prod 1, Player 1 has MC prod 2, Player 2 has MC prod 3
        // Player 2 wins the Banker award

        var scores = Scoring.CalculateFinalScores(state);
        Assert.Equal(5, scores.First(s => s.PlayerId == 2).AwardPoints);
    }

    [Fact]
    public void Score_Award_SecondPlace2VP()
    {
        var state = CreateEndGameState() with
        {
            FundedAwards = [new AwardFunding("Banker", 0)],
        };
        // Player 2 has highest MC prod (3), Player 1 second (2)

        var scores = Scoring.CalculateFinalScores(state);
        Assert.Equal(2, scores.First(s => s.PlayerId == 1).AwardPoints);
    }

    [Fact]
    public void Score_Award_TieForFirst_BothGet5VP_NoSecond()
    {
        // Give players 0 and 1 the same MC production
        var state = CreateEndGameState();
        state = state.UpdatePlayer(0, p => p with { Production = p.Production with { MegaCredits = 5 } });
        state = state.UpdatePlayer(1, p => p with { Production = p.Production with { MegaCredits = 5 } });
        state = state.UpdatePlayer(2, p => p with { Production = p.Production with { MegaCredits = 2 } });
        state = state with { FundedAwards = [new AwardFunding("Banker", 2)] };

        var scores = Scoring.CalculateFinalScores(state);

        Assert.Equal(5, scores.First(s => s.PlayerId == 0).AwardPoints);
        Assert.Equal(5, scores.First(s => s.PlayerId == 1).AwardPoints);
        Assert.Equal(0, scores.First(s => s.PlayerId == 2).AwardPoints); // No 2nd place when tied for 1st
    }

    [Fact]
    public void Score_Award_2Player_NoSecondPlace()
    {
        var state = CreateEndGameState(playerCount: 2) with
        {
            FundedAwards = [new AwardFunding("Thermalist", 0)],
        };
        // Give player 0 more heat so there's a clear winner
        state = state.UpdatePlayer(0, p => p with { Resources = p.Resources with { Heat = 10 } });
        state = state.UpdatePlayer(1, p => p with { Resources = p.Resources with { Heat = 3 } });

        var scores = Scoring.CalculateFinalScores(state);

        // Player 0 gets 5 VP for 1st, player 1 gets 0 (no 2nd place in 2-player)
        Assert.Equal(5, scores.First(s => s.PlayerId == 0).AwardPoints);
        Assert.Equal(0, scores.First(s => s.PlayerId == 1).AwardPoints);
    }

    [Fact]
    public void Score_Award_FunderDoesntGetBonus()
    {
        // Player 0 funds the award but Player 2 wins it
        var state = CreateEndGameState() with
        {
            FundedAwards = [new AwardFunding("Banker", 0)],
        };

        var scores = Scoring.CalculateFinalScores(state);

        // Player 2 wins (MC prod 3), not player 0 who funded it
        Assert.Equal(5, scores.First(s => s.PlayerId == 2).AwardPoints);
    }

    // ── Tiebreaker ─────────────────────────────────────────────

    [Fact]
    public void Score_Tiebreaker_MostMCWins()
    {
        var state = CreateEndGameState(playerCount: 2);
        // Give both players same TR
        state = state.UpdatePlayer(0, p => p with { TerraformRating = 25, Resources = p.Resources with { MegaCredits = 30 } });
        state = state.UpdatePlayer(1, p => p with { TerraformRating = 25, Resources = p.Resources with { MegaCredits = 10 } });

        var scores = Scoring.CalculateFinalScores(state);

        // Same total score, but player 0 has more MC
        Assert.Equal(0, scores[0].PlayerId);
    }

    // ── Total Score ────────────────────────────────────────────

    [Fact]
    public void Score_TotalIsSum()
    {
        var state = CreateEndGameState() with
        {
            ClaimedMilestones = [new MilestoneClaim("Terraformer", 0)],
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(5, 3), new PlacedTile(TileType.Greenery, 0, new HexCoord(5, 3))),
        };

        var scores = Scoring.CalculateFinalScores(state);
        var p0 = scores.First(s => s.PlayerId == 0);

        Assert.Equal(
            p0.TerraformRating + p0.MilestonePoints + p0.AwardPoints +
            p0.GreeneryPoints + p0.CityPoints + p0.CardPoints,
            p0.TotalScore);
    }

    // ── Milestone Eligibility ──────────────────────────────────

    [Fact]
    public void Milestone_Terraformer_Requires35TR()
    {
        var state = CreateEndGameState();
        state = state.UpdatePlayer(0, p => p with { TerraformRating = 34 });

        var error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Terraformer");
        Assert.NotNull(error);

        state = state.UpdatePlayer(0, p => p with { TerraformRating = 35 });
        error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Terraformer");
        Assert.Null(error);
    }

    [Fact]
    public void Milestone_Mayor_Requires3Cities()
    {
        var state = CreateEndGameState() with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(3, 1), new PlacedTile(TileType.City, 0, new HexCoord(3, 1)))
                .Add(new HexCoord(7, 3), new PlacedTile(TileType.City, 0, new HexCoord(7, 3))),
        };

        var error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Mayor");
        Assert.NotNull(error); // Only 2 cities

        state = state with
        {
            PlacedTiles = state.PlacedTiles
                .Add(new HexCoord(5, 7), new PlacedTile(TileType.City, 0, new HexCoord(5, 7))),
        };

        error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Mayor");
        Assert.Null(error); // 3 cities
    }

    [Fact]
    public void Milestone_Generalist_Requires6ProductionTypes()
    {
        var state = CreateEndGameState();
        state = state.UpdatePlayer(0, p => p with
        {
            Production = new ProductionSet(MegaCredits: 1, Steel: 1, Titanium: 1, Plants: 1, Energy: 1, Heat: 0),
        });

        var error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Generalist");
        Assert.NotNull(error); // Only 5

        state = state.UpdatePlayer(0, p => p with
        {
            Production = p.Production with { Heat = 1 },
        });
        error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Generalist");
        Assert.Null(error); // All 6
    }

    [Fact]
    public void Milestone_Specialist_Requires10Production()
    {
        var state = CreateEndGameState();
        state = state.UpdatePlayer(0, p => p with
        {
            Production = new ProductionSet(Energy: 9),
        });

        var error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Specialist");
        Assert.NotNull(error);

        state = state.UpdatePlayer(0, p => p with
        {
            Production = p.Production with { Energy = 10 },
        });
        error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Specialist");
        Assert.Null(error);
    }

    [Fact]
    public void Milestone_PolarExplorer_Requires3TilesInBottomRows()
    {
        var state = CreateEndGameState(playerCount: 2) with
        {
            Map = MapName.Hellas,
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(3, 8), new PlacedTile(TileType.City, 0, new HexCoord(3, 8)))
                .Add(new HexCoord(5, 9), new PlacedTile(TileType.Greenery, 0, new HexCoord(5, 9)))
                .Add(new HexCoord(4, 9), new PlacedTile(TileType.Greenery, 0, new HexCoord(4, 9))),
        };

        var error = MilestoneAndAwardLogic.CheckMilestoneEligibility(state, 0, "Polar Explorer");
        Assert.Null(error); // 3 tiles in rows 8-9
    }

    // ── Award Scoring Metrics ──────────────────────────────────

    [Fact]
    public void Award_Miner_SumsSteelAndTitanium()
    {
        var state = CreateEndGameState();
        state = state.UpdatePlayer(0, p => p with
        {
            Resources = p.Resources with { Steel = 7, Titanium = 3 },
        });

        var player = state.GetPlayer(0);
        var score = MilestoneAndAwardLogic.GetAwardScore(state, player, "Miner");
        Assert.Equal(10, score);
    }

    [Fact]
    public void Award_Landlord_CountsOwnedTiles()
    {
        var state = CreateEndGameState() with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(new HexCoord(5, 3), new PlacedTile(TileType.City, 0, new HexCoord(5, 3)))
                .Add(new HexCoord(6, 3), new PlacedTile(TileType.Greenery, 0, new HexCoord(6, 3)))
                .Add(new HexCoord(7, 3), new PlacedTile(TileType.Ocean, null, new HexCoord(7, 3))), // Ocean unowned
        };

        var player = state.GetPlayer(0);
        var score = MilestoneAndAwardLogic.GetAwardScore(state, player, "Landlord");
        Assert.Equal(2, score); // City + Greenery, not ocean
    }

    [Fact]
    public void Award_EstateDealer_CountsTilesAdjacentToOcean()
    {
        var ocean = new HexCoord(4, 1); // Ocean on Tharsis
        var adjacent = new HexCoord(5, 1); // Adjacent to ocean
        var notAdjacent = new HexCoord(8, 7); // Far from ocean

        var state = CreateEndGameState() with
        {
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty
                .Add(ocean, new PlacedTile(TileType.Ocean, null, ocean))
                .Add(adjacent, new PlacedTile(TileType.City, 0, adjacent))
                .Add(notAdjacent, new PlacedTile(TileType.Greenery, 0, notAdjacent)),
        };

        var player = state.GetPlayer(0);
        var score = MilestoneAndAwardLogic.GetAwardScore(state, player, "Estate Dealer");
        Assert.Equal(1, score); // Only the city adjacent to ocean counts
    }
}
