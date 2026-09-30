namespace TmEngine.BgaImport.Tests;

public class BgaMoveTextTests
{
    [Fact]
    public void Reveals_ReturnsRevealedCardsInOrder_WithWhetherEachWasKept()
    {
        var move = new BgaMove
        {
            Description = "Abtsein plays card Acquired Space Agency | Abtsein reveals Mass Converter: it does not have a Space tag"
                + " | Abtsein reveals Deimos Down: it has a Space tag | Abtsein reveals Wave Power: it does not have a Space tag",
        };

        Assert.Equal(
            [new("Mass Converter", false), new("Deimos Down", true), new("Wave Power", false)],
            BgaMoveText.Reveals(move).ToList());
    }

    [Fact]
    public void Reveals_IsEmpty_WhenNothingWasRevealed()
    {
        Assert.Empty(BgaMoveText.Reveals(new BgaMove { Description = "Abtsein pays 8 Plant" }));
    }
}
