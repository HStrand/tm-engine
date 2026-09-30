using System.Collections.Immutable;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;
using TmEngine.Domain.Moves;

namespace TmEngine.Domain.Tests.Engine;

public class DeckTests
{
    private static GameState CreateState(
        ImmutableList<string> drawPile, ImmutableList<string> discardPile, int shuffleSeed = 0) => new()
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
        Players = [PlayerState.CreateInitial(0, 20), PlayerState.CreateInitial(1, 20)],
        PlacedTiles = ImmutableDictionary<HexCoord, PlacedTile>.Empty,
        ClaimedMilestones = [],
        FundedAwards = [],
        DrawPile = drawPile,
        DiscardPile = discardPile,
        ShuffleSeed = shuffleSeed,
        MoveNumber = 0,
    };

    [Fact]
    public void Draw_TakesCardsFromTheTop()
    {
        var state = CreateState(["a", "b", "c"], ["x"]);

        var (after, cards) = Deck.Draw(state, 2);

        Assert.Equal(["a", "b"], cards);
        Assert.Equal(["c"], after.DrawPile);
        Assert.Equal(["x"], after.DiscardPile);
        Assert.Equal(0, after.ReshuffleCount);
    }

    [Fact]
    public void Draw_ReshufflesTheDiscardPile_WhenTheDrawPileRunsOut()
    {
        var state = CreateState(["a"], ["x", "y", "z"]);

        var (after, cards) = Deck.Draw(state, 3);

        Assert.Equal("a", cards[0]);
        Assert.Equal(3, cards.Count);
        Assert.Subset(new HashSet<string> { "x", "y", "z" }, cards.Skip(1).ToHashSet());
        Assert.Single(after.DrawPile);
        Assert.Empty(after.DiscardPile);
        Assert.Equal(1, after.ReshuffleCount);
    }

    [Fact]
    public void Draw_ReturnsWhatThereIs_WhenBothPilesAreExhausted()
    {
        var state = CreateState(["a"], ["x"]);

        var (after, cards) = Deck.Draw(state, 5);

        Assert.Equal(2, cards.Count);
        Assert.Empty(after.DrawPile);
        Assert.Empty(after.DiscardPile);
    }

    [Fact]
    public void Reshuffle_IsDeterministic_ForTheSameSeed()
    {
        ImmutableList<string> discard = [.. Enumerable.Range(0, 30).Select(i => $"card{i}")];

        var (_, first) = Deck.Draw(CreateState([], discard, shuffleSeed: 7), 30);
        var (_, again) = Deck.Draw(CreateState([], discard, shuffleSeed: 7), 30);
        var (_, otherSeed) = Deck.Draw(CreateState([], discard, shuffleSeed: 8), 30);

        Assert.Equal(first, again);
        Assert.NotEqual(first, otherSeed);
    }

    [Fact]
    public void Reshuffle_OrdersCardsDifferently_EachTimeInTheSameGame()
    {
        ImmutableList<string> cards = [.. Enumerable.Range(0, 30).Select(i => $"card{i}")];

        var (afterFirst, first) = Deck.Draw(CreateState([], cards), 30);
        var (_, second) = Deck.Draw(afterFirst with { DiscardPile = cards }, 30);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void DrawToHand_AddsTheDrawnCardsToThePlayersHand()
    {
        var state = CreateState(["a", "b", "c"], []);

        var after = Deck.DrawToHand(state, playerId: 1, count: 2);

        Assert.Equal(["a", "b"], after.GetPlayer(1).Hand);
        Assert.Empty(after.GetPlayer(0).Hand);
    }

    [Fact]
    public void SellPatents_PutsTheSoldCardsOnTheDiscardPile()
    {
        var state = CreateState([], ["x"]).UpdatePlayer(0, p => p with { Hand = ["card1", "card2", "card3"] });

        var (after, result) = GameEngine.Apply(state, new SellPatentsMove(0, ["card1", "card3"]));

        Assert.True(result.IsSuccess);
        Assert.Equal(["x", "card1", "card3"], after.DiscardPile);
        Assert.Equal(["card2"], after.GetPlayer(0).Hand);
    }

    [Fact]
    public void NewGeneration_DealsResearchCardsFromAReshuffledDiscardPile_WhenTheDrawPileIsShort()
    {
        ImmutableList<string> discard = [.. Enumerable.Range(0, 10).Select(i => $"discard{i}")];
        var state = CreateState(["a", "b"], discard);

        var after = PhaseManager.StartNewGeneration(state);

        Assert.All(after.Research!.AvailableCards, cards => Assert.Equal(Constants.ResearchCardsDealt, cards.Count));
        Assert.Equal(["a", "b"], after.Research.AvailableCards[0].Take(2));
        Assert.Equal(4, after.DrawPile.Count);
        Assert.Empty(after.DiscardPile);
    }
}
