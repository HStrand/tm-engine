using System.Collections.Immutable;
using TmEngine.Domain.Cards.Effects;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.Domain.Tests.Engine;

public class EffectExecutorTests
{
    private static GameState CreateTestState()
    {
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
            Players =
            [
                PlayerState.CreateInitial(0, 20) with
                {
                    Resources = new ResourceSet(MegaCredits: 50, Steel: 5, Titanium: 5, Plants: 10, Energy: 5, Heat: 10),
                    Production = ProductionSet.Zero,
                },
                PlayerState.CreateInitial(1, 20) with
                {
                    Resources = new ResourceSet(MegaCredits: 30, Plants: 8),
                    Production = new ProductionSet(MegaCredits: 2),
                },
            ],
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = ImmutableList.Create("draw1", "draw2", "draw3"),
            DiscardPile = [],
            MoveNumber = 0,

        };
    }

    // ── Production Changes ─────────────────────────────────────

    [Fact]
    public void ChangeProduction_IncreasesPlayerProduction()
    {
        var state = CreateTestState();
        var effect = new ChangeProductionEffect(ResourceType.Steel, 2);

        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.Null(pending);
        Assert.Equal(2, newState.Players[0].Production.Steel);
    }

    [Fact]
    public void ChangeProduction_CanDecrease()
    {
        var state = CreateTestState();
        state = state.UpdatePlayer(0, p => p with { Production = p.Production with { Energy = 3 } });

        var effect = new ChangeProductionEffect(ResourceType.Energy, -1);
        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(2, newState.Players[0].Production.Energy);
    }

    // ── Resource Changes ───────────────────────────────────────

    [Fact]
    public void ChangeResource_AddsResources()
    {
        var state = CreateTestState();
        var effect = new ChangeResourceEffect(ResourceType.Titanium, 3);

        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(8, newState.Players[0].Resources.Titanium); // 5 + 3
    }

    // ── Remove Resource (red-bordered) ─────────────────────────

    [Fact]
    public void RemoveResource_SingleTarget_CreatesOptionalPending()
    {
        var state = CreateTestState();
        // Only player 1 has plants (besides player 0)
        var effect = new RemoveResourceEffect(ResourceType.Plants, 3);

        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.NotNull(pending);
        var removePending = Assert.IsType<RemoveResourcePending>(pending);
        Assert.True(removePending.IsOptional);
        Assert.Contains(1, removePending.ValidTargetPlayerIds);
        Assert.Equal(8, newState.Players[1].Resources.Plants); // not yet removed
    }

    [Fact]
    public void RemoveResource_NoTargets_SkipsGracefully()
    {
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with { Resources = p.Resources with { Titanium = 0 } });

        var effect = new RemoveResourceEffect(ResourceType.Titanium, 2);
        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.Null(pending);
        // No change since player 0 is the active player and can't target self
        Assert.Equal(5, newState.Players[0].Resources.Titanium);
    }

    // ── Protected Habitats ─────────────────────────────────────

    [Fact]
    public void ProtectedHabitats_BlocksPlantRemoval_SoleOpponent()
    {
        // Player 1 has Protected Habitats. Asteroid-style plant removal yields no pending.
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Add("173"),
        });

        var effect = new RemoveResourceEffect(ResourceType.Plants, 3);
        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.Null(pending);
        Assert.Equal(8, newState.Players[1].Resources.Plants); // unchanged
    }

    [Fact]
    public void ProtectedHabitats_DoesNotBlockNonPlantRemoval()
    {
        // Sabotage removes titanium/steel/MC — Protected Habitats must not shield these.
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Add("173"),
            Resources = p.Resources with { Titanium = 3 },
        });

        var effect = new RemoveResourceEffect(ResourceType.Titanium, 2);
        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.NotNull(pending);
        var removePending = Assert.IsType<RemoveResourcePending>(pending);
        Assert.Contains(1, removePending.ValidTargetPlayerIds);
    }

    [Fact]
    public void ProtectedHabitats_BlocksAnimalRemoval()
    {
        // Player 1 has Protected Habitats and an animal on Birds (072).
        // Predators-style remove should find no valid target.
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Add("173").Add("072"),
            CardResources = p.CardResources.SetItem("072", 2),
        });

        var effect = new RemoveCardResourceEffect(CardResourceType.Animal, 1, AnyPlayer: true);
        var (newState, pending) = EffectExecutor.Execute(state, 0, effect, sourceCardId: "024");

        Assert.Null(pending);
        Assert.Equal(2, newState.Players[1].CardResources["072"]); // unchanged
    }

    [Fact]
    public void ProtectedHabitats_BlocksMicrobeRemoval()
    {
        // Player 1 has Protected Habitats and microbes on Decomposers (131).
        // Ants-style microbe removal should find no valid target.
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Add("173").Add("131"),
            CardResources = p.CardResources.SetItem("131", 3),
        });

        var effect = new RemoveCardResourceEffect(CardResourceType.Microbe, 1, AnyPlayer: true);
        var (newState, pending) = EffectExecutor.Execute(state, 0, effect, sourceCardId: "035");

        Assert.Null(pending);
        Assert.Equal(3, newState.Players[1].CardResources["131"]);
    }

    [Fact]
    public void ProtectedHabitats_AllowsSelfRemovalOnAnyPlayerEffect()
    {
        // Acting player has Protected Habitats AND an animal on Birds. They play Predators
        // themselves — they must still be able to remove from their own animals.
        var state = CreateTestState();
        state = state.UpdatePlayer(0, p => p with
        {
            PlayedCards = p.PlayedCards.Add("173").Add("072"),
            CardResources = p.CardResources.SetItem("072", 2),
        });
        // No opponent animals.

        var effect = new RemoveCardResourceEffect(CardResourceType.Animal, 1, AnyPlayer: true);
        var (newState, pending) = EffectExecutor.Execute(state, 0, effect, sourceCardId: "024");

        // Auto-resolves (1 valid card = player 0's Birds).
        Assert.Null(pending);
        Assert.Equal(1, newState.Players[0].CardResources["072"]);
    }

    [Fact]
    public void ProtectedHabitats_OneProtectedOneUnprotected_OnlyUnprotectedTargetable()
    {
        // Player 1 protected, but player 0 isn't the only opponent here — we reuse the
        // 2-player setup but give player 0 the active role against player 1 (the only
        // opponent). To simulate a 3-player "mixed" scenario, we instead verify the
        // filter behavior by toggling protection between tests.
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Add("173"),
        });

        // Protected → no target
        var (_, pending1) = EffectExecutor.Execute(state, 0,
            new RemoveResourceEffect(ResourceType.Plants, 2));
        Assert.Null(pending1);

        // Remove Protected Habitats → target appears
        state = state.UpdatePlayer(1, p => p with
        {
            PlayedCards = p.PlayedCards.Remove("173"),
        });
        var (_, pending2) = EffectExecutor.Execute(state, 0,
            new RemoveResourceEffect(ResourceType.Plants, 2));
        var removePending = Assert.IsType<RemoveResourcePending>(pending2);
        Assert.Contains(1, removePending.ValidTargetPlayerIds);
    }

    [Fact]
    public void IsProtectedFromRemoval_ReturnsFalse_WhenNoProtection()
    {
        var state = CreateTestState();
        Assert.False(RequirementChecker.IsProtectedFromRemoval(state.Players[0]));
    }

    [Fact]
    public void IsProtectedFromRemoval_ReturnsTrue_WhenProtectedHabitatsInPlay()
    {
        var state = CreateTestState();
        state = state.UpdatePlayer(0, p => p with { PlayedCards = p.PlayedCards.Add("173") });
        Assert.True(RequirementChecker.IsProtectedFromRemoval(state.Players[0]));
    }

    // ── Global Parameters ──────────────────────────────────────

    [Fact]
    public void RaiseOxygen_IncreasesOxygenAndTR()
    {
        var state = CreateTestState();
        var effect = new RaiseOxygenEffect(1);

        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(1, newState.Oxygen);
        Assert.Equal(21, newState.Players[0].TerraformRating); // 20 + 1
    }

    [Fact]
    public void RaiseTemperature_IncreasesTempAndTR()
    {
        var state = CreateTestState();
        var effect = new RaiseTemperatureEffect(1);

        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(-28, newState.Temperature); // -30 + 2
        Assert.Equal(21, newState.Players[0].TerraformRating);
    }

    [Fact]
    public void RaiseTemperature_MultipleSteps()
    {
        var state = CreateTestState();
        var effect = new RaiseTemperatureEffect(2);

        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(-26, newState.Temperature); // -30 + 4
        Assert.Equal(22, newState.Players[0].TerraformRating); // +2 TR
    }

    [Fact]
    public void PlaceOcean_TriggersPendingAction()
    {
        var state = CreateTestState();
        var effect = new PlaceOceanEffect(1);

        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.NotNull(pending);
        Assert.IsType<PlaceTilePending>(pending);
        var tilePending = (PlaceTilePending)pending;
        Assert.Equal(TileType.Ocean, tilePending.TileType);
        Assert.True(tilePending.ValidLocations.Length > 0);
    }

    // ── Tile Placement ─────────────────────────────────────────

    [Fact]
    public void PlaceTile_City_TriggersPendingAction()
    {
        var state = CreateTestState();
        var effect = new PlaceTileEffect(TileType.City);

        var (_, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.NotNull(pending);
        Assert.IsType<PlaceTilePending>(pending);
        var tilePending = (PlaceTilePending)pending;
        Assert.Equal(TileType.City, tilePending.TileType);
    }

    [Fact]
    public void PlaceTile_WithIsolatedConstraint_FiltersLocations()
    {
        var state = CreateTestState();
        var existingTile = new HexCoord(5, 5);
        state = state with
        {
            PlacedTiles = state.PlacedTiles.Add(existingTile, new PlacedTile(TileType.City, 0, existingTile)),
        };

        var effect = new PlaceTileEffect(TileType.City, PlacementConstraint.Isolated);
        var (_, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.NotNull(pending);
        var tilePending = (PlaceTilePending)pending!;

        // No location should be adjacent to the existing tile
        foreach (var loc in tilePending.ValidLocations)
        {
            Assert.DoesNotContain(loc, existingTile.GetAdjacentCoords());
        }
    }

    // ── Card Draw ──────────────────────────────────────────────

    [Fact]
    public void DrawCards_DrawsFromPile()
    {
        var state = CreateTestState();
        state = state.UpdatePlayer(0, p => p with { Hand = ImmutableList<string>.Empty });

        var effect = new DrawCardsEffect(2);
        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(2, newState.Players[0].Hand.Count);
        Assert.Equal("draw1", newState.Players[0].Hand[0]);
        Assert.Equal("draw2", newState.Players[0].Hand[1]);
        Assert.Single(newState.DrawPile); // 3 - 2 = 1
    }

    // ── TR Changes ─────────────────────────────────────────────

    [Fact]
    public void ChangeTR_AdjustsTR()
    {
        var state = CreateTestState();
        var effect = new ChangeTREffect(-3);

        var (newState, _) = EffectExecutor.Execute(state, 0, effect);

        Assert.Equal(17, newState.Players[0].TerraformRating); // 20 - 3
    }

    // ── Passive Modifiers (no-op at execution) ─────────────────

    [Fact]
    public void PassiveModifiers_DoNotChangeState()
    {
        var state = CreateTestState();

        var effects = new Effect[]
        {
            new RequirementModifierEffect(2),
            new SteelValueModifierEffect(1),
            new TitaniumValueModifierEffect(1),
            new TagDiscountEffect(Tag.Space, 2),
            new GlobalDiscountEffect(1),
            new HeatAsPaymentEffect(),
        };

        foreach (var effect in effects)
        {
            var (newState, pending) = EffectExecutor.Execute(state, 0, effect);
            Assert.Null(pending);
            // State should be unchanged — these are passive modifiers
            Assert.Equal(state.Players[0].Resources, newState.Players[0].Resources);
        }
    }

    // ── ReduceAnyProduction ────────────────────────────────────

    [Fact]
    public void ReduceAnyProduction_SingleQualifyingPlayer_ReducesAutomatically()
    {
        // Use Steel (floor 0) so only the player who has ≥1 steel prod qualifies.
        // (MC production has a floor of -5, so both players would qualify there.)
        var state = CreateTestState();
        state = state.UpdatePlayer(1, p => p with
        {
            Production = p.Production with { Steel = 2 },
        });
        var effect = new ReduceAnyProductionEffect(ResourceType.Steel, 1);

        var (newState, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.Null(pending);
        Assert.Equal(1, newState.Players[1].Production.Steel); // 2 - 1
    }

    [Fact]
    public void ReduceAnyProduction_MultipleQualifying_TriggersPendingAction()
    {
        var state = CreateTestState();
        // Give both players production
        state = state.UpdatePlayer(0, p => p with { Production = p.Production with { MegaCredits = 3 } });
        state = state.UpdatePlayer(1, p => p with { Production = p.Production with { MegaCredits = 2 } });

        var effect = new ReduceAnyProductionEffect(ResourceType.MegaCredits, 1);
        var (_, pending) = EffectExecutor.Execute(state, 0, effect);

        Assert.NotNull(pending);
        Assert.IsType<ReduceProductionPending>(pending);
    }
}
