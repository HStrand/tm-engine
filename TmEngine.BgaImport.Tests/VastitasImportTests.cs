using Newtonsoft.Json;
using tm_engine.Serialization;
using TmEngine.Domain.Models;

namespace TmEngine.BgaImport.Tests;

/// <summary>
/// A real Vastitas Borealis game, replayed by the importer. Every fork point is validated against BGA's own
/// scores and trackers, so this is what holds the board (hexes, ocean spaces, bonuses, the centre hex) to BGA.
/// </summary>
public class VastitasImportTests
{
    private static readonly JsonSerializerSettings Json = SerializationSettings.Create();

    [Fact]
    public void VastitasGame_CanBePlayedOn()
    {
        var points = Fixtures.ForkPoints(Fixtures.Vastitas2p);

        Assert.True(points.Eligible, string.Join("; ", points.Issues.Select(i => i.Message)));
        Assert.NotEmpty(points.ForkPoints);
    }

    [Fact]
    public void VastitasGame_ImportsAtEveryForkPoint_AgreeingWithBga()
    {
        var log = Fixtures.Load(Fixtures.Vastitas2p);

        foreach (var fork in Fixtures.ForkPoints(Fixtures.Vastitas2p).ForkPoints)
        {
            var result = BgaImporter.Import(log, new BgaImportOptions(fork.MoveIndex));

            Assert.True(result.Success, $"move index {fork.MoveIndex}: {string.Join("; ", result.Issues.Select(i => i.Message))}");
            Assert.Empty(result.Issues);
            Assert.Equal(MapName.VastitasBorealis, result.State!.Map);
        }
    }

    [Fact]
    public void VastitasGame_HasForkPointsAfterTheCentreHexIsTaken()
    {
        // StrandedKnight's greenery on the centre hex: 4 M€ for a temperature step.
        var log = Fixtures.Load(Fixtures.Vastitas2p);
        var placed = log.Moves.FindIndex(m => m.Description?.Contains("places Forest on Hex 5,5") == true);
        Assert.True(placed > 0);

        var later = Fixtures.ForkPoints(Fixtures.Vastitas2p).ForkPoints.First(f => f.MoveIndex > placed + 3);
        var result = BgaImporter.Import(log, new BgaImportOptions(later.MoveIndex));

        Assert.True(result.Success);
        Assert.Equal(TileType.Greenery, result.State!.PlacedTiles[new HexCoord(5, 5)].Type);
    }

    [Fact]
    public void MapName_IsWrittenTheWayTheWebsiteNamesTheMap()
    {
        Assert.Equal("\"Vastitas Borealis\"", JsonConvert.SerializeObject(MapName.VastitasBorealis, Json));
        Assert.Equal(MapName.VastitasBorealis, JsonConvert.DeserializeObject<MapName>("\"Vastitas Borealis\"", Json));
        Assert.Equal("\"Tharsis\"", JsonConvert.SerializeObject(MapName.Tharsis, Json));
    }
}
