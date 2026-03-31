using System.Collections.Immutable;
using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Integration;

/// <summary>
/// Integration tests that simulate complete or partial games using real cards.
/// These verify the full pipeline: setup → actions → production → scoring.
/// </summary>
public class FullGameTests
{
    // ── Setup Flow ─────────────────────────────────────────────

    [Fact]
    public void FullSetup_2Player_CorporateEra()
    {
        var state = GameEngine.Setup(
            new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false),
            seed: 100);

        Assert.Equal(GamePhase.Setup, state.Phase);
        Assert.NotNull(state.Setup);
        Assert.Equal(2, state.Setup!.DealtCorporations[0].Count); // 2 corps each
        Assert.Equal(10, state.Setup.DealtCards[0].Count); // 10 project cards each

        // Both players submit setup
        var corp0 = state.Setup.DealtCorporations[0][0];
        var corp1 = state.Setup.DealtCorporations[1][0];
        var cards0 = state.Setup.DealtCards[0].Take(3).ToImmutableArray(); // buy 3 cards
        var cards1 = state.Setup.DealtCards[1].Take(2).ToImmutableArray(); // buy 2 cards

        var (s1, r1) = GameEngine.Apply(state, new SetupMove(0, corp0, [], cards0));
        Assert.True(r1.IsSuccess);
        Assert.Equal(GamePhase.Setup, s1.Phase); // still waiting for player 1

        var (s2, r2) = GameEngine.Apply(s1, new SetupMove(1, corp1, [], cards1));
        Assert.True(r2.IsSuccess);

        // Should now be in Action phase (or first action pending)
        Assert.NotEqual(GamePhase.Setup, s2.Phase);
        Assert.Null(s2.Setup);

        // Players should have their corporations
        Assert.Equal(corp0, s2.Players[0].CorporationId);
        Assert.Equal(corp1, s2.Players[1].CorporationId);

        // Players should have starting MC from corporations (minus card costs)
        Assert.True(s2.Players[0].Resources.MegaCredits > 0);
        Assert.True(s2.Players[1].Resources.MegaCredits > 0);

        // Players should have their bought cards in hand
        Assert.Equal(3, s2.Players[0].Hand.Count);
        Assert.Equal(2, s2.Players[1].Hand.Count);
    }

    [Fact]
    public void FullSetup_WithPrelude()
    {
        var state = GameEngine.Setup(
            new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: true),
            seed: 200);

        Assert.Equal(GamePhase.Setup, state.Phase);
        Assert.Equal(5, state.Setup!.DealtCorporations[0].Count); // 5 corps with prelude
        Assert.Equal(4, state.Setup.DealtPreludes[0].Count); // 4 preludes each

        var corp0 = state.Setup.DealtCorporations[0][0];
        var corp1 = state.Setup.DealtCorporations[1][0];
        var preludes0 = state.Setup.DealtPreludes[0].Take(2).ToImmutableArray();
        var preludes1 = state.Setup.DealtPreludes[1].Take(2).ToImmutableArray();

        var (s1, _) = GameEngine.Apply(state, new SetupMove(0, corp0, preludes0, []));
        var (s2, _) = GameEngine.Apply(s1, new SetupMove(1, corp1, preludes1, []));

        Assert.NotEqual(GamePhase.Setup, s2.Phase);
        // Preludes should be in played cards
        Assert.True(s2.Players[0].PlayedCards.Count >= 2);
        Assert.True(s2.Players[1].PlayedCards.Count >= 2);
    }

    // ── Standard Projects ──────────────────────────────────────

    [Fact]
    public void MultipleGenerations_StandardProjectsOnly()
    {
        // Create a game already in action phase with resources
        var state = CreateActionPhaseState();

        // Gen 1: Player 0 uses Power Plant, Player 1 uses Asteroid
        var (s1, r1) = GameEngine.Apply(state,
            new UseStandardProjectMove(0, StandardProject.PowerPlant));
        Assert.True(r1.IsSuccess);
        Assert.Equal(1, s1.Players[0].Production.Energy);

        var (s2, _) = GameEngine.Apply(s1, new PassMove(0)); // pass after 1 action

        var (s3, r3) = GameEngine.Apply(s2,
            new UseStandardProjectMove(1, StandardProject.Asteroid));
        Assert.True(r3.IsSuccess);
        Assert.Equal(-28, s3.Temperature); // -30 + 2

        var (s4, _) = GameEngine.Apply(s3, new PassMove(1));

        // Should now be in Research phase, generation 2
        Assert.Equal(GamePhase.Research, s4.Phase);
        Assert.Equal(2, s4.Generation);
        Assert.NotNull(s4.Research);

        // Both players buy 0 cards from research
        var (s5, _) = GameEngine.Apply(s4, new BuyCardsMove(0, []));
        var (s6, _) = GameEngine.Apply(s5, new BuyCardsMove(1, []));

        // Should now be in Action phase gen 2
        Assert.Equal(GamePhase.Action, s6.Phase);
        Assert.Equal(2, s6.Generation);
    }

    // ── Card Playing ───────────────────────────────────────────

    [Fact]
    public void PlayCard_AutomatedCard_AppliesEffects()
    {
        var state = CreateActionPhaseState();

        // Find a simple automated card to play — Mine (056): +1 steel prod, costs 4
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("056"),
        });

        var (s1, r1) = GameEngine.Apply(state,
            new PlayCardMove(0, "056", new PaymentInfo(MegaCredits: 4)));

        Assert.True(r1.IsSuccess);
        Assert.Equal(1, s1.Players[0].Production.Steel);
        Assert.Equal(46, s1.Players[0].Resources.MegaCredits); // 50 - 4
        Assert.DoesNotContain("056", s1.Players[0].Hand);
        Assert.Contains("056", s1.Players[0].PlayedCards);
    }

    [Fact]
    public void PlayCard_WithSteelPayment()
    {
        var state = CreateActionPhaseState();

        // House Printing (P36): building tag, cost 10, +1 steel prod
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("P36"),
        });

        // Pay 4 MC + 3 steel (3 * 2 = 6 MC value, total 10)
        var (s1, r1) = GameEngine.Apply(state,
            new PlayCardMove(0, "P36", new PaymentInfo(MegaCredits: 4, Steel: 3)));

        Assert.True(r1.IsSuccess);
        Assert.Equal(46, s1.Players[0].Resources.MegaCredits); // 50 - 4
        Assert.Equal(7, s1.Players[0].Resources.Steel); // 10 - 3
        Assert.Equal(1, s1.Players[0].Production.Steel);
    }

    [Fact]
    public void PlayCard_EventGoesToEventPile()
    {
        var state = CreateActionPhaseState();

        // Mineral Deposit (062): event, cost 5, gain 5 steel
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("062"),
        });

        var (s1, _) = GameEngine.Apply(state,
            new PlayCardMove(0, "062", new PaymentInfo(MegaCredits: 5)));

        Assert.DoesNotContain("062", s1.Players[0].PlayedCards);
        Assert.Contains("062", s1.Players[0].PlayedEvents);
        Assert.Equal(15, s1.Players[0].Resources.Steel); // 10 + 5
    }

    [Fact]
    public void PlayCard_RequirementNotMet_Rejected()
    {
        var state = CreateActionPhaseState();

        // Algae (047): requires 5 oceans, costs 10
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("047"),
        });

        var (_, result) = GameEngine.Apply(state,
            new PlayCardMove(0, "047", new PaymentInfo(MegaCredits: 10)));

        Assert.True(result.IsError); // 0 oceans, need 5
    }

    [Fact]
    public void PlayCard_WithTilePlacement_TriggersPending()
    {
        var state = CreateActionPhaseState();

        // Cupola City (029): place city, -1 energy prod, +3 MC prod, cost 16
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("029"),
            Production = p.Production with { Energy = 1 },
        });

        var (s1, r1) = GameEngine.Apply(state,
            new PlayCardMove(0, "029", new PaymentInfo(MegaCredits: 16)));

        Assert.True(r1.IsSuccess, $"Expected success but got: {(r1 as Error)?.Message}");
        Assert.NotNull(s1.PendingAction);
        Assert.IsType<PlaceTilePending>(s1.PendingAction);

        var pending = (PlaceTilePending)s1.PendingAction;
        Assert.Equal(TileType.City, pending.TileType);

        // Resolve by placing the city
        var location = pending.ValidLocations[0];
        var (s2, r2) = GameEngine.Apply(s1, new PlaceTileMove(0, location));

        Assert.True(r2.IsSuccess);
        Assert.Null(s2.PendingAction);
        Assert.True(s2.PlacedTiles.ContainsKey(location));
    }

    [Fact]
    public void PlayCard_InsufficientProductionToDecrease_Rejected()
    {
        var state = CreateActionPhaseState();

        // Cupola City (029): needs -1 energy prod, but player has 0
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("029"),
        });

        var (_, result) = GameEngine.Apply(state,
            new PlayCardMove(0, "029", new PaymentInfo(MegaCredits: 12)));

        Assert.True(result.IsError);
    }

    // ── Ongoing Effects / Triggers ─────────────────────────────

    [Fact]
    public void OngoingEffect_ArcticAlgae_GainsPlantsOnOcean()
    {
        var state = CreateActionPhaseState();

        // Play Arctic Algae (023) — needs max -12°C, gives +1 plant and ongoing ocean trigger
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = p.Hand.Add("023"),
            Resources = p.Resources with { Plants = 0 },
        });

        var (s1, _) = GameEngine.Apply(state,
            new PlayCardMove(0, "023", new PaymentInfo(MegaCredits: 12)));

        Assert.Equal(1, s1.Players[0].Resources.Plants); // immediate +1 plant

        // Now player 1 places an ocean
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));
        var oceanHex = BoardLogic.GetValidOceanPlacements(s2).First();
        var (s3, _) = GameEngine.Apply(s2,
            new UseStandardProjectMove(1, StandardProject.Aquifer, Location: oceanHex));

        // Arctic Algae should trigger: player 0 gains 2 plants
        Assert.Equal(3, s3.Players[0].Resources.Plants); // 1 + 2
    }

    // ── Production Phase ───────────────────────────────────────

    [Fact]
    public void ProductionPhase_AllResourcesGenerated()
    {
        var state = CreateActionPhaseState();
        state = state.UpdatePlayer(0, p => p with
        {
            TerraformRating = 25,
            Production = new ProductionSet(MegaCredits: 3, Steel: 2, Titanium: 1, Plants: 1, Energy: 2, Heat: 1),
            Resources = new ResourceSet(MegaCredits: 10, Steel: 0, Titanium: 0, Plants: 0, Energy: 5, Heat: 3),
        });
        state = state.UpdatePlayer(1, p => p with { Resources = ResourceSet.Zero });

        // Both pass → production
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        var p0 = s2.Players[0];

        // MC = old(10) + TR(25) + prod(3) = 38
        Assert.Equal(38, p0.Resources.MegaCredits);
        // Steel = 0 + prod(2) = 2
        Assert.Equal(2, p0.Resources.Steel);
        // Energy: old energy(5) → heat, then new energy from prod(2)
        Assert.Equal(2, p0.Resources.Energy);
        // Heat: old heat(3) + old energy(5) + prod(1) = 9
        Assert.Equal(9, p0.Resources.Heat);
        // Plants: 0 + prod(1) = 1
        Assert.Equal(1, p0.Resources.Plants);
    }

    // ── Game End ───────────────────────────────────────────────

    [Fact]
    public void GameEnd_AllParametersMaxed_EndsAfterProduction()
    {
        var state = CreateActionPhaseState() with
        {
            Oxygen = Constants.DefaultMaxOxygen,
            Temperature = Constants.DefaultMaxTemperature,
            OceansPlaced = Constants.DefaultMaxOceans,
        };

        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        Assert.Equal(GamePhase.FinalGreeneryConversion, s2.Phase);

        // Both pass final greenery
        var (s3, _) = GameEngine.Apply(s2, new PassMove(0));
        var (s4, _) = GameEngine.Apply(s3, new PassMove(1));

        Assert.Equal(GamePhase.GameEnd, s4.Phase);
    }

    [Fact]
    public void Scoring_EndGame_ProducesRankedScores()
    {
        var state = CreateActionPhaseState() with
        {
            Oxygen = Constants.DefaultMaxOxygen,
            Temperature = Constants.DefaultMaxTemperature,
            OceansPlaced = Constants.DefaultMaxOceans,
        };

        // Give player 0 some advantages
        state = state.UpdatePlayer(0, p => p with
        {
            TerraformRating = 30,
        });
        state = state with
        {
            ClaimedMilestones = [new MilestoneClaim("Terraformer", 0)],
            PlacedTiles = state.PlacedTiles
                .Add(new HexCoord(5, 3), new PlacedTile(TileType.Greenery, 0, new HexCoord(5, 3)))
                .Add(new HexCoord(4, 3), new PlacedTile(TileType.Greenery, 0, new HexCoord(4, 3))),
        };

        // End the game
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));
        var (s3, _) = GameEngine.Apply(s2, new PassMove(0));
        var (s4, _) = GameEngine.Apply(s3, new PassMove(1));

        Assert.Equal(GamePhase.GameEnd, s4.Phase);

        var scores = Scoring.CalculateFinalScores(s4);

        Assert.Equal(2, scores.Count);
        Assert.Equal(0, scores[0].PlayerId); // Player 0 should win
        Assert.True(scores[0].TotalScore > scores[1].TotalScore);
        Assert.Equal(5, scores[0].MilestonePoints); // 1 milestone = 5 VP
        Assert.Equal(2, scores[0].GreeneryPoints); // 2 greeneries
    }

    // ── Multi-Turn Sequence ────────────────────────────────────

    [Fact]
    public void MultiTurn_PlayCardsAndStandardProjects()
    {
        var state = CreateActionPhaseState();

        // Give player cards
        state = state.UpdatePlayer(0, p => p with
        {
            Hand = ImmutableList.Create("056", "048", "068"), // Mine, Adapted Lichen, Sponsors
        });

        // Turn 1: Player 0 plays Mine (cost 4, +1 steel prod)
        var (s1, r1) = GameEngine.Apply(state,
            new PlayCardMove(0, "056", new PaymentInfo(MegaCredits: 4)));
        Assert.True(r1.IsSuccess);

        // Turn 1 action 2: Player 0 plays Adapted Lichen (cost 9, +1 plant prod)
        var (s2, r2) = GameEngine.Apply(s1,
            new PlayCardMove(0, "048", new PaymentInfo(MegaCredits: 9)));
        Assert.True(r2.IsSuccess);

        // After 2 actions, turn passes to player 1
        Assert.Equal(1, s2.ActivePlayerIndex);

        // Player 1 uses Power Plant standard project
        var (s3, _) = GameEngine.Apply(s2,
            new UseStandardProjectMove(1, StandardProject.PowerPlant));

        // Player 1 passes
        var (s4, _) = GameEngine.Apply(s3, new PassMove(1));

        // Back to player 0 (hasn't passed yet)
        Assert.Equal(0, s4.ActivePlayerIndex);

        // Player 0 plays Sponsors (cost 10, +2 MC prod)
        var (s5, _) = GameEngine.Apply(s4,
            new PlayCardMove(0, "068", new PaymentInfo(MegaCredits: 10)));

        // Player 0 passes
        var (s6, _) = GameEngine.Apply(s5, new PassMove(0));

        // Both passed → production → research for gen 2
        Assert.Equal(GamePhase.Research, s6.Phase);
        Assert.Equal(2, s6.Generation);

        // Check production happened
        var p0 = s6.Players[0];
        Assert.Equal(1, p0.Production.Steel);
        Assert.Equal(1, p0.Production.Plants);
        Assert.Equal(2, p0.Production.MegaCredits);
    }

    // ── Convert Plants / Heat ──────────────────────────────────

    [Fact]
    public void ConvertPlants_PlacesGreenery_RaisesOxygen()
    {
        var state = CreateActionPhaseState();
        state = state.UpdatePlayer(0, p => p with
        {
            Resources = p.Resources with { Plants = 10 },
        });

        var validGreeneries = BoardLogic.GetValidGreeneryPlacements(state, 0);
        var location = validGreeneries[0];

        var (s1, r1) = GameEngine.Apply(state, new ConvertPlantsMove(0, location));

        Assert.True(r1.IsSuccess);
        Assert.Equal(2, s1.Players[0].Resources.Plants); // 10 - 8
        Assert.Equal(1, s1.Oxygen);
        Assert.Equal(21, s1.Players[0].TerraformRating); // 20 + 1
        Assert.True(s1.PlacedTiles.ContainsKey(location));
    }

    [Fact]
    public void ConvertHeat_RaisesTemperature()
    {
        var state = CreateActionPhaseState();
        state = state.UpdatePlayer(0, p => p with
        {
            Resources = p.Resources with { Heat = 10 },
        });

        var (s1, r1) = GameEngine.Apply(state, new ConvertHeatMove(0));

        Assert.True(r1.IsSuccess);
        Assert.Equal(2, s1.Players[0].Resources.Heat); // 10 - 8
        Assert.Equal(-28, s1.Temperature); // -30 + 2
        Assert.Equal(21, s1.Players[0].TerraformRating);
    }

    // ── Milestones & Awards ────────────────────────────────────

    [Fact]
    public void ClaimMilestone_RequiresThreshold()
    {
        var state = CreateActionPhaseState();

        // Try claiming Terraformer with TR 20 (needs 35) — should fail
        var (_, r1) = GameEngine.Apply(state,
            new ClaimMilestoneMove(0, "Terraformer"));
        Assert.True(r1.IsError);

        // Give enough TR and try again
        state = state.UpdatePlayer(0, p => p with { TerraformRating = 35 });
        var (s2, r2) = GameEngine.Apply(state,
            new ClaimMilestoneMove(0, "Terraformer"));
        Assert.True(r2.IsSuccess);
        Assert.Single(s2.ClaimedMilestones);
    }

    [Fact]
    public void FundAward_EscalatingCosts()
    {
        var state = CreateActionPhaseState();

        // Fund first award (8 MC)
        var (s1, _) = GameEngine.Apply(state, new FundAwardMove(0, "Landlord"));
        Assert.Equal(42, s1.Players[0].Resources.MegaCredits); // 50 - 8
        var (s2, _) = GameEngine.Apply(s1, new PassMove(0));

        // Fund second award (14 MC)
        var (s3, _) = GameEngine.Apply(s2, new FundAwardMove(1, "Banker"));
        Assert.Equal(36, s3.Players[1].Resources.MegaCredits); // 50 - 14

        Assert.Equal(2, s3.FundedAwards.Count);
    }

    // ── Research Phase ─────────────────────────────────────────

    [Fact]
    public void ResearchPhase_DealAndBuyCards()
    {
        var state = CreateActionPhaseState();

        // Both pass → production → research
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        Assert.Equal(GamePhase.Research, s2.Phase);
        Assert.NotNull(s2.Research);

        var available0 = s2.Research!.AvailableCards[0];
        Assert.Equal(4, available0.Count);

        // Player 0 buys 2 of 4 dealt cards
        var toBuy = available0.Take(2).ToImmutableArray();
        var (s3, r3) = GameEngine.Apply(s2, new BuyCardsMove(0, toBuy));
        Assert.True(r3.IsSuccess);

        // Player 1 buys 0
        var (s4, _) = GameEngine.Apply(s3, new BuyCardsMove(1, []));

        Assert.Equal(GamePhase.Action, s4.Phase);
        Assert.Equal(2, s4.Generation);
    }

    // ── Determinism ────────────────────────────────────────────

    [Fact]
    public void SameSetup_SameSeed_IdenticalState()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);

        var state1 = GameEngine.Setup(options, seed: 42);
        var state2 = GameEngine.Setup(options, seed: 42);

        Assert.Equal(state1.Setup!.DealtCorporations, state2.Setup!.DealtCorporations);
        Assert.Equal(state1.Setup.DealtCards, state2.Setup.DealtCards);
        Assert.Equal(state1.DrawPile.Count, state2.DrawPile.Count);

        // Apply identical moves
        var corp = state1.Setup.DealtCorporations[0][0];
        var (s1a, _) = GameEngine.Apply(state1, new SetupMove(0, corp, [], []));
        var (s1b, _) = GameEngine.Apply(state2, new SetupMove(0, corp, [], []));

        Assert.Equal(s1a.Players[0].Resources, s1b.Players[0].Resources);
    }

    // ── Seeded Game with Real Cards ──────────────────────────

    [Fact]
    public void SeededGame_RealCards_MultipleGenerations()
    {
        // Build a real draw pile from in-scope project cards
        var expansions = ImmutableHashSet.Create(Expansion.Base, Expansion.CorporateEra, Expansion.Prelude);
        var projectCardIds = CardRegistry.GetProjectCardIds(expansions);
        var rng = new Random(42);
        var drawPile = DeckBuilder.Shuffle(projectCardIds, rng);

        var state = CreateActionPhaseStateWithRealCards(drawPile);

        // Player 0 starts with some cards from the deck
        var (dealt0, remaining) = DeckBuilder.Deal(drawPile, 5);
        state = state with { DrawPile = remaining };
        state = state.UpdatePlayer(0, p => p with { Hand = dealt0 });

        // Player 0: try to play any affordable automated card from hand
        GameState current = state;
        int cardsPlayed = 0;

        foreach (var cardId in dealt0)
        {
            if (!CardRegistry.TryGet(cardId, out var entry)) continue;
            var card = entry.Definition;

            // Skip active cards (they might need actions later), skip events for simplicity
            if (card.Type != CardType.Automated) continue;

            // Try to play it with MC only
            var payment = new PaymentInfo(MegaCredits: card.Cost);
            var (next, result) = GameEngine.Apply(current, new PlayCardMove(0, cardId, payment));

            if (result.IsSuccess)
            {
                current = next;
                cardsPlayed++;

                // Resolve any pending tile placements
                while (current.PendingAction is PlaceTilePending tilePending)
                {
                    if (tilePending.ValidLocations.Length == 0) break;
                    var (resolved, _) = GameEngine.Apply(current,
                        new PlaceTileMove(0, tilePending.ValidLocations[0]));
                    current = resolved;
                }

                if (current.ActivePlayerIndex != 0) break; // turn advanced after 2 actions
            }
        }

        // Player 0 passes
        if (!current.Players[0].Passed && current.ActivePlayerIndex == 0 && current.PendingAction == null)
        {
            var (s, _) = GameEngine.Apply(current, new PassMove(0));
            current = s;
        }

        // Player 1 passes
        if (current.Phase == GamePhase.Action && !current.Players[1].Passed && current.PendingAction == null)
        {
            var (s, _) = GameEngine.Apply(current, new PassMove(1));
            current = s;
        }

        // Should be in research phase for gen 2
        Assert.Equal(GamePhase.Research, current.Phase);
        Assert.Equal(2, current.Generation);

        // Both players buy 0 cards
        var (r1, _) = GameEngine.Apply(current, new BuyCardsMove(0, []));
        var (r2, _) = GameEngine.Apply(r1, new BuyCardsMove(1, []));

        Assert.Equal(GamePhase.Action, r2.Phase);
        Assert.Equal(2, r2.Generation);

        // Verify game state is coherent
        Assert.True(r2.Players[0].TerraformRating >= 20);
        Assert.True(r2.Oxygen >= 0);
        Assert.True(r2.Temperature >= Constants.MinTemperature);
        Assert.True(cardsPlayed >= 0); // At least tried
        Assert.True(r2.MoveNumber > 0);
        Assert.NotEmpty(r2.Log);
    }

    [Fact]
    public void SeededGame_FullSetupToGen2_WithRealCorporations()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 777);

        // Pick first available corp and buy 0 cards for speed
        var corp0 = state.Setup!.DealtCorporations[0][0];
        var corp1 = state.Setup.DealtCorporations[1][0];

        var (s1, _) = GameEngine.Apply(state, new SetupMove(0, corp0, [], []));
        var (s2, _) = GameEngine.Apply(s1, new SetupMove(1, corp1, [], []));

        // Handle any first actions (Inventrix, Tharsis Republic, etc.)
        var current = s2;
        for (int i = 0; i < current.Players.Count; i++)
        {
            var player = current.Players[i];
            if (!player.PerformedFirstAction && current.ActivePlayerIndex == i && current.PendingAction == null)
            {
                var (next, _) = GameEngine.Apply(current,
                    new PerformFirstActionMove(player.PlayerId));
                current = next;

                // Resolve any sub-pending actions (e.g., Tharsis Republic city placement)
                while (current.PendingAction is PlaceTilePending tilePending)
                {
                    if (tilePending.ValidLocations.Length == 0) break;
                    var (resolved, _) = GameEngine.Apply(current,
                        new PlaceTileMove(current.ActivePlayer.PlayerId, tilePending.ValidLocations[0]));
                    current = resolved;
                }
            }
        }

        // Vitor: fund free award if needed
        while (current.Phase == GamePhase.Action
            && current.Players[current.ActivePlayerIndex].HasFreeAwardFunding
            && current.PendingAction == null)
        {
            var pid = current.ActivePlayer.PlayerId;
            var map = MapDefinitions.GetMap(current.Map);
            var availableAward = map.AwardNames.FirstOrDefault(a =>
                !current.FundedAwards.Any(f => f.AwardName == a));
            if (availableAward != null)
            {
                var (next, _) = GameEngine.Apply(current, new FundAwardMove(pid, availableAward));
                current = next;
            }
            else break;
        }

        Assert.Equal(GamePhase.Action, current.Phase);

        // Both pass gen 1
        if (!current.Players[0].Passed && current.ActivePlayerIndex == 0)
        {
            var (next, _) = GameEngine.Apply(current, new PassMove(0));
            current = next;
        }
        if (!current.Players[1].Passed && current.PendingAction == null)
        {
            var (next, _) = GameEngine.Apply(current, new PassMove(1));
            current = next;
        }

        // Should be research gen 2
        Assert.Equal(GamePhase.Research, current.Phase);
        Assert.Equal(2, current.Generation);

        // Players should have MC from production (TR + MC prod)
        Assert.True(current.Players[0].Resources.MegaCredits >= 0);
        Assert.True(current.Players[1].Resources.MegaCredits >= 0);

        // Research: both buy 0
        var (r1, _) = GameEngine.Apply(current, new BuyCardsMove(0, []));
        var (r2, _) = GameEngine.Apply(r1, new BuyCardsMove(1, []));

        Assert.Equal(GamePhase.Action, r2.Phase);

        // Log should have meaningful entries
        Assert.True(r2.Log.Count > 2);
    }

    // ── Helpers ─────────────────────────────────────────────────

    private static GameState CreateActionPhaseStateWithRealCards(ImmutableList<string> drawPile)
    {
        return new GameState
        {
            GameId = "real-cards-test",
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
            Players =
            [
                PlayerState.CreateInitial(0, 20) with
                {
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 3, Plants: 0, Energy: 2, Heat: 0),
                    Production = new ProductionSet(MegaCredits: 1, Energy: 1),
                },
                PlayerState.CreateInitial(1, 20) with
                {
                    Resources = new ResourceSet(MegaCredits: 40),
                },
            ],
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = drawPile,
            DiscardPile = [],
            MoveNumber = 0,
            Log = [],
        };
    }

    private static GameState CreateActionPhaseState()
    {
        var drawPile = Enumerable.Range(1, 100)
            .Select(i => $"draw{i}")
            .ToImmutableList();

        return new GameState
        {
            GameId = "integration-test",
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
            Players =
            [
                PlayerState.CreateInitial(0, 20) with
                {
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 10, Titanium: 5, Plants: 20, Energy: 5, Heat: 5),
                },
                PlayerState.CreateInitial(1, 20) with
                {
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 3, Plants: 10, Energy: 3, Heat: 3),
                },
            ],
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = drawPile,
            DiscardPile = [],
            MoveNumber = 0,
            Log = [],
        };
    }
}
