using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Engine;

public class SetupAndResearchTests
{
    // ── DeckBuilder ────────────────────────────────────────────

    [Fact]
    public void Shuffle_IsDeterministic_WithSameSeed()
    {
        var items = ImmutableArray.Create("a", "b", "c", "d", "e");

        var result1 = DeckBuilder.Shuffle(items, new Random(42));
        var result2 = DeckBuilder.Shuffle(items, new Random(42));

        Assert.Equal(result1, result2);
    }

    [Fact]
    public void Shuffle_ProducesDifferentOrder_WithDifferentSeed()
    {
        var items = ImmutableArray.Create("a", "b", "c", "d", "e", "f", "g", "h");

        var result1 = DeckBuilder.Shuffle(items, new Random(42));
        var result2 = DeckBuilder.Shuffle(items, new Random(99));

        // Extremely unlikely to be the same
        Assert.NotEqual(result1, result2);
    }

    [Fact]
    public void Deal_ReturnsCorrectCountAndRemainder()
    {
        var deck = ImmutableList.Create("a", "b", "c", "d", "e");

        var (dealt, remaining) = DeckBuilder.Deal(deck, 3);

        Assert.Equal(3, dealt.Count);
        Assert.Equal(2, remaining.Count);
        Assert.Equal(["a", "b", "c"], dealt);
        Assert.Equal(["d", "e"], remaining);
    }

    [Fact]
    public void Deal_DealsFromTop()
    {
        var deck = ImmutableList.Create("top", "mid", "bottom");

        var (dealt, _) = DeckBuilder.Deal(deck, 1);

        Assert.Single(dealt);
        Assert.Equal("top", dealt[0]);
    }

    [Fact]
    public void Deal_HandlesRequestingMoreThanAvailable()
    {
        var deck = ImmutableList.Create("a", "b");

        var (dealt, remaining) = DeckBuilder.Deal(deck, 5);

        Assert.Equal(2, dealt.Count);
        Assert.Empty(remaining);
    }

    [Fact]
    public void GetEnabledExpansions_BaseAlwaysIncluded()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: false, DraftVariant: false, PreludeExpansion: false);
        var expansions = DeckBuilder.GetEnabledExpansions(options);

        Assert.Contains(Expansion.Base, expansions);
    }

    [Fact]
    public void GetEnabledExpansions_CorporateEraWhenEnabled()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var expansions = DeckBuilder.GetEnabledExpansions(options);

        Assert.Contains(Expansion.CorporateEra, expansions);
    }

    [Fact]
    public void GetEnabledExpansions_PreludeWhenEnabled()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: true);
        var expansions = DeckBuilder.GetEnabledExpansions(options);

        Assert.Contains(Expansion.Prelude, expansions);
    }

    // ── Game Setup (no cards registered yet — tests use Action phase) ──

    [Fact]
    public void Setup_WithRegisteredCards_StartsInSetupPhase()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        Assert.Equal(GamePhase.Setup, state.Phase);
        Assert.Equal(2, state.Players.Count);
        Assert.NotNull(state.Setup);
    }

    [Fact]
    public void Setup_StandardGame_PlayersStartWithProduction()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: false, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        foreach (var player in state.Players)
        {
            Assert.Equal(1, player.Production.MegaCredits);
            Assert.Equal(1, player.Production.Steel);
            Assert.Equal(1, player.Production.Titanium);
            Assert.Equal(1, player.Production.Plants);
            Assert.Equal(1, player.Production.Energy);
            Assert.Equal(1, player.Production.Heat);
        }
    }

    [Fact]
    public void Setup_CorporateEra_PlayersStartWithNoProduction()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        foreach (var player in state.Players)
        {
            Assert.Equal(0, player.Production.MegaCredits);
            Assert.Equal(0, player.Production.Steel);
        }
    }

    [Fact]
    public void Setup_PlayersHaveCorrectStartingTR()
    {
        var options = new GameSetupOptions(3, MapName.Hellas, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        Assert.Equal(3, state.Players.Count);
        foreach (var player in state.Players)
        {
            Assert.Equal(Constants.StartingTR, player.TerraformRating);
        }
    }

    [Fact]
    public void Setup_InitialGlobalParameters()
    {
        var options = new GameSetupOptions(2, MapName.Tharsis, CorporateEra: true, DraftVariant: false, PreludeExpansion: false);
        var state = GameEngine.Setup(options, seed: 42);

        Assert.Equal(Constants.MinOxygen, state.Oxygen);
        Assert.Equal(Constants.MinTemperature, state.Temperature);
        Assert.Equal(0, state.OceansPlaced);
        Assert.Equal(1, state.Generation);
    }

    // ── Research Phase (simulated with manual state) ───────────

    [Fact]
    public void Research_BuyCards_DeductsMCAndAddsToHand()
    {
        var state = CreateStateInResearchPhase();

        var (newState, result) = GameEngine.Apply(state,
            new BuyCardsMove(0, ["r1", "r2"]));

        Assert.True(result.IsSuccess);
        var cost = 2 * Constants.CardBuyCost;
        Assert.Equal(50 - cost, newState.Players[0].Resources.MegaCredits);
        Assert.Contains("r1", newState.Players[0].Hand);
        Assert.Contains("r2", newState.Players[0].Hand);
    }

    [Fact]
    public void Research_BuyCards_DiscardsUnboughtCards()
    {
        var state = CreateStateInResearchPhase();

        var (newState, _) = GameEngine.Apply(state,
            new BuyCardsMove(0, ["r1"]));

        // r2, r3, r4 should be discarded
        Assert.Contains("r2", newState.DiscardPile);
        Assert.Contains("r3", newState.DiscardPile);
        Assert.Contains("r4", newState.DiscardPile);
    }

    [Fact]
    public void Research_BuyZeroCards_IsValid()
    {
        var state = CreateStateInResearchPhase();

        var (newState, result) = GameEngine.Apply(state, new BuyCardsMove(0, []));

        Assert.True(result.IsSuccess);
        Assert.Equal(50, newState.Players[0].Resources.MegaCredits); // No cost
    }

    [Fact]
    public void Research_InvalidCard_IsRejected()
    {
        var state = CreateStateInResearchPhase();

        var (_, result) = GameEngine.Apply(state,
            new BuyCardsMove(0, ["not_dealt"]));

        Assert.True(result.IsError);
    }

    [Fact]
    public void Research_AllPlayersBuy_TransitionsToAction()
    {
        var state = CreateStateInResearchPhase();

        // Player 0 buys
        var (s1, _) = GameEngine.Apply(state, new BuyCardsMove(0, ["r1"]));
        Assert.Equal(GamePhase.Research, s1.Phase); // Still research, player 1 hasn't bought

        // Player 1 buys
        var (s2, _) = GameEngine.Apply(s1, new BuyCardsMove(1, ["s1", "s2"]));
        Assert.Equal(GamePhase.Action, s2.Phase); // Both done, now action phase
        Assert.Null(s2.Research);
    }

    [Fact]
    public void Research_InsufficientMC_IsRejected()
    {
        var state = CreateStateInResearchPhase();
        state = state.UpdatePlayer(0, p => p with { Resources = p.Resources with { MegaCredits = 5 } });

        // Trying to buy 3 cards = 9 MC, but only have 5
        var (_, result) = GameEngine.Apply(state, new BuyCardsMove(0, ["r1", "r2", "r3"]));

        Assert.True(result.IsError);
    }

    // ── Full Generation Loop with Research ─────────────────────

    [Fact]
    public void FullLoop_ActionToResearchToAction()
    {
        // Start in action phase
        var state = CreateStateInActionPhaseWithDrawPile();

        // Both players pass → production → new generation → research phase
        var (s1, _) = GameEngine.Apply(state, new PassMove(0));
        var (s2, _) = GameEngine.Apply(s1, new PassMove(1));

        Assert.Equal(GamePhase.Research, s2.Phase);
        Assert.Equal(2, s2.Generation);
        Assert.NotNull(s2.Research);

        // Each player should have 4 cards available
        Assert.Equal(4, s2.Research!.AvailableCards[0].Count);
        Assert.Equal(4, s2.Research.AvailableCards[1].Count);

        // Both buy 0 cards
        var (s3, _) = GameEngine.Apply(s2, new BuyCardsMove(0, []));
        var (s4, _) = GameEngine.Apply(s3, new BuyCardsMove(1, []));

        Assert.Equal(GamePhase.Action, s4.Phase);
    }

    // ── Helper Methods ─────────────────────────────────────────

    private static GameState CreateStateInResearchPhase()
    {
        return new GameState
        {
            GameId = "test",
            Map = MapName.Tharsis,
            CorporateEra = true,
            DraftVariant = false,
            PreludeExpansion = false,
            Phase = GamePhase.Research,
            Generation = 2,
            ActivePlayerIndex = 1, // First player is 1 for gen 2
            FirstPlayerIndex = 1,
            Oxygen = 0,
            Temperature = Constants.MinTemperature,
            OceansPlaced = 0,
            Players =
            [
                PlayerState.CreateInitial(0, 20) with { Resources = new ResourceSet(MegaCredits: 50) },
                PlayerState.CreateInitial(1, 20) with { Resources = new ResourceSet(MegaCredits: 50) },
            ],
            PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
            ClaimedMilestones = [],
            FundedAwards = [],
            DrawPile = [],
            DiscardPile = [],
            Research = new ResearchState
            {
                AvailableCards =
                [
                    ImmutableList.Create("r1", "r2", "r3", "r4"),
                    ImmutableList.Create("s1", "s2", "s3", "s4"),
                ],
                Submitted = [false, false],
            },
            MoveNumber = 0,
            Log = [],
        };
    }

    private static GameState CreateStateInActionPhaseWithDrawPile()
    {
        // Create a state with enough cards in the draw pile for research
        var drawPile = Enumerable.Range(1, 20).Select(i => $"card{i}").ToImmutableList();

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
            Players =
            [
                PlayerState.CreateInitial(0, 20) with { Resources = new ResourceSet(MegaCredits: 50) },
                PlayerState.CreateInitial(1, 20) with { Resources = new ResourceSet(MegaCredits: 50) },
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
