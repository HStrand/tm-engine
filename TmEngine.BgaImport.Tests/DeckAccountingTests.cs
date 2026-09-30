using TmEngine.Domain.Cards;
using TmEngine.Domain.Engine;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// The deck at a fork is rebuilt from the cards the log names. That only works if the log names every
/// card that left the draw pile, so: cards named == deck size - cards still in the draw pile.
/// </summary>
public class DeckAccountingTests
{
    [Theory]
    [MemberData(nameof(Fixtures.OnSupportedMaps), MemberType = typeof(Fixtures))]
    public void LogNamesEveryCardThatLeftTheDrawPile(string fixture)
    {
        var log = Fixtures.Load(fixture);
        var deck = ProjectDeck(log);

        var named = BgaLogCards.NamesIn(log)
            .Select(CardNameMap.Resolve)
            .Where(id => id is not null && deck.Contains(id))
            .ToHashSet();
        var finalDrawPile = log.Moves.Select(m => m.GameState?.DrawPile).Last(n => n is not null)!.Value;

        var leftTheDeck = deck.Count - finalDrawPile;
        Assert.Equal(leftTheDeck, named.Count);
    }

    [Theory]
    [MemberData(nameof(Fixtures.OnSupportedMaps), MemberType = typeof(Fixtures))]
    public void LogNamesNoProjectCardOutsideTheGamesDeck(string fixture)
    {
        var log = Fixtures.Load(fixture);
        var deck = ProjectDeck(log);

        var foreign = BgaLogCards.NamesIn(log)
            .Select(CardNameMap.Resolve)
            .Where(id => id is not null && IsProjectCard(id) && !deck.Contains(id))
            .Distinct()
            .Select(id => CardRegistry.GetDefinition(id!).Name)
            .ToList();

        Assert.Empty(foreign);
    }

    private static HashSet<string> ProjectDeck(BgaGameLog log)
    {
        var expansions = DeckBuilder.GetEnabledExpansions(new GameSetupOptions(
            PlayerCount: log.Players.Count,
            Map: MapName.Tharsis,
            CorporateEra: log.CorporateEraOn == true,
            DraftVariant: log.DraftOn == true,
            PreludeExpansion: log.PreludeOn == true));
        return [.. CardRegistry.GetProjectCardIds(expansions)];
    }

    private static bool IsProjectCard(string id) =>
        CardRegistry.GetDefinition(id).Type is CardType.Automated or CardType.Active or CardType.Event;
}
