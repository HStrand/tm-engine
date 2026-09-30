using TmEngine.Domain.Cards;

namespace TmEngine.BgaImport.Tests;

public class CardNameMapTests
{
    [Fact]
    public void EngineCardNames_AreUniqueAfterNormalisation()
    {
        var clashes = CardRegistry.All.Values
            .GroupBy(e => CardNameMap.Normalize(e.Definition.Name))
            .Where(g => g.Count() > 1)
            .Select(g => string.Join(" / ", g.Select(e => $"{e.Definition.Id} {e.Definition.Name}")))
            .ToList();

        Assert.Empty(clashes);
    }

    [Theory]
    [InlineData("Methane From Titan", "018")]
    [InlineData("CrediCor", "CORP01")]
    [InlineData("Psychrophiles", "P39")]
    [InlineData("Inventors' Guild", "006")]
    public void Resolve_IgnoresCaseAndPunctuation(string bgaName, string expectedId)
    {
        Assert.Equal(expectedId, CardNameMap.Resolve(bgaName));
    }

    [Theory]
    [MemberData(nameof(Fixtures.OnSupportedMaps), MemberType = typeof(Fixtures))]
    public void EveryCardNameInLog_ResolvesToAnEngineCard(string fixture)
    {
        var log = Fixtures.Load(fixture);

        var unresolved = BgaLogCards.NamesIn(log).Where(n => !CardNameMap.TryResolve(n, out _)).Distinct().Order().ToList();

        Assert.Empty(unresolved);
    }
}
